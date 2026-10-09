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
