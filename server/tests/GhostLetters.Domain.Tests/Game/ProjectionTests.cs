using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;

namespace GhostLetters.Domain.Tests.Game;

/// <summary>Тайная информация не должна уходить тем, кому её знать не положено.</summary>
public class ProjectionTests
{
    private static GameState TenPlayersInRound1(ImitatorMode imitator = ImitatorMode.None)
    {
        var settings = new GameSettings { Roles = new RoleOptions(Imitator: imitator) };
        var state = TestGame.Create(players: 10, settings: settings);
        state.ToRound1();
        return state;
    }

    [Fact]
    public void Detective_SeesOnlyOwnRoleAndGhost_NoTruth_NoForeignHands()
    {
        var state = TenPlayersInRound1();
        var detective = state.WithRole(Role.Detective);

        var view = GameProjection.For(state, detective.Id);

        view.Me!.Role.Should().Be(Role.Detective);
        view.Me.Hand.Should().Equal(detective.Hand);
        view.Truth.Should().BeNull();
        view.Players.Where(p => p.KnownRole is not null).Select(p => p.Id)
            .Should().BeEquivalentTo(new[] { detective.Id, state.Ghost.Id });
        view.Players.Should().OnlyContain(p => p.HandCount == 5);
    }

    [Fact]
    public void Ghost_SeesAllRolesAndTruth_ImitatorLooksLikeDetective()
    {
        var state = TenPlayersInRound1(ImitatorMode.ReplaceDetective);
        var imitator = state.WithRole(Role.Imitator);

        var view = GameProjection.For(state, state.Ghost.Id);

        view.Truth.Should().Equal(state.Truth!);
        view.Players.Should().OnlyContain(p => p.KnownRole != null);
        view.Players.Single(p => p.Id == imitator.Id).KnownRole.Should().Be(Role.Detective);
    }

    [Fact]
    public void Killer_SeesAccomplices_AndTruth()
    {
        var state = TenPlayersInRound1();

        var view = GameProjection.For(state, state.WithRole(Role.Killer).Id);

        view.Truth.Should().NotBeNull();
        view.Players.Count(p => p.KnownRole == Role.Accomplice).Should().Be(2);
        view.Players.Should().NotContain(p => p.KnownRole == Role.Witness || p.KnownRole == Role.Expert);
    }

    [Fact]
    public void Witness_SeesKiller_ButNotTruth()
    {
        var state = TenPlayersInRound1();

        var view = GameProjection.For(state, state.WithRole(Role.Witness).Id);

        view.Truth.Should().BeNull();
        view.Players.Single(p => p.Id == state.WithRole(Role.Killer).Id).KnownRole.Should().Be(Role.Killer);
        view.Players.Should().NotContain(p => p.KnownRole == Role.Accomplice);
    }

    [Fact]
    public void Expert_SeesTruth_ButNoRoles()
    {
        var state = TenPlayersInRound1();
        var expert = state.WithRole(Role.Expert);

        var view = GameProjection.For(state, expert.Id);

        view.Truth.Should().NotBeNull();
        view.Players.Count(p => p.KnownRole is not null).Should().Be(2);
    }

    [Fact]
    public void Imitator_IsUnknownToKillerTeam()
    {
        var state = TenPlayersInRound1(ImitatorMode.ReplaceDetective);
        var imitator = state.WithRole(Role.Imitator);

        foreach (var role in new[] { Role.Killer, Role.Accomplice, Role.Witness })
        {
            var view = GameProjection.For(state, state.WithRole(role).Id);
            view.Players.Single(p => p.Id == imitator.Id).KnownRole.Should().BeNull();
        }
    }

    [Fact]
    public void Mailbox_VisibleOnlyToGhost_DuringPick()
    {
        var state = TenPlayersInRound1();
        state.SendAll();

        GameProjection.For(state, state.Ghost.Id).MailboxForGhost.Should().HaveCount(10);
        GameProjection.For(state, state.WithRole(Role.Killer).Id).MailboxForGhost.Should().BeNull();
        GameProjection.For(state, state.WithRole(Role.Detective).Id).MailboxCount.Should().Be(10);
    }

    [Fact]
    public void TableScreen_HasNoSecrets()
    {
        var state = TenPlayersInRound1();

        var view = GameProjection.For(state, null);

        view.Me.Should().BeNull();
        view.Truth.Should().BeNull();
        view.AllowedCommands.Should().BeEmpty();
        view.Players.Where(p => p.KnownRole is not null).Should().ContainSingle().Which.IsGhost.Should().BeTrue();
    }

    [Fact]
    public void AllowedCommands_FollowPhaseAndRole()
    {
        var state = TestGame.Create(players: 5);
        var detective = state.WithRole(Role.Detective);
        GameProjection.For(state, detective.Id).AllowedCommands.Should().Equal(nameof(AckRole));

        state.AckAll();
        GameProjection.For(state, detective.Id).AllowedCommands.Should().BeEmpty();
        GameProjection.For(state, state.WithRole(Role.Killer).Id).AllowedCommands.Should().Equal(nameof(ChooseTruth));

        state.ToFirstClueFromNight();
        GameProjection.For(state, state.Ghost.Id).AllowedCommands.Should().Equal(nameof(GiveFirstClue));
    }

    [Fact]
    public void MyLetters_ShowRevealOutcomeOnlyAfterGhostDecided()
    {
        var state = TestGame.Create(players: 4);
        state.ToRound1();
        var detective = state.WithRole(Role.Detective);
        state.SendAll(first: detective);

        GameProjection.For(state, detective.Id).Me!.Letters.Should().ContainSingle().Which.Revealed.Should().BeNull();

        var mine = state.Letters.Single(l => l.From == detective.Id).CardId;
        state.Run(state.Ghost, new RevealHints([mine]));

        GameProjection.For(state, detective.Id).Me!.Letters.Single().Revealed.Should().BeTrue();
    }
}

internal static class ProjectionTestExtensions
{
    public static void ToFirstClueFromNight(this GameState state) =>
        state.Run(GameEngine.TruthChooser(state), new ChooseTruth(Enumerable.Repeat(0, state.Board.Count).ToList()));
}
