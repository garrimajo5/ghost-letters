using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>
/// Правки баланса (docs/BOT_WINRATE_PLAN.md): Призрак примеряет письма на стол, мирные шлют письма-проверки
/// и читают чужие исчезнувшие письма, команда Убийцы не спорит с ясными рядами. Скрытую истину читает только тот,
/// кому она положена по роли.
/// </summary>
public sealed class BotWinrateTests
{
    private static readonly Guid Ghost = Guid.NewGuid(), Me = Guid.NewGuid(), Ann = Guid.NewGuid(), Bob = Guid.NewGuid();

    // Ряд 0: кольцо | кошелёк | лодка; ряд 1: меч | кот | роза.
    private static readonly CardTags Tags = new(new Dictionary<string, HashSet<string>>
    {
        ["ring"] = ["ring", "gold", "jewel"], ["wallet"] = ["wallet", "money", "leather"], ["boat"] = ["boat", "water"],
        ["sword"] = ["sword", "steel", "weapon"], ["cat"] = ["cat", "animal", "fur"], ["rose"] = ["rose", "flower", "red"],
        // Письма: однозначные (по одной карте) и размытое.
        ["crown"] = ["gold", "jewel", "king"], ["purse"] = ["money", "leather", "bag"], ["dagger"] = ["steel", "weapon", "blade"],
        ["tulip"] = ["flower", "red", "spring"], ["blur"] = ["gold", "money", "steel", "flower"], ["stone"] = ["stone"],
    });

    private static BotMind Mind(BotPersonality? p = null, IReadOnlyList<ChatOpinion>? opinions = null) =>
        new(p ?? BotPersonality.Default, new Dictionary<Guid, PlayerHistory>(), opinions ?? [],
            new Dictionary<Guid, string> { [Ghost] = "Призрак", [Me] = "Я", [Ann] = "Аня", [Bob] = "Боб" });

    private static PlayerView View(Role role, string[] allowed, string[] hand, IReadOnlyList<int>? truth = null,
        IReadOnlyList<string>? mailbox = null, IReadOnlyList<HintGroupView>? hints = null, Phase phase = Phase.Mailbox,
        FinaleView? finale = null, IReadOnlyList<MyLetterView>? letters = null) => new(
        Guid.NewGuid(), 1, phase, 1, 4, DiscussionMode.FreeChat,
        [new BoardRowView(Category.Motive, ["ring", "wallet", "boat"]), new BoardRowView(Category.Method, ["sword", "cat", "rose"])],
        hints ?? [], 0,
        [
            new PlayerInfoView(Ghost, 0, true, Role.Ghost, false, 5),
            new PlayerInfoView(Me, 1, false, role, false, 5),
            new PlayerInfoView(Ann, 2, false, null, false, 5),
            new PlayerInfoView(Bob, 3, false, null, false, 5),
        ],
        new MeView(Me, role, hand, letters ?? []), truth, 0, mailbox, null, null, null, [], allowed, finale);

    [Fact]
    public void Tuning_ParsesOverDefaults()
    {
        var t = BotTuning.Parse("FocusBase=0, GhostPlans=false, ProbePlausible=1.5");
        t.FocusBase.Should().Be(0);
        t.GhostPlans.Should().BeFalse();
        t.ProbePlausible.Should().Be(1.5);
        t.FocusStrict.Should().Be(BotTuning.Default.FocusStrict);
        FluentActions.Invoking(() => BotTuning.Parse("Nope=1")).Should().Throw<FormatException>();
        using (BotTuning.Use(t)) BotTuning.Current.Should().Be(t);
        BotTuning.Current.Should().Be(BotTuning.Default);
    }

    [Fact]
    public void Ghost_RevealsLettersThatSharpenTheTruth_HidesThoseThatPointElsewhere()
    {
        // Истина: кошелёк и меч. Кошелёк и кинжал указывают на истину; корона — на ложное кольцо.
        var view = View(Role.Ghost, [nameof(RevealHints)], [], truth: [1, 0], mailbox: ["purse", "dagger", "crown", "stone"], phase: Phase.GhostPick);
        var reveal = (RevealHints)BotPlayer.Decide(view, new Random(1), Tags, Mind())!;
        reveal.CardIds.Should().BeEquivalentTo(["purse", "dagger"]);
    }

