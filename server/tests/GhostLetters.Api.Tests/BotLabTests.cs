using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>Лаборатория мыслей: своя раскладка и подсказки — мысли каждого характера и совет Призрака-бота.</summary>
public sealed class BotLabTests
{
    // Ряд «Мотив»: кольцо | кошелёк | лодка; «Способ»: меч | кот | роза.
    private static readonly CardTags Tags = new(new Dictionary<string, HashSet<string>>
    {
        ["ring"] = ["ring", "gold", "jewel"], ["wallet"] = ["wallet", "money", "leather"], ["boat"] = ["boat", "water"],
        ["sword"] = ["sword", "steel", "weapon"], ["cat"] = ["cat", "animal", "fur"], ["rose"] = ["rose", "flower", "red"],
        ["crown"] = ["gold", "jewel", "king"], ["knife"] = ["steel", "weapon", "kitchen"], ["purse"] = ["money", "leather", "bag"],
        ["stone"] = ["stone"],
    });

    private static readonly LabRow[] Board = [new("Motive", ["ring", "wallet", "boat"]), new("Method", ["sword", "cat", "rose"])];

    [Fact]
    public void Think_EveryBotReadsHints_AndReportsWhy()
    {
        var result = BotLabService.Think(new LabThinkRequest(Board, [new LabHint(1, ["crown"]), new LabHint(2, ["knife"])], [0, 0]), Tags);
        result.Bots.Should().HaveCount(6);
        foreach (var bot in result.Bots)
        {
            bot.Rows.Should().HaveCount(2);
            foreach (var row in bot.Rows) row.Cards.Sum(c => c.Belief).Should().BeApproximately(1, 1e-6);
            bot.Rows[0].Cards[0].Belief.Should().BeGreaterThan(bot.Rows[0].Cards[1].Belief, bot.Bot);
            bot.Rows[0].Cards[0].Links.Should().Contain(l => l.Hint == "crown");
            bot.Correct.Should().NotBeNull();
        }
        result.Bots.Count(b => b.Correct == 2).Should().BeGreaterThan(3);
    }

    [Fact]
    public void Think_NoHints_EvenBeliefs_AndFilterByBot()
    {
        var result = BotLabService.Think(new LabThinkRequest(Board, [], Bots: ["Пуаро"]), Tags);
        result.Bots.Should().ContainSingle().Which.Bot.Should().Be("Пуаро");
        result.Bots[0].Correct.Should().BeNull();
        result.Bots[0].Rows[0].Cards.Select(c => c.Belief).Should().OnlyContain(b => Math.Abs(b - 1.0 / 3) < .05);
    }

    [Fact]
    public void Suggest_GhostPicksLettersPointingToTruth()
    {
        var s = BotLabService.Suggest(new LabSuggestRequest(Board, [1, 0], [], ["purse", "stone", "knife"]), Tags);
        s.Should().HaveCount(3);
        s.Single(x => x.Card == "purse").Pick.Should().BeTrue();
        s.Single(x => x.Card == "stone").Pick.Should().BeFalse();
    }
}
