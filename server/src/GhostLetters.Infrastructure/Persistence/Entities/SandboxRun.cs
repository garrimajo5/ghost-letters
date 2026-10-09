namespace GhostLetters.Infrastructure.Persistence.Entities;

/// <summary>Separate from live games: never updates users, stats or bot relationships.</summary>
public sealed class SandboxRun
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Scenario { get; set; } = "";
    public int Seed { get; set; }
    public string Summary { get; set; } = "{}";
    public string Report { get; set; } = "{}";
}
