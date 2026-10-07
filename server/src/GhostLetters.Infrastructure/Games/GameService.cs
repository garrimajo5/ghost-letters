using System.Collections.Concurrent;
using System.Text.Json;
using GhostLetters.Application;
using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Lobbies;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GhostLetters.Infrastructure.Games;

public sealed record CommandRequest(string Type, JsonElement? Payload, int? ExpectedVersion, string? ClientCommandId);

public sealed record CommandResult(int Version, bool Duplicate);

public sealed record GameEventDto(long Seq, int Version, string Type, Guid? Actor, string? Detail, DateTimeOffset At);

/// <summary>Кто смотрит партию: игрок со своей проекцией или экран стола.</summary>
public sealed record Viewer(Guid UserId, bool IsTable);

/// <summary>
/// Сервис партии: команды по очереди (блокировка на партию + версия в БД),
/// запись снимка и журнала, рассылка проекций, ходы по таймауту.
/// </summary>
public sealed class GameService(
    GhostLettersDbContext db,
    IRealtimeNotifier notifier,
    LobbyService lobbies,
    TimeProvider time,
    ILogger<GameService> logger)
{
    public const int MaxClientCommandIdLength = 64;

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    public async Task<Viewer> RequireViewerAsync(Guid gameId, Guid userId, CancellationToken ct)
    {
        var game = await db.Games.AsNoTracking().Select(g => new { g.Id, g.LobbyId }).FirstOrDefaultAsync(g => g.Id == gameId, ct)
                   ?? throw AppException.NotFound("Партия не найдена.");
        if (await db.GamePlayers.AnyAsync(p => p.GameId == gameId && p.UserId == userId, ct))
        {
            return new Viewer(userId, IsTable: false);
        }

        var table = game.LobbyId is { } lobbyId && await db.LobbyMembers.AnyAsync(
            m => m.LobbyId == lobbyId && m.UserId == userId && m.JoinMode == JoinModes.Table, ct);
        return table ? new Viewer(userId, IsTable: true) : throw AppException.Forbidden("Вы не участвуете в этой партии.");
    }

    public async Task<PlayerView> GetViewAsync(Guid gameId, Guid userId, CancellationToken ct)
    {
        var viewer = await RequireViewerAsync(gameId, userId, ct);
        var game = await db.Games.AsNoTracking().FirstAsync(g => g.Id == gameId, ct);
        return GameProjection.For(GameStore.Read(game), viewer.IsTable ? null : userId);
    }

    public async Task<DateTimeOffset?> GetDeadlineAsync(Guid gameId, CancellationToken ct) =>
        await db.Games.Where(g => g.Id == gameId).Select(g => g.PhaseDeadline).FirstOrDefaultAsync(ct);

    /// <summary>Пропущенные события после seq, только те, что видны этому игроку.</summary>
    public async Task<IReadOnlyList<GameEventDto>> GetEventsAsync(Guid gameId, Guid userId, long after, CancellationToken ct)
    {
        var viewer = await RequireViewerAsync(gameId, userId, ct);
        var me = viewer.IsTable ? (Guid?)null : userId;
        var rows = await db.GameEvents.AsNoTracking()
            .Where(e => e.GameId == gameId && e.Seq > after && (e.VisibleTo == null || e.VisibleTo == me))
            .OrderBy(e => e.Seq).Take(500).ToListAsync(ct);
        return rows.Select(e => new GameEventDto(
            e.Seq, e.Version, e.Type, e.ActorUserId, GameJson.Deserialize<EventPayload>(e.Payload).Detail, e.CreatedAt)).ToList();
    }

    /// <summary>Команда игрока. Повтор с тем же clientCommandId возвращает прежнюю версию.</summary>
    public async Task<CommandResult> ExecuteAsync(Guid gameId, Guid userId, CommandRequest request, CancellationToken ct)
    {
        var clientId = string.IsNullOrWhiteSpace(request.ClientCommandId) ? null : request.ClientCommandId.Trim();
        if (clientId is { Length: > MaxClientCommandIdLength })
        {
            throw AppException.Validation($"clientCommandId — до {MaxClientCommandIdLength} символов.");
        }

        var command = GameJson.ParseCommand(request.Type, request.Payload);
        var viewer = await RequireViewerAsync(gameId, userId, ct);
        if (viewer.IsTable)
        {
            throw AppException.Forbidden("Экран стола не делает ходов.");
        }

        return await WithGameAsync<CommandResult>(gameId, ct, async (game, state) =>
        {
            if (clientId is not null)
            {
                var done = await db.GameEvents.AsNoTracking()
                    .Where(e => e.GameId == gameId && e.ActorUserId == userId && e.ClientCommandId == clientId)
                    .Select(e => (int?)e.Version).FirstOrDefaultAsync(ct);
                if (done is { } doneVersion)
                {
                    return (new CommandResult(doneVersion, Duplicate: true), (Applied?)null);
                }
            }

            if (request.ExpectedVersion is { } expected && expected != state.Version)
            {
                throw AppException.Conflict(AppException.Codes.VersionConflict,
                    $"Состояние изменилось: версия {state.Version}, а не {expected}. Обновите вид.");
            }

            var events = GameEngine.Execute(state, userId, command);
            return (new CommandResult(state.Version, Duplicate: false),
                new Applied(events, userId, clientId, $"Command:{command.GetType().Name}"));
        });
    }

    /// <summary>Сделать ходы по таймауту во всех партиях, где время вышло. Возвращает, сколько партий сдвинулось.</summary>
    public async Task<int> TimeoutDueAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var due = await db.Games.AsNoTracking()
            .Where(g => g.Status == GameStatuses.Active && g.PhaseDeadline != null && g.PhaseDeadline <= now)
            .OrderBy(g => g.PhaseDeadline).Select(g => g.Id).Take(50).ToListAsync(ct);

        var moved = 0;
        foreach (var gameId in due)
        {
            try
            {
                var applied = await WithGameAsync<bool>(gameId, ct, (game, state) =>
                {
                    if (game.Status != GameStatuses.Active || game.PhaseDeadline is null || game.PhaseDeadline > time.GetUtcNow())
                    {
                        return Task.FromResult((false, (Applied?)null));
                    }

                    var events = GameEngine.Timeout(state);
                    return Task.FromResult((true, (Applied?)new Applied(events, null, null, "Timeout")));
                });
                moved += applied ? 1 : 0;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Таймаут партии {GameId} не применён", gameId);
            }
        }

        return moved;
    }

    /// <summary>Загрузить партию под блокировкой, применить действие, сохранить и разослать.</summary>
    private async Task<T> WithGameAsync<T>(Guid gameId, CancellationToken ct, Func<Game, GameState, Task<(T Result, Applied? Change)>> action)
    {
        var gate = Locks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var game = await db.Games.FirstOrDefaultAsync(g => g.Id == gameId, ct)
                       ?? throw AppException.NotFound("Партия не найдена.");
            var state = GameStore.Read(game);
            var before = GameStore.StepKey(state);

            var (result, applied) = await action(game, state);
            if (applied is null)
            {
                return result;
            }

            var now = time.GetUtcNow();
            var settings = GameJson.Deserialize<LobbySettings>(game.Settings);
            GameStore.Write(game, state, settings, now, phaseChanged: GameStore.StepKey(state) != before);

            var seq = await db.GameEvents.Where(e => e.GameId == gameId).MaxAsync(e => (long?)e.Seq, ct) ?? 0;
            var records = applied.Events.DefaultIfEmpty(new GameEvent(applied.Source)).Select(e => new GameEventRecord
            {
                GameId = gameId,
                Seq = ++seq,
                Version = state.Version,
                Type = e.Type,
                Payload = GameJson.Serialize(new EventPayload(e.Detail)),
                ActorUserId = e.Actor ?? applied.Actor,
                Visibility = e.OnlyFor is null ? EventVisibility.All : EventVisibility.User,
                VisibleTo = e.OnlyFor,
                CreatedAt = now,
            }).ToList();

            // Id команды — только на первом событии, по нему ищется повтор.
            records[0].ClientCommandId = applied.ClientCommandId;
            db.GameEvents.AddRange(records);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw AppException.Conflict(AppException.Codes.VersionConflict, "Партию одновременно изменили. Повторите ход.");
            }

            await notifier.GameChangedAsync(state, applied.Events, ct);
            if (game.Status == GameStatuses.Finished && game.LobbyId is { } lobbyId)
            {
                await lobbies.GameFinishedAsync(lobbyId, ct);
            }

            return result;
        }
        catch (GameRuleException e)
        {
            throw new AppException(e.Code, e.Message, RuleStatus(e.Code));
        }
        finally
        {
            gate.Release();
        }
    }

    public static int RuleStatus(string code) => code switch
    {
        GameRuleException.Codes.Validation => 400,
        GameRuleException.Codes.NotAllowed or GameRuleException.Codes.UnknownPlayer => 403,
        _ => 409,
    };

    private sealed record Applied(IReadOnlyList<GameEvent> Events, Guid? Actor, string? ClientCommandId, string Source);

    private sealed record EventPayload(string? Detail);
}
