using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Infrastructure.Lobbies;

public sealed record LobbyMemberDto(Guid UserId, string Nickname, string AvatarColor, int Seat, string Mode, bool IsReady, bool IsBot, Guid? AvatarId = null);

public sealed record LobbyDto(
    Guid Id,
    string Code,
    string Title,
    Guid HostUserId,
    string Status,
    LobbySettings Settings,
    Guid? CurrentGameId,
    IReadOnlyList<LobbyMemberDto> Members);

public sealed record CreateLobbyRequest(string? Title, LobbySettings? Settings);

public sealed record JoinLobbyRequest(string? Mode);

/// <summary>Публичный список идущих партий, без ролей и состояния колоды.</summary>
public sealed record WatchableGameDto(string Code, string Title, Guid GameId, string Phase, int Players);

public sealed record ReadyRequest(bool Ready);

public sealed record KickRequest(Guid UserId);

public sealed record StartGameResponse(Guid GameId);
