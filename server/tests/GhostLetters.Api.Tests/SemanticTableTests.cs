using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

public sealed class SemanticTableTests
{
    private static readonly CardTags Tags = CardTags.Parse("""
        {"clue":["weapon","red","dominant-red","shape-long"],
         "meaning":["weapon"],"shape":["tree","shape-long"],
         "colour":["fruit","red","dominant-red"],"background":["fruit","red"]}
        """);

    [Theory]
    [InlineData(0,0,1)]
    [InlineData(0,1,0)]
    [InlineData(1,0,0)]
    public void MeaningBeatsVisualCoincidenceForEveryPersonality(double meaning, double shape, double colour)
    {
        var p = new BotPersonality { Meaning = meaning, Shape = shape, Color = colour };
        var score = Tags.Similarity("clue", "meaning", p.Attention, 0);
        score.Should().BeGreaterThan(Tags.Similarity("clue", "shape", p.Attention, 0));
        score.Should().BeGreaterThan(Tags.Similarity("clue", "colour", p.Attention, 0));
        Tags.Similarity("clue", "background", p.Attention, 0).Should().Be(0);
    }

    [Fact]
    public void DifferentVisualPreferencesRemainWhenMeaningIsAbsent()
    {
        var colour = new BotPersonality { Meaning = 0, Color = 1, Shape = 0 }.Attention;
        var shape = new BotPersonality { Meaning = 0, Color = 0, Shape = 1 }.Attention;
        Tags.Similarity("clue", "colour", colour).Should().BeGreaterThan(Tags.Similarity("clue", "shape", colour));
        Tags.Similarity("clue", "shape", shape).Should().BeGreaterThan(Tags.Similarity("clue", "colour", shape));
    }

    [Fact]
    public void TinyColourDetailsCannotReintroduceColourDominance()
    {
        var tags = Tags.WithDetails("""{"clue":[{"tag":"accent-red","weight":0.1,"label":"красные точки"}],"background":[{"tag":"accent-red","weight":0.1,"label":"красные точки"}]}""");
        tags.Similarity("clue", "background", (.7,.2,.1), 1).Should().Be(0);
        tags.Explain("clue", "background", (.7,.2,.1), 1).Should().Be("явной связи не вижу");
    }

    [Fact]
    public void HumanLinksPreserveSourceAndExclusion_WithoutVotingForSource()
    {
        var author = Guid.NewGuid();
        var opinions = TableReasoning.Read(author, ["clue", "meaning", "background"],
            ["улика", "думаю, эта:0", "исключаю:0"], new HashSet<string> { "clue", "meaning", "background" }).ToList();
        opinions.Should().HaveCount(2);
        opinions[0].SourceCard.Should().Be("clue");
        opinions[0].Strength.Should().Be(1);
        opinions[1].Strength.Should().Be(-1);
        TableReasoning.Read(author, ["meaning"], ["думаю, эта:99"], new HashSet<string> { "meaning" })
            .Single().SourceCard.Should().BeNull();
    }

    [Fact]
    public void NightWaitCannotRevealInstantBotKiller_AndIsBounded()
    {
        var now = DateTimeOffset.UtcNow;
        var bot = Guid.NewGuid();
        BotService.WaitForNight(bot, now, now.AddSeconds(20), null).Should().BeTrue();
        BotService.WaitForNight(bot, now, now.AddSeconds(85), null, now.AddSeconds(80), new string('а', 400)).Should().BeTrue();
        BotService.WaitForNight(bot, now, now.AddMinutes(3), null, now.AddMinutes(3), "ещё").Should().BeFalse();
        BotService.WaitForNight(bot, now, now, now.AddSeconds(4)).Should().BeFalse();
    }

    [Fact]
    public void LongSpeechTakesLongerButNeverBlocksForever()
    {
        BotDiscussion.SpeechTime(new string('а', 500)).Should().BeGreaterThan(BotDiscussion.SpeechTime("Да"));
        BotDiscussion.SpeechTime(new string('а', 1000)).Should().Be(TimeSpan.FromSeconds(45));
    }
}
