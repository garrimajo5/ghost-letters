using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>
/// Версии вместо суммы: подсказку объясняет одна истина. Две одежды подряд закрепляют свитер, а не делают одеждой
/// и второй ряд; пара подсказок может указывать на одну карту; замысел письма («кидал на тиару») учитывается с доверием.
/// </summary>
public sealed class BotTheoryVersionsTests
{
    private static readonly Guid Ghost = Guid.NewGuid(), Me = Guid.NewGuid(), Ann = Guid.NewGuid(), Bob = Guid.NewGuid();

    private static readonly CardTags Tags = new(new Dictionary<string, HashSet<string>>
    {
        // Мотив: свитер | кольцо | лодка; способ: шарф | нож | роза; тайна: дорожка | кот | тиара.
        ["sweater"] = ["sweater", "clothing", "wool", "warm"], ["ring"] = ["ring", "gold", "finger"], ["boat"] = ["boat", "water", "sail"],
        ["scarf"] = ["scarf", "clothing", "wool", "neck"], ["knife"] = ["knife", "steel", "kitchen"], ["rose"] = ["rose", "flower", "red"],
        ["treadmill"] = ["treadmill", "run", "sport", "screen", "electric"], ["cat"] = ["cat", "animal", "fur"],
        ["tiara"] = ["tiara", "gold", "jewel", "head"],
        // Подсказки.
        ["mitten"] = ["mitten", "clothing", "wool", "warm"], ["hat"] = ["hat", "clothing", "wool", "warm"],
        ["boots"] = ["boots", "run", "leather"], ["tv"] = ["tv", "screen", "electric"],
        ["brooch"] = ["brooch", "gold", "jewel"],
    });

    private static PlayerView View(params HintGroupView[] hints) => new(
        Guid.NewGuid(), 1, Phase.Discussion, hints.Select(h => h.Round).DefaultIfEmpty(1).Max(), 4, DiscussionMode.FreeChat,
        [
            new BoardRowView(Category.Motive, ["sweater", "ring", "boat"]),
            new BoardRowView(Category.Method, ["scarf", "knife", "rose"]),
            new BoardRowView(Category.Place, ["treadmill", "cat", "tiara"]),
        ],
        hints, 0,
        [
            new PlayerInfoView(Ghost, 0, true, Role.Ghost, false, 5),
            new PlayerInfoView(Me, 1, false, Role.Detective, false, 5),
            new PlayerInfoView(Ann, 2, false, null, false, 5),
            new PlayerInfoView(Bob, 3, false, null, false, 5),
        ],
        new MeView(Me, Role.Detective, [], []), null, 0, null, null, null, null, [], [], null);

    private static BotMind Mind(params ChatOpinion[] opinions) =>
        new(BotPersonality.Default, new Dictionary<Guid, PlayerHistory>(), opinions,
            new Dictionary<Guid, string> { [Ghost] = "Призрак", [Me] = "Я", [Ann] = "Аня", [Bob] = "Боб" });

    private static HintTheory Theory(BotTuning tuning, PlayerView view, BotMind mind)
    {
        using var _ = BotTuning.Use(tuning);
        return BotPlayer.Theory(view, Tags, mind)!;
    }

    [Fact]
    public void TwoClothesInARow_ConfirmOneCard_NotTwoClothes()
    {
        var view = View(new HintGroupView(1, ["mitten"]), new HintGroupView(2, ["hat"]));
        var versions = Theory(BotTuning.Default, view, Mind());
        var sum = Theory(BotTuning.Default with { TheoryVersions = false }, view, Mind());
        // Свитер — тёплая одежда, как обе подсказки: его закрепляют обе.
        versions.Truth["sweater"].Should().BeGreaterThan(versions.Truth["scarf"]);
        versions.Best[0].Should().Be("sweater");
        // Шарф не становится истиной «заодно»: сумма тянула его вверх, версии — нет.
        versions.Truth["scarf"].Should().BeLessThan(sum.Truth["scarf"]);
        versions.Explanations.Select(e => e.Kind).Should().Equal("direct", "confirm");
        versions.Explanations.Should().OnlyContain(e => e.Card == "sweater");
    }

    [Fact]
    public void OneLetterCanIntroduceTwoRowsAndTheNextCanConfirmItsSecondTarget()
    {
        var tags = new CardTags(new Dictionary<string, HashSet<string>>
        {
            ["duet"] = ["music", "pair"], ["piano"] = ["music"], ["twins"] = ["pair"],
            ["again"] = ["pair"], ["boat"] = ["sail"], ["rose"] = ["flower"],
        });
        var view = View(new HintGroupView(1, ["duet"]), new HintGroupView(2, ["again"])) with
        {
            Board = [new(Category.Motive, ["piano", "boat"]), new(Category.Method, ["twins", "rose"])],
        };
        var theory = BotPlayer.Theory(view, tags, Mind())!;
        var duet = theory.Explanations[0];
        new[] { duet.Card, duet.SecondCard }.Should().BeEquivalentTo(new[] { "piano", "twins" });
        theory.Explanations[1].Should().Match<HintExplanation>(e => e.Card == "twins" && e.Kind == "confirm" && e.SecondCard == null);
    }

    [Fact]
    public void VoteKeepsAClueExplainedWhenMarginalizingWouldLoseIt()
    {
        var tags = new CardTags(new Dictionary<string, HashSet<string>>
        {
            ["bells"] = ["music"], ["song"] = ["music"], ["piano"] = ["music"], ["flute"] = ["music"],
            ["oven"] = ["cook", "electric", "kitchen"], ["pot"] = ["cook", "container", "liquid"],
        });
        var view = View(new HintGroupView(1, ["bells"]), new HintGroupView(2, ["song"]), new HintGroupView(3, ["pot"])) with
        {
            Board = [new(Category.Motive, ["piano", "a", "b", "c", "d"]), new(Category.Place, ["flute", "oven", "e", "f", "g"])],
        };
        var mind = Mind();
        using var _ = BotTuning.Use(BotTuning.Default with { TheoryMulti = 0 });
        var theory = BotPlayer.Theory(view, tags, mind)!;
        theory.Truth["flute"].Should().BeGreaterThan(theory.Truth["oven"]);
        theory.Best.Should().Equal("piano", "oven");
        BotPlayer.Plan(view, tags, mind, new Random(1)).Should().Equal(0, 1);
    }

    [Fact]
    public void PairOfHints_PointTogether_AndEveryHintExplained()
    {
        var view = View(new HintGroupView(1, ["mitten", "boots", "tv"]));
        var theory = Theory(BotTuning.Default, view, Mind());
        theory.Best.Should().Equal("sweater", theory.Best[1], "treadmill");
        theory.Explanations.Where(e => e.Hint is "boots" or "tv").Should().OnlyContain(e => e.Card == "treadmill");
        theory.Unexplained.Should().BeEmpty();
    }

    [Fact]
    public void Intent_TrustedCheckConfirmed_LiftsItsTarget()
    {
        var view = View(new HintGroupView(1, ["brooch"]));
        var mine = Mind(new ChatOpinion(Me, "tiara", .5, IsCheck: true, SourceCard: "brooch") { Round = 1 });
        var without = Theory(BotTuning.Default with { TheoryIntent = 0 }, view, mine);
        var with = Theory(BotTuning.Default, view, mine);
        with.Truth["tiara"].Should().BeGreaterThan(without.Truth["tiara"]);
        with.Explanations.Single().Should().Match<HintExplanation>(e => e.Card == "tiara" && e.Kind == "intent");
    }
}
