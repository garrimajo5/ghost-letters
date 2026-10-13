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
public sealed class BotService(GhostLettersDbContext db, GameService games, ChatService chat, CardTags baselineTags, CardTagStore tagStore, ILogger<BotService> logger, TimeProvider time, ReactionService reactions,
    Speech.SpeechKit speech, Speech.SpeechWriter writer, Speech.PhraseLearner phrases)
{
    private CardTags tags = baselineTags;
    /// <summary>Сколько бот-Убийца в партии без таймеров ждёт подсказку живого Сообщника после последнего хода.</summary>
    public static readonly TimeSpan SoloTeamWait = TimeSpan.FromMinutes(2);

    /// <summary>Когда партия последний раз сдвинулась (версия) — для ожидания без таймера.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (int Version, DateTimeOffset Since)> Changed = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (Phase Phase, int Round, DateTimeOffset Since)> PhaseStarted = new();

    /// <summary>
    /// Публичная реплика бота: если в партии боты говорят голосом и синтез настроен — с озвучкой его голосом.
    /// Озвучка не удалась — реплика уходит текстом, как раньше.
    /// </summary>
    public async Task SayAsync(GameState state, Guid botId, SendChatRequest request, CancellationToken ct)
    {
        var manner = Speech.SpokenStyle.MannerOf(botId);
        var profile = await (from b in db.BotProfiles.AsNoTracking()
                             join u in db.Users.AsNoTracking() on b.UserId equals u.Id
                             where b.UserId == botId
                             select new { b.Voice, b.VoiceRole, b.About, u.Nickname }).FirstOrDefaultAsync(ct);
        var (voice, role) = Speech.BotVoices.For(botId, profile?.Voice, profile?.VoiceRole);
        var male = Speech.BotVoices.Find(voice)?.Male ?? true;
        IReadOnlyList<(string Intent, string Emotion, string Template)>? learned = null;
        double? risk = null;
        if (request.Channel == ChatChannels.Public && request.MediaId is null)
        {
            risk = (await MindAsync(state, botId, ct))?.Personality.Risk ?? .5;
            // Фразы, выученные у людей с той же манерой, — тем же ботам, по характеру подходящих эмоций.
            learned = await phrases.ForAsync(manner, ct);
        }

        // В чат — как написал бы человек: короче, своими словами, в манере бота и его роде; карты остаются ссылками.
        if (request.Channel == ChatChannels.Public && !string.IsNullOrWhiteSpace(request.Text))
        {
            var earlier = await db.ChatMessages.AsNoTracking()
                .Where(m => m.GameId == state.Id && m.AuthorId == botId && m.Channel == ChatChannels.Public)
                .OrderByDescending(m => m.CreatedAt).Take(6).Select(m => m.Text ?? "").ToListAsync(ct);
            request = request with
            {
                Text = Speech.ChatStyle.Humanize(request.Text, Random.Shared, manner, male, risk ?? .5, learned,
                    Speech.PhraseBook.EmotionsFor(manner, risk ?? .5), earlier),
            };
        }

        // Голос читает то же, что написано в чате (раньше — свой пересказ шаблона, и текст с голосом расходились).
        if (speech.Enabled && state.Settings.BotVoice && request.Channel == ChatChannels.Public && request.MediaId is null)
        {
            var names = new List<string>();
            var text = Speech.SpeechText.For(request.Text, request.CardIds ?? [], request.CardNotes, state.Board, state.Hints, tags, names);
            if (text.Length > 0)
            {
                var name = (profile?.Nickname ?? "").Replace("Бот ", "").Trim();
                // Вслух — то же, что в чате (уже живым текстом), с названиями карт; YandexGPT (если подключён) пересказывает.
                text = await writer.RephraseAsync(text, names.Distinct().ToList(), new Speech.Speaker(name, profile?.About ?? "", male), ct)
                       ?? Speech.SpokenStyle.Rephrase(text, Random.Shared, manner, male, learned, Speech.PhraseBook.EmotionsFor(manner, risk ?? .5));
                if (await speech.SynthesizeAsync(text, voice, role, ct) is { } audio)
                {
                    try { request = request with { MediaId = await chat.SaveBotVoiceAsync(botId, audio, ct) }; }
                    catch (AppException e) { logger.LogWarning("Озвучка бота не сохранена: {Error}", e.Message); }
                }
            }
        }

        await chat.SendAsync(state.Id, botId, request, ct);
    }

    /// <summary>Сделать ходы ботов. Возвращает число сделанных ходов.</summary>
    public async Task<int> TickAsync(CancellationToken ct)
    {
        var recentFinish = time.GetUtcNow() - TimeSpan.FromHours(1);
        var rows = await (
                from p in db.GamePlayers.AsNoTracking()
                join u in db.Users.AsNoTracking() on p.UserId equals u.Id
                join g in db.Games.AsNoTracking() on p.GameId equals g.Id
                where u.IsBot && (g.Status == GameStatuses.Active ||
                    // Разбор Призрака — уже после конца партии (после наград): играющие могут спокойно выйти.
                    (g.Status == GameStatuses.Finished && g.FinishedAt >= recentFinish && p.Role == "Ghost" &&
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
        // Люди уже нажали «Готов» в финальном обсуждении — боты договаривают коротко, а не по шесть реплик.
        var humansReady = state.Phase == Phase.Discussion && state.Investigators.Any(p => !bots.Contains(p.Id)) &&
            state.Investigators.Where(p => !bots.Contains(p.Id)).All(p => state.Done.Contains(p.Id));
        foreach (var botId in bots.OrderBy(_ => rng.Next()))
        {
            if (state.Phase == Phase.Night && state.Player(botId).Role.IsKillerTeam())
            {
                if (now - phaseSince < TimeSpan.FromSeconds(8)) continue;
                if (await NightTalkAsync(state, botId, ct)) { return 1; }
                var recent = await db.ChatMessages.AsNoTracking().Where(m => m.GameId == state.Id &&
                    m.Channel == ChatChannels.KillerTeam && m.CreatedAt >= phaseSince)
                    .OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync(ct);
                // Один человек с ботами и без Сообщников: долгая ночь ничего не скрывает (Убийца — точно бот),
                // обсуждать не с кем — Убийца лишь недолго «думает», человек не ждёт минуту.
                var alone = solo && !state.Players.Any(p => p.Id != botId && p.Role == Role.Accomplice);
                if (state.Player(botId).Role == Role.Killer && WaitForNight(botId, phaseSince, now, row.PhaseDeadline,
                    recent?.CreatedAt, recent?.Text, recent is null ? null : await ReadingTimeAsync(recent, ct), alone)) continue;
            }
            // На охоте Сообщник-бот не молча подсказывает, а пишет команде, кого и почему подозревает.
            if (state.Phase is Phase.Hunt or Phase.BlackmailerHunt && state.Player(botId).Role == Role.Accomplice &&
                await HuntTalkAsync(state, botId, ct)) return 1;
            if (WaitsForTeam(state, botId, bots, row.PhaseDeadline, now, solo ? since : null))
            {
                continue;
            }

            // Письмо бот кидает не сразу, а через случайное время в пределах таймера — человек тоже может успеть первым.
            if (state.Phase == Phase.Mailbox && !state.Done.Contains(botId) && now - phaseSince < LetterDelay(state, botId, phaseSince, row.PhaseDeadline))
            {
                continue;
            }

            if (await TryMoveAsync(state, botId, rng, ct, humansReady))
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

    /// <summary>
    /// Через сколько после начала «Писем» бот отправляет своё: 3 с плюс случайная (но постоянная для бота и раунда) доля
    /// окна. Окно — до 45 с и не позже чем за 15 с до конца таймера; без таймера — до 20 с.
    /// </summary>
    public static TimeSpan LetterDelay(GameState state, Guid botId, DateTimeOffset phaseSince, DateTimeOffset? deadline)
    {
        var window = deadline is { } end ? Math.Clamp((end - phaseSince).TotalSeconds - 15 - 3, 0, 45) : 20;
        var share = (uint)HashCode.Combine(botId, state.Id, state.Round) % 1000 / 1000.0;
        return TimeSpan.FromSeconds(3 + window * share);
    }

    public static bool WaitForNight(Guid bot, DateTimeOffset started, DateTimeOffset now, DateTimeOffset? deadline,
        DateTimeOffset? lastSpeech = null, string? text = null, TimeSpan? speechTime = null, bool alone = false)
    {
        if (deadline is { } end && end - now <= TimeSpan.FromSeconds(5)) return false;
        if (alone) return now - started < TimeSpan.FromSeconds(4 + bot.ToByteArray()[0] % 7);
        if (now - started >= TimeSpan.FromMinutes(2)) return false;
        return now - started < TimeSpan.FromSeconds(60 + bot.ToByteArray()[0] % 21) ||
            (lastSpeech is { } at && now - at < (speechTime ?? BotDiscussion.SpeechTime(text ?? "")));
    }

    private async Task<bool> HuntTalkAsync(GameState state, Guid bot, CancellationToken ct)
    {
        if (!state.TeamSuggestions.TryGetValue(bot, out var suggestion) || suggestion.Target is not { } target) return false;
        var phaseStart = PhaseStarted.TryGetValue(state.Id, out var started) && started.Phase == state.Phase ? started.Since : DateTimeOffset.MinValue;
        // Одна реплика за охоту (фаза началась не раньше, чем бот её заметил).
        if (phaseStart == DateTimeOffset.MinValue || await db.ChatMessages.AnyAsync(m => m.GameId == state.Id && m.AuthorId == bot &&
                m.Channel == ChatChannels.KillerTeam && m.CreatedAt >= phaseStart, ct)) return false;
        var name = await db.Users.AsNoTracking().Where(u => u.Id == target).Select(u => u.Nickname).FirstOrDefaultAsync(ct) ?? "";
        if (name.StartsWith("Бот ", StringComparison.Ordinal)) name = name[4..];
        var role = suggestion.Guess switch
        {
            Role.Witness => "Свидетель", Role.Expert => "Эксперт", Role.Blackmailer => "Шантажист", _ => null,
        };
        string[] lines = role is null
            ? [$"По-моему, это {name}. Что скажешь?", $"Я бы указал на {name} — слишком уж уверенно играл. Согласен?"]
            : [$"Думаю, {role} — {name}: слишком точно шёл к истине. Берём?", $"Мой вариант: {name}, {role}. Что скажешь?",
               $"{name} очень похож на {suggestion.Guess switch { Role.Witness => "свидетеля", Role.Expert => "эксперта", _ => "шантажиста" }} — указываем?"];
        var pick = new Random(HashCode.Combine(state.Id, bot, state.Phase));
        try
        {
            await chat.SendAsync(state.Id, bot, new SendChatRequest(ChatChannels.KillerTeam, lines[pick.Next(lines.Length)], null, [], []), ct);
            return true;
        }
        catch (AppException e) { logger.LogDebug("Реплика на охоте пропущена: {Error}", e.Message); return false; }
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
        // Ночью Убийца говорит с командой коротко и по-разному, как человек.
        string[] first = ["Думаю взять вот эти. Что скажете? Пока не подтверждаю.", "Мой вариант — эти карты. Поменяем что-нибудь?",
            "Предлагаю такие. Если есть идея лучше — пишите, я ещё не нажал.", "Давайте обсудим: я бы загадал эти. Что изменим?"];
        string[] next = ["Учёл ваши подсказки — вот что выходит. Можно ещё возразить.", "Ок, послушал. Сейчас так — есть возражения?",
            "Подправил. Если всё устраивает — подтверждаю."];
        // Сообщник не загадывает сам — он подсказывает Убийце.
        if (state.Player(bot).Role != Role.Killer)
        {
            first = ["Я бы взял эти. Решать тебе.", "Предлагаю вот такие — их будет трудно угадать.", "Мой совет — эти карты. Что думаешь?"];
            next = ["Посмотрел твой вариант — так тоже можно.", "Ок, если тебе так удобнее — поддержу.", "Учёл — оставил бы так."];
        }

        var pick = new Random(HashCode.Combine(state.Id, bot, state.Round, own.Count));
        var text = own.Count == 0 ? first[pick.Next(first.Length)] : next[pick.Next(next.Length)];
        try
        {
            await chat.SendAsync(state.Id, bot, new SendChatRequest(ChatChannels.KillerTeam, text, null, cards,
                cards.Select(_ => "думаю, эта").ToList()), ct);
            return true;
        }
        catch (AppException e) { logger.LogDebug("Ночная реплика бота пропущена: {Error}", e.Message); return false; }
    }

    /// <summary>Кто из ботов уже отреагировал эмодзи на событие (партия, бот, раунд, фаза).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Guid, Guid, int, Phase), bool> Reacted = new();

    /// <summary>
    /// Боты реагируют, как люди: через несколько секунд после открытия писем — 🤔/😮/😱 (Призрак — 👻),
    /// в итогах — радость или досада. Не всегда и не все; это не ход и не сообщение в чат.
    /// </summary>
    /// <summary>На какие реплики людей бот уже отреагировал (партия, бот, сообщение).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Guid, Guid, Guid), bool> ReactedTo = new();

    /// <summary>
    /// Реакция на реплику человека, как за живым столом: обвинили бота — 😮/🙈/🤡, согласились с ним — 👍,
    /// пошутили — 😂. Через пару секунд после реплики, не на каждую и не все боты сразу.
    /// </summary>
    private async Task TryReactToHumanAsync(GameState state, PlayerView view, Guid botId, Random rng, CancellationToken ct)
    {
        if (state.Phase != Phase.Discussion || view.Me?.Role == Role.Ghost) return;
        var now = time.GetUtcNow();
        var last = await db.ChatMessages.AsNoTracking().Where(m => m.GameId == state.Id && m.Round == state.Round &&
                m.Channel == ChatChannels.Public && m.AuthorId != null && m.AuthorId != botId && m.CreatedAt > now.AddSeconds(-25))
            .OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync(ct);
        if (last?.Text is not { Length: > 0 } text || now - last.CreatedAt < TimeSpan.FromSeconds(2 + (botId.GetHashCode() & 3))) return;
        if (ReactedTo.Count > 20_000) ReactedTo.Clear();
        if (!ReactedTo.TryAdd((state.Id, botId, last.Id), true)) return;
        if (await db.Users.AsNoTracking().AnyAsync(u => u.Id == last.AuthorId && u.IsBot, ct)) return;
        var ids = state.Players.Select(p => p.Id).ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nickname, ct);
        var mine = names.GetValueOrDefault(botId, "");
        var bare = mine.StartsWith("Бот ", StringComparison.Ordinal) ? mine[4..] : mine;
        string? emoji = null;
        if (AccusationReader.Read(last.AuthorId!.Value, text, names).Any(a => a.Target == botId))
            emoji = rng.NextDouble() < .7 ? new[] { "😮", "🙈", "🤡" }[rng.Next(3)] : null;
        else if (bare.Length > 1 && System.Text.RegularExpressions.Regex.IsMatch(text,
                     $@"(?<![\p{{L}}]){System.Text.RegularExpressions.Regex.Escape(bare)}[\p{{L}}]{{0,2}},?\s+(?:согласен|согласна|поддерживаю|поддержу|прав|права|верно)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            emoji = rng.NextDouble() < .5 ? "👍" : null;
        else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(?<![\p{L}])(?:ха-?ха|хах|лол|ахах)|😂|🤣", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            emoji = rng.NextDouble() < .35 ? "😂" : null;
        if (emoji is null) return;
        try { await reactions.SendAsync(state.Id, botId, emoji, ct); }
        catch (AppException e) { logger.LogDebug("Реакция бота пропущена: {Error}", e.Message); }
    }

    /// <summary>
    /// После итогов люди перекидываются парой слов: «Хорошая партия!», «Эх, почти!». Бот — не всегда, один раз,
    /// через несколько секунд после итогов и только если за столом есть люди.
    /// </summary>
    private async Task TryFarewellAsync(GameState state, PlayerView view, Guid botId, CancellationToken ct)
    {
        if (state.Phase != Phase.AwardNomination || state.Result is not { } result || view.Me?.Role == Role.Ghost) return;
        var pick = new Random(HashCode.Combine(state.Id, botId, "farewell"));
        if (pick.NextDouble() > .45) return;
        if (!PhaseStarted.TryGetValue(state.Id, out var started) || started.Phase != state.Phase ||
            time.GetUtcNow() - started.Since < TimeSpan.FromSeconds(4 + pick.Next(10))) return;
        if (Reacted.ContainsKey((state.Id, botId, -2, state.Phase))) return;
        Reacted[(state.Id, botId, -2, state.Phase)] = true;
        var ids = state.Players.Select(p => p.Id).ToList();
        if (!await db.Users.AsNoTracking().AnyAsync(u => ids.Contains(u.Id) && !u.IsBot, ct)) return;
        var won = result.Winners.Contains(botId);
        string[] lines = won
            ? view.Me?.Role.IsKillerTeam() == true
                ? ["Простите, это были мы!", "Хорошо, что не раскусили.", "Было непросто держаться. Хорошая партия!"]
                : ["Хорошая партия!", "Ура, получилось!", "Спасибо за игру — было интересно."]
            : ["Эх, почти! Хорошо сыграли.", "Поздравляю победителей!", "Обидно, но было интересно.", "Ну вы даёте. Спасибо за игру!"];
        try { await SayAsync(state, botId, new SendChatRequest(ChatChannels.Public, lines[pick.Next(lines.Length)], null, [], []), ct); }
        catch (AppException e) { logger.LogDebug("Прощание бота пропущено: {Error}", e.Message); }
    }

    private async Task TryReactAsync(GameState state, PlayerView view, Guid botId, Random rng, CancellationToken ct)
    {
        await TryReactToHumanAsync(state, view, botId, rng, ct);
        await TryFarewellAsync(state, view, botId, ct);
        var round = state.Phase == Phase.Discussion ? state.Round : -1;
        if (state.Phase is not (Phase.Discussion or Phase.AwardNomination) || Reacted.ContainsKey((state.Id, botId, round, state.Phase))) return;
        // Человек реагирует не мгновенно: у каждого бота своя задержка 3–10 с.
        if (PhaseStarted.TryGetValue(state.Id, out var started) && started.Phase == state.Phase &&
            time.GetUtcNow() - started.Since < TimeSpan.FromSeconds(3 + (botId.GetHashCode() & 7))) return;
        if (Reacted.Count > 20_000) Reacted.Clear();
        Reacted[(state.Id, botId, round, state.Phase)] = true;
        string? emoji = null;
        if (state.Phase == Phase.Discussion && state.Hints.LastOrDefault(h => h.Round == state.Round) is { } hints && rng.NextDouble() < .35)
            emoji = view.Me?.Role == Role.Ghost ? null : hints.Cards.Count == 0 ? "😱" : hints.Cards.Count >= 2 ? "😮" : "🤔";
        else if (state.Phase == Phase.AwardNomination && state.Result is { } result && rng.NextDouble() < .6)
            emoji = result.Winners.Contains(botId) ? (rng.Next(2) == 0 ? "🔥" : "❤️") : (rng.Next(2) == 0 ? "🙈" : "😱");
        if (emoji is null) return;
        try
        {
            await reactions.SendAsync(state.Id, botId, emoji, ct);
        }
        catch (AppException e)
        {
            logger.LogDebug("Реакция бота пропущена: {Error}", e.Message);
        }
    }

    private async Task<bool> TryMoveAsync(GameState state, Guid botId, Random rng, CancellationToken ct, bool humansReady = false)
    {
        var view = GameProjection.For(state, botId);
        await TryReactAsync(state, view, botId, rng, ct);
        // Боту нечего делать (ночь чужой команды, ждёт других, не его слово) — не собираем «мнение»:
        // это пять запросов к базе и разбор всего чата партии на каждом такте.
        var ghostDebrief = state.Phase == Phase.Finished && state.Result is not null && view.Me?.Role == Role.Ghost;
        var mayTalk = state.Phase == Phase.Discussion && view.Me is { } me && me.Role != Role.Ghost;
        var mayTable = state.Phase is Phase.Discussion or Phase.Voting && view.Table is { CanPost: true };
        if (!ghostDebrief && !mayTalk && !mayTable && !BotPlayer.HasMove(view)) return false;
        var mind = await MindAsync(state, botId, ct);
        if (ghostDebrief && await DebriefAsync(state, botId, mind, ct) is { } debrief) return debrief;
        if (await TrySpeakAsync(state, view, botId, rng, mind, ct, humansReady))
        {
            return true;
        }

        var (tabled, tablePending) = await TryTableAsync(state, view, botId, mind, ct);
        if (tabled) return true;

        if (state.Phase == Phase.Discussion && state.IsFinalTalk && view.Me?.Role != Role.Ghost &&
            BotDiscussion.ShouldWait(botId, await DiscussionAsync(state, ct), time.GetUtcNow(), humansReady)) return false;

        if (state.Phase == Phase.Discussion && BotLetterTactics.Pending(botId, await DiscussionAsync(state, ct))) return false;
        if (await SpeakerStillReadingAsync(state, view, botId, ct)) return false;

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
        await SayAsync(state, botId, new SendChatRequest(ChatChannels.Public, report.Text, null, report.Cards, report.Notes), ct);
        return true;
    }

    /// <summary>
    /// В рации бот держит слово, пока стол читает последнюю реплику: чужую — потом говорит сам, а не отдаёт
    /// слово молча (иначе после длинной реплики все боты пропускали ход); свою — чтобы её дочитали до смены фазы.
    /// </summary>
    private async Task<bool> SpeakerStillReadingAsync(GameState state, PlayerView view, Guid botId, CancellationToken ct)
    {
        if (state.Phase != Phase.Discussion || view.Discussion != DiscussionMode.Radio || view.CurrentSpeaker != botId ||
            view.Me?.Role == Role.Ghost) return false;
        var lastPublic = await db.ChatMessages.AsNoTracking().Where(m => m.GameId == state.Id &&
            m.Round == state.Round && m.Channel == ChatChannels.Public).OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync(ct);
        return lastPublic is not null && time.GetUtcNow() - lastPublic.CreatedAt < await ReadingTimeAsync(lastPublic, ct);
    }

    /// <summary>Раз за раунд обсуждения бот говорит, что думает (в рации — когда у него слово).</summary>
    private async Task<bool> TrySpeakAsync(GameState state, PlayerView view, Guid botId, Random rng, BotMind? mind, CancellationToken ct,
        bool humansReady = false)
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
                await SayAsync(state, botId, new SendChatRequest(ChatChannels.Public, tactic.Text, null, tactic.Cards, tactic.Notes), ct);
                return true;
            }
            catch (AppException e) { logger.LogDebug("Заявление о письме пропущено: {Error}", e.Message); return false; }
        }
        if (BotLetterTactics.Pending(botId, letterDiscussion)) return false;
        if (state.IsFinalTalk)
        {
            var messages = await DiscussionAsync(state, ct);
            var own = messages.Where(m => m.Author == botId).ToList();
            var now = time.GetUtcNow();
            if (messages.Count > 0 && now - messages[^1].At < BotDiscussion.SpeechTime(messages[^1].Text, messages[^1].DurationMs)) return false;
            if (own.Count > 0 && now - own[^1].At < TimeSpan.FromSeconds(12)) return false;
            if (humansReady && own.Count >= BotDiscussion.WrapUpMessages) return false;
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
                await SayAsync(state, botId, TableReasoning.Speech(view, tags, mind, finalLine), ct);
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
        // Прямой вопрос этому боту от человека («Ватсон, почему тайна 2?») — короткий ответ, а не вся версия заново.
        if (said.Count < 4 && mind is not null && await QuestionToAsync(state, botId, mind, said.Count > 0 ? said[0].CreatedAt : null, ct) is { } question)
        {
            var asked = question.CardIds.Count > 0 ? (IReadOnlyList<string>)question.CardIds
                : Speech.SpokenCards.Read(question.Text ?? "", state.Board, state.Hints,
                    state.Letters.Where(l => l.From == question.AuthorId).Select(l => l.CardId).ToList(), tags).Cards;
            if (BotPlayer.Answer(view, rng, tags, mind, mind.Names.GetValueOrDefault(question.AuthorId!.Value, "Игрок"), question.Text ?? "", asked) is { } answer)
            {
                if (AccusationReader.Read(question.AuthorId!.Value, question.Text, mind.Names).Any(a => a.Target == botId))
                {
                    var who = mind.Names.GetValueOrDefault(question.AuthorId!.Value, "");
                    var call = who.StartsWith("Бот ", StringComparison.Ordinal) ? who[4..] : who;
                    // «Тестер, это не я. Тестер, тайна 2: …» — имя второй раз не повторяем.
                    var rest = answer.Text.StartsWith(call + ", ", StringComparison.Ordinal) ? Capital(answer.Text[(call.Length + 2)..]) : answer.Text;
                    answer = (Defense(call, Speech.SpokenStyle.MannerOf(botId), rng) + rest, answer.Cards, answer.Notes);
                }

                try
                {
                    await SayAsync(state, botId, new SendChatRequest(ChatChannels.Public, answer.Text, null, answer.Cards, answer.Notes), ct);
                    return true;
                }
                catch (AppException e) { logger.LogDebug("Ответ бота пропущен: {Error}", e.Message); }
            }
        }

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

        // Обвинили — человек сначала отвечает на это, потом говорит о картах.
        if (mind is not null && await AccusedByAsync(state, botId, mind, said.Count > 0 ? said[0].CreatedAt : null, ct) is { } accuser)
            line = (Defense(accuser, Speech.SpokenStyle.MannerOf(botId), rng) + line.Text, line.Cards, line.Notes);

        try
        {
            await SayAsync(state, botId, TableReasoning.Speech(view, tags, mind, line), ct);
            return true;
        }
        catch (AppException e)
        {
            logger.LogDebug("Бот {Bot} не смог написать: {Error}", botId, e.Message);
            return false;
        }
    }

    /// <summary>Кто из людей обвинил бота после его последней реплики (имя для ответа) — или null.</summary>
    private async Task<string?> AccusedByAsync(GameState state, Guid botId, BotMind mind, DateTimeOffset? since, CancellationToken ct)
    {
        var after = since ?? DateTimeOffset.MinValue;
        var recent = await db.ChatMessages.AsNoTracking().Where(m => m.GameId == state.Id && m.Round == state.Round &&
                m.Channel == ChatChannels.Public && m.AuthorId != null && m.AuthorId != botId && m.CreatedAt > after)
            .OrderByDescending(m => m.CreatedAt).Take(8).Select(m => new { m.AuthorId, m.Text }).ToListAsync(ct);
        if (recent.Count == 0) return null;
        var authors = recent.Select(m => m.AuthorId!.Value).Distinct().ToList();
        var bots = await db.Users.AsNoTracking().Where(u => authors.Contains(u.Id) && u.IsBot).Select(u => u.Id).ToListAsync(ct);
        var accuser = recent.FirstOrDefault(m => !bots.Contains(m.AuthorId!.Value) &&
            AccusationReader.Read(m.AuthorId!.Value, m.Text, mind.Names).Any(a => a.Target == botId));
        if (accuser is null) return null;
        var name = mind.Names.GetValueOrDefault(accuser.AuthorId!.Value, "");
        return name.StartsWith("Бот ", StringComparison.Ordinal) ? name[4..] : name;
    }

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Ответ на обвинение — без слов-подозрений, чтобы самого бота не сочли обвинителем.</summary>
    public static string Defense(string who, Speech.SpokenStyle.Manner manner, Random rng)
    {
        string[] lines = manner switch
        {
            Speech.SpokenStyle.Manner.Lively => [$"Я?! {who}, ты что, я мирный! ", $"{who}, да ладно, я за стол играю! ", $"Ну нет, {who}, это не я. "],
            Speech.SpokenStyle.Manner.Formal => [$"{who}, позволь возразить: я играю за стол. ", $"{who}, это ошибка — я не из чёрных. "],
            _ => [$"{who}, зря ты так — я мирный. ", $"{who}, это не я. ", $"{who}, я не из чёрных, посмотри на мои письма. "],
        };
        return lines[rng.Next(lines.Length)];
    }

    /// <summary>Последний вопрос человека этому боту в раунде после его последней реплики: имя бота и «?».</summary>
    private async Task<ChatMessage?> QuestionToAsync(GameState state, Guid botId, BotMind mind, DateTimeOffset? since, CancellationToken ct)
    {
        var name = mind.Names.GetValueOrDefault(botId, "");
        var bare = name.StartsWith("Бот ", StringComparison.Ordinal) ? name[4..] : name;
        if (bare.Length < 2) return null;
        var after = since ?? DateTimeOffset.MinValue;
        var recent = await db.ChatMessages.AsNoTracking().Where(m => m.GameId == state.Id && m.Round == state.Round &&
                m.Channel == ChatChannels.Public && m.AuthorId != null && m.AuthorId != botId && m.CreatedAt > after)
            .OrderByDescending(m => m.CreatedAt).Take(10).ToListAsync(ct);
        if (recent.Count == 0) return null;
        var authors = recent.Select(m => m.AuthorId!.Value).Distinct().ToList();
        var bots = await db.Users.AsNoTracking().Where(u => authors.Contains(u.Id) && u.IsBot).Select(u => u.Id).ToListAsync(ct);
        var pattern = $@"(?<![\p{{L}}]){System.Text.RegularExpressions.Regex.Escape(bare)}(?![\p{{L}}])";
        return recent.FirstOrDefault(m => !bots.Contains(m.AuthorId!.Value) && (m.Text ?? "").Contains('?') &&
            System.Text.RegularExpressions.Regex.IsMatch(m.Text ?? "", pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase));
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
        var playerIds = state.Players.Select(p => p.Id).ToList();
        var botPlayers = (await db.Users.AsNoTracking().Where(u => playerIds.Contains(u.Id) && u.IsBot).Select(u => u.Id).ToListAsync(ct)).ToHashSet();
        var perMessage = messages.Select(m =>
        {
            IReadOnlyList<string> cards = m.CardIds;
            IReadOnlyList<string> notes = m.CardNotes;
            // Человек написал словами, без карт («мотив пять — оно», «точно не клетка») — боты понимают так же, как речь.
            if (cards.Count == 0 && !string.IsNullOrWhiteSpace(m.Text) && !botPlayers.Contains(m.AuthorId!.Value))
            {
                var ownLetters = state.Letters.Where(l => l.From == m.AuthorId).Select(l => l.CardId).ToList();
                var read = Speech.SpokenCards.Read(m.Text, state.Board, state.Hints, ownLetters, tags);
                (cards, notes) = (read.Cards, read.Notes);
            }

            return (IReadOnlyList<ChatOpinion>)TableReasoning.Read(m.AuthorId!.Value, cards, notes, board).ToList();
        }).ToList();
        var opinions = perMessage.SelectMany(o => o).ToList();

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
        return new BotMind(personality, history, latestOpinions, names, latestAccusations, Breadth(messages.Select(m => (m.AuthorId!.Value, (IReadOnlyList<string>)m.CardNotes))), affinities, claims,
            BotMind.Latest(perMessage));
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
