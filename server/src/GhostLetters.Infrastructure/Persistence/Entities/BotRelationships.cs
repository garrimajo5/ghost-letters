namespace GhostLetters.Infrastructure.Persistence.Entities;

public sealed class BotRelationship
{
    public Guid BotId { get; set; }
    public Guid PlayerId { get; set; }
    public int Score { get; set; }
    public int SharedGames { get; set; }
    public string Components { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Completion ledger: retries must not change relationships twice.</summary>
public sealed class BotRelationshipGame
{
    public Guid GameId { get; set; }
    public Guid BotId { get; set; }
}

/// <summary>
/// Как игрок назвал карту голосом, когда бот переспросил (««грабли» — это какая карта?»). Valid: true — название
/// подтвердилось (по разметке или проверкой модели), запоминаем; null — проверить не удалось, только на эту партию;
/// false — явно не та карта: не учитываем и помним как недобросовестное.
/// </summary>
public sealed class CardAlias
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid GameId { get; set; }
    public string CardId { get; set; } = string.Empty;
    public string Phrase { get; set; } = string.Empty;
    public bool? Valid { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Фраза, выученная у человека: мысль (intent), эмоция, манера и шаблон с местами {card}/{player}.
/// Боты с той же манерой говорят ею. Админ может удалить неудачную.
/// </summary>
public sealed class PhraseTemplate
{
    public Guid Id { get; set; }
    public string Intent { get; set; } = string.Empty;
    public string Emotion { get; set; } = string.Empty;
    public string Manner { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public Guid AuthorId { get; set; }
    public int Seen { get; set; }
    public int Uses { get; set; }
    public bool Blocked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
