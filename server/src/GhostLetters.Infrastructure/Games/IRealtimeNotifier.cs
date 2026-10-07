using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Lobbies;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Рассылка изменений подписчикам (SignalR). Вызывается после фиксации транзакции.</summary>
public interface IRealtimeNotifier
{
    Task LobbyChangedAsync(LobbyDto lobby, CancellationToken ct);

    Task GameStartedAsync(Guid lobbyId, Guid gameId, CancellationToken ct);

    /// <summary>Новое состояние партии: каждому игроку — его проекция, экрану стола — общая.</summary>
    Task GameChangedAsync(GameState state, IReadOnlyList<GameEvent> events, CancellationToken ct);

    /// <summary>Сообщение чата: адресатам по id и, если toTable, экрану стола.</summary>
    Task ChatAsync(ChatMessageDto message, IReadOnlyList<Guid> recipients, bool toTable, CancellationToken ct);
}

/// <summary>Заглушка, когда реалтайм не подключён.</summary>
public sealed class NullRealtimeNotifier : IRealtimeNotifier
{
    public Task LobbyChangedAsync(LobbyDto lobby, CancellationToken ct) => Task.CompletedTask;

    public Task GameStartedAsync(Guid lobbyId, Guid gameId, CancellationToken ct) => Task.CompletedTask;

    public Task GameChangedAsync(GameState state, IReadOnlyList<GameEvent> events, CancellationToken ct) => Task.CompletedTask;

    public Task ChatAsync(ChatMessageDto message, IReadOnlyList<Guid> recipients, bool toTable, CancellationToken ct) =>
        Task.CompletedTask;
}
