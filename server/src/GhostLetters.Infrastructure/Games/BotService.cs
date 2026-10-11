using System.Text.Json;
using GhostLetters.Application;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Ходы ботов: за такт — по одному ходу в каждой партии, где боту есть что делать.</summary>
public sealed class BotService(GhostLettersDbContext db, GameService games, ChatService chat, CardTags baselineTags, CardTagStore tagStore, ILogger<BotService> logger, TimeProvider time)
{
    private CardTags tags = baselineTags;
    /// <summary>Сколько бот-Убийца в партии без таймеров ждёт подсказку живого Сообщника после последнего хода.</summary>
    public static readonly TimeSpan SoloTeamWait = TimeSpan.FromMinutes(2);

    /// <summary>Когда партия последний раз сдвинулась (версия) — для ожидания без таймера.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (int Version, DateTimeOffset Since)> Changed = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (Phase Phase, int Round, DateTimeOffset Since)> PhaseStarted = new();

    /// <summary>Сделать ходы ботов. Возвращает число сделанных ходов.</summary>
    public async Task<int> TickAsync(CancellationToken ct)
    {
        var recentFinish = time.GetUtcNow() - TimeSpan.FromHours(1);
        var rows = await (
                from p in db.GamePlayers.AsNoTracking()
                join u in db.Users.AsNoTracking() on p.UserId equals u.Id
                join g in db.Games.AsNoTracking() on p.GameId equals g.Id
                where u.IsBot && (g.Status == GameStatuses.Active ||
                    (g.Status == GameStatuses.Finished && g.FinishedAt >= recentFinish && p.Role == "Ghost" &&
                     db.ChatMessages.Any(m => m.GameId == g.Id && m.AuthorId == p.UserId && m.Text != null && m.Text.StartsWith(GhostDebrief.Prefix)) &&
                     !db.ChatMessages.Any(m => m.GameId == g.Id && m.AuthorId == p.UserId && m.Text != null && m.Text.EndsWith(GhostDebrief.Completed))))
                select new { p.GameId, p.UserId })
            .ToListAsync(ct);

        var moves = 0;
        var active = rows.Select(r => r.GameId).ToHashSet();
        foreach (var gone in Changed.Keys.Where(k => !active.Contains(k)).ToList())
        {
            Changed.TryRemove(gone, out _);
            PhaseStarted.TryRemove(gone, out _);
        }

        foreach (var key in PastGames.Keys.Where(k => !active.Contains(k.Game)).ToList())
        {
            PastGames.TryRemove(key, out _);
        }

        if (rows.Count == 0) return 0;
        tags = await tagStore.LoadAsync(ct);

        foreach (var game in rows.GroupBy(r => r.GameId))
        {
            try
            {
                moves += await TickGameAsync(game.Key, game.Select(r => r.UserId).ToHashSet(), ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Одна сломанная партия не должна останавливать ботов в остальных: ошибка повторялась бы
                // на каждом такте (случайность детерминирована), а партии после неё в списке стояли бы.
                logger.LogError(e, "Ошибка хода бота в партии {GameId}", game.Key);
                db.ChangeTracker.Clear();
            }
        }

        return moves;
    }

    /// <summary>Не больше одного хода бота в одной партии. Возвращает 1, если бот сходил.</summary>
    private async Task<int> TickGameAsync(Guid gameId, HashSet<Guid> bots, CancellationToken ct)
    {
        var row = await db.Games.AsNoTracking().FirstAsync(g => g.Id == gameId, ct);
        var state = GameStore.Read(row);
        var rng = new Random(HashCode.Combine(state.Seed, state.Version));
        var now = time.GetUtcNow();
        var phaseSince = PhaseStarted.AddOrUpdate(gameId, _ => (state.Phase, state.Round, now),
            (_, old) => old.Phase == state.Phase && old.Round == state.Round ? old : (state.Phase, state.Round, now)).Since;
        var since = Changed.AddOrUpdate(gameId, _ => (state.Version, now), (_, old) => old.Version == state.Version ? old : (state.Version, now)).Since;
        // Один человек с ботами — таймеров нет, боты ждут человека.
        var solo = state.Players.Count - bots.Count <= 1;
        foreach (var botId in bots.OrderBy(_ => rng.Next()))
        {
            if (state.Phase == Phase.Night && state.Player(botId).Role.IsKillerTeam())
            {
                if (now - phaseSince < TimeSpan.FromSeconds(8)) continue;
                if (await NightTalkAsync(state, botId, ct)) { return 1; }
                var recent = await db.ChatMessages.AsNoTracking().Where(m => m.GameId == state.Id &&
                    m.Channel == ChatChannels.KillerTeam && m.CreatedAt >= phaseSince)
                    .OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync(ct);
                if (state.Player(botId).Role == Role.Killer && WaitForNight(botId, phaseSince, now, row.PhaseDeadline,
                    recent?.CreatedAt, recent?.Text, recent is null ? null : await ReadingTimeAsync(recent, ct))) continue;
            }
            if (WaitsForTeam(state, botId, bots, row.PhaseDeadline, now, solo ? since : null))
            {
                continue;
            }

            if (await TryMoveAsync(state, botId, rng, ct))
            {
                return 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// Бот-Убийца ночью и на охоте даёт живым Сообщникам время подсказать: ждёт, пока все не подскажут
    /// или до конца фазы не останется 30 секунд. Без таймера — только в партии одного человека с ботами
    /// (<paramref name="soloSince"/> — когда партия последний раз сдвинулась): ждёт до <see cref="SoloTeamWait"/>.
    /// </summary>
    public static bool WaitsForTeam(GameState state, Guid botId, IReadOnlySet<Guid> bots, DateTimeOffset? deadline, DateTimeOffset now,
        DateTimeOffset? soloSince = null)
    {
        if (!GameEngine.TeamSuggestPhase(state) || state.Player(botId).Role != Role.Killer)
        {
            return false;
        }

        var pending = state.Players.Any(p => p.Role == Role.Accomplice && !bots.Contains(p.Id) && !state.TeamSuggestions.ContainsKey(p.Id));
        return deadline is { } d
            ? pending && d - now > TimeSpan.FromSeconds(30)
            : pending && soloSince is { } since && now - since < SoloTeamWait;
    }

    public static bool WaitForNight(Guid bot, DateTimeOffset started, DateTimeOffset now, DateTimeOffset? deadline,
        DateTimeOffset? lastSpeech = null, string? text = null, TimeSpan? speechTime = null)
    {
        if (deadline is { } end && end - now <= TimeSpan.FromSeconds(5)) return false;
        if (now - started >= TimeSpan.FromMinutes(2)) return false;
        return now - started < TimeSpan.FromSeconds(60 + bot.ToByteArray()[0] % 21) ||
            (lastSpeech is { } at && now - at < (speechTime ?? BotDiscussion.SpeechTime(text ?? "")));
    }

    private async Task<bool> NightTalkAsync(GameState state, Guid bot, CancellationToken ct)
    {
        if (state.Players.Count(p => p.Role.IsKillerTeam()) < 2) return false;
        var messages = await db.ChatMessages.AsNoTracking().Where(m => m.GameId == state.Id &&
            m.Channel == ChatChannels.KillerTeam && m.Round == state.Round).OrderBy(m => m.CreatedAt).ToListAsync(ct);
        var own = messages.Where(m => m.AuthorId == bot).ToList();
        if (own.Count >= 2 || (messages.Count > 0 && time.GetUtcNow() - messages[^1].CreatedAt < await ReadingTimeAsync(messages[^1], ct))) return false;
        if (own.Count > 0 && !messages.Any(m => m.AuthorId != bot && m.CreatedAt > own[^1].CreatedAt)) return false;
        var view = GameProjection.For(state, bot);
        var columns = BotPlayer.Decide(view, new Random(state.Seed), tags, await MindAsync(state, bot, ct)) switch
        {
            ChooseTruth choice => choice.Columns,
            TeamSuggest suggestion => suggestion.Columns,
            _ => null,
        };
        var cards = columns is null ? new List<string>() : columns.Select((c, r) => view.Board[r].Cards[c]).Take(4).ToList();
        var text = own.Count == 0 ? "Давайте обсудим выбор. Предлагаю эти карты — что изменим? Я ещё не подтверждаю." :
            "Послушал предложения. Показываю текущий вариант; можно возразить до подтверждения.";
        try
        {
            await chat.SendAsync(state.Id, bot, new SendChatRequest(ChatChannels.KillerTeam, text, null, cards,
                cards.Select(_ => "думаю, эта").ToList()), ct);
            return true;
        }
        catch (AppException e) { logger.LogDebug("Ночная реплика бота пропущена: {Error}", e.Message); return false; }
    }

    private async Task<bool> TryMoveAsync(GameState state, Guid botId, Random rng, CancellationToken ct)
    {
        var view = GameProjection.For(state, botId);
        // Боту нечего делать (ночь чужой команды, ждёт других, не его слово) — не собираем «мнение»:
        // это пять запросов к базе и разбор всего чата партии на каждом такте.
        var ghostDebrief = state.Result is not null && view.Me?.Role == Role.Ghost;
        var mayTalk = state.Phase == Phase.Discussion && view.Me is { } me && me.Role != Role.Ghost;
        var mayTable = state.Phase is Phase.Discussion or Phase.Voting && view.Table is { CanPost: true };
        if (!ghostDebrief && !mayTalk && !mayTable && !BotPlayer.HasMove(view)) return false;
        var mind = await MindAsync(state, botId, ct);
        if (state.Result is not null && view.Me?.Role == Role.Ghost &&
            await DebriefAsync(state, botId, mind, ct) is { } debrief) return debrief;
        if (await TrySpeakAsync(state, view, botId, rng, mind, ct))
        {
            return true;
        }

        var (tabled, tablePending) = await TryTableAsync(state, view, botId, mind, ct);
        if (tabled) return true;

        if (state.Phase == Phase.Discussion && state.Round >= state.TotalRounds && view.Me?.Role != Role.Ghost &&
            BotDiscussion.ShouldWait(botId, await DiscussionAsync(state, ct), time.GetUtcNow())) return false;

        if (state.Phase == Phase.Discussion && BotLetterTactics.Pending(botId, await DiscussionAsync(state, ct))) return false;

        var command = BotPlayer.Decide(view, rng, tags, mind);
        if (command is null)
        {
            return false;
        }

        // Не заканчиваем раунд, пока не разложили свою версию на доске (шагов за раунд — ограниченно).
        if (command is ReadyNextRound && tablePending) return false;

        try
        {
            await games.ExecuteAsync(state.Id, botId, Request(command, state.Version), ct);
            return true;
        }
        catch (AppException e) when (command is HuntPick hunt && e.Code == AppException.Codes.Validation)
        {
            // Угадали роль, которой нет в партии, — пробуем другую.
            var other = hunt.Guess == Role.Expert ? Role.Witness : Role.Expert;
            await games.ExecuteAsync(state.Id, botId, Request(hunt with { Guess = other }, state.Version), ct);
            return true;
        }
        catch (AppException e)
        {
            logger.LogDebug("Бот {Bot} не сходил {Command}: {Error}", botId, command.GetType().Name, e.Message);
            return false;
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Guid Game, Guid Bot, int Round), int> TableFailures = new();

    /// <summary>
    /// Шаг бота у доски улик: изменение доски (если есть) и мысль вслух в канал table.
    /// Возвращает, сделан ли шаг и остались ли ещё шаги в этом раунде.
    /// </summary>
    private async Task<(bool Acted, bool Pending)> TryTableAsync(GameState state, PlayerView view, Guid botId, BotMind? mind, CancellationToken ct)
    {
        if (state.Phase is not (Phase.Discussion or Phase.Voting) || view.Table is not { CanPost: true } ||
            view.Me is null || view.Me.Role == Role.Ghost) return (false, false);
        var key = (state.Id, botId, state.Round);
        if (TableFailures.GetValueOrDefault(key) >= 3) return (false, false);
        var recent = await db.ChatMessages.AsNoTracking()
            .Where(m => m.GameId == state.Id && m.AuthorId == botId && m.Channel == ChatChannels.Table && m.Round == state.Round)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .Select(m => new BotThought(m.CreatedAt, m.CardIds, m.CardNotes)).ToListAsync(ct);
        // Пауза между шагами — без расчёта версии: такт идёт раз в 1,5 с, шаг — раз в 6 с.
        if (!BotTable.Due(recent, time.GetUtcNow()))
            return (false, state.Phase == Phase.Discussion && recent.Count < BotTable.MaxStepsPerRound);
        var plan = BotPlayer.Plan(view, tags, mind, BotTable.Random(state, botId));
        var step = BotTable.Next(view, tags, mind, plan, recent);
        if (step is null) return (false, false);
        try
        {
            if (step.Ops.Count > 0)
                await games.ExecuteAsync(state.Id, botId, Request(new TablePost(step.Ops), state.Version), ct);
            await chat.ThinkAsync(state.Id, botId, step.Thought, step.Cards, step.Notes, ct);
            return (true, true);
        }
        catch (AppException e)
        {
            if (e.Code != AppException.Codes.VersionConflict) TableFailures.AddOrUpdate(key, 1, (_, n) => n + 1);
            logger.LogDebug("Бот {Bot} не смог изменить доску: {Error}", botId, e.Message);
            return (false, false);
        }
    }

    private async Task<bool?> DebriefAsync(GameState state, Guid botId, BotMind? mind, CancellationToken ct)
    {
        var reports = GhostDebrief.Compose(state, tags, mind ?? BotMind.Neutral);
        var sent = await db.ChatMessages.AsNoTracking().Where(m => m.GameId == state.Id && m.AuthorId == botId &&
            m.Channel == ChatChannels.Public && m.Text != null && m.Text.StartsWith(GhostDebrief.Prefix))
            .OrderBy(m => m.CreatedAt).ToListAsync(ct);
        if (sent.Count >= reports.Count) return null;
        // Wait without counting a move, so timers and other players can advance.
        if (sent.Count > 0 && time.GetUtcNow() - sent[^1].CreatedAt < await ReadingTimeAsync(sent[^1], ct)) return false;
        var report = reports[sent.Count];
        await chat.SendAsync(state.Id, botId, new SendChatRequest(ChatChannels.Public, report.Text, null, report.Cards, report.Notes), ct);
        return true;
    }

    /// <summary>Раз за раунд обсуждения бот говорит, что думает (в рации — когда у него слово).</summary>
    private async Task<bool> TrySpeakAsync(GameState state, PlayerView view, Guid botId, Random rng, BotMind? mind, CancellationToken ct)
    {
        if (state.Phase != Phase.Discussion ||
            (view.Discussion == DiscussionMode.Radio && view.CurrentSpeaker != botId && view.FloorGrantedTo != botId))
        {
            return false;
        }

        if (view.Me?.Role == Role.Ghost) return false;
        var lastPublic = await db.ChatMessages.AsNoTracking().Where(m => m.GameId == state.Id &&
            m.Round == state.Round && m.Channel == ChatChannels.Public).OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync(ct);
        if (lastPublic is not null && time.GetUtcNow() - lastPublic.CreatedAt < await ReadingTimeAsync(lastPublic, ct)) return false;
        var letterDiscussion = await DiscussionAsync(state, ct);
        if (mind is not null && letterDiscussion.Count(m => m.Author == botId) < BotDiscussion.MaxMessages &&
            BotLetterTactics.Compose(view, mind, letterDiscussion, rng, time.GetUtcNow()) is { } tactic)
        {
            try
            {
                await chat.SendAsync(state.Id, botId, new SendChatRequest(ChatChannels.Public, tactic.Text, null, tactic.Cards, tactic.Notes), ct);
                return true;
            }
            catch (AppException e) { logger.LogDebug("Заявление о письме пропущено: {Error}", e.Message); return false; }
        }
        if (BotLetterTactics.Pending(botId, letterDiscussion)) return false;
        if (state.Round >= state.TotalRounds)
        {
            var messages = await DiscussionAsync(state, ct);
            var own = messages.Where(m => m.Author == botId).ToList();
            var now = time.GetUtcNow();
            if (messages.Count > 0 && now - messages[^1].At < BotDiscussion.SpeechTime(messages[^1].Text, messages[^1].DurationMs)) return false;
            if (own.Count > 0 && now - own[^1].At < TimeSpan.FromSeconds(12)) return false;
            if (mind is null)
            {
                var ids = state.Players.Select(p => p.Id).ToList();
                var names = await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nickname, ct);
                mind = BotMind.Neutral with { Names = names };
            }
            var proposal = BotDiscussion.Compose(view, tags, mind, messages, rng);
            if (proposal is not { } finalLine) return false;
            try
            {
                await chat.SendAsync(state.Id, botId, TableReasoning.Speech(view, tags, mind, finalLine), ct);
                return true;
            }
            catch (AppException e)
            {
                logger.LogDebug("Бот {Bot} не смог обсудить версию: {Error}", botId, e.Message);
                return false;
            }
        }

        var said = await db.ChatMessages.Where(m => m.GameId == state.Id && m.AuthorId == botId &&
                m.Round == state.Round && m.Channel == ChatChannels.Public)
            .OrderByDescending(m => m.CreatedAt).ToListAsync(ct);
        if (said.Count >= 2) return false;
        // Одно объяснение и один содержательный ответ после чужой реплики, без бесконечного эха.
        if (said.Count > 0 && !await db.ChatMessages.AnyAsync(m => m.GameId == state.Id && m.Round == state.Round &&
            m.Channel == ChatChannels.Public && m.AuthorId != null && m.AuthorId != botId && m.CreatedAt > said[0].CreatedAt, ct))
            return false;
        var speech = said.Count == 0 ? BotPlayer.Say(view, rng, tags, mind) : BotPlayer.Reply(view, rng, tags, mind);
        if (speech is not { } line)
        {
            return false;
        }

        try
        {
            await chat.SendAsync(state.Id, botId, TableReasoning.Speech(view, tags, mind, line), ct);
            return true;
        }
        catch (AppException e)
        {
            logger.LogDebug("Бот {Bot} не смог написать: {Error}", botId, e.Message);
            return false;
        }
    }

    private async Task<List<DiscussionLine>> DiscussionAsync(GameState state, CancellationToken ct) =>
        await db.ChatMessages.AsNoTracking().Where(m => m.GameId == state.Id && m.Round == state.Round &&
                m.Channel == ChatChannels.Public && m.AuthorId != null)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .Select(m => new DiscussionLine(m.AuthorId!.Value, m.Text ?? "", m.CardIds, m.CreatedAt,
                db.MediaFiles.Where(f => f.Id == m.MediaId).Select(f => (int?)f.DurationMs).FirstOrDefault(), m.CardNotes)).ToListAsync(ct);

    private async Task<TimeSpan> ReadingTimeAsync(ChatMessage message, CancellationToken ct) =>
        BotDiscussion.SpeechTime(message.Text ?? "", message.MediaId is { } id
            ? await db.MediaFiles.Where(m => m.Id == id).Select(m => (int?)m.DurationMs).FirstOrDefaultAsync(ct) : null);

    /// <summary>Сколько помним прошлые партии бота с этими игроками, прежде чем перечитать их из базы.</summary>
    public static readonly TimeSpan MemoryCacheLifetime = TimeSpan.FromMinutes(10);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Guid Game, Guid Bot), (DateTimeOffset At, List<RememberedPlayer> Past)> PastGames = new();

    /// <summary>
    /// Прошлые законченные партии бота с игроками этой партии. Запрос идёт по всей истории бота, а мнение
    /// бота считается на каждом такте, поэтому результат держим в памяти на партию (обновляется раз в 10 минут).
    /// </summary>
    private async Task<List<RememberedPlayer>> PastGamesAsync(Guid gameId, Guid botId, List<Guid> others, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (PastGames.TryGetValue((gameId, botId), out var cached) && now - cached.At < MemoryCacheLifetime)
        {
            return cached.Past;
        }

        var past = await (from mine in db.GamePlayers.AsNoTracking()
                          join g in db.Games.AsNoTracking() on mine.GameId equals g.Id
                          join other in db.GamePlayers.AsNoTracking() on mine.GameId equals other.GameId
                          where mine.UserId == botId && g.Id != gameId && g.Status == GameStatuses.Finished
                                && others.Contains(other.UserId)
                          select new RememberedPlayer(g.Id, other.UserId, other.Role, g.FinishedAt ?? g.StartedAt))
            .ToListAsync(ct);
        PastGames[(gameId, botId)] = (now, past);
        return past;
    }

    /// <summary>
    /// Характер бота на эту партию, его память о соигроках (только партии с ним) и мнения стола из чата.
    /// Бот без характера в кабинете — null: играет «классически».
    /// </summary>
    public async Task<BotMind?> MindAsync(GameState state, Guid botId, CancellationToken ct)
    {
        var profile = await db.BotProfiles.AsNoTracking().FirstOrDefaultAsync(b => b.UserId == botId, ct);
        if (profile is null)
        {
            if (!await db.Users.AnyAsync(u => u.Id == botId && u.IsBot, ct)) return null;
            profile = new BotProfile { UserId = botId };
        }

        var personality = BotAdminService.Personality(profile).ForGame(state.Id, botId);
        var others = state.Players.Select(p => p.Id).Where(id => id != botId).ToList();

        var past = personality.Memory <= 0 ? [] : await PastGamesAsync(state.Id, botId, others, ct);
        var history = BotMemory.Recall(past, personality.Memory);

        var canReadTeam = state.Player(botId).Role.IsKillerTeam();
        var messages = await db.ChatMessages.AsNoTracking()
            .Where(m => m.GameId == state.Id && (m.Channel == ChatChannels.Public || (canReadTeam && m.Channel == ChatChannels.KillerTeam)) && m.AuthorId != null && m.AuthorId != botId)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .Select(m => new { m.AuthorId, m.CardIds, m.CardNotes, m.Text })
            .ToListAsync(ct);
        var board = state.Board.SelectMany(r => r.Cards).ToHashSet();
        var opinions = messages.SelectMany(m => TableReasoning.Read(m.AuthorId!.Value, m.CardIds, m.CardNotes, board)).ToList();

        var everyone = state.Players.Select(p => p.Id).ToList();
        var names = await db.Users.AsNoTracking().Where(u => everyone.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nickname, ct);
        // Обвинения из чата (боты и люди): «Убийца — Олег», «Подозреваю, что Маша из чёрных».
        var accusations = messages.SelectMany(m => AccusationReader.Read(m.AuthorId!.Value, m.Text, names)).ToList();
        // Повторение одной версии не делает её весомее. Последнее мнение заменяет прежнее.
        var latestOpinions = opinions.GroupBy(o => (o.Author, o.CardId, o.IsCheck)).Select(g => g.Last()).ToList();
        var latestAccusations = accusations.GroupBy(a => (a.Author, a.Target)).Select(g => g.Last()).ToList();
        var affinities = await db.BotRelationships.AsNoTracking().Where(r => r.BotId == botId && others.Contains(r.PlayerId))
            .ToDictionaryAsync(r => r.PlayerId, r => r.Score, ct);
        var claims = PublicRoleClaims.Read(messages.Select(m => (m.AuthorId!.Value, m.Text ?? "")));
        return new BotMind(personality, history, latestOpinions, names, latestAccusations, Breadth(messages.Select(m => (m.AuthorId!.Value, (IReadOnlyList<string>)m.CardNotes))), affinities, claims);
    }

    /// <summary>
    /// Насколько широко каждый читает письма: в сообщениях, где автор показал своё письмо («кидал эту»),
    /// сколько карт поля он отметил «проверял эту» — в среднем по его сообщениям.
    /// </summary>
    public static IReadOnlyDictionary<Guid, double> Breadth(IEnumerable<(Guid Author, IReadOnlyList<string> Notes)> messages) =>
        messages
            .Where(m => m.Notes.Any(n => n.StartsWith("кидал", StringComparison.Ordinal)))
            .GroupBy(m => m.Author)
            .ToDictionary(g => g.Key, g => g.Average(m => (double)m.Notes.Count(n => n.StartsWith("проверял", StringComparison.Ordinal))));

    private static CommandRequest Request(GameCommand command, int version) => new(
        command.GetType().Name,
        JsonSerializer.SerializeToElement(command, command.GetType(), GameJson.Options),
        version,
        null);
}

/// <summary>Фоновый такт ботов (Bots:Enabled, по умолчанию включено; Bots:TickMs — пауза между ходами).</summary>
public sealed class BotHostedService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<BotHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Bots:Enabled", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(configuration.GetValue("Bots:TickMs", 1500)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<BotService>().TickAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Ошибка такта ботов");
            }
        }
    }
}
