using GhostLetters.Application;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Games;

public sealed record CardAnnotation(IReadOnlyList<string> Tags, IReadOnlyList<CardTags.Detail> Meanings, IReadOnlyList<CardTags.Detail> Details);
public sealed record AdminCardDto(Guid Id, string ImageKey, string? Title, string SetCode, bool IsActive, int Version, CardAnnotation Annotation);
public sealed record CardSetDto(string Code, string Title, IReadOnlyList<string> Cards);
public sealed record AdminCardPage(IReadOnlyList<AdminCardDto> Cards, int Total, IReadOnlyList<CardSetDto> Sets);
public sealed record SaveCardRequest(string? Title, string SetCode, bool IsActive, int Version, CardAnnotation Annotation);

/// <summary>
/// Разметка из базы поверх базовой. Такт ботов идёт раз в 1,5 с, а разметка меняется только в админке:
/// перечитываем её, лишь когда изменился отпечаток (число размеченных карт и сумма их версий).
/// </summary>
public sealed class CardTagStore(GhostLettersDbContext db, CardTags baseline)
{
    private static readonly object Gate = new();
    private static (CardTags Baseline, string? Database, int Count, long Versions, CardTags Tags)? _cached;

    public async Task<CardTags> LoadAsync(CancellationToken ct)
    {
        var print = await db.Cards.AsNoTracking().Where(c => c.Annotations != null)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Versions = g.Sum(c => (long)c.MetadataVersion) })
            .FirstOrDefaultAsync(ct);
        var (count, versions) = (print?.Count ?? 0, print?.Versions ?? 0);
        var database = db.Database.GetConnectionString();
        lock (Gate)
        {
            if (_cached is { } hit && ReferenceEquals(hit.Baseline, baseline) && hit.Database == database && hit.Count == count && hit.Versions == versions)
                return hit.Tags;
        }

        var cards = await db.Cards.AsNoTracking().Where(c => c.Annotations != null)
            .Select(c => new { c.ImageKey, c.Annotations }).ToListAsync(ct);
        var tags = baseline.WithAnnotations(cards.ToDictionary(c => c.ImageKey,
            c => GameJson.Deserialize<CardAnnotation>(c.Annotations!)));
        lock (Gate) _cached = (baseline, database, count, versions, tags);
        return tags;
    }
}

public sealed class CardAdminService(GhostLettersDbContext db, BotAdminService admins, CardTags baseline)
{
    public async Task<IReadOnlyList<CardSetDto>> PublicSetsAsync(CancellationToken ct)
    {
        var sets = await db.CardSets.AsNoTracking().OrderBy(s => s.Code).ToListAsync(ct);
        var cards = await db.Cards.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.ImageKey)
            .Select(c => new { c.SetId, c.ImageKey }).ToListAsync(ct);
        return sets.Select(s => new CardSetDto(s.Code, s.Title, cards.Where(c => c.SetId == s.Id).Select(c => c.ImageKey).ToList())).ToList();
    }

    public async Task<AdminCardPage> ListAsync(Guid admin, string? query, string? setCode, bool? active, int page, CancellationToken ct)
    {
        admins.RequireAdmin(admin);
        if (page < 0 || page > 10000 || query?.Length > 80) throw AppException.Validation("Неверные параметры поиска.");
        var rows = from c in db.Cards.AsNoTracking() join s in db.CardSets.AsNoTracking() on c.SetId equals s.Id select new { c, s.Code };
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLowerInvariant();
            rows = rows.Where(x => x.c.ImageKey.ToLower().Contains(term) || (x.c.Title != null && x.c.Title.ToLower().Contains(term)));
        }
        if (!string.IsNullOrEmpty(setCode)) rows = rows.Where(x => x.Code == setCode);
        if (active is not null) rows = rows.Where(x => x.c.IsActive == active);
        var total = await rows.CountAsync(ct);
        var found = await rows.OrderBy(x => x.c.ImageKey).ThenBy(x => x.c.Id).Skip(page * 40).Take(40).ToListAsync(ct);
        var sets = await db.CardSets.AsNoTracking().OrderBy(s => s.Code).Select(s => new CardSetDto(s.Code, s.Title, new List<string>())).ToListAsync(ct);
        return new AdminCardPage(found.Select(x => Dto(x.c, x.Code)).ToList(), total, sets);
    }

    public async Task<AdminCardDto> SaveAsync(Guid admin, Guid id, SaveCardRequest request, CancellationToken ct)
    {
        admins.RequireAdmin(admin);
        var annotation = Validate(request.Annotation);
        var title = request.Title?.Trim();
        if (title?.Length > 64) throw AppException.Validation("Название — до 64 символов.");
        var set = await db.CardSets.AsNoTracking().FirstOrDefaultAsync(s => s.Code == request.SetCode, ct)
            ?? throw AppException.Validation("Набор не найден.");
        var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw AppException.NotFound("Карта не найдена.");
        if (card.MetadataVersion != request.Version) throw Conflict();
        if (card.SetId != set.Id) card.SetManuallyAssigned = true;
        card.SetId = set.Id;
        card.Title = string.IsNullOrEmpty(title) ? null : title;
        card.IsActive = request.IsActive;
        card.Annotations = GameJson.Serialize(annotation);
        card.MetadataVersion++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Conflict(); }
        return Dto(card, set.Code);
    }

    private static AppException Conflict() => AppException.Conflict("CARD_CHANGED", "Карту уже изменили. Обновите список и откройте её заново.");

    private AdminCardDto Dto(Card card, string setCode) => new(card.Id, card.ImageKey, card.Title, setCode, card.IsActive,
        card.MetadataVersion, card.Annotations is null ? baseline.AnnotationOf(card.ImageKey) : GameJson.Deserialize<CardAnnotation>(card.Annotations));

    private static CardAnnotation Validate(CardAnnotation? value)
    {
        if (value?.Tags is null || value.Meanings is null || value.Details is null ||
            value.Tags.Count > 40 || value.Meanings.Count > 60 || value.Details.Count > 60)
            throw AppException.Validation("Нужны метки, смыслы и детали: до 40, 60 и 60 соответственно.");
        string Tag(string? raw)
        {
            var t = raw?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(t) || t.Length > 64 || t.Any(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_'))
                throw AppException.Validation("Ключ метки — до 64 букв, цифр, дефисов или подчёркиваний.");
            return t;
        }
        List<CardTags.Detail> Weighted(IReadOnlyList<CardTags.Detail> items) => items.Select(d =>
        {
            if (d is null || string.IsNullOrWhiteSpace(d.Label) || d.Label.Trim().Length > 80 || !double.IsFinite(d.Weight) || d.Weight <= 0 || d.Weight > 1)
                throw AppException.Validation("Смыслу или детали нужны подпись до 80 символов и вес больше 0, не выше 1.");
            return d with { Tag = Tag(d.Tag), Label = d.Label.Trim() };
        }).ToList();
        var tags = value.Tags.Select(Tag).ToList();
        var meanings = Weighted(value.Meanings); var details = Weighted(value.Details);
        if (tags.Any(t => CardTags.Group(t) == 0) || meanings.Any(d => CardTags.Group(d.Tag) != 0))
            throw AppException.Validation("Цвет и shape-* укажите в метках; предметы и темы — в смыслах.");
        var all = tags.Concat(meanings.Select(d => d.Tag)).Concat(details.Select(d => d.Tag)).ToList();
        if (all.Distinct().Count() != all.Count) throw AppException.Validation("Не повторяйте один ключ в разных признаках карты.");
        return new(tags, meanings, details);
    }
}
