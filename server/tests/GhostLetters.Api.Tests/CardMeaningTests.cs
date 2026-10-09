using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

public sealed class CardMeaningTests
{
    [Fact]
    public void SecondaryMeaningDependsOnPersonality_AndExplainsOnlyVisibleLinks()
    {
        var tags = CardTags.Empty.WithAnnotations(new Dictionary<string, CardAnnotation>
        {
            ["a"] = new([], [new("flower", 1, "Цветок"), new("memory", .4, "Воспоминание")], []),
            ["b"] = new([], [new("memory", 1, "Воспоминание")], []),
            ["c"] = new([], [new("memory", .1, "Воспоминание")], []),
        });
        tags.Similarity("a", "b", (1, 0, 0), 0, 0).Should().Be(0);
        tags.Explain("a", "b", (1, 0, 0), 0, 0).Should().Be("явной связи не вижу");
        var open = tags.Similarity("a", "b", (1, 0, 0), 0, 1);
        open.Should().BeGreaterThan(tags.Similarity("a", "b", (1, 0, 0), 0, .2));
        open.Should().BeGreaterThan(tags.Similarity("a", "c", (1, 0, 0), 0, 1));
        tags.Explain("a", "b", (1, 0, 0), 0, 1).Should().Be("по смыслу: Воспоминание");
        CardTags.Empty.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PersonalityBoundariesSurviveGameVariationAndApiRoundtrip(double value)
    {
        var p = new BotPersonality { SecondaryMeanings = value, Variability = 1 };
        BotSpectra.From(p).ToPersonality().SecondaryMeanings.Should().Be(value);
        p.ForGame(Guid.NewGuid(), Guid.NewGuid()).SecondaryMeanings.Should().Be(value);
    }
}
