namespace GhostLetters.Infrastructure.Persistence.Entities;

public static class LobbyStatuses
{
    public const string Open = "open";
    public const string InGame = "in_game";
    public const string Closed = "closed";
}

public sealed class Lobby
{
    public const int CodeLength = 6;

    public Guid Id { get; set; }

    /// <summary>Код для входа; уникален среди незакрытых лобби.</summary>
    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public Guid HostUserId { get; set; }

    public string Status { get; set; } = LobbyStatuses.Open;

    /// <summary>Настройки партии (JSON).</summary>
    public string Settings { get; set; } = "{}";

    public Guid? CurrentGameId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public static class JoinModes
{
    public const string Player = "player";
    public const string Table = "table";
    public const string Spectator = "spectator";
}

public sealed class LobbyMember
{
    public Guid LobbyId { get; set; }

    public Guid UserId { get; set; }

    public int Seat { get; set; }

    public string JoinMode { get; set; } = JoinModes.Player;

    public bool IsReady { get; set; }

    public DateTimeOffset JoinedAt { get; set; }
}
