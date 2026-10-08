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
    public static readonly CardTags Empty = new(new Dictionary<string, HashSet<string>>());

    private readonly IReadOnlyDictionary<string, HashSet<string>> _tags;

    public CardTags(IReadOnlyDictionary<string, HashSet<string>> tags) => _tags = tags;

    public int Count => _tags.Count;

    public IReadOnlyCollection<string> Of(string cardId) => _tags.TryGetValue(cardId, out var t) ? t : [];

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
        logger.LogInformation("Теги карт: {Count} из {Path}", tags.Count, path);
        return tags;
    }
}
