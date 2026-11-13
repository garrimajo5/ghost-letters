using System.Text.Json;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

public sealed class ReviewedCardCatalogTests
{
    [Fact]
    public void PythonControlReportUsesTheProductionSimilarityScores()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "client", "assets", "cards")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var root = dir!.FullName;
        var cards = Path.Combine(root, "client", "assets", "cards");
        var tags = CardTags.Parse(File.ReadAllText(Path.Combine(cards, "tags.json")))
            .WithDetails(File.ReadAllText(Path.Combine(cards, "details.json")))
            .WithAssociations(File.ReadAllText(Path.Combine(cards, "associations.json")));
        using var tuning = BotTuning.Use(BotTuning.Default);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "CARDS_RETAG_REPORT.json")));
        foreach (var query in report.RootElement.GetProperty("top10").EnumerateObject())
        {
            foreach (var neighbor in query.Value.EnumerateArray())
            {
                var card = neighbor.GetProperty("card").GetString()!;
                tags.Similarity(query.Name, card).Should().BeApproximately(neighbor.GetProperty("score").GetDouble(), 1e-8,
                    $"the Python report must use the server score for {query.Name} → {card}");
            }
        }
        foreach (var check in report.RootElement.GetProperty("checks").EnumerateArray())
        {
            if (!check.TryGetProperty("score", out var score)) continue;
            var source = check.GetProperty("source").GetString()!;
            var target = check.GetProperty("target").GetString()!;
            tags.Similarity(source, target).Should().BeApproximately(score.GetDouble(), 1e-10);
        }
    }
}
