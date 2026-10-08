using System.Text.Json;
using GhostLetters.Application;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
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
            foreach (var botId in game.Select(r => r.UserId).OrderBy(_ => rng.Next()))
            {
                if (await TryMoveAsync(state, botId, rng, ct))
                {
                    moves++;
                    break;
                }
            }
        }

        return moves;
    }

    private async Task<bool> TryMoveAsync(GameState state, Guid botId, Random rng, CancellationToken ct)
    {
        var view = GameProjection.For(state, botId);
        if (await TrySpeakAsync(state, view, botId, rng, ct))
        {
            return true;
        }

        var command = BotPlayer.Decide(view, rng, tags);
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
    private async Task<bool> TrySpeakAsync(GameState state, PlayerView view, Guid botId, Random rng, CancellationToken ct)
    {
        if (state.Phase != Phase.Discussion ||
            (view.Discussion == DiscussionMode.Radio && view.CurrentSpeaker != botId && view.FloorGrantedTo != botId))
        {
            return false;
        }

        var said = await db.ChatMessages.AnyAsync(m => m.GameId == state.Id && m.AuthorId == botId && m.Round == state.Round, ct);
        if (said || BotPlayer.Say(view, rng, tags) is not { } line)
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
