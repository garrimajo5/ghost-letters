using System.Text;
using System.Text.Json;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>Временный зонд для плейтеста: полная партия ботов — роли, ходы и реплики (не сливать).</summary>
public sealed class PlaytestProbeTests
{
    [Theory]
    [InlineData(11)]
    [InlineData(23)]
    [InlineData(37)]
    public void Probe(int seed)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "client", "assets", "cards", "tags.json"))) dir = dir.Parent;
        var cards = Path.Combine(dir!.FullName, "client", "assets", "cards");
        var tags = CardTags.Parse(File.ReadAllText(Path.Combine(cards, "tags.json"))).WithDetails(File.ReadAllText(Path.Combine(cards, "details.json")))
            .WithAssociations(File.ReadAllText(Path.Combine(cards, "associations.json")));
        var names = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(Path.Combine(cards, "names.json")))!;
        string N(string id) => names.TryGetValue(id, out var n) && n.Length > 0 ? n[0] : id;
        var deck = Enumerable.Range(1, 758).Select(i => $"orig_{i:0000}").ToList();
        var report = BotSandboxRunner.Run("full-game", seed, deck, tags, default);
        var seat = report.Seats.ToDictionary(s => s.Id, s => s.Name);
        var final = report.Frames.Last().State;
        var b = new StringBuilder();
        b.Append($"ПАРТИЯ seed={seed} {report.Status} winner={report.Winner} rows={report.CorrectRows}/{report.TotalRows}\n");
        foreach (var p in final.Players) b.Append($"  {seat.GetValueOrDefault(p.Id, "?")}: {p.Role}\n");
        b.Append("ПОЛЕ: " + string.Join(" | ", final.Board.Select((r, i) => $"{r.Category}: " + string.Join(", ", r.Cards.Select((c, j) => $"{j + 1}.{N(c)}{(final.Truth is { } t && t.Count > i && t[i] == j ? "*" : "")}")))) + "\n");
        var shown = 0;
        string? last = null;
        foreach (var f in report.Frames)
        {
            for (; shown < Math.Min(f.MessageCount, report.Messages.Count); shown++)
            {
                var m = report.Messages[shown];
                var cs = m.Cards.Count == 0 ? "" : " [" + string.Join(", ", m.Cards.Select((c, i) => N(c) + (i < m.Notes.Count ? ":" + m.Notes[i] : ""))) + "]";
                b.Append($"  r{m.Round} {seat.GetValueOrDefault(m.Author, "?")}: {m.Spoken ?? m.Text}{cs}\n");
            }
            var cmd = f.Command is { } c ? c.GetRawText() : "";
            foreach (var id in deck.Where(cmd.Contains)) cmd = cmd.Replace(id, N(id));
            var line = $"{f.Action} {(f.Actor is { } a ? seat.GetValueOrDefault(a, "?") : "")} {(cmd.Length > 140 ? cmd[..140] : cmd)}";
            if (line != last && !f.Action.Contains("Typing", StringComparison.Ordinal)) b.Append("> " + line + "\n");
            last = line;
        }
        b.Append("ПОДСКАЗКИ: " + string.Join(" | ", final.Hints.Select(h => $"р{h.Round}: " + string.Join(", ", h.Cards.Select(N)))) + "\n");
        b.Append("ПИСЬМА: " + string.Join(" | ", final.Letters.Select(l => $"р{l.Round} {seat.GetValueOrDefault(l.From, "?")}: {N(l.CardId)}{(l.Revealed ? "+" : "-")}")) + "\n");
        var text = b.ToString();
        if (Environment.GetEnvironmentVariable("PLAYTEST_OUT") is { Length: > 0 } outDir)
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, $"game-{seed}.txt"), text);
            return;
        }
        Assert.Fail(text.Length > 60000 ? text[..60000] : text);
    }
}
