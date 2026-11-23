using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;

namespace GhostLetters.Api.Tests;

/// <summary>Лаборатория мыслей: своя раскладка и подсказки — мысли каждого характера и совет Призрака-бота.</summary>
public sealed class BotLabTests(Xunit.Abstractions.ITestOutputHelper output)
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

    private static CardTags Catalog()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "client", "assets", "cards"))) dir = dir.Parent;
        var cards = Path.Combine(dir!.FullName, "client", "assets", "cards");
        return CardTags.Parse(File.ReadAllText(Path.Combine(cards, "tags.json")))
            .WithDetails(File.ReadAllText(Path.Combine(cards, "details.json")))
            .WithAssociations(File.ReadAllText(Path.Combine(cards, "associations.json")));
    }

    [Fact]
    public void VanishedDartDoesNotCheckTheRadiatorOnTheReportedBoard()
    {
        var tags = Catalog();
        var bot = Guid.NewGuid();
        BoardRowView[] board = [
            new(Category.Motive, ["orig_0499","zhizha_0174","orig_0091","orig_0266","orig_0548"]),
            new(Category.Place, ["orig_0610","orig_0348","orig_0408","orig_0211","orig_0180"]),
            new(Category.Method, ["zhizha_0074","orig_0483","zhizha_0143","orig_0552","zhizha_0161"]),
            new(Category.Secret, ["zhizha_0054","orig_0687","orig_0170","zhizha_0147","zhizha_0004"])];
        PlayerView view = new(Guid.NewGuid(), 1, Phase.Discussion, 1, 4, DiscussionMode.FreeChat,
            board, [], 0, [new(bot, 0, false, Role.Detective, false, 0)], new(bot, Role.Detective, [], [new(1, "orig_0210", false)]),
            null, 0, null, null, null, null, [], [], null);
        foreach (var preset in BotPresets.All)
        {
            var mind = BotMind.Neutral with { Personality = preset.P };
            var line = BotPlayer.Say(view, new Random(1), tags, mind)!.Value;
            line.Cards.Zip(line.Notes).Should().NotContain(x => x.First == "orig_0170" && x.Second.Contains(":0"), preset.Name);
            var withTarget = view with { Board = [.. board[..3], new(Category.Secret, ["orig_0232", "orig_0170"])] };
            var clear = BotPlayer.Say(withTarget, new Random(1), tags, mind)!.Value;
            clear.Cards.Zip(clear.Notes).Should().Contain(x => x.First == "orig_0232" && x.Second.Contains(":0"), preset.Name);
        }
    }

    [Fact]
    public void Deal1423066809_PaperPlaneAndTwoPlantsPointToNotebookAndDriedFlowers()
    {
        var tags = Catalog();
        LabRow[] board = [
            new("Motive", ["orig_0194", "orig_0426", "orig_0488", "orig_0450", "orig_0466"]),
            new("Place", ["orig_0121", "orig_0462", "orig_0028", "orig_0493", "zhizha_0155"]),
            new("Method", ["orig_0473", "orig_0548", "zhizha_0052", "zhizha_0003", "zhizha_0002"]),
            new("Secret", ["zhizha_0194", "orig_0215", "orig_0621", "orig_0237", "orig_0184"]),
        ];
        LabHint[] hints = [
            new(1, ["orig_0167", "orig_0102"]), new(2, ["orig_0091"]),
            new(3, ["orig_0095"]), new(4, ["orig_0123"]),
        ];
        var beforeCactus = BotLabService.Think(new(board, hints[..3]), tags).Bots.ToDictionary(b => b.Bot);
        foreach (var bot in BotLabService.Think(new(board, hints), tags).Bots)
        {
            var notebook = bot.Rows[0].Cards[2];
            var flowers = bot.Rows[1].Cards[3];
            output.WriteLine($"{bot.Bot}: votes {string.Join(", ", bot.Rows.Select(r => r.Cards[r.Vote].Card))}, notebook {notebook.Belief:P1}, flowers {flowers.Belief:P1}");
            bot.Rows[0].Vote.Should().Be(2, bot.Bot);
            bot.Rows[1].Vote.Should().Be(3, bot.Bot);
            notebook.Links.Should().Contain(l => l.Hint == "orig_0102" && l.Why.Contains("лист бумаги"));
            foreach (var plant in new[] { "orig_0095", "orig_0123" })
                flowers.Links.Should().Contain(l => l.Hint == plant && l.Why.Contains("растение"));
            flowers.Belief.Should().BeGreaterThan(beforeCactus[bot.Bot].Rows[1].Cards[3].Belief);
        }
    }

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
