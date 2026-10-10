using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;

namespace GhostLetters.Domain.Tests.Game;

/// <summary>Сообщники подсказывают Убийце: ночью — истинные улики, в финале — кого искать. Решает Убийца.</summary>
public class TeamSuggestTests
{
    [Fact]
    public void Night_AccompliceSuggests_KillerSeesIt_OthersDoNot()
    {
        var state = TestGame.Create(players: 7);
        state.AckAll();
        var accomplice = state.WithRole(Role.Accomplice);
        var killer = state.WithRole(Role.Killer);

        GameProjection.For(state, accomplice.Id).AllowedCommands.Should().Contain(nameof(TeamSuggest));
        state.Run(accomplice, new TeamSuggest(Columns: [1, 2, 3, 0]));

        var killerView = GameProjection.For(state, killer.Id);
        killerView.TeamSuggestions.Should().ContainSingle().Which.Columns.Should().Equal(1, 2, 3, 0);
        GameProjection.For(state, accomplice.Id).TeamSuggestions.Should().ContainSingle();
        GameProjection.For(state, state.WithRole(Role.Detective).Id).TeamSuggestions.Should().BeNull();
        GameProjection.For(state, state.Ghost.Id).TeamSuggestions.Should().BeNull();
        state.Phase.Should().Be(Phase.Night, "решает Убийца");
    }

    [Fact]
    public void Night_OnlyAccomplicesSuggest_AndOnePerRow()
    {
        var state = TestGame.Create(players: 7);
        state.AckAll();

        var byDetective = () => state.Run(state.WithRole(Role.Detective), new TeamSuggest(Columns: [0, 0, 0, 0]));
        byDetective.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.NotAllowed);
        var tooFew = () => state.Run(state.WithRole(Role.Accomplice), new TeamSuggest(Columns: [0]));
        tooFew.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.Validation);
    }

    [Fact]
    public void Night_KillerTimesOut_SuggestionBecomesTruth_AndSuggestionsReset()
    {
        var state = TestGame.Create(players: 7);
        state.AckAll();
        state.Run(state.WithRole(Role.Accomplice), new TeamSuggest(Columns: [2, 2, 2, 2]));

        GameEngine.Timeout(state);

        state.Truth.Should().Equal(2, 2, 2, 2);
        state.TeamSuggestions.Should().BeEmpty();
    }

    [Fact]
    public void Hunt_AccompliceSuggestsWitness_KillerSeesIt_AndDecides()
    {
        var state = FinaleGame.ToVoting(players: 7);
        state.VoteAllRows(0);
        state.VoteKiller(state.WithRole(Role.Detective));
        state.Phase.Should().Be(Phase.Hunt);
        var accomplice = state.WithRole(Role.Accomplice);
        var witness = state.WithRole(Role.Witness);

        GameProjection.For(state, accomplice.Id).AllowedCommands.Should().Contain(nameof(TeamSuggest));
        GameProjection.For(state, accomplice.Id).HuntRoles.Should().Equal([Role.Witness], "Эксперта на 7 игроков нет — назвать его нельзя");
        var expert = () => state.Run(accomplice, new TeamSuggest(Target: witness.Id, Guess: Role.Expert));
        expert.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.Validation);
        state.Run(accomplice, new TeamSuggest(Target: witness.Id, Guess: Role.Witness));

        GameProjection.For(state, state.WithRole(Role.Killer).Id).TeamSuggestions.Should().ContainSingle()
            .Which.Target.Should().Be(witness.Id);
        state.Phase.Should().Be(Phase.Hunt);

        state.Run(state.WithRole(Role.Killer), new HuntPick(witness.Id));
        state.Hunt!.Success.Should().BeTrue();
    }

    [Fact]
    public void Hunt_KillerTimesOut_TeamSuggestionDecides()
    {
        var state = FinaleGame.ToVoting(players: 7);
        state.VoteAllRows(0);
        state.VoteKiller(state.WithRole(Role.Detective));
        state.Run(state.WithRole(Role.Accomplice), new TeamSuggest(Target: state.WithRole(Role.Witness).Id));

        GameEngine.Timeout(state);

        state.Hunt!.Success.Should().BeTrue();
        state.Result!.Side.Should().Be(WinningSide.Killer);
    }
}

/// <summary>Сообщник ночью советует и что выбрать, и что не брать.</summary>
public class TeamAvoidTests
{
    [Fact]
    public void Night_AccompliceMarksCardsToAvoid_KillerSeesThem()
    {
        var state = TestGame.Create(players: 7);
        state.AckAll();
        var accomplice = state.WithRole(Role.Accomplice);

        state.Run(accomplice, new TeamSuggest(Avoid: [new BoardCellRef(0, 1), new BoardCellRef(2, 3)]));

        var suggestion = GameProjection.For(state, state.WithRole(Role.Killer).Id).TeamSuggestions.Should().ContainSingle().Subject;
        suggestion.Columns.Should().BeNull();
        suggestion.Avoid.Should().Equal(new BoardCellRef(0, 1), new BoardCellRef(2, 3));

        state.Run(accomplice, new TeamSuggest(Columns: [0, 0, 0, 0], Avoid: [new BoardCellRef(1, 2)]));
        GameProjection.For(state, state.WithRole(Role.Killer).Id).TeamSuggestions!.Single().Columns.Should().Equal(0, 0, 0, 0);
    }

    [Fact]
    public void Night_AvoidValidation()
    {
        var state = TestGame.Create(players: 7);
        state.AckAll();
        var accomplice = state.WithRole(Role.Accomplice);

        var nothing = () => state.Run(accomplice, new TeamSuggest());
        nothing.Should().Throw<GameRuleException>();
        var outside = () => state.Run(accomplice, new TeamSuggest(Avoid: [new BoardCellRef(9, 0)]));
        outside.Should().Throw<GameRuleException>();
        var both = () => state.Run(accomplice, new TeamSuggest(Columns: [1, 1, 1, 1], Avoid: [new BoardCellRef(0, 1)]));
        both.Should().Throw<GameRuleException>();
    }

    [Fact]
    public void Night_KillerTimesOut_AvoidOnlySuggestion_FallsBackToRandom()
    {
        var state = TestGame.Create(players: 7);
        state.AckAll();
        state.Run(state.WithRole(Role.Accomplice), new TeamSuggest(Avoid: [new BoardCellRef(0, 0)]));

        GameEngine.Timeout(state);

        state.Truth.Should().HaveCount(state.Board.Count);
    }
}
