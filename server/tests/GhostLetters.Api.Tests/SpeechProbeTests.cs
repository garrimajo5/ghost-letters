using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>Временный зонд: показать, как боты говорят в полной партии (не сливать).</summary>
public sealed class SpeechProbeTests
{
    [Fact]
    public void Probe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "client", "assets", "cards", "tags.json"))) dir = dir.Parent;
        var cards = Path.Combine(dir!.FullName, "client", "assets", "cards");
        var tags = CardTags.Parse(File.ReadAllText(Path.Combine(cards, "tags.json"))).WithDetails(File.ReadAllText(Path.Combine(cards, "details.json")))
            .WithAssociations(File.ReadAllText(Path.Combine(cards, "associations.json")));
        var deck = Enumerable.Range(1, 758).Select(i => $"orig_{i:0000}").ToList();
        var lines = new List<string>();
        foreach (var seed in new[] { 3, 7 })
        {
            var report = BotSandboxRunner.Run("full-game", seed, deck, tags, default);
            var names = report.Seats.ToDictionary(s => s.Id, s => s.Name);
            lines.AddRange(report.Messages.Where(m => m.Spoken is not null).Take(18).Select(m => $"[{names[m.Author]}] {m.Spoken}"));
        }
        Assert.Fail("ОБРАЗЦЫ\n" + string.Join("\n", lines));
    }
}
