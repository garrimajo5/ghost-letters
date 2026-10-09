using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GhostLetters.Infrastructure.Games;

/// <summary>
/// Что нарисовано на картах: несколько тегов на карту (предмет, цвет, материал, тема) из tags.json рядом с cards.json.
/// Ботам этого хватает, чтобы «видеть» сходство писем и улик, не подглядывая в чужие роли.
/// </summary>
public sealed class CardTags
{
    public sealed record Detail(string Tag, double Weight, string Label);
    private IReadOnlyDictionary<string, List<Detail>> _details = new Dictionary<string, List<Detail>>();
    public static readonly CardTags Empty = new(new Dictionary<string, HashSet<string>>());

    private readonly IReadOnlyDictionary<string, HashSet<string>> _tags;

    public CardTags(IReadOnlyDictionary<string, HashSet<string>> tags) => _tags = tags;

    public int Count => _tags.Count;

    public IReadOnlyCollection<string> Of(string cardId) => _tags.TryGetValue(cardId, out var t) ? t : [];

    public CardTags WithDetails(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, List<Detail>>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        if (raw.Values.SelectMany(v => v).Any(d => string.IsNullOrWhiteSpace(d.Tag) ||
            string.IsNullOrWhiteSpace(d.Label) || !double.IsFinite(d.Weight) || d.Weight <= 0 || d.Weight > 1))
        {
            throw new FormatException("Деталям нужны тег, подпись и вес от 0 до 1.");
        }

        // Some older main tags already describe tiny objects (e.g. the roses on
        // the cage). Once classified as a detail, do not also count them as the
        // main subject: Details=0 must really ignore them.
        var main = _tags.ToDictionary(e => e.Key, e => e.Value
            .Except((raw.GetValueOrDefault(e.Key) ?? []).Select(d => d.Tag)).ToHashSet());
        return new CardTags(main) { _details = raw };
    }

    private Dictionary<string, double> DetailsOf(string card) =>
        (_details.GetValueOrDefault(card) ?? []).GroupBy(d => d.Tag)
        .ToDictionary(g => g.Key, g => g.Max(d => d.Weight));

    public double Similarity(string a, string b, (double Meaning, double Shape, double Color) attention, double details)
    {
        var whole = Similarity(a, b, attention);
        if (details <= 0 || a == b) return whole;
        var x = DetailsOf(a);
        var y = DetailsOf(b);
        if (x.Count == 0 && y.Count == 0) return whole;
        // Мелкая роза на клетке может связываться и с главным предметом «роза» на другой карте.
        foreach (var t in x.Keys.Union(y.Keys).ToList())
        {
            if (Of(a).Contains(t)) x[t] = Math.Max(x.GetValueOrDefault(t), y.GetValueOrDefault(t));
            if (Of(b).Contains(t)) y[t] = Math.Max(y.GetValueOrDefault(t), x.GetValueOrDefault(t));
        }
        // Неразмеченная карта без известных связей не штрафуется за отсутствие разметки.
        if (x.Count == 0 || y.Count == 0) return whole;
        var union = x.Keys.Union(y.Keys).Sum(t => Math.Max(x.GetValueOrDefault(t), y.GetValueOrDefault(t)));
        var shared = x.Keys.Intersect(y.Keys).Sum(t => Math.Min(x[t], y[t]));
        var focus = Math.Clamp(details, 0, 1);
        return (1 - focus) * whole + focus * shared / union;
    }

    /// <summary>Причины из тех же признаков, по которым бот действительно сравнил карты.</summary>
    public string Explain(string a, string b, (double Meaning, double Shape, double Color) attention, double details)
    {
        var da = _details.GetValueOrDefault(a) ?? [];
        var db = _details.GetValueOrDefault(b) ?? [];
        if ((da.Count == 0 || db.Count == 0) && !da.Any(d => Of(b).Contains(d.Tag)) && !db.Any(d => Of(a).Contains(d.Tag)))
            details = 0; // Same fallback to the main image as Similarity.
        var reasons = new List<(double Score, string Text)>();
        var common = Of(a).Intersect(Of(b)).ToList();
        foreach (var (group, weight, label) in new[] {
            (0, attention.Meaning, "по смыслу: общий предмет или тема"),
            (1, attention.Shape, "по форме: похожий силуэт"),
            (2, attention.Color, "по цвету: общая палитра") })
        {
            if (common.Any(t => Group(t) == group) && weight > 0 && details < 1)
                reasons.Add((weight * (1 - details) * common.Count(t => Group(t) == group) /
                    Of(a).Union(Of(b)).Count(t => Group(t) == group), label));
        }
        var other = DetailsOf(b);
        foreach (var d in _details.GetValueOrDefault(a) ?? [])
            if (details > 0 && (other.ContainsKey(d.Tag) || Of(b).Contains(d.Tag)))
                reasons.Add((details * Math.Min(other.GetValueOrDefault(d.Tag, d.Weight), d.Weight), "по деталям: " + d.Label));
        foreach (var d in _details.GetValueOrDefault(b) ?? [])
            if (details > 0 && Of(a).Contains(d.Tag))
                reasons.Add((details * d.Weight, "по деталям: " + d.Label));
        return reasons.Count == 0 ? "явной связи не вижу" :
            string.Join("; ", reasons.OrderByDescending(r => r.Score).Take(2).Select(r => r.Text));
    }

