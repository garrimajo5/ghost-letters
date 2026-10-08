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
public sealed class BotService(GhostLettersDbContext db, GameService games, ChatService chat, CardTags tags, ILogger<BotService> logger)
{
    /// <summary>Сделать ходы ботов. Возвращает число сделанных ходов.</summary>
    public async Task<int> TickAsync(CancellationToken ct)
    {
        var rows = await (
                from p in db.GamePlayers.AsNoTracking()
                join u in db.Users.AsNoTracking() on p.UserId equals u.Id
                join g in db.Games.AsNoTracking() on p.GameId equals g.Id
                where u.IsBot && g.Status == GameStatuses.Active
                select new { p.GameId, p.UserId })
            .ToListAsync(ct);

        var moves = 0;
        foreach (var game in rows.GroupBy(r => r.GameId))
        {
            var row = await db.Games.AsNoTracking().FirstAsync(g => g.Id == game.Key, ct);
            var state = GameStore.Read(row);
            var rng = new Random(HashCode.Combine(state.Seed, state.Version));
            var bots = game.Select(r => r.UserId).ToHashSet();
            foreach (var botId in bots.OrderBy(_ => rng.Next()))
            {
                if (WaitsForTeam(state, botId, bots, row.PhaseDeadline, DateTimeOffset.UtcNow))
                {
                    continue;
                }

                if (await TryMoveAsync(state, botId, rng, ct))
                {
                    moves++;
                    break;
                }
            }
        }

        return moves;
    }

    /// <summary>
    /// Бот-Убийца ночью и на охоте даёт живым Сообщникам время подсказать: ждёт, пока все не подскажут
    /// или до конца фазы не останется 30 секунд. Без таймера не ждёт.
    /// </summary>
    public static bool WaitsForTeam(GameState state, Guid botId, IReadOnlySet<Guid> bots, DateTimeOffset? deadline, DateTimeOffset now)
    {
        if (deadline is null || !GameEngine.TeamSuggestPhase(state) || state.Player(botId).Role != Role.Killer)
        {
            return false;
        }

        var pending = state.Players.Any(p => p.Role == Role.Accomplice && !bots.Contains(p.Id) && !state.TeamSuggestions.ContainsKey(p.Id));
        return pending && deadline.Value - now > TimeSpan.FromSeconds(30);
    }

    private async Task<bool> TryMoveAsync(GameState state, Guid botId, Random rng, CancellationToken ct)
    {
        var view = GameProjection.For(state, botId);
        var mind = await MindAsync(state, botId, ct);
        if (await TrySpeakAsync(state, view, botId, rng, mind, ct))
        {
            return true;
        }

        var command = BotPlayer.Decide(view, rng, tags, mind);
        if (command is null)
        {
            return false;
        }

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

    /// <summary>Раз за раунд обсуждения бот говорит, что думает (в рации — когда у него слово).</summary>
    private async Task<bool> TrySpeakAsync(GameState state, PlayerView view, Guid botId, Random rng, BotMind? mind, CancellationToken ct)
    {
        if (state.Phase != Phase.Discussion ||
            (view.Discussion == DiscussionMode.Radio && view.CurrentSpeaker != botId && view.FloorGrantedTo != botId))
        {
            return false;
        }

        var said = await db.ChatMessages.AnyAsync(m => m.GameId == state.Id && m.AuthorId == botId && m.Round == state.Round, ct);
        if (said || BotPlayer.Say(view, rng, tags, mind) is not { } line)
        {
            return false;
        }

        try
        {
            await chat.SendAsync(state.Id, botId, new SendChatRequest(ChatChannels.Public, line.Text, null, line.Cards, line.Notes), ct);
            return true;
        }
        catch (AppException e)
        {
            logger.LogDebug("Бот {Bot} не смог написать: {Error}", botId, e.Message);
            return false;
        }
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
            return null;
        }

        var personality = BotAdminService.Personality(profile).ForGame(state.Id, botId);
        var others = state.Players.Select(p => p.Id).Where(id => id != botId).ToList();

        var past = await (from mine in db.GamePlayers.AsNoTracking()
                          join g in db.Games.AsNoTracking() on mine.GameId equals g.Id
                          join other in db.GamePlayers.AsNoTracking() on mine.GameId equals other.GameId
                          where mine.UserId == botId && g.Id != state.Id && g.Status == GameStatuses.Finished
                                && others.Contains(other.UserId)
                          select new { other.UserId, other.Role })
            .ToListAsync(ct);
        var history = past.GroupBy(x => x.UserId).ToDictionary(
            gr => gr.Key,
            gr => new PlayerHistory(
                gr.Count(),
                gr.Count(x => x.Role is nameof(Role.Killer) or nameof(Role.Accomplice)),
                gr.Count(x => x.Role is nameof(Role.Witness) or nameof(Role.Expert))));

        var messages = await db.ChatMessages.AsNoTracking()
            .Where(m => m.GameId == state.Id && m.Channel == ChatChannels.Public && m.AuthorId != null && m.AuthorId != botId)
            .Select(m => new { m.AuthorId, m.CardIds, m.CardNotes })
            .ToListAsync(ct);
        var board = state.Board.SelectMany(r => r.Cards).ToHashSet();
        var opinions = new List<ChatOpinion>();
        foreach (var m in messages)
        {
            for (var i = 0; i < m.CardIds.Count; i++)
            {
                var note = i < m.CardNotes.Count ? m.CardNotes[i] : string.Empty;
                if (!board.Contains(m.CardIds[i]) || note.StartsWith("кидал", StringComparison.Ordinal))
                {
                    continue;
                }

                opinions.Add(new ChatOpinion(m.AuthorId!.Value, m.CardIds[i], note.StartsWith("проверял", StringComparison.Ordinal) ? 0.5 : 1));
            }
        }

        var names = await db.Users.AsNoTracking().Where(u => others.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nickname, ct);
        return new BotMind(personality, history, opinions, names);
    }

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