    [Fact]
    public void Detective_SendsAnUnambiguousProbe_NotABlurryLetter()
    {
        var view = View(Role.Detective, [nameof(SendLetter)], ["blur", "crown", "stone"]);
        var letter = (SendLetter)BotPlayer.Decide(view, new Random(1), Tags, Mind())!;
        letter.CardIds.Should().Equal("crown");
    }

    [Fact]
    public void Detectives_SplitCloseProbesBetweenSeats()
    {
        // Корона и кинжал — равноценные проверки; игроки на соседних местах берут разные.
        PlayerView ForSeat(Guid id, int seat)
        {
            var v = View(Role.Detective, [nameof(SendLetter)], ["crown", "dagger"]);
            return v with
            {
                Players = v.Players.Select(p => p.Id == id ? p with { Seat = 1 } : p.Id == Me ? p with { Seat = seat } : p).ToList(),
            };
        }

        var first = ((SendLetter)BotPlayer.Decide(ForSeat(Ann, 1), new Random(1), Tags, Mind())!).CardIds.Single();
        var second = ((SendLetter)BotPlayer.Decide(ForSeat(Ann, 2), new Random(1), Tags, Mind())!).CardIds.Single();
        new[] { first, second }.Should().BeEquivalentTo(["crown", "dagger"]);
    }

    [Fact]
    public void OthersVanishedLetter_WeakensTheCardItChecked()
    {
        // Аня сказала: «кидал корону — проверял кольцо», короны среди открытых писем нет — значит, исчезла.
        var view = View(Role.Detective, [], ["stone"], phase: Phase.Discussion);
        var claim = new ChatOpinion(Ann, "ring", .5, IsCheck: true, SourceCard: "crown");
        var plain = BotPlayer.Evidence(view, Tags, Mind(), "ring");
        BotPlayer.Evidence(view, Tags, Mind(opinions: [claim]), "ring").Should().BeLessThan(plain);
        BotPlayer.Evidence(view, Tags, Mind(opinions: [claim]), "wallet").Should().Be(BotPlayer.Evidence(view, Tags, Mind(), "wallet"));

        // Если корону открыли, это не исчезнувшее письмо.
        var shown = view with { Hints = [new HintGroupView(1, ["crown"])] };
        BotPlayer.Evidence(shown, Tags, Mind(opinions: [claim]), "ring").Should().Be(BotPlayer.Evidence(shown, Tags, Mind(), "ring"));
        using (BotTuning.Use(BotTuning.Default with { OthersVanished = 0 }))
            BotPlayer.Evidence(view, Tags, Mind(opinions: [claim]), "ring").Should().Be(plain);
    }

    [Fact]
    public void Killer_UsuallyVotesWithTheTableOnAClearRow()
    {
        // Открыт кошелёк — ряд мотива ясен: кошелёк истинен. Голос против выдал бы Убийцу и ничего не изменил.
        var stage = new VoteStageView(0, VoteStageKind.Row, 0, 1, [0, 1, 2], []);
        var finale = new FinaleView(stage, 3, null, [], [], [], null, null, null, [], []);
        var view = View(Role.Killer, [nameof(CastVote)], ["stone"], truth: [1, 0], hints: [new HintGroupView(1, ["purse"])],
            phase: Phase.Voting, finale: finale);
        var risky = Mind(BotPersonality.Default with { Risk = 1 });
        var honest = Enumerable.Range(0, 200).Count(s => ((CastVote)BotPlayer.Decide(view, new Random(s), Tags, risky)!).Column == 1);
        honest.Should().BeGreaterThan(120);
        using (BotTuning.Use(BotTuning.Default with { KillerBlend = false }))
            Enumerable.Range(0, 200).Count(s => ((CastVote)BotPlayer.Decide(view, new Random(s), Tags, risky)!).Column == 1)
                .Should().BeLessThan(honest);
    }
}
