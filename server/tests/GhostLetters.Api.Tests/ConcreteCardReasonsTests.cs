using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

public sealed class ConcreteCardReasonsTests
{
    [Theory]
    [InlineData("music", "музыка")]
    [InlineData("sound", "звук")]
    [InlineData("dominant-red", "красный")]
    [InlineData("shape-round", "круглая")]
    public void NamesTheActualSharedFeature(string tag, string expected)
    {
        var tags = new CardTags(new Dictionary<string, HashSet<string>>
        { ["a"] = [tag], ["b"] = [tag] });
        tags.Explain("a", "b", (.7, .2, .1), 0).Should().Contain(expected);
    }

    [Fact]
    public void DoesNotInventColorsOrMeanings()
    {
        var tags = new CardTags(new Dictionary<string, HashSet<string>>
        { ["a"] = ["music", "dominant-red"], ["b"] = ["water", "dominant-blue"] });
        tags.Explain("a", "b", (.7, .2, .1), 0).Should().Be("явной связи не вижу");
    }
}
