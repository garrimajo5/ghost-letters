using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Infrastructure.Lobbies;

public sealed record LobbyMemberDto(Guid UserId, string Nickname, string AvatarColor, int Seat, string Mode, bool IsReady, bool IsBot);

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

public sealed record ReadyRequest(bool Ready);

public sealed record KickRequest(Guid UserId);

public sealed record StartGameResponse(Guid GameId);
