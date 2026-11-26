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
            .WithAssociations(File.ReadAllText(Path.Combine(cards, "associations.json")))
            .WithNames(File.ReadAllText(Path.Combine(cards, "names.json")))
            .WithReviewedWeights(File.ReadAllText(Path.Combine(cards, "weights.json")));
    }

    [Fact]
    public void ColorSharesAndShapeSalienceAffectTheVotesEvidence()
    {
        var tags = CardTags.Parse("""{"a":[],"b":[],"c":[]}""").WithReviewedWeights("""
            {"a":{"meanings":{"toy":1},"colors":{"red":0.5,"white":0.5},"shapes":{"ring":0.2}},
             "b":{"meanings":{"book":1},"colors":{"red":1},"shapes":{"flat":1}},
             "c":{"meanings":{"money":1},"colors":{"red":1},"shapes":{"ring":1}}}
            """);
        tags.Similarity("a", "b").Should().BeApproximately(.025, .00001);
        tags.Similarity("b", "c").Should().BeApproximately(.05, .00001);
        tags.Similarity("a", "c").Should().BeApproximately(.12 * .2, .00001);
        var reading = tags.AtBoard([new(Category.Motive, ["b","c"])]);
        reading.Similarity("a", "b").Should().Be(0, "форма кольца уже объясняет письмо на этом столе");
        reading.Kind("a", "c").Should().Be(CardTags.LinkKind.Shape);
    }

    [Fact]
    public void ReviewedMeaningsKeepObjectsActionsAndColoursDistinct()
    {
        var tags = Catalog();
        string Label(string card, string key) => tags.AnnotationOf(card).Meanings.Single(m => m.Tag == key).Label;
        Label("orig_0241", "handle").Should().Be("рукоятка");
        Label("orig_0102", "fly").Should().Be("летать");
        Label("orig_0539", "housefly").Should().Be("муха");
        Label("orig_0523", "tie").Should().Be("завязывать");
        Label("orig_0537", "necktie").Should().Be("галстук");
        Label("orig_0679", "orange-fruit").Should().Be("апельсин");
        tags.AnnotationOf("orig_0679").Colors!["orange"].Should().Be(1);
        tags.SearchByTag("муха").Should().Contain("orig_0539").And.NotContain("orig_0102");
        tags.AnnotationOf("orig_0655").Details.Should().Contain(d => d.Tag == "ribbon-bow" && d.Label == "бант");
        tags.Of("orig_0529").Should().Contain("fly").And.NotContain("ride");
        tags.Of("orig_0711").Should().Contain("run").And.NotContain("ride");
        var orange = BotLabService.Think(new([new("Motive", ["orig_0679", "orig_0718"])], [new(1, ["orig_0685"])]), tags);
        foreach (var bot in orange.Bots) bot.Rows[0].Vote.Should().Be(0, bot.Bot);
    }

    [Fact]
    public void PhotoAlbumChecksThePhotoInsteadOfCashForEveryPersonality()
    {
        var tags = Catalog();
        var album = tags.AnnotationOf("orig_0099");
        tags.NameOf("orig_0099").Should().Be("фотоальбом");
        album.Meanings.First().Tag.Should().Be("photo-album");
        album.Meanings.Should().NotContain(m => m.Tag == "money" || m.Tag == "wallet");
        album.Colors!["brown"].Should().Be(.8);
        album.Details.Should().Contain(d => d.Tag == "stitching" && d.Weight < .1);
        tags.SearchByTag("кошелёк").Should().NotContain("orig_0099");
        var board = new LabRow[] { new("Motive", ["orig_0139", "orig_0599", "orig_0208"]) };
        foreach (var bot in BotLabService.Think(new(board, [new(1, ["orig_0099"])]), tags).Bots)
            bot.Rows[0].Vote.Should().Be(0, bot.Bot);
        var hint = BotLabService.Suggest(new(board, [0], [], ["orig_0099", "orig_0648"]), tags);
        hint.Single(c => c.Card == "orig_0099").Pick.Should().BeTrue();
        hint.Single(c => c.Card == "orig_0648").Pick.Should().BeFalse();
    }

    [Fact]
    public void PetBowlPointsToFoodBeforeBubbleWrap()
    {
        LabRow[] board = [new("Motive", ["orig_0688", "orig_0443"])];
        var result = BotLabService.Think(new(board, [new(1, ["orig_0215"])]), Catalog());
        foreach (var bot in result.Bots) bot.Rows[0].Vote.Should().Be(1, bot.Bot);
    }

    [Fact]
    public void LaptopAndPlugExplainTheSharedFunctionBeforeHardness()
    {
        var reason = Catalog().Explain("orig_0150", "zhizha_0198", (.8, .1, .1), .5);
        reason.Should().MatchRegex("электричеств|устройств").And.NotContain("твёрдый");
    }

    [Fact]
    public void DressPointsToSwimwearAndPistolPointsToHolster()
    {
        var tags = Catalog();
        LabRow[] board = [new("Motive", ["orig_0414", "orig_0367"])];
        foreach (var (hint, expected) in new[] { ("orig_0376", 0), ("orig_0279", 1) })
        {
            var result = BotLabService.Think(new(board, [new(1, [hint])]), tags);
            foreach (var bot in result.Bots) bot.Rows[0].Vote.Should().Be(expected, bot.Bot);
        }
    }

    [Fact]
    public void BroomAndSpinningTopExplainThePlushToyWithoutCertifyingSkis()
    {
        var tags = Catalog();
        LabRow[] board = [
            new("Motive", ["orig_0562","zhizha_0057","orig_0704","orig_0725","orig_0245"]),
            new("Place", ["orig_0162","zhizha_0092","orig_0401","zhizha_0073","zhizha_0151"]),
            new("Method", ["orig_0154","orig_0698","orig_0544","orig_0706","orig_0450"]),
            new("Secret", ["orig_0188","orig_0460","orig_0445","orig_0120","zhizha_0170"])];
        var bots = BotLabService.Think(new(board, [new(0, ["orig_0030"]), new(1, ["orig_0496"]), new(2, [])]), tags).Bots;
        foreach (var bot in bots)
        {
            bot.Rows[1].Vote.Should().Be(4, bot.Bot);
            var skis = bot.Rows[2].Cards[3];
            skis.Belief.Should().BeLessThan(.30, bot.Bot);
            skis.Links.Where(l => l.Hint == "orig_0030").Should().NotContain(l => l.Why.Contains("замах"), bot.Bot);
        }
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
    public void Deal1384647065_BootsSupportTwoRowsAndCauldronResolvesTheSecret()
    {
        var tags = Catalog();
        LabRow[] board = [
            new("Motive", ["zhizha_0147","orig_0381","orig_0103","orig_0123","orig_0679"]),
            new("Place", ["orig_0176","orig_0159","orig_0292","orig_0351","zhizha_0117"]),
            new("Method", ["zhizha_0170","orig_0523","orig_0710","orig_0093","orig_0184"]),
            new("Secret", ["orig_0025","orig_0355","zhizha_0198","orig_0591","orig_0500"])];
        LabHint[] hints = [new(1, ["orig_0095"]), new(2, ["orig_0096"]), new(3, ["orig_0376"]), new(4, ["orig_0188"])];
        var bootsRound = BotLabService.Think(new(board, hints[..2], Rounds: 5), tags).Bots.ToDictionary(b => b.Bot);
        var dressRound = BotLabService.Think(new(board, hints[..3], Rounds: 5), tags).Bots.ToDictionary(b => b.Bot);
        foreach (var bot in BotLabService.Think(new(board, hints, Rounds: 5), tags).Bots)
        {
            var boots = bootsRound[bot.Bot].Explanations[1];
            new[] { boots.Card, boots.SecondCard }.Should().BeEquivalentTo(["orig_0292", "orig_0523"], bot.Bot);
            var dress = dressRound[bot.Bot].Explanations[2];
            dress.Should().Match<HintExplanation>(e => e.Kind == "confirm" && e.Card == "orig_0523" && e.SecondCard == null, bot.Bot);
            dressRound[bot.Bot].Rows[2].Cards[1].Belief.Should().BeGreaterThan(bootsRound[bot.Bot].Rows[2].Cards[1].Belief, bot.Bot);
            bot.Explanations[3].Should().Match<HintExplanation>(e => e.Card == "orig_0500" && e.SecondCard == null, bot.Bot);
            bot.Rows.Select(r => r.Vote).Should().Equal([3, 2, 1, 4], bot.Bot);
            bot.Rows.Select(r => r.Cards[r.Vote].Card).Should().Equal(bot.TheoryBest, bot.Bot);
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
