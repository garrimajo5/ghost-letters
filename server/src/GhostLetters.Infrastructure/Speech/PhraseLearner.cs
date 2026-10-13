using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>Запоминает фразы людей (из чата и голоса) и отдаёт ботам подходящие их манере.</summary>
public sealed class PhraseLearner(GhostLettersDbContext db, CardTagStore tagStore, SpeechKit speech, TimeProvider time, ILogger<PhraseLearner> logger)
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

    private sealed record BotRow(Guid Id, string Nickname, string Voice, string VoiceRole);

    private async Task<List<BotRow>> BotsAsync(CancellationToken ct) =>
        await (from b in db.BotProfiles.AsNoTracking()
               join u in db.Users.AsNoTracking() on b.UserId equals u.Id
               where b.Enabled
               orderby u.Nickname
               select new BotRow(b.UserId, u.Nickname, b.Voice, b.VoiceRole)).ToListAsync(ct);

    /// <summary>Выученные фразы для кабинета: новые сверху, с автором и ботами, которые говорят этой манерой.</summary>
    public async Task<PhraseListDto> ListAsync(int take, CancellationToken ct)
    {
        var rows = await db.PhraseTemplates.AsNoTracking().OrderByDescending(p => p.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);
        var authors = rows.Select(r => r.AuthorId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => authors.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nickname, ct);
        var speakers = (await BotsAsync(ct)).GroupBy(b => SpokenStyle.MannerOf(b.Id).ToString())
            .ToDictionary(g => g.Key, g => g.Select(b => new PhraseSpeakerDto(b.Id, b.Nickname)).ToList());
        return new PhraseListDto(speech.Enabled,
            rows.Select(r => new PhraseDto(r.Id, r.Intent, r.Emotion, r.Manner, r.Template, names.GetValueOrDefault(r.AuthorId, "—"), r.Seen, r.Blocked, r.CreatedAt)).ToList(),
            speakers);
    }

    /// <summary>
    /// Проиграть фразу голосом бота: бот той же манеры (или выбранный), вместо {card} — случайная карта,
    /// вместо {player} — другой бот. Без синтеза на сервере — только текст.
    /// </summary>
    public async Task<PhrasePlayDto?> PlayAsync(Guid id, Guid? botId, CancellationToken ct)
    {
        var row = await db.PhraseTemplates.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return null;
        var bots = await BotsAsync(ct);
        var speaker = bots.FirstOrDefault(b => b.Id == botId)
                      ?? bots.Where(b => SpokenStyle.MannerOf(b.Id).ToString() == row.Manner).OrderBy(_ => Random.Shared.Next()).FirstOrDefault()
                      ?? bots.OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
        var tags = await tagStore.LoadAsync(ct);
        var cards = tags.NamedCards.ToList();
        var card = cards.Count > 0 ? tags.NameOf(cards[Random.Shared.Next(cards.Count)]) : "клетка";
        var other = bots.Where(b => b.Id != speaker?.Id).Select(b => b.Nickname).OrderBy(_ => Random.Shared.Next()).FirstOrDefault() ?? "Аня";
        var text = PhraseBook.Fill(row.Template, card ?? "клетка", other);
        var (voice, role) = speaker is null ? (BotVoices.All[0].Id, BotVoices.All[0].Roles.FirstOrDefault()) : BotVoices.For(speaker.Id, speaker.Voice, speaker.VoiceRole);
        if (BotVoices.Find(voice)?.Male == false) text = SpokenStyle.Feminine(text);
        var audio = speech.Enabled && text.Length > 0 ? await speech.SynthesizeAsync(text, voice, role, ct) : null;
        return new PhrasePlayDto(text, speaker?.Nickname ?? "", audio is null ? null : Convert.ToBase64String(audio.Mp3));
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

public sealed record PhraseListDto(bool VoiceEnabled, IReadOnlyList<PhraseDto> Phrases, IReadOnlyDictionary<string, List<PhraseSpeakerDto>> Speakers);

public sealed record PhraseSpeakerDto(Guid Id, string Nickname);

/// <summary>Сказанная фраза: текст, кто сказал и MP3 (base64; null — озвучка выключена или не удалась).</summary>
public sealed record PhrasePlayDto(string Text, string Speaker, string? Audio);

public sealed record PhraseDto(Guid Id, string Intent, string Emotion, string Manner, string Template, string Author, int Seen, bool Blocked, DateTimeOffset CreatedAt);
