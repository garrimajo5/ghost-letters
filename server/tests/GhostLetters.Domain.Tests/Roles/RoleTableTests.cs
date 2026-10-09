using GhostLetters.Domain.Roles;

namespace GhostLetters.Domain.Tests.Roles;

public class RoleTableTests
{
    // Таблица «Число игроков → Роли» из правил (с. 10).
    [Theory]
    [InlineData(4, 0, false, false, 2)]
    [InlineData(5, 0, false, false, 3)]
    [InlineData(6, 0, false, false, 4)]
    [InlineData(7, 1, true, false, 3)]
    [InlineData(8, 1, true, false, 4)]
    [InlineData(9, 1, true, false, 5)]
    [InlineData(10, 2, true, true, 4)]
    [InlineData(11, 2, true, true, 5)]
    [InlineData(12, 2, true, true, 6)]
    public void Compose_DefaultOptions_MatchesRulebookTable(int players, int accomplices, bool witness, bool expert, int detectives)
    {
        var roles = RoleTable.Compose(players);

        roles.Should().HaveCount(players);
        roles.Count(r => r == Role.Ghost).Should().Be(1);
        roles.Count(r => r == Role.Killer).Should().Be(1);
        roles.Count(r => r == Role.Accomplice).Should().Be(accomplices);
        roles.Contains(Role.Witness).Should().Be(witness);
        roles.Contains(Role.Expert).Should().Be(expert);
        roles.Count(r => r == Role.Detective).Should().Be(detectives);
        roles.Should().NotContain(new[] { Role.Blackmailer, Role.Imitator });
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Compose_TwoOrThreePlayers_IsCooperative(int players)
    {
        var roles = RoleTable.Compose(players);

        roles.Should().BeEquivalentTo(
            new[] { Role.Ghost }.Concat(Enumerable.Repeat(Role.Detective, players - 1)));
        RoleTable.IsCooperative(roles).Should().BeTrue();
    }

    [Fact]
    public void Compose_KillerDisabled_IsCooperativeEvenWithManyPlayers()
    {
        var roles = RoleTable.Compose(9, new RoleOptions(KillerEnabled: false));

        roles.Should().HaveCount(9);
        roles.Count(r => r == Role.Detective).Should().Be(8);
        RoleTable.IsCooperative(roles).Should().BeTrue();
    }

    [Fact]
    public void Compose_WitnessAndExpertDisabled_BecomeDetectives()
    {
        var roles = RoleTable.Compose(10, new RoleOptions(UseWitness: false, UseExpert: false));

        roles.Should().NotContain(new[] { Role.Witness, Role.Expert });
        roles.Count(r => r == Role.Detective).Should().Be(6);
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(12, true)]
    public void Compose_Blackmailer_OnlyFromEightPlayers_ReplacesDetective(int players, bool expected)
    {
        var baseline = RoleTable.Compose(players);
        var roles = RoleTable.Compose(players, new RoleOptions(UseBlackmailer: true));

        roles.Contains(Role.Blackmailer).Should().Be(expected);
        roles.Count(r => r == Role.Detective).Should()
            .Be(baseline.Count(r => r == Role.Detective) - (expected ? 1 : 0));
    }

    [Fact]
    public void Compose_ImitatorReplacingDetective()
    {
        var roles = RoleTable.Compose(5, new RoleOptions(Imitator: ImitatorMode.ReplaceDetective));

        roles.Should().BeEquivalentTo(new[] { Role.Ghost, Role.Killer, Role.Imitator, Role.Detective, Role.Detective });
    }

    [Fact]
    public void Compose_ImitatorReplacingAccomplice()
    {
        var roles = RoleTable.Compose(7, new RoleOptions(Imitator: ImitatorMode.ReplaceAccomplice));

        roles.Should().Contain(Role.Imitator);
        roles.Should().NotContain(Role.Accomplice);
        roles.Count(r => r == Role.Detective).Should().Be(3);
    }

    [Fact]
    public void Compose_ImitatorBelowFivePlayers_Throws()
    {
        var act = () => RoleTable.Compose(4, new RoleOptions(Imitator: ImitatorMode.ReplaceDetective));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Compose_ImitatorReplacingAccompliceWithoutAccomplices_Throws()
    {
        var act = () => RoleTable.Compose(6, new RoleOptions(Imitator: ImitatorMode.ReplaceAccomplice));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Compose_ImitatorIgnoredInCooperative()
    {
        var roles = RoleTable.Compose(6, new RoleOptions(KillerEnabled: false, Imitator: ImitatorMode.ReplaceDetective));

        roles.Should().NotContain(Role.Imitator);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(13)]
    public void Compose_PlayerCountOutOfRange_Throws(int players)
    {
        var act = () => RoleTable.Compose(players);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Compose_AllOptionsAtTwelve_StillHasDetectives()
    {
        var roles = RoleTable.Compose(12, new RoleOptions(UseBlackmailer: true, Imitator: ImitatorMode.ReplaceDetective));

        roles.Should().HaveCount(12);
        roles.Count(r => r == Role.Detective).Should().Be(4);
    }

    [Fact]
    public void ExtraAccomplice_SixPlayers_GhostKillerAccompliceAndThreeDetectives()
    {
        var roles = RoleTable.Compose(6, new RoleOptions(ExtraAccomplices: 1));

        roles.Should().BeEquivalentTo(new[] { Role.Ghost, Role.Killer, Role.Accomplice, Role.Detective, Role.Detective, Role.Detective });
    }

    [Theory]
    [InlineData(8, 1, 2)]
    [InlineData(10, 1, 3)]
    [InlineData(12, 2, 4)]
    public void ExtraAccomplices_AddToTableAccomplices(int players, int extra, int accomplices)
    {
        var roles = RoleTable.Compose(players, new RoleOptions(ExtraAccomplices: extra));

        roles.Count(r => r == Role.Accomplice).Should().Be(accomplices);
        roles.Should().HaveCount(players);
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(5, 1)]
    [InlineData(7, 1)]
    [InlineData(6, 2)]
    [InlineData(8, 3)]
    [InlineData(8, -1)]
    public void ExtraAccomplices_KillerTeamMustStayMinority(int players, int extra)
    {
        var act = () => RoleTable.Compose(players, new RoleOptions(ExtraAccomplices: extra));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ExtraAccomplice_WithImitatorReplacingAccomplice()
    {
        var roles = RoleTable.Compose(6, new RoleOptions(ExtraAccomplices: 1, Imitator: ImitatorMode.ReplaceAccomplice));

        roles.Should().Contain(Role.Imitator).And.NotContain(Role.Accomplice);
    }

    [Theory]
    [InlineData(2, true, -1)]
    [InlineData(3, true, 3)]
    [InlineData(6, false, -1)]
    [InlineData(6, false, 3)]
    public void ExtraAccomplices_InvalidCountIsRejectedInCooperativeToo(int players, bool killer, int extra)
    {
        var act = () => RoleTable.Compose(players, new RoleOptions(KillerEnabled: killer, ExtraAccomplices: extra));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Teams_AreClassifiedCorrectly()
    {
        Role.Killer.IsKillerTeam().Should().BeTrue();
        Role.Accomplice.IsKillerTeam().Should().BeTrue();
        Role.Witness.IsDetectiveTeam().Should().BeTrue();
        Role.Ghost.IsDetectiveTeam().Should().BeTrue();
        Role.Imitator.IsKillerTeam().Should().BeFalse();
        Role.Imitator.IsDetectiveTeam().Should().BeFalse();
        Role.Blackmailer.IsDetectiveTeam().Should().BeFalse();
    }
}
