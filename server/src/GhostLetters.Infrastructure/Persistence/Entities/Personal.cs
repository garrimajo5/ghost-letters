namespace GhostLetters.Infrastructure.Persistence.Entities;

/// <summary>Личная заметка игрока о другом игроке в партии.</summary>
public sealed class PlayerNote
{
    public Guid GameId { get; set; }

    public Guid OwnerId { get; set; }

    public Guid TargetUserId { get; set; }

    /// <summary>Подозрение: -2..2.</summary>
    public int Suspicion { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>Записи по раундам и цитаты из чата (JSON).</summary>
    public string Entries { get; set; } = "[]";

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Пометки игрока на карте поля: ✕, ✓ и «считаю истинной».</summary>
public sealed class CardMark
{
    public Guid GameId { get; set; }

    public Guid OwnerId { get; set; }

    public string CardId { get; set; } = string.Empty;

    public int Crosses { get; set; }

    public int Checks { get; set; }

    public bool Believed { get; set; }

    /// <summary>Откуда сведения (JSON <c>MarkSources</c>): кто проверял, чьё это письмо, что я говорю о своём письме.</summary>
    public string Sources { get; set; } = "{}";
}

public sealed class Like
{
    public Guid GameId { get; set; }

    public Guid FromUserId { get; set; }

    public Guid ToUserId { get; set; }
}

/// <summary>Выдвижение на ачивку. Код номинации — из каталога или свой.</summary>
public sealed class AwardNomination
{
    public Guid Id { get; set; }

    public Guid GameId { get; set; }

    public Guid NominatorId { get; set; }

    public Guid NomineeId { get; set; }

    public string NominationCode { get; set; } = string.Empty;
}

public sealed class AwardVote
{
    public Guid GameId { get; set; }

    public Guid VoterId { get; set; }

    public Guid AwardNominationId { get; set; }
}

/// <summary>Полка ачивок игрока.</summary>
public sealed class UserAchievement
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string NominationCode { get; set; } = string.Empty;

    public Guid GameId { get; set; }

    public int Votes { get; set; }

    public DateTimeOffset AwardedAt { get; set; }
}

public sealed class UserStats
{
    public const int InitialRating = 1000;

    public Guid UserId { get; set; }

    public int Games { get; set; }

    public int Wins { get; set; }

    /// <summary>Победы по ролям (JSON: {"Detective": 3, ...}).</summary>
    public string RoleWins { get; set; } = "{}";

    public int Rating { get; set; } = InitialRating;

    public int LikesReceived { get; set; }
}

public sealed class RatingHistory
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid GameId { get; set; }

    public int Delta { get; set; }

    public int RatingAfter { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
