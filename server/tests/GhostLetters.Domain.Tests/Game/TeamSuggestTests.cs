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
