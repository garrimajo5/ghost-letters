using System.Text.Json;
using GhostLetters.Application;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GhostLetters.Infrastructure.Games;

/// <summary>
/// Карты наборов. Источник — cards.json от tools/cards (Cards:ManifestPath).
/// Для разработки без манифеста можно задать Cards:SeedOriginalCount — тогда «Оригинальный»
/// заполняется id orig_0001…, теми же, что выдаёт нарезка.
/// </summary>
public sealed class CardCatalog(GhostLettersDbContext db, IConfiguration configuration, ILogger<CardCatalog> logger)
{
    /// <summary>Колода партии: все активные карты выбранных наборов.</summary>
    public async Task<List<string>> DeckAsync(IReadOnlyList<string> setCodes, CancellationToken ct)
    {
        var sets = await db.CardSets.Where(s => setCodes.Contains(s.Code)).Select(s => new { s.Id, s.Code }).ToListAsync(ct);
        var missing = setCodes.Except(sets.Select(s => s.Code)).ToList();
        if (missing.Count > 0)
        {
            throw AppException.Validation($"Нет наборов карт: {string.Join(", ", missing)}.");
        }

        var ids = sets.Select(s => s.Id).ToList();
        return await db.Cards.Where(c => ids.Contains(c.SetId) && c.IsActive)
            .OrderBy(c => c.ImageKey).Select(c => c.ImageKey).ToListAsync(ct);
    }

    /// <summary>Заполнить карты при старте сервера. Существующие карты не трогает.</summary>
    public async Task EnsureSeededAsync(CancellationToken ct)
    {
        var manifest = configuration["Cards:ManifestPath"];
        if (!string.IsNullOrWhiteSpace(manifest) && File.Exists(manifest))
        {
            await ImportManifestAsync(manifest, ct);
            return;
        }

        var count = configuration.GetValue<int>("Cards:SeedOriginalCount");
        if (count > 0)
        {
            var keys = Enumerable.Range(1, count).Select(i => $"orig_{i:0000}").ToList();
            var added = await AddMissingAsync(CatalogSeed.OriginalSetId, keys, ct);
            logger.LogInformation("Карты-заглушки «Оригинального» набора: добавлено {Added}", added);
        }
    }

    /// <summary>
    /// Карты из манифеста: новые добавляются, а карта, которую перенесли в другой набор (разметка по символу
    /// на карте), переезжает туда же — id карты не меняется, так что идущие партии это не задевает.
    /// </summary>
    public async Task<int> ImportManifestAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<Manifest>(stream, GameJson.Options, ct)
                       ?? throw new InvalidOperationException("Пустой манифест карт.");
        var added = 0;
        var moved = 0;
        foreach (var set in manifest.Sets)
        {
            var setId = await db.CardSets.Where(s => s.Code == set.Code).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct)
                        ?? throw new InvalidOperationException($"Набора {set.Code} нет в каталоге.");
            var keys = set.Cards.Select(c => c.Id).Distinct().ToList();
            var known = await db.Cards.Where(c => keys.Contains(c.ImageKey)).ToListAsync(ct);
            foreach (var card in known.Where(c => c.SetId != setId))
            {
                card.SetId = setId;
                moved++;
            }

            await db.SaveChangesAsync(ct);
            added += await AddMissingAsync(setId, keys.Except(known.Select(c => c.ImageKey)).ToList(), ct);
        }

        logger.LogInformation("Импорт карт из {Path}: добавлено {Added}, перенесено в другой набор {Moved}", path, added, moved);
        return added;
    }

    private async Task<int> AddMissingAsync(Guid setId, IReadOnlyList<string> keys, CancellationToken ct)
    {
        var existing = (await db.Cards.Where(c => c.SetId == setId).Select(c => c.ImageKey).ToListAsync(ct)).ToHashSet();
        var fresh = keys.Where(k => !existing.Contains(k)).Distinct().ToList();
        db.Cards.AddRange(fresh.Select(k => new Card { Id = Guid.NewGuid(), SetId = setId, ImageKey = k }));
        await db.SaveChangesAsync(ct);
        return fresh.Count;
    }

    private sealed record Manifest(int Version, List<ManifestSet> Sets);

    private sealed record ManifestSet(string Code, string Title, List<ManifestCard> Cards);

    private sealed record ManifestCard(string Id, string File);
}
