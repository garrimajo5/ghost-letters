using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>
/// Строгость ассоциаций: строгий одним письмом проверяет одну карту («выложил деньги — проверил деньги»),
/// широкий — всё, что хоть как-то связано. Призрак слушает, сколько карт стол проверяет, и открывает письма с поправкой на это.
/// </summary>
public sealed class BotStrictnessTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Ghost = Guid.NewGuid();
    private static readonly Guid Ann = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    // Монета похожа на кольцо сильнее всего (золото, металл), слабее — на кошелёк (деньги) и меч (металл), на лодку — никак.
    private static readonly CardTags Tags = new(new Dictionary<string, HashSet<string>>
    {
        ["coin"] = ["coin", "money", "metal", "gold"],
        ["ring"] = ["ring", "gold", "metal"],
        ["wallet"] = ["wallet", "money", "leather"],
        ["sword"] = ["sword", "metal", "weapon"],
        ["boat"] = ["boat", "water", "travel"],
        ["cat"] = ["cat", "animal"],
    });

    private static readonly Dictionary<Guid, string> Names = new() { [Ann] = "Аня", [Bob] = "Боб", [Ghost] = "Лена", [Me] = "Я" };

    private static BotMind Mind(BotPersonality p, IReadOnlyDictionary<Guid, double>? breadth = null) =>
        new(p, new Dictionary<Guid, PlayerHistory>(), [], Names, null, breadth);

    // Ряд 0: ring | wallet; ряд 1: sword | boat.
    private static PlayerView View(Phase phase, string[] allowed, Role role, IReadOnlyList<MyLetterView>? letters = null,
        IReadOnlyList<int>? truth = null, IReadOnlyList<string>? mailbox = null, IReadOnlyList<HintGroupView>? hints = null) => new(
        Guid.NewGuid(), 1, phase, 2, 3, DiscussionMode.FreeChat,
        [new BoardRowView(Category.Motive, ["ring", "wallet"]), new BoardRowView(Category.Place, ["sword", "boat"])],
        hints ?? [], 0,
        [
            new PlayerInfoView(Ghost, 0, true, Role.Ghost, false, 5),
            new PlayerInfoView(Me, 1, false, role, false, 5),
            new PlayerInfoView(Ann, 2, false, null, false, 5),
            new PlayerInfoView(Bob, 3, false, null, false, 5),
        ],
        new MeView(Me, role, ["coin", "cat"], letters ?? []),
        truth, 0, mailbox, null, null, null, [], allowed, null);

    private static int CheckedCount(BotPersonality p)
    {
        var view = View(Phase.Discussion, [], Role.Detective, letters: [new MyLetterView(2, "coin", true)]);
        var said = BotPlayer.Say(view, new Random(3), Tags, Mind(p));
        said.Should().NotBeNull();
        return said!.Value.Notes.Count(n => n.StartsWith("проверял эту", StringComparison.Ordinal));
    }

    [Fact]
    public void Breadth_StrictOne_LooseUpToFour_MiddleThreeAsBefore()
    {
        new BotPersonality { Strictness = 1 }.CheckBreadth.Should().Be(1);
        new BotPersonality { Strictness = 0.5 }.CheckBreadth.Should().Be(3);
        new BotPersonality { Strictness = 0 }.CheckBreadth.Should().Be(4);
    }

    [Fact]
    public void Discussion_StrictNamesOneCheckedCard_LooseNamesEverythingRelated()
    {
        CheckedCount(new BotPersonality { Strictness = 1 }).Should().Be(1, "монетой проверял только кольцо");
        CheckedCount(new BotPersonality { Strictness = 0 }).Should().Be(3, "кольцо, кошелёк и меч — всё, что хоть как-то связано");
    }

    [Fact]
    public void Ghost_ListensHowStrictTheTableIs()
    {
        // Истина: кошелёк и лодка. Монета на кошелёк похожа, но ещё сильнее — на кольцо.
        var view = View(Phase.GhostPick, [nameof(RevealHints)], Role.Ghost, truth: [1, 1], mailbox: ["coin"]);
        var strictTable = new Dictionary<Guid, double> { [Ann] = 1, [Bob] = 1 };
        var looseTable = new Dictionary<Guid, double> { [Ann] = 3.5, [Bob] = 4 };

        var forStrict = (RevealHints)BotPlayer.Decide(view, new Random(1), Tags, Mind(BotPersonality.Default, strictTable))!;
        var forLoose = (RevealHints)BotPlayer.Decide(view, new Random(1), Tags, Mind(BotPersonality.Default, looseTable))!;

        forStrict.CardIds.Should().BeEmpty("строгий стол монетой проверит только кольцо — подсказка его запутает");
        forLoose.CardIds.Should().Equal(["coin"], "широкий стол увидит за монетой и кошелёк");
    }

    [Fact]
    public void ReadingHints_StrictLooksOnlyAtTheClosestCard()
    {
        // Открыта монета. Строгий считает, что она про кольцо, а кошелёк почти не при чём; широкий не исключает кошелёк.
        var hints = new[] { new HintGroupView(1, ["coin"]) };
        var finale = new FinaleView(new VoteStageView(0, VoteStageKind.Row, 0, 1, [0, 1], []), 3, null, [], [], [], null, null, null, [], []);
        var view = View(Phase.Voting, [nameof(CastVote)], Role.Detective, hints: hints) with { Finale = finale };

        var strict = Brainless(view, new BotPersonality { Strictness = 1, Compromise = 0 });
        var loose = Brainless(view, new BotPersonality { Strictness = 0, Compromise = 0 });

        strict.Should().Be(0);
        loose.Should().Be(0, "кольцо всё равно ближе");
        BotPlayer.Evidence(view, Tags, Mind(new BotPersonality { Strictness = 1 }), "wallet")
            .Should().BeLessThan(BotPlayer.Evidence(view, Tags, Mind(new BotPersonality { Strictness = 0 }), "wallet"));
    }

    [Fact]
    public void Letters_StrictPicksALetterThatPointsOnlyAtTheTarget()
    {
        // Цель — кошелёк. Бумажник похож на кошелёк сильнее, но так же и на кольцо; сумочка — только на кошелёк.
        var tags = new CardTags(new Dictionary<string, HashSet<string>>
        {
            ["billfold"] = ["money", "leather", "metal", "gold"],
            ["purse"] = ["purse", "money", "bag"],
            ["ring"] = ["ring", "gold", "metal"],
            ["wallet"] = ["wallet", "money", "leather"],
        });
        var view = View(Phase.Mailbox, [nameof(SendLetter)], Role.Expert, truth: [1]) with
        {
            Me = new MeView(Me, Role.Expert, ["billfold", "purse"], []),
            Board = [new BoardRowView(Category.Motive, ["ring", "wallet"])],
        };

        var strict = (SendLetter)BotPlayer.Decide(view, new Random(4), tags, Mind(new BotPersonality { Strictness = 1 }))!;
        var loose = (SendLetter)BotPlayer.Decide(view, new Random(4), tags, Mind(new BotPersonality { Strictness = 0 }))!;

        strict.CardIds.Should().Equal(["purse"], "строгий: сумочка указывает на кошелёк и ни на что больше");
        loose.CardIds.Should().Equal(["billfold"], "широкому всё равно, что бумажник похож и на кольцо");
    }

    [Fact]
    public void Breadth_FromChat_HowManyCardsEachPlayerChecksPerLetter()
    {
        var breadth = BotService.Breadth(
        [
            (Ann, ["кидал эту", "проверял эту", "проверял эту", "думаю, эта"]),
            (Ann, ["кидал эту", "проверял эту"]),
            (Bob, ["думаю, эта"]),
        ]);

        breadth.Should().ContainKey(Ann).WhoseValue.Should().Be(1.5);
        breadth.Should().NotContainKey(Bob, "Боб не рассказывал о своём письме");
        Mind(BotPersonality.Default, breadth).TableBreadth.Should().Be(1.5);
        Mind(BotPersonality.Default).TableBreadth.Should().Be(3, "никто не рассказывал — как раньше");
    }

    private static int? Brainless(PlayerView view, BotPersonality p) =>
        ((CastVote)BotPlayer.Decide(view, new Random(2), Tags, Mind(p))!).Column;

    [Fact]
    public void EqualSimilarity_StrictReaderAndGhostAgreeOnTheOneCheckedCard()
    {
        var tags = new CardTags(new Dictionary<string, HashSet<string>>
        {
            ["coin"] = ["coin", "money", "silver"],
            ["ring"] = ["money", "ring"],
            ["wallet"] = ["money", "wallet"],
            ["boat"] = ["water"],
        });
        var personality = new BotPersonality { Strictness = 1, Meaning = 1, Shape = 0, Color = 0 };
        var reader = View(Phase.Discussion, [], Role.Detective,
            letters: [new MyLetterView(2, "coin", true)], hints: [new HintGroupView(2, ["coin"])]);
        var said = BotPlayer.Say(reader, new Random(3), tags, Mind(personality))!.Value;
        var checkedCards = said.Cards.Where((_, i) => said.Notes[i].StartsWith("проверял эту", StringComparison.Ordinal));
        checkedCards.Should().Equal("ring");
        BotPlayer.Evidence(reader, tags, Mind(personality), "ring").Should().BeGreaterThan(0);
        BotPlayer.Evidence(reader, tags, Mind(personality), "wallet").Should().Be(0);

        // Wallet ties with ring, but lies beyond a one-card reader's actual choice.
        var ghost = View(Phase.GhostPick, [nameof(RevealHints)], Role.Ghost,
            truth: [1, 1], mailbox: ["coin"]);
        var narrow = new Dictionary<Guid, double> { [Ann] = 1 };
        var wide = new Dictionary<Guid, double> { [Ann] = 3 };
        // A cautious Ghost hides a weak clue that points to the wrong tied card.
        var cautious = personality with { Risk = 0, Variability = 0 };
        var forNarrow = (RevealHints)BotPlayer.Decide(ghost, new Random(1), tags, Mind(cautious, narrow))!;
        var forWide = (RevealHints)BotPlayer.Decide(ghost, new Random(1), tags, Mind(cautious, wide))!;
        forNarrow.CardIds.Should().BeEmpty();
        forWide.CardIds.Should().Equal("coin");
    }
}
