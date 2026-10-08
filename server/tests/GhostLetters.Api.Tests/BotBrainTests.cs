using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>Решения ботов по тегам карт — без базы, на собранной вручную проекции.</summary>
public sealed class BotBrainTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Ghost = Guid.NewGuid();
    private static readonly Guid Ann = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static readonly CardTags Tags = new(new Dictionary<string, HashSet<string>>
    {
        ["knife"] = ["knife", "metal", "sharp", "weapon"],
        ["rose"] = ["rose", "flower", "red", "love"],
        ["boat"] = ["boat", "sea", "water", "travel"],
        ["cat"] = ["cat", "animal", "fur"],
        ["sword"] = ["sword", "metal", "sharp", "weapon"],
        ["tulip"] = ["tulip", "flower", "love"],
        ["ship"] = ["ship", "sea", "water", "wood"],
        ["dog"] = ["dog", "animal", "fur"],
    });

    // Ряд 0: knife | rose; ряд 1: boat | cat. Истина — knife и cat.
    private static PlayerView View(
        Phase phase,
        string[] allowed,
        Role role,
        IReadOnlyList<int>? truth = null,
        IReadOnlyList<string>? hand = null,
        IReadOnlyList<HintGroupView>? hints = null,
        IReadOnlyList<string>? mailbox = null,
        FinaleView? finale = null,
        IReadOnlyList<PlayerInfoView>? players = null) => new(
        Guid.NewGuid(), 1, phase, 1, 3, DiscussionMode.FreeChat,
        [new BoardRowView(Category.Motive, ["knife", "rose"]), new BoardRowView(Category.Place, ["boat", "cat"])],
        hints ?? [], 0,
        players ??
        [
            new PlayerInfoView(Ghost, 0, true, Role.Ghost, false, 5),
            new PlayerInfoView(Me, 1, false, role, false, 5),
            new PlayerInfoView(Ann, 2, false, null, false, 5),
            new PlayerInfoView(Bob, 3, false, null, false, 5),
        ],
        new MeView(Me, role, hand ?? [], []),
        truth, mailbox?.Count ?? 0, mailbox, null, null, null, [], allowed, finale);

    private static FinaleView Finale(VoteStageView? stage, IReadOnlyList<VoteRecordView>? votes = null, IReadOnlyList<VoteOutcomeView>? outcomes = null) =>
        new(stage, 3, null, votes ?? [], outcomes ?? [], [], null, null, null, [], []);

    [Fact]
    public void Similarity_SharedTags()
    {
        Tags.Similarity("knife", "sword").Should().BeGreaterThan(0.5);
        Tags.Similarity("knife", "rose").Should().Be(0);
        Tags.Similarity("knife", "knife").Should().Be(1);
        Tags.Similarity("knife", "unknown").Should().Be(0);
    }

    [Fact]
    public void Ghost_RevealsLettersThatPointToTruth()
    {
        var view = View(Phase.GhostPick, [nameof(RevealHints)], Role.Ghost, truth: [0, 1], mailbox: ["sword", "tulip", "dog", "ship"]);

        var command = (RevealHints)BotPlayer.Decide(view, new Random(1), Tags)!;

        command.CardIds.Should().BeEquivalentTo(["sword", "dog"]);
    }

    [Fact]
    public void Detective_VotesForCardTheHintsPointTo()
    {
        var stage = new VoteStageView(0, VoteStageKind.Row, 0, 1, [0, 1], []);
        var view = View(Phase.Voting, [nameof(CastVote)], Role.Detective,
            hints: [new HintGroupView(1, ["tulip"])], finale: Finale(stage));

        var command = (CastVote)BotPlayer.Decide(view, new Random(2), Tags)!;

        command.Column.Should().Be(1, "подсказка-тюльпан указывает на розу");
    }

    [Fact]
    public void Killer_VotesAgainstTheTruth()
    {
        var stage = new VoteStageView(1, VoteStageKind.Row, 1, 1, [0, 1], []);
        var view = View(Phase.Voting, [nameof(CastVote)], Role.Killer, truth: [0, 1],
            hints: [new HintGroupView(1, ["dog"])], finale: Finale(stage));

        var command = (CastVote)BotPlayer.Decide(view, new Random(3), Tags)!;

        command.Column.Should().Be(0, "Убийца уводит голоса от истинной карты");
    }

    [Fact]
    public void Detective_SendsLetterThatTestsABoardCard()
    {
        var view = View(Phase.Mailbox, [nameof(SendLetter)], Role.Detective, hand: ["sword", "tulip", "ship", "dog", "zzz"]);

        var command = (SendLetter)BotPlayer.Decide(view, new Random(4), Tags)!;

        command.CardIds.Should().ContainSingle().Which.Should().NotBe("zzz", "письмо должно быть похоже на какую-то улику на столе");
    }

    [Fact]
    public void Killer_HuntsWhoeverVotedAgainstHim()
    {
        var killerStage = 2;
        var players = new List<PlayerInfoView>
        {
            new(Ghost, 0, true, Role.Ghost, false, 5),
            new(Me, 1, false, Role.Killer, false, 5),
            new(Ann, 2, false, null, false, 5),
            new(Bob, 3, false, null, false, 5),
        };
        var votes = new List<VoteRecordView>
        {
            new(killerStage, 1, Ann, null, Bob),
            new(killerStage, 1, Bob, null, Me),
        };
        var view = View(Phase.Hunt, [nameof(HuntPick)], Role.Killer, truth: [0, 1], players: players,
            finale: Finale(null, votes));

        var command = (HuntPick)BotPlayer.Decide(view, new Random(5), Tags)!;

        command.Target.Should().Be(Bob, "Боб голосовал за Убийцу — похоже, он Свидетель");
    }

    [Fact]
    public void WithoutTags_BotsStillMakeLegalMoves()
    {
        var view = View(Phase.GhostPick, [nameof(RevealHints)], Role.Ghost, truth: [0, 1], mailbox: ["sword", "tulip"]);

        var command = (RevealHints)BotPlayer.Decide(view, new Random(6), CardTags.Empty)!;

        command.CardIds.Except(["sword", "tulip"]).Should().BeEmpty("без тегов — любые из присланных, от нуля до всех");
    }

    [Fact]
    public void Detective_SaysWhichCardTheHintsPointTo()
    {
        var view = View(Phase.Discussion, [nameof(ReadyNextRound)], Role.Detective, hints: [new HintGroupView(1, ["tulip"])]);

        var line = BotPlayer.Say(view, new Random(7), Tags)!.Value;

        line.Cards.Should().Equal("rose");
        line.Text.Should().Contain("2");
    }

    [Fact]
    public void Killer_TalksUpAFakeCard_GhostKeepsSilent()
    {
        var killer = View(Phase.Discussion, [nameof(ReadyNextRound)], Role.Killer, truth: [0, 1]);
        var ghost = View(Phase.Discussion, [], Role.Ghost, truth: [0, 1]);

        for (var seed = 0; seed < 20; seed++)
        {
            var line = BotPlayer.Say(killer, new Random(seed), Tags)!.Value;
            line.Cards.Should().ContainSingle();
            new[] { "knife", "cat" }.Should().NotContain(line.Cards[0], "Убийца хвалит ложную карту");
        }

        BotPlayer.Say(ghost, new Random(8), Tags).Should().BeNull();
    }

    [Fact]
    public void Awards_BotNeverNominatesOrVotesForItself()
    {
        var nominate = View(Phase.AwardNomination, [nameof(Nominate)], Role.Detective);
        var votes = Finale(null) with
        {
            Awards =
            [
                new AwardEntryView(0, "sherlock", Me, 1, false, null, false),
                new AwardEntryView(1, "best_liar", Ann, 1, true, null, false),
                new AwardEntryView(2, "steel_balls", Bob, 1, false, null, false),
            ],
        };
        var vote = View(Phase.AwardVoting, [nameof(AwardVote)], Role.Detective, finale: votes);

        for (var seed = 0; seed < 30; seed++)
        {
            var n = (Nominate)BotPlayer.Decide(nominate, new Random(seed), Tags)!;
            n.Nominee.Should().NotBe(Me);
            var v = (AwardVote)BotPlayer.Decide(vote, new Random(seed), Tags)!;
            v.Entry.Should().Be(2, "своё выдвижение и выдвижение себя не в счёт");
        }
    }

    [Fact]
    public void Detective_SuspectsWhoVotedAgainstTheHints()
    {
        // Ряд 0 уже решён; подсказка-тюльпан указывает на розу (столбец 1). Аня голосовала за нож, Боб — за розу.
        var votes = new List<VoteRecordView>
        {
            new(0, 1, Ann, 0, null),
            new(0, 1, Bob, 1, null),
        };
        var outcomes = new List<VoteOutcomeView> { new(0, VoteStageKind.Row, 0, 1, null, null, false, null) };
        var stage = new VoteStageView(2, VoteStageKind.Killer, -1, 1, [], [Ann, Bob]);
        var view = View(Phase.Voting, [nameof(CastVote)], Role.Detective,
            hints: [new HintGroupView(1, ["tulip"])], finale: Finale(stage, votes, outcomes));

        var suspects = Enumerable.Range(0, 20).Select(seed => ((CastVote)BotPlayer.Decide(view, new Random(seed), Tags)!).Suspect).ToList();

        suspects.Should().OnlyContain(s => s == Ann, "Аня голосовала против подсказок");
    }

    [Fact]
    public void Bots_TellWhichLetterTheySent_LiarsSometimesClaimSomeoneElses()
    {
        var hints = new List<HintGroupView> { new(1, ["sword", "dog"]) };
        PlayerView WithLetter(Role role, bool? revealed) => View(Phase.Discussion, [nameof(ReadyNextRound)], role, truth: role == Role.Killer ? new[] { 0, 1 } : null, hints: hints) with
        {
            Me = new MeView(Me, role, [], [new MyLetterView(1, revealed == true ? "sword" : "tulip", revealed)]),
        };

        var honest = BotPlayer.Say(WithLetter(Role.Detective, false), new Random(1), Tags)!.Value;
        honest.Cards[0].Should().Be("tulip", "детектив честно показывает своё исчезнувшее письмо");
        honest.Text.Should().Contain("исчезла");

        var lies = Enumerable.Range(0, 30)
            .Select(seed => BotPlayer.Say(WithLetter(Role.Killer, false), new Random(seed), Tags)!.Value)
            .Count(l => l.Cards[0] is "sword" or "dog");
        lies.Should().BeGreaterThan(5, "Убийца иногда выдаёт чужую открытую подсказку за своё письмо");
    }

    [Fact]
    public void Ghost_RevealsNothing_WhenNoLetterLooksLikeTruth()
    {
        var view = View(Phase.GhostPick, [nameof(RevealHints)], Role.Ghost, truth: [0, 1], mailbox: ["tulip", "ship"]);

        var command = (RevealHints)BotPlayer.Decide(view, new Random(9), Tags)!;

        command.CardIds.Should().BeEmpty();
    }

    [Fact]
    public void Bot_LabelsLetterAndTheBoardCardsItChecked()
    {
        var view = View(Phase.Discussion, [nameof(ReadyNextRound)], Role.Detective) with
        {
            Me = new MeView(Me, Role.Detective, [], [new MyLetterView(1, "sword", false)]),
        };

        var line = BotPlayer.Say(view, new Random(3), Tags)!.Value;

        line.Cards[0].Should().Be("sword");
        line.Notes[0].Should().Be("кидал эту");
        line.Notes.Should().HaveSameCount(line.Cards);
        line.Cards[line.Notes.ToList().IndexOf("проверял эту")].Should().Be("knife", "меч похож на нож на поле");
        line.Text.Should().Contain("Проверял: мотив 1");
    }

    [Fact]
    public void Similarity_ColorAndShapeLinkCards_ButWeakerThanMeaning()
    {
        var tags = new CardTags(new Dictionary<string, HashSet<string>>
        {
            ["apple"] = ["apple", "fruit", "sweet", "red", "shape-round"],
            ["ball"] = ["ball", "toy", "red", "shape-round"],
            ["pear"] = ["pear", "fruit", "sweet", "green", "shape-tall"],
            ["brick"] = ["brick", "stone", "gray", "shape-compact"],
        });

        tags.Similarity("apple", "ball").Should().BeGreaterThan(0, "красное и круглое");
        tags.Similarity("apple", "brick").Should().Be(0);
        tags.Similarity("apple", "pear").Should().BeGreaterThan(tags.Similarity("apple", "ball"), "общий смысл весит больше цвета и формы");
    }
}