    /// <summary>Цвета и форма (shape-*) — визуальные теги: они тоже связывают карты, но вдвое слабее смысла.</summary>
    private static readonly HashSet<string> Colors =
        ["red", "orange", "yellow", "green", "blue", "purple", "pink", "brown", "black", "white", "gray"];

    public static double Weight(string tag) => tag.StartsWith("shape-", StringComparison.Ordinal) || Colors.Contains(tag) ? 0.5 : 1;

    /// <summary>
    /// Похожесть двух карт 0…1: взвешенная доля общих тегов (мера Жаккара; цвет и форма весят половину).
    /// Одна и та же карта — 1.
    /// </summary>
    public double Similarity(string a, string b)
    {
        if (a == b)
        {
            return 1;
        }

        if (!_tags.TryGetValue(a, out var x) || !_tags.TryGetValue(b, out var y) || x.Count == 0 || y.Count == 0)
        {
            return 0;
        }

        var common = x.Where(y.Contains).Sum(Weight);
        return common == 0 ? 0 : common / (x.Sum(Weight) + y.Sum(Weight) - common);
    }

    /// <summary>
    /// Похожесть глазами конкретного бота: сходство по смыслу, по форме и по цвету считается отдельно
    /// (мера Жаккара внутри каждой группы тегов) и смешивается в долях его внимания.
    /// Группы, которых нет ни у одной из двух карт, не участвуют — их доля делится между остальными.
    /// </summary>
    public double Similarity(string a, string b, (double Meaning, double Shape, double Color) attention)
    {
        if (a == b)
        {
            return 1;
        }

        if (!_tags.TryGetValue(a, out var x) || !_tags.TryGetValue(b, out var y) || x.Count == 0 || y.Count == 0)
        {
            return 0;
        }

        double total = 0, weight = 0;
        foreach (var (group, w) in new[] { (0, attention.Meaning), (1, attention.Shape), (2, attention.Color) })
        {
            var gx = x.Where(t => Group(t) == group).ToHashSet();
            var gy = y.Where(t => Group(t) == group).ToHashSet();
            if (gx.Count == 0 && gy.Count == 0)
            {
                continue;
            }

            var common = gx.Count(gy.Contains);
            total += w * common / (gx.Count + gy.Count - common);
            weight += w;
        }

        return weight <= 0 ? 0 : total / weight;
    }

    /// <summary>0 — смысл, 1 — форма (shape-*), 2 — цвет.</summary>
    public static int Group(string tag) => tag.StartsWith("shape-", StringComparison.Ordinal) ? 1 : Colors.Contains(tag) ? 2 : 0;

    public static CardTags Parse(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? [];
        return new CardTags(raw.ToDictionary(e => e.Key, e => e.Value.Select(t => t.Trim().ToLowerInvariant()).ToHashSet()));
    }

    /// <summary>Cards:TagsPath, а если не задан — tags.json рядом с Cards:ManifestPath. Нет файла — пустые теги (боты ходят наугад).</summary>
    public static CardTags FromConfiguration(IConfiguration configuration, ILogger logger)
    {
        var path = configuration["Cards:TagsPath"];
        var manifest = configuration["Cards:ManifestPath"];
        if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(manifest))
        {
            path = Path.Combine(Path.GetDirectoryName(manifest) ?? ".", "tags.json");
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            logger.LogInformation("Теги карт не найдены ({Path}) — боты будут ходить наугад", path);
            return Empty;
        }

        var tags = Parse(File.ReadAllText(path));
        var detailsPath = Path.Combine(Path.GetDirectoryName(path) ?? ".", "details.json");
        if (File.Exists(detailsPath)) tags = tags.WithDetails(File.ReadAllText(detailsPath));
        logger.LogInformation("Теги карт: {Count} из {Path}", tags.Count, path);
        return tags;
    }
}
