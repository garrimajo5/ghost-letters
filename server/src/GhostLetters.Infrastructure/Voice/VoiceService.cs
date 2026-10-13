using System.Collections.Concurrent;
using GhostLetters.Application;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using GhostLetters.Infrastructure.Speech;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Voice;

/// <summary>Сказанное голосом: распознанный текст и то, что из него поняли (карты с подписями).</summary>
public sealed record TranscriptDto(string Text, IReadOnlyList<string> CardIds, IReadOnlyList<string> CardNotes, ChatMessageDto? Message);

/// <summary>Ответ на переспрос бота: какой карте игрок отдал своё название.</summary>
public sealed record AliasRequest(Guid QuestionId, string CardId);

/// <summary>Итог: принято ли название (valid — запомнили, unverified — только на партию, invalid — не та карта).</summary>
public sealed record AliasResult(string Verdict, string Phrase, string CardId);

/// <summary>Голосовой чат партии: доступ к комнате и «уши» ботов — расшифровка сказанного в чат.</summary>
public sealed class VoiceService(GhostLettersDbContext db, GameService games, VoiceRooms rooms, SpeechKit speech,
    ChatService chat, CardTagStore tagStore, SpeechWriter writer, BotService bots, TimeProvider time)
{
    /// <summary>Префикс расшифровки в чате: по нему клиент показывает «голосом».</summary>
    public const string TranscriptPrefix = "Голосом: ";

    public const long MaxTranscriptBytes = 1_100_000;

    /// <summary>Сколько недобросовестных названий за партию — и бот больше не переспрашивает этого игрока.</summary>
    public const int BadAliasesToStopAsking = 2;

    /// <summary>Снижение симпатии ботов партии за явно неверное название карты.</summary>
    public const int BadAliasPenalty = 5;

    private sealed record Pending(Guid GameId, Guid UserId, Guid BotId, string Phrase, Category? Row, string Note, DateTimeOffset At);

    /// <summary>Переспросы ботов: id вопроса в чате → кто, что сказал и с каким смыслом. Живут 10 минут.</summary>
    private static readonly ConcurrentDictionary<Guid, Pending> Questions = new();

    public async Task<VoiceAccess> AccessAsync(Guid gameId, Guid userId, CancellationToken ct)
    {
        if (!rooms.Enabled) throw new AppException("VOICE_OFF", "Голосовой чат не настроен на сервере.", 503);
        await games.RequireViewerAsync(gameId, userId, ct);
        var game = await db.Games.AsNoTracking().FirstAsync(g => g.Id == gameId, ct);
        var name = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Nickname).FirstOrDefaultAsync(ct) ?? "?";
        return rooms.Access(GameStore.Read(game), userId, name);
    }

    /// <summary>
    /// Кусок речи игрока (WAV 16 кГц моно, до 30 с) → текст в общий чат от его имени, с картами, которые он назвал.
    /// Так боты «слышат» людей. Карту назвали непонятно — бот переспрашивает, игрок отвечает нажатием на карту.
    /// </summary>
    public async Task<TranscriptDto> TranscribeAsync(Guid gameId, Guid userId, Stream audio, long length, CancellationToken ct)
    {
        if (length is <= 0 or > MaxTranscriptBytes) throw AppException.Validation("Запись — до 30 секунд.");
        var viewer = await games.RequireViewerAsync(gameId, userId, ct);
        if (viewer.IsObserver) throw AppException.Forbidden("Зритель не говорит.");
        var game = await db.Games.AsNoTracking().FirstAsync(g => g.Id == gameId, ct);
        var state = GameStore.Read(game);
        var player = state.Player(userId);
        if (player.Role == Role.Ghost && state.Result is null) throw AppException.Forbidden("Призрак до итогов молчит.");
        using var buffer = new MemoryStream();
        await audio.CopyToAsync(buffer, ct);
        var text = await speech.RecognizeAsync(buffer.ToArray(), 16000, ct);
        if (text is null) return new TranscriptDto("", [], [], null);
        return await PostAsync(state, userId, text, ct);
    }

    /// <summary>Распознанный текст — в чат с понятыми картами; непонятное название — переспрос бота.</summary>
    public async Task<TranscriptDto> PostAsync(GameState state, Guid userId, string text, CancellationToken ct)
    {
        var tags = await tagStore.LoadAsync(ct);
        var own = state.Letters.Where(l => l.From == userId).Select(l => l.CardId).ToList();
        var aliases = await AliasesAsync(state.Id, userId, ct);
        var read = SpokenCards.Read(text, state.Board, state.Hints, own, tags, aliases);
        var line = TranscriptPrefix + text;
        if (line.Length > ChatService.MaxTextLength) line = line[..ChatService.MaxTextLength];
        var message = await chat.SendAsync(state.Id, userId, new SendChatRequest(ChatChannels.Public, line, null, read.Cards, read.Notes), ct);
        if (read.Unclear is { } unclear) await AskAsync(state, userId, unclear, ct);
        return new TranscriptDto(text, read.Cards, read.Notes, message);
    }

    /// <summary>
    /// Как игрок называет карты: его подтверждённые названия, непроверенные — только в этой партии, и общие —
    /// подтверждённые хотя бы у двух разных игроков. Недобросовестные не учитываются.
    /// </summary>
    public async Task<Dictionary<string, string>> AliasesAsync(Guid gameId, Guid userId, CancellationToken ct)
    {
        var mine = await db.CardAliases.AsNoTracking()
            .Where(a => a.UserId == userId && (a.Valid == true || (a.Valid == null && a.GameId == gameId)))
            .OrderBy(a => a.CreatedAt).Select(a => new { a.Phrase, a.CardId }).ToListAsync(ct);
        var shared = await db.CardAliases.AsNoTracking().Where(a => a.Valid == true)
            .GroupBy(a => new { a.Phrase, a.CardId }).Where(g => g.Select(a => a.UserId).Distinct().Count() >= 2)
            .Select(g => g.Key).ToListAsync(ct);
        var result = new Dictionary<string, string>();
        foreach (var a in shared) result.TryAdd(a.Phrase, a.CardId);
        foreach (var a in mine) result[a.Phrase] = a.CardId;
        return result;
    }

    /// <summary>Бот партии переспрашивает: «Аня, «грабли» — это какая карта? Нажмите на неё».</summary>
    private async Task AskAsync(GameState state, Guid userId, SpokenCards.Unknown unclear, CancellationToken ct)
    {
        var bad = await db.CardAliases.CountAsync(a => a.GameId == state.Id && a.UserId == userId && a.Valid == false, ct);
        if (bad >= BadAliasesToStopAsking) return;
        var playerIds = state.Players.Where(p => p.Id != userId && (p.Role != Role.Ghost || state.Result is not null)).Select(p => p.Id).ToList();
        var botIds = await db.Users.AsNoTracking().Where(u => playerIds.Contains(u.Id) && u.IsBot).Select(u => u.Id).ToListAsync(ct);
        if (botIds.Count == 0) return;
        var now = time.GetUtcNow();
        foreach (var old in Questions.Where(q => now - q.Value.At > TimeSpan.FromMinutes(10)).Select(q => q.Key).ToList()) Questions.TryRemove(old, out _);
        if (Questions.Values.Any(q => q.GameId == state.Id && q.UserId == userId && q.Phrase == unclear.Phrase)) return;
        var bot = botIds[Random.Shared.Next(botIds.Count)];
        var name = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Nickname).FirstAsync(ct);
        if (name.StartsWith("Бот ", StringComparison.Ordinal)) name = name[4..];
        var where = unclear.Row is { } row ? $" в ряду «{RowName(row)}»" : "";
        var variants = new[]
        {
            $"{name}, «{unclear.Phrase}» — это какая карта{where}? Нажмите на неё.",
            $"{name}, не понял: «{unclear.Phrase}» — это какая карта{where}? Покажите нажатием.",
            $"{name}, уточни: «{unclear.Phrase}» — это какая карта{where}? Нажми на неё.",
        };
        var text = variants[Random.Shared.Next(variants.Length)];
        await bots.SayAsync(state, bot, new SendChatRequest(ChatChannels.Public, text, null, [], []), ct);
        var question = await db.ChatMessages.AsNoTracking()
            .Where(m => m.GameId == state.Id && m.AuthorId == bot && m.Text == text).OrderByDescending(m => m.CreatedAt)
            .Select(m => m.Id).FirstAsync(ct);
        Questions[question] = new Pending(state.Id, userId, bot, unclear.Phrase, unclear.Row, unclear.Note, now);
    }

    /// <summary>
    /// Игрок нажал карту в ответ на переспрос. Название проверяется по разметке карты (и моделью, если подключена):
    /// подтвердилось — боты запоминают; явно не та карта — не учитывают, симпатия ботов партии к игроку снижается.
    /// </summary>
    public async Task<AliasResult> AnswerAsync(Guid gameId, Guid userId, AliasRequest request, CancellationToken ct)
    {
        if (!Questions.TryGetValue(request.QuestionId, out var pending) || pending.GameId != gameId || pending.UserId != userId)
            throw AppException.NotFound("Вопрос уже неактуален.");
        var game = await db.Games.AsNoTracking().FirstAsync(g => g.Id == gameId, ct);
        var state = GameStore.Read(game);
        var own = state.Letters.Where(l => l.From == userId).Select(l => l.CardId).ToList();
        var onTable = state.Board.SelectMany(r => r.Cards).Concat(state.Hints.SelectMany(h => h.Cards)).ToList();
        if (!onTable.Contains(request.CardId) && !own.Contains(request.CardId)) throw AppException.Validation("Такой карты нет на столе.");
        if (pending.Row is { } row && state.Board.FirstOrDefault(r => r.Category == row) is { } wanted && !wanted.Cards.Contains(request.CardId))
            throw AppException.Validation($"Нужна карта из ряда «{RowName(row)}».");
        Questions.TryRemove(request.QuestionId, out _);

        var tags = await tagStore.LoadAsync(ct);
        var verdict = CardNaming.Check(pending.Phrase, request.CardId, onTable, tags);
        if (verdict == CardNaming.Verdict.Unverified)
        {
            var annotation = tags.AnnotationOf(request.CardId);
            var features = annotation.Meanings.Select(m => m.Label).Concat(annotation.Details.Select(d => d.Label)).ToList();
            verdict = await writer.JudgeAsync(pending.Phrase, tags.NamesOf(request.CardId), features, ct) switch
            {
                true => CardNaming.Verdict.Valid,
                false => CardNaming.Verdict.Invalid,
                null => CardNaming.Verdict.Unverified,
            };
        }

        db.CardAliases.Add(new CardAlias
        {
            Id = Guid.NewGuid(), UserId = userId, GameId = gameId, CardId = request.CardId, Phrase = pending.Phrase,
            Valid = verdict switch { CardNaming.Verdict.Valid => true, CardNaming.Verdict.Invalid => false, _ => null },
            CreatedAt = time.GetUtcNow(),
        });
        var name = tags.NameOf(request.CardId) ?? "эта карта";
        var playerName = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Nickname).FirstAsync(ct);
        if (playerName.StartsWith("Бот ", StringComparison.Ordinal)) playerName = playerName[4..];
        if (verdict == CardNaming.Verdict.Invalid)
        {
            var others = state.Players.Select(p => p.Id).ToList();
            var judges = await db.Users.Where(u => others.Contains(u.Id) && u.IsBot).Select(u => u.Id).ToListAsync(ct);
            var relations = await db.BotRelationships.Where(r => judges.Contains(r.BotId) && r.PlayerId == userId).ToListAsync(ct);
            foreach (var botId in judges)
            {
                var relation = relations.FirstOrDefault(r => r.BotId == botId);
                if (relation is null)
                {
                    relation = new BotRelationship { BotId = botId, PlayerId = userId };
                    db.BotRelationships.Add(relation);
                }
                relation.Score = Math.Clamp(relation.Score - BadAliasPenalty, -100, 100);
                relation.UpdatedAt = time.GetUtcNow();
            }

            await db.SaveChangesAsync(ct);
            await bots.SayAsync(state, pending.BotId, new SendChatRequest(ChatChannels.Public,
                $"{playerName}, «{pending.Phrase}» и {name}? Не похоже — не засчитываю.", null, [], []), ct);
            return new AliasResult("invalid", pending.Phrase, request.CardId);
        }

        await db.SaveChangesAsync(ct);
        // Название принято: сказанное — как показ на столе (карта с тем смыслом, что был в речи).
        var reference = CardRef(state, request.CardId);
        await chat.SendAsync(gameId, userId, new SendChatRequest(ChatChannels.Public, $"«{pending.Phrase}» — это {reference}.", null,
            [request.CardId], [pending.Note == "кидал эту" && !own.Contains(request.CardId) ? "думаю, эта" : pending.Note]), ct);
        var reply = verdict == CardNaming.Verdict.Valid
            ? $"Понял, {playerName}: «{pending.Phrase}» — это {name}. Запомню."
            : $"Хорошо, {playerName}, считаю, что «{pending.Phrase}» — это {name}.";
        await bots.SayAsync(state, pending.BotId, new SendChatRequest(ChatChannels.Public, reply, null, [], []), ct);
        return new AliasResult(verdict == CardNaming.Verdict.Valid ? "valid" : "unverified", pending.Phrase, request.CardId);
    }

    /// <summary>Ссылка на карту, которую клиент покажет картинкой: «мотив 2», «Подсказка р.1 №1».</summary>
    private static string CardRef(GameState state, string card)
    {
        foreach (var row in state.Board)
        {
            var column = row.Cards.IndexOf(card);
            if (column >= 0) return $"{SpeechText.CategoryWord(row.Category)} {column + 1}";
        }

        foreach (var hint in state.Hints)
        {
            var index = hint.Cards.IndexOf(card);
            if (index >= 0) return hint.Round == 0 ? "Зацепка" : $"Подсказка р.{hint.Round} №{index + 1}";
        }

        return "вот эта";
    }

    private static string RowName(Category row) => row switch
    {
        Category.Motive => "Мотив",
        Category.Place => "Место",
        Category.Method => "Способ",
        _ => "Тайна",
    };
}
