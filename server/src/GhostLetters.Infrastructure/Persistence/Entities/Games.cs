namespace GhostLetters.Infrastructure.Persistence.Entities;

public static class GameStatuses
{
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Finished = "finished";
    public const string Abandoned = "abandoned";
}

/// <summary>Партия: снимок состояния домена (JSONB) и версия для очереди команд.</summary>
public sealed class Game
{
    public Guid Id { get; set; }

    public Guid? LobbyId { get; set; }

    public string Status { get; set; } = GameStatuses.Active;

    public string Settings { get; set; } = "{}";

    public int Seed { get; set; }

    /// <summary>Полное состояние GameState. Наружу не отдаётся.</summary>
    public string State { get; set; } = "{}";

    public int Version { get; set; }

    public string Phase { get; set; } = string.Empty;

    public DateTimeOffset? PhaseDeadline { get; set; }

    public string? Result { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>Место в партии. Роль читает только сервер.</summary>
public sealed class GamePlayer
{
    public Guid GameId { get; set; }

    public Guid UserId { get; set; }

    public int Seat { get; set; }

    public string Role { get; set; } = string.Empty;

    public string? CharacterCode { get; set; }

    public bool IsConnected { get; set; }
}

public static class EventVisibility
{
    public const string All = "all";
    public const string User = "user";
}

/// <summary>Журнал событий партии: повтор, догрузка после переподключения, данные для ботов.</summary>
public sealed class GameEventRecord
{
    public Guid GameId { get; set; }

    public long Seq { get; set; }

    public int Version { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = "{}";

    public Guid? ActorUserId { get; set; }

    public string Visibility { get; set; } = EventVisibility.All;

    /// <summary>Получатель личного события.</summary>
    public Guid? VisibleTo { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public static class ChatChannels
{
    public const string Public = "public";
    public const string KillerTeam = "killer_team";
}

public static class ChatKinds
{
    public const string Text = "text";
    public const string Voice = "voice";
    public const string System = "system";
}

public sealed class ChatMessage
{
    public Guid Id { get; set; }

    public Guid GameId { get; set; }

    public int Round { get; set; }

    public string Channel { get; set; } = ChatChannels.Public;

    public Guid? AuthorId { get; set; }

    public string Kind { get; set; } = ChatKinds.Text;

    public string? Text { get; set; }

    public Guid? MediaId { get; set; }

    /// <summary>Карты, упомянутые в сообщении.</summary>
    public List<string> CardIds { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Голосовое сообщение.</summary>
public sealed class Media
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public int DurationMs { get; set; }

    public long SizeBytes { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Голос финала — копия из состояния для статистики.</summary>
public sealed class Vote
{
    public Guid Id { get; set; }

    public Guid GameId { get; set; }

    public int Stage { get; set; }

    /// <summary>row / killer.</summary>
    public string StageKind { get; set; } = string.Empty;

    public int Attempt { get; set; }

    public Guid VoterId { get; set; }

    public int? Column { get; set; }

    public Guid? SuspectId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
