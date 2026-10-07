using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;

namespace GhostLetters.Domain.Tests.Game;

public class SetupTests
{
    [Fact]
    public void Create_DealsRolesByTable_AndLaysOutBoard()
    {
        var state = TestGame.Create(players: 7);

        state.Phase.Should().Be(Phase.RoleReveal);
        state.TotalRounds.Should().Be(4);
        state.Players.Select(p => p.Role).Should().BeEquivalentTo(RoleTable.Compose(7));
        state.Board.Select(r => r.Category).Should().Equal(Category.Motive, Category.Place, Category.Method, Category.Secret);
        state.Board.Should().OnlyContain(r => r.Cards.Count == 5);
        state.Board.SelectMany(r => r.Cards).Should().OnlyHaveUniqueItems();
        state.Deck.Should().HaveCount(300 - 20);
        state.Players.Should().OnlyContain(p => p.Hand.Count == 0);
        state.Truth.Should().BeNull();
    }

    [Fact]
    public void Create_WithoutSecretRow_HasThreeRows()
    {
        var state = TestGame.Create(settings: new GameSettings { UseSecretRow = false, Columns = 6 });

        state.Board.Select(r => r.Category).Should().Equal(Category.Motive, Category.Place, Category.Method);
        state.Board.Should().OnlyContain(r => r.Cards.Count == 6);
    }

    [Fact]
    public void Create_SameSeed_IsDeterministic()
    {
        var a = TestGame.Create(seed: 7);
        var b = TestGame.Create(seed: 7);
        var c = TestGame.Create(seed: 8);

        b.Board.SelectMany(r => r.Cards).Should().Equal(a.Board.SelectMany(r => r.Cards));
        b.Players.Select(p => p.Role).Should().Equal(a.Players.Select(p => p.Role));
        c.Board.SelectMany(r => r.Cards).Should().NotEqual(a.Board.SelectMany(r => r.Cards));
    }

    [Fact]
    public void Create_RoundsOverride_IsUsed()
    {
        TestGame.Create(settings: new GameSettings { Rounds = 2 }).TotalRounds.Should().Be(2);
    }

    [Fact]
    public void Create_TooSmallDeck_Throws()
    {
        var act = () => TestGame.Create(players: 7, deck: TestGame.Deck.Take(30).ToList());

        act.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.Validation);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    public void Create_InvalidColumns_Throws(int columns)
    {
        var act = () => TestGame.Create(settings: new GameSettings { Columns = columns });

        act.Should().Throw<GameRuleException>();
    }

    [Fact]
    public void RoleReveal_NightStartsAfterEveryoneAcknowledged()
    {
        var state = TestGame.Create(players: 4);

        foreach (var p in state.Players.Take(3))
        {
            state.Run(p, new AckRole());
        }

        state.Phase.Should().Be(Phase.RoleReveal);
        state.Run(state.Players[3], new AckRole());
        state.Phase.Should().Be(Phase.Night);
    }

    [Fact]
    public void Night_OnlyKillerChoosesTruth_ThenHandsAreDealt()
    {
        var state = TestGame.Create(players: 7);
        state.AckAll();
        var columns = new List<int> { 1, 2, 3, 4 };

        var byDetective = () => state.Run(state.WithRole(Role.Detective), new ChooseTruth(columns));
        byDetective.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.NotAllowed);

        state.Run(state.WithRole(Role.Killer), new ChooseTruth(columns));

        state.Truth.Should().Equal(columns);
        state.Phase.Should().Be(Phase.FirstClue);
        state.Players.Should().OnlyContain(p => p.Hand.Count == 5);
        state.Players.SelectMany(p => p.Hand).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Night_InCooperative_GhostChoosesTruth()
    {
        var state = TestGame.Create(players: 3);
        state.AckAll();

        GameEngine.TruthChooser(state).Should().BeSameAs(state.Ghost);
        state.Run(state.Ghost, new ChooseTruth([0, 0, 0, 0]));

        state.Phase.Should().Be(Phase.FirstClue);
    }

    [Theory]
    [InlineData(new[] { 0, 0, 0 })]
    [InlineData(new[] { 0, 0, 0, 5 })]
    [InlineData(new[] { -1, 0, 0, 0 })]
    public void Night_InvalidTruth_Throws(int[] columns)
    {
        var state = TestGame.Create();
        state.AckAll();

        var act = () => state.Run(state.WithRole(Role.Killer), new ChooseTruth(columns));

        act.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.Validation);
        state.Phase.Should().Be(Phase.Night);
    }

    [Fact]
    public void FirstClue_GhostPlaysCard_AndRefills()
    {
        var state = TestGame.Create();
        state.ToFirstClue();
        var card = state.Ghost.Hand[2];

        state.Run(state.Ghost, new GiveFirstClue(card));

        state.Hints.Should().ContainSingle(h => h.Round == 0).Which.Cards.Should().Equal(card);
        state.Ghost.Hand.Should().HaveCount(5).And.NotContain(card);
        state.Phase.Should().Be(Phase.Mailbox);
        state.Round.Should().Be(1);
    }

    [Fact]
    public void FirstClue_OnlyGhost()
    {
        var state = TestGame.Create();
        state.ToFirstClue();
        var detective = state.WithRole(Role.Detective);

        var act = () => state.Run(detective, new GiveFirstClue(detective.Hand[0]));

        act.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.NotAllowed);
    }

    [Fact]
    public void Command_InWrongPhase_Throws()
    {
        var state = TestGame.Create();

        var act = () => state.Run(state.Ghost, new RevealHints([]));

        act.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.PhaseMismatch);
    }

    [Fact]
    public void Command_FromStranger_Throws()
    {
        var state = TestGame.Create();

        var act = () => GameEngine.Execute(state, Guid.NewGuid(), new AckRole());

        act.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.UnknownPlayer);
    }

    [Fact]
    public void Version_IncrementsOnEveryCommand()
    {
        var state = TestGame.Create(players: 4);

        state.AckAll();

        state.Version.Should().Be(4);
    }
}
