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

    /// <summary>Похожесть двух карт 0…1: доля общих тегов (мера Жаккара). Одна и та же карта — 1.</summary>
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

        var common = x.Count(y.Contains);
        return common == 0 ? 0 : common / (double)(x.Count + y.Count - common);
    }

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
