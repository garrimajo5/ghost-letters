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

        command.CardIds.Should().OnlyContain(c => c == "sword" || c == "tulip");
    }
}
