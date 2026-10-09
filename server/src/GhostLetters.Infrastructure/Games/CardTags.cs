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
    private IReadOnlyDictionary<string, IReadOnlyList<Detail>> _meanings = new Dictionary<string, IReadOnlyList<Detail>>();
    private IReadOnlyDictionary<string, Dictionary<string, double>> _meaningWeights = new Dictionary<string, Dictionary<string, double>>();
    public static readonly CardTags Empty = new(new Dictionary<string, HashSet<string>>());

    private readonly IReadOnlyDictionary<string, HashSet<string>> _tags;

    public CardTags(IReadOnlyDictionary<string, HashSet<string>> tags) => _tags = tags;

    public int Count => _tags.Count;

    public IReadOnlyCollection<string> Of(string cardId) => _tags.TryGetValue(cardId, out var t) ? t : [];

    public CardAnnotation AnnotationOf(string card) => new(Of(card).Where(t => Group(t) != 0).Order().ToList(),
        _meanings.GetValueOrDefault(card) ?? Of(card).Where(t => Group(t) == 0).Order().Select(t => new Detail(t, 1, t)).ToList(),
        _details.GetValueOrDefault(card) ?? []);

    public CardTags WithAnnotations(IReadOnlyDictionary<string, CardAnnotation> annotations)
    {
        var tags = _tags.ToDictionary(e => e.Key, e => e.Value);
        var details = _details.ToDictionary(e => e.Key, e => e.Value);
        var meanings = _meanings.ToDictionary(e => e.Key, e => e.Value);
        var weights = _meaningWeights.ToDictionary(e => e.Key, e => e.Value);
        foreach (var (key, value) in annotations)
        {
            tags[key] = value.Tags.Concat(value.Meanings.Select(m => m.Tag)).ToHashSet();
            details[key] = value.Details.ToList(); meanings[key] = value.Meanings;
            weights[key] = value.Meanings.ToDictionary(m => m.Tag, m => m.Weight);
        }
        return new CardTags(tags) { _details = details, _meanings = meanings, _meaningWeights = weights };
    }

    private double Salience(string card, string tag, double secondary)
    {
        var weight = _meaningWeights.GetValueOrDefault(card)?.GetValueOrDefault(tag, 1) ?? 1;
        return weight >= 1 ? 1 : weight * Math.Clamp(secondary, 0, 1);
    }

    private IReadOnlyCollection<string> Visible(string card, double secondary) => Of(card).Where(t => Salience(card, t, secondary) > 0).ToList();

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

    public double Similarity(string a, string b, (double Meaning, double Shape, double Color) attention, double details, double secondaryMeanings = 0.35)
    {
        var whole = MainSimilarity(a, b, attention, secondaryMeanings);
        if (details <= 0 || a == b) return whole;
        var x = DetailsOf(a);
        var y = DetailsOf(b);
        if (x.Count == 0 && y.Count == 0) return whole;
        // Мелкая роза на клетке может связываться и с главным предметом «роза» на другой карте.
        foreach (var t in x.Keys.Union(y.Keys).ToList())
        {
            if (Visible(a, secondaryMeanings).Contains(t)) x[t] = Math.Max(x.GetValueOrDefault(t), y.GetValueOrDefault(t));
            if (Visible(b, secondaryMeanings).Contains(t)) y[t] = Math.Max(y.GetValueOrDefault(t), x.GetValueOrDefault(t));
        }
        // Неразмеченная карта без известных связей не штрафуется за отсутствие разметки.
        if (x.Count == 0 || y.Count == 0) return whole;
        var union = x.Keys.Union(y.Keys).Sum(t => Math.Max(x.GetValueOrDefault(t), y.GetValueOrDefault(t)));
        var shared = x.Keys.Intersect(y.Keys).Sum(t => Math.Min(x[t], y[t]));
        var focus = Math.Clamp(details, 0, 1);
        return (1 - focus) * whole + focus * shared / union;
    }

    /// <summary>Причины из тех же признаков, по которым бот действительно сравнил карты.</summary>
    public string Explain(string a, string b, (double Meaning, double Shape, double Color) attention, double details, double secondaryMeanings = 0.35)
    {
        var va = Visible(a, secondaryMeanings);
        var vb = Visible(b, secondaryMeanings);
        var da = _details.GetValueOrDefault(a) ?? [];
        var db = _details.GetValueOrDefault(b) ?? [];
        if ((da.Count == 0 || db.Count == 0) && !da.Any(d => vb.Contains(d.Tag)) && !db.Any(d => va.Contains(d.Tag)))
            details = 0; // Same fallback to the main image as Similarity.
        var reasons = new List<(double Score, string Text)>();
        var common = va.Intersect(vb).ToList();
        foreach (var (group, weight, label) in new[] {
            (0, attention.Meaning, "по смыслу: общий предмет или тема"),
            (1, attention.Shape, "по форме: похожий силуэт"),
            (2, attention.Color, "по цвету: общая палитра") })
        {
            if (common.Any(t => Group(t) == group) && weight > 0 && details < 1)
            {
                var selected = common.Where(t => Group(t) == group)
                    .OrderByDescending(t => Math.Min(Salience(a, t, secondaryMeanings), Salience(b, t, secondaryMeanings))).First();
                var meaning = _meanings.GetValueOrDefault(a)?.FirstOrDefault(m => m.Tag == selected)
                    ?? _meanings.GetValueOrDefault(b)?.FirstOrDefault(m => m.Tag == selected);
                var text = group == 0 && meaning is not null ? "по смыслу: " + meaning.Label : label;
                reasons.Add((weight * (1 - details) * Math.Min(Salience(a, selected, secondaryMeanings), Salience(b, selected, secondaryMeanings)), text));
            }
        }
        var other = DetailsOf(b);
        foreach (var d in _details.GetValueOrDefault(a) ?? [])
            if (details > 0 && (other.ContainsKey(d.Tag) || vb.Contains(d.Tag)))
                reasons.Add((details * Math.Min(other.GetValueOrDefault(d.Tag, d.Weight), d.Weight), "по деталям: " + d.Label));
        foreach (var d in _details.GetValueOrDefault(b) ?? [])
            if (details > 0 && va.Contains(d.Tag))
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
        => MainSimilarity(a, b, attention, 0.35);

    private double MainSimilarity(string a, string b, (double Meaning, double Shape, double Color) attention, double secondary)
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

            var union = gx.Union(gy).Sum(t => Math.Max(gx.Contains(t) ? Salience(a, t, secondary) : 0, gy.Contains(t) ? Salience(b, t, secondary) : 0));
            if (union <= 0) continue;
            var common = gx.Intersect(gy).Sum(t => Math.Min(Salience(a, t, secondary), Salience(b, t, secondary)));
            var confidence = Math.Min(gx.Select(t => Salience(a, t, secondary)).DefaultIfEmpty(0).Max(),
                gy.Select(t => Salience(b, t, secondary)).DefaultIfEmpty(0).Max());
            total += w * common / union * confidence;
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
