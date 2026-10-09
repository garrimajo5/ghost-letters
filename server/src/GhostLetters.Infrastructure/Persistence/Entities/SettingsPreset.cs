namespace GhostLetters.Infrastructure.Persistence.Entities;

public sealed class SettingsPreset
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Settings { get; set; } = "{}";
}
