using System.Text.Json;
using GhostLetters.Application;
using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Auth;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Lobbies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GhostLetters.Api.Realtime;

/// <summary>
/// Хаб /hubs/play. Клиент подписывается на лобби и партию; сервер шлёт LobbyUpdated, GameStarted,
/// GameView (личная проекция) и GameEvents. Ошибки — HubException с текстом «КОД: сообщение».
/// </summary>
[Authorize]
public sealed class PlayHub(LobbyService lobbies, GameService games, ChatService chat) : Hub
{
    public const string Path = "/hubs/play";

    public static string LobbyGroup(Guid lobbyId) => $"lobby:{lobbyId}";

    public static string TableGroup(Guid gameId) => $"table:{gameId}";

    public Task<LobbyDto> SubscribeLobby(Guid lobbyId) => Guard(async () =>
    {
        var lobby = await lobbies.GetAsync(lobbyId, UserId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, LobbyGroup(lobbyId));
        return lobby;
    });

    public Task UnsubscribeLobby(Guid lobbyId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, LobbyGroup(lobbyId));

    public Task UnsubscribeGame(Guid gameId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, TableGroup(gameId));

    /// <summary>Игрок получает личную проекцию через Clients.User; зритель и экран стола — публичную через группу.</summary>
    public Task<GameSnapshot> SubscribeGame(Guid gameId) => Guard(async () =>
    {
        var viewer = await games.RequireViewerAsync(gameId, UserId, Context.ConnectionAborted);
        if (viewer.IsObserver)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TableGroup(gameId));
        }

        return await games.SnapshotAsync(gameId, UserId, Context.ConnectionAborted);
    });

    public Task<CommandResult> Command(Guid gameId, string type, JsonElement? payload, int? expectedVersion, string? clientCommandId) =>
        Guard(() => games.ExecuteAsync(gameId, UserId, new CommandRequest(type, payload, expectedVersion, clientCommandId),
            Context.ConnectionAborted));

    /// <summary>Сообщение в чат партии (текст, голосовое по mediaId, упомянутые карты).</summary>
    public Task<ChatMessageDto> SendChat(Guid gameId, string? channel, string? text, Guid? mediaId, IReadOnlyList<string>? cardIds) =>
        Guard(() => chat.SendAsync(gameId, UserId, new SendChatRequest(channel, text, mediaId, cardIds), Context.ConnectionAborted));

    private Guid UserId => Context.User!.UserId();

    private static async Task<T> Guard<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (AppException e)
        {
            throw new HubException($"{e.Code}: {e.Message}");
        }
        catch (GameRuleException e)
        {
            throw new HubException($"{e.Code}: {e.Message}");
        }
    }
}

/// <summary>Пользователь SignalR — sub из токена.</summary>
public sealed class SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirst("sub")?.Value;
}
