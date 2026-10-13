using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>Запоминает фразы людей (из чата и голоса) и отдаёт ботам подходящие их манере.</summary>
public sealed class PhraseLearner(GhostLettersDbContext db, CardTagStore tagStore, TimeProvider time, ILogger<PhraseLearner> logger)
{
    /// <summary>Сколько разных фраз держим на одну мысль и манеру — старые и редкие вытесняются.</summary>
    public const int PerKind = 200;

    /// <summary>Выучить фразы из реплики человека. Ошибки не мешают чату.</summary>
    public async Task LearnAsync(GameState state, Guid authorId, string? text, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var ids = state.Players.Select(p => p.Id).ToList();
            var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => u.Nickname).ToListAsync(ct);
            var tags = await tagStore.LoadAsync(ct);
            foreach (var phrase in PhraseBook.Learn(text, state.Board, state.Hints, names, tags))
            {
                var known = await db.PhraseTemplates.FirstOrDefaultAsync(p => p.Hash == phrase.Hash, ct);
                if (known is not null)
                {
                    known.Seen++;
                    continue;
                }

                db.PhraseTemplates.Add(new PhraseTemplate
                {
                    Id = Guid.NewGuid(), Intent = phrase.Intent, Emotion = phrase.Emotion, Manner = phrase.Manner.ToString(),
                    Template = phrase.Template, Hash = phrase.Hash, AuthorId = authorId, Seen = 1, CreatedAt = time.GetUtcNow(),
                });
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is DbUpdateException or InvalidOperationException)
        {
            logger.LogDebug(e, "Фраза не выучена");
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Выученные фразы для манеры бота: (мысль, эмоция) → шаблоны. Боты не говорят фразами, которые сами и выучили у ботов.</summary>
    public async Task<IReadOnlyList<(string Intent, string Emotion, string Template)>> ForAsync(SpokenStyle.Manner manner, CancellationToken ct)
    {
        var key = manner.ToString();
        return (await db.PhraseTemplates.AsNoTracking().Where(p => p.Manner == key && !p.Blocked)
                .OrderByDescending(p => p.Seen).ThenByDescending(p => p.CreatedAt).Take(PerKind * 6)
                .Select(p => new { p.Intent, p.Emotion, p.Template }).ToListAsync(ct))
            .Select(p => (p.Intent, p.Emotion, p.Template)).ToList();
    }

    /// <summary>Выученные фразы для кабинета: новые сверху, с автором.</summary>
    public async Task<IReadOnlyList<PhraseDto>> ListAsync(int take, CancellationToken ct)
    {
        var rows = await db.PhraseTemplates.AsNoTracking().OrderByDescending(p => p.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);
        var authors = rows.Select(r => r.AuthorId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => authors.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nickname, ct);
        return rows.Select(r => new PhraseDto(r.Id, r.Intent, r.Emotion, r.Manner, r.Template, names.GetValueOrDefault(r.AuthorId, "—"), r.Seen, r.Blocked, r.CreatedAt)).ToList();
    }

    /// <summary>Запретить (или вернуть) фразу: запрещённую боты не говорят, а повтор её не выучивает заново.</summary>
    public async Task<bool> BlockAsync(Guid id, bool blocked, CancellationToken ct)
    {
        var row = await db.PhraseTemplates.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return false;
        row.Blocked = blocked;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed record PhraseDto(Guid Id, string Intent, string Emotion, string Manner, string Template, string Author, int Seen, bool Blocked, DateTimeOffset CreatedAt);
