using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;

namespace GhostLetters.Domain.Tests.Game;

public class OzonTests
{
    private static GameState Create(int seed) => TestGame.Create(4,
        new GameSettings { Rounds = 1, Roles = new RoleOptions(RandomKillerOmission: true) }, seed);

    [Fact]
    public void FourPlayers_OmitRandomNonGhostRole_WithoutPubliclyRevealingAbsence()
    {
        var games = Enumerable.Range(0, 64).Select(Create).ToList();
        games.Should().Contain(g => g.HasKiller).And.Contain(g => !g.HasKiller);
        foreach (var game in games)
        {
            game.Players.Should().HaveCount(4);
            game.Players.Count(p => p.Role == Role.Ghost).Should().Be(1);
            game.Players.Count(p => p.Role == Role.Detective).Should().Be(game.HasKiller ? 2 : 3);
            game.Players.Select(p => p.Role).Should().Equal(Create(game.Seed).Players.Select(p => p.Role));
            game.ToRound1();
            var detective = game.WithRole(Role.Detective);
            var view = GameProjection.For(game, detective.Id);
            view.Truth.Should().BeNull();
            view.Players.Where(p => p.Id != detective.Id && !p.IsGhost).Should().OnlyContain(p => p.KnownRole == null);
        }
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void GhostVote_MeansNoKiller_AndCorrectAnswerAllowsOneWrongRow(bool killerPresent, bool voteGhost, bool correct)
    {
        var game = Enumerable.Range(0, 64).Select(Create).First(g => g.HasKiller == killerPresent);
        game.ToRound1();
        game.PlayRound();
        game.Phase.Should().Be(Phase.Voting);
        game.VoteStages.Should().HaveCount(5);
        game.VoteStages.Last().CandidateSuspects.Should().Contain(game.Ghost.Id);
        for (var row = 0; row < 4; row++)
        {
            foreach (var voter in game.Investigators.ToList()) game.Run(voter, new CastVote(row == 0 ? 1 : 0, null));
        }
        var suspect = voteGhost ? game.Ghost.Id : game.WithRole(Role.Killer).Id;
        foreach (var voter in game.Investigators.ToList())
            game.Run(voter, new CastVote(null, voter.Id == suspect ? game.Ghost.Id : suspect));
        game.VoteOutcomes.Last().Correct.Should().Be(correct);
        game.Result.Should().NotBeNull();
        game.Result!.Solved.Should().Be(correct);
        game.Result.CorrectRows.Should().Be(3);
        game.Arrested.Should().NotContain(game.Ghost.Id);
    }

    [Fact]
    public void RandomOmission_IsOnlyForFourPlayers()
    {
        var act = () => RoleTable.Compose(5, new RoleOptions(RandomKillerOmission: true));
        act.Should().Throw<ArgumentException>();
    }
}
