using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Lobbies;
using Microsoft.AspNetCore.SignalR;

namespace GhostLetters.Api.Realtime;

/// <summary>Рассылка через SignalR: каждому игроку — его проекция и видимые ему события.</summary>
public sealed class HubNotifier(IHubContext<PlayHub> hub) : IRealtimeNotifier
{
    public Task LobbyChangedAsync(LobbyDto lobby, CancellationToken ct) =>
        hub.Clients.Group(PlayHub.LobbyGroup(lobby.Id)).SendAsync("LobbyUpdated", lobby, ct);

    public Task GameStartedAsync(Guid lobbyId, Guid gameId, CancellationToken ct) =>
        hub.Clients.Group(PlayHub.LobbyGroup(lobbyId)).SendAsync("GameStarted", new { lobbyId, gameId }, ct);

    public async Task GameChangedAsync(GameState state, IReadOnlyList<GameEvent> events, CancellationToken ct)
    {
        foreach (var player in state.Players)
        {
            var client = hub.Clients.User(player.Id.ToString());
            await client.SendAsync("GameView", GameProjection.For(state, player.Id), ct);
            var visible = events.Where(e => e.OnlyFor is null || e.OnlyFor == player.Id).ToList();
            if (visible.Count > 0)
            {
                await client.SendAsync("GameEvents", new { gameId = state.Id, version = state.Version, events = visible }, ct);
            }
        }

        var table = hub.Clients.Group(PlayHub.TableGroup(state.Id));
        await table.SendAsync("GameView", GameProjection.For(state, null), ct);
        await table.SendAsync("GameEvents",
            new { gameId = state.Id, version = state.Version, events = events.Where(e => e.OnlyFor is null).ToList() }, ct);
    }

    public async Task ChatAsync(ChatMessageDto message, IReadOnlyList<Guid> recipients, bool toTable, CancellationToken ct)
    {
        await hub.Clients.Users(recipients.Select(r => r.ToString()).ToList()).SendAsync("ChatMessage", message, ct);
        if (toTable)
        {
            await hub.Clients.Group(PlayHub.TableGroup(message.GameId)).SendAsync("ChatMessage", message, ct);
        }
    }
}
