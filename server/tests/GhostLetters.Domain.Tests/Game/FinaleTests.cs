using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;

namespace GhostLetters.Domain.Tests.Game;

/// <summary>Истинные улики в тестах — нулевой столбец каждого ряда.</summary>
public class FinaleTests
{
    private const int Right = 0;
    private const int Wrong = 1;

    [Fact]
    public void Voting_RowsThenKiller_GhostDoesNotVote()
    {
        var state = FinaleGame.ToVoting(players: 7);

        state.VoteStages.Select(s => s.Kind).Should().Equal(
            VoteStageKind.Row, VoteStageKind.Row, VoteStageKind.Row, VoteStageKind.Row, VoteStageKind.Killer);
        state.VoteStages.Last().CandidateSuspects.Should().HaveCount(6).And.NotContain(state.Ghost.Id);

        var ghostVote = () => state.Run(state.Ghost, new CastVote(Right, null));
        ghostVote.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.NotAllowed);
    }

    [Fact]
    public void Voting_Cooperative_HasNoKillerStage()
    {
        var state = FinaleGame.ToVoting(players: 3);

        state.VoteStages.Should().HaveCount(4).And.OnlyContain(s => s.Kind == VoteStageKind.Row);
    }

    [Fact]
    public void Votes_HiddenUntilEveryoneVoted_ThenOpen()
    {
        var state = FinaleGame.ToVoting(players: 5);
        var voters = state.Investigators.ToList();

        state.Run(voters[0], new CastVote(Wrong, null));

        GameProjection.For(state, voters[0].Id).Finale!.MyVote.Should().Be(new MyVoteView(Wrong, null));
        GameProjection.For(state, voters[1].Id).Finale!.MyVote.Should().BeNull();
        GameProjection.For(state, voters[1].Id).Finale!.Votes.Should().BeEmpty();
        GameProjection.For(state, null).Players.Single(p => p.Id == voters[0].Id).HasActed.Should().BeTrue();

        foreach (var p in voters.Skip(1))
        {
            state.Run(p, new CastVote(Right, null));
        }

        var view = GameProjection.For(state, null).Finale!;
        view.Votes.Should().HaveCount(4);
        view.Votes.Single(v => v.Voter == voters[0].Id).Column.Should().Be(Wrong);
        view.Outcomes.Should().ContainSingle().Which.Column.Should().Be(Right);
        view.Outcomes[0].Correct.Should().BeNull("правильность рядов раскрывается только в итогах");
        view.CurrentStage!.Index.Should().Be(1);
    }

    [Fact]
    public void Vote_Twice_OrForSelf_OrOutsideCandidates_Throws()
    {
        var state = FinaleGame.ToVoting(players: 5);
        var p = state.Investigators.First();

        var outside = () => state.Run(p, new CastVote(7, null));
        outside.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.Validation);

        state.Run(p, new CastVote(null, null));
        var twice = () => state.Run(p, new CastVote(Right, null));
        twice.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.NotAllowed);

        foreach (var other in state.Investigators.Skip(1).ToList())
        {
            state.Run(other, new CastVote(Right, null));
        }

        state.VoteAllRows(Right);
        var self = () => state.Run(p, new CastVote(null, p.Id));
        self.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.Validation);
    }

    [Fact]
    public void Tie_LeadsToDiscussion_ThenRevoteAmongLeaders()
    {
        var state = FinaleGame.ToVoting(players: 7);

        state.VoteSplit(Right, Wrong);

        state.Phase.Should().Be(Phase.VoteTie);
        state.CurrentVoteStage!.Attempt.Should().Be(2);
        state.CurrentVoteStage.CandidateColumns.Should().Equal(Right, Wrong);

        state.ReadyAll();
        state.Phase.Should().Be(Phase.Voting);
        var outsider = () => state.Run(state.Investigators.First(), new CastVote(2, null));
        outsider.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.Validation);

        state.VoteRow(Wrong);
        state.VoteOutcomes.Single().Column.Should().Be(Wrong);
        state.VoteRecords.Should().HaveCount(12);
    }

    [Fact]
    public void Tie_AfterRevoteLimit_IsDecidedByLot()
    {
        var state = FinaleGame.ToVoting(players: 7);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            state.VoteSplit(Right, Wrong);
            state.Phase.Should().Be(Phase.VoteTie);
            state.ReadyAll();
        }

        state.VoteSplit(Right, Wrong);

        state.Phase.Should().Be(Phase.Voting);
        state.VoteStageIndex.Should().Be(1);
        var outcome = state.VoteOutcomes.Single();
        outcome.ByLot.Should().BeTrue();
        outcome.Column!.Value.Should().BeOneOf(Right, Wrong);
    }

    [Fact]
    public void AllRowsRight_HuntMissed_DetectivesWin()
    {
        var state = FinaleGame.ToVoting(players: 7);
        var detective = state.WithRole(Role.Detective);

        state.VoteAllRows(Right);
        state.VoteKiller(detective);

        state.Solved.Should().BeTrue();
        state.Phase.Should().Be(Phase.Hunt);
        state.Run(state.WithRole(Role.Killer), new HuntPick(detective.Id));

        state.Phase.Should().Be(Phase.AwardNomination);
        state.Result!.Side.Should().Be(WinningSide.Detectives);
        state.Result.Winners.Should().BeEquivalentTo(
            state.Players.Where(p => p.Role is Role.Ghost or Role.Detective or Role.Witness).Select(p => p.Id));
    }

    [Fact]
    public void Solved_ButWitnessFound_KillerTeamWins()
    {
        var state = FinaleGame.ToVoting(players: 7);
        state.VoteAllRows(Right);
        state.VoteKiller(state.WithRole(Role.Detective));

        var notKiller = () => state.Run(state.WithRole(Role.Accomplice), new HuntPick(state.WithRole(Role.Witness).Id));
        notKiller.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.NotAllowed);

        state.Run(state.WithRole(Role.Killer), new HuntPick(state.WithRole(Role.Witness).Id));

        state.Hunt!.Success.Should().BeTrue();
        state.Result!.Side.Should().Be(WinningSide.Killer);
        state.Result.Winners.Should().BeEquivalentTo(new[] { state.WithRole(Role.Killer).Id, state.WithRole(Role.Accomplice).Id });
    }

    [Fact]
    public void OneRowWrong_PlusKillerCaught_IsSolved()
    {
        var state = FinaleGame.ToVoting(players: 7);
        state.VoteRow(Wrong);
        state.VoteRow(Right);
        state.VoteRow(Right);
        state.VoteRow(Right);

        state.VoteKiller(state.WithRole(Role.Killer));

        state.Solved.Should().BeTrue();
        state.VoteOutcomes.Last().RevealedRole.Should().Be(Role.Killer);
    }

    [Fact]
    public void TwoRowsWrong_EvenWithKillerCaught_NotSolved_HuntStillHappens()
    {
        var state = FinaleGame.ToVoting(players: 7);
        state.VoteRow(Wrong);
        state.VoteRow(Wrong);
        state.VoteRow(Right);
        state.VoteRow(Right);

        state.VoteKiller(state.WithRole(Role.Killer));

        state.Solved.Should().BeFalse();
        state.Phase.Should().Be(Phase.Hunt);
        state.Run(state.WithRole(Role.Killer), new HuntPick(state.WithRole(Role.Detective).Id));
        state.Result!.Side.Should().Be(WinningSide.Killer);
        state.Result.CorrectRows.Should().Be(2);
    }

    [Fact]
    public void AccompliceArrested_IsRevealed_AndExtraArrestFollows()
    {
        var state = FinaleGame.ToVoting(players: 7);
        var accomplice = state.WithRole(Role.Accomplice);
        state.VoteAllRows(Wrong);

        state.VoteKiller(accomplice);

        state.VoteStages.Should().HaveCount(6);
        state.Phase.Should().Be(Phase.Voting);
        state.CurrentVoteStage!.CandidateSuspects.Should().HaveCount(5).And.NotContain(accomplice.Id);
        GameProjection.For(state, state.WithRole(Role.Detective).Id).Finale!.Outcomes.Last()
            .RevealedRole.Should().Be(Role.Accomplice);
        GameProjection.For(state, accomplice.Id).AllowedCommands.Should().NotContain(nameof(CastVote));
        var arrestedVote = () => state.Run(accomplice, new CastVote(null, null));
        arrestedVote.Should().Throw<GameRuleException>();

        state.VoteKiller(state.WithRole(Role.Detective));
        state.Run(state.WithRole(Role.Killer), new HuntPick(state.WithRole(Role.Detective).Id));

        state.Result!.Side.Should().Be(WinningSide.Killer);
        state.Result.Winners.Should().BeEquivalentTo(new[] { state.WithRole(Role.Killer).Id, accomplice.Id },
            "арестованный Сообщник побеждает вместе с командой Убийцы");
    }

    [Fact]
    public void ArrestedAccomplice_IsSkippedOnTimeoutAndRevote()
    {
        var state = FinaleGame.ToVoting(players: 7);
        state.VoteAllRows(Wrong);
        var accomplice = state.WithRole(Role.Accomplice);
        state.VoteKiller(accomplice);
        var stage = state.VoteStageIndex;
        GameEngine.Timeout(state);
        state.VoteRecords.Where(v => v.Stage == stage).Should().NotContain(v => v.Voter == accomplice.Id);
        state.Phase.Should().Be(Phase.VoteTie);
        GameProjection.For(state, accomplice.Id).AllowedCommands.Should().NotContain(nameof(ReadyRevote));
        var ready = () => state.Run(accomplice, new ReadyRevote());
        ready.Should().Throw<GameRuleException>();
        foreach (var voter in state.EligibleVoters.ToList()) state.Run(voter, new ReadyRevote());
        state.Phase.Should().Be(Phase.Voting);
    }

    [Fact]
    public void ImitatorArrested_ImitatorWins_NoExtraArrest_KillerNotCaught()
    {
        var settings = new GameSettings { Rounds = 1, Roles = new RoleOptions(Imitator: ImitatorMode.ReplaceDetective) };
        var state = FinaleGame.ToVoting(players: 7, settings);
        var imitator = state.WithRole(Role.Imitator);
        state.VoteAllRows(Right);

        state.VoteKiller(imitator);

        state.VoteStages.Should().HaveCount(5);
        state.VoteOutcomes.Last().RevealedRole.Should().Be(Role.Imitator);
        state.Run(state.WithRole(Role.Killer), new HuntPick(state.WithRole(Role.Detective).Id));

        state.Result!.KillerCaught.Should().BeFalse();
        state.Result.ImitatorWon.Should().BeTrue();
        state.Result.Side.Should().Be(WinningSide.Detectives);
        state.Result.Winners.Should().Contain(imitator.Id);
    }

    [Fact]
    public void Blackmailer_NotFound_NamesTruth_AndWins()
    {
        var state = FinaleGame.ToVoting(players: 8, FinaleGame.WithBlackmailer);
        var killer = state.WithRole(Role.Killer);
        var blackmailer = state.WithRole(Role.Blackmailer);
        state.VoteAllRows(Wrong);
        state.VoteKiller(state.WithRole(Role.Detective));

        state.Run(killer, new HuntPick(state.WithRole(Role.Detective).Id));
        state.Phase.Should().Be(Phase.BlackmailerHunt);
        state.Run(killer, new BlackmailerPick(state.WithRole(Role.Witness).Id));
        state.Phase.Should().Be(Phase.BlackmailerClaim);

        var partial = () => state.Run(blackmailer, new NameTruth([0, 0, 0]));
        partial.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.Validation);
        state.Run(blackmailer, new NameTruth([0, 0, 0, 0]));

        state.Result!.BlackmailerWon.Should().BeTrue();
        state.Result.Side.Should().Be(WinningSide.Blackmailer);
        state.Result.Winners.Should().Equal(blackmailer.Id);
    }

    [Fact]
    public void Blackmailer_WrongClaim_KillerTeamWins()
    {
        var state = FinaleGame.ToVoting(players: 8, FinaleGame.WithBlackmailer);
        var killer = state.WithRole(Role.Killer);
        state.VoteAllRows(Wrong);
        state.VoteKiller(state.WithRole(Role.Detective));
        state.Run(killer, new HuntPick(state.WithRole(Role.Detective).Id));
        state.Run(killer, new BlackmailerPick(state.WithRole(Role.Witness).Id));

        state.Run(state.WithRole(Role.Blackmailer), new NameTruth([0, 0, 0, 1]));

        state.Result!.Side.Should().Be(WinningSide.Killer);
        state.Result.Winners.Should().BeEquivalentTo(new[] { killer.Id, state.WithRole(Role.Accomplice).Id });
    }

    [Fact]
    public void Hunt_WithWitnessAndExpert_KillerMustNameExactRole()
    {
        var state = FinaleGame.ToVoting(players: 10);
        var killer = state.WithRole(Role.Killer);
        var witness = state.WithRole(Role.Witness);
        state.VoteAllRows(Right);
        state.VoteKiller(state.WithRole(Role.Detective));
        state.Phase.Should().Be(Phase.Hunt);

        var noRole = () => state.Run(killer, new HuntPick(witness.Id));
        noRole.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.Validation);
        var notHuntedRole = () => state.Run(killer, new HuntPick(witness.Id, Role.Detective));
        notHuntedRole.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.Validation);

        state.Run(killer, new HuntPick(witness.Id, Role.Expert));

        state.Hunt!.Success.Should().BeFalse("Свидетель найден, но роль названа неверно");
        state.Result!.Side.Should().Be(WinningSide.Detectives);
    }

    [Fact]
    public void Hunt_WithWitnessAndExpert_ExactRole_KillerWins()
    {
        var state = FinaleGame.ToVoting(players: 10);
        state.VoteAllRows(Right);
        state.VoteKiller(state.WithRole(Role.Detective));

        state.Run(state.WithRole(Role.Killer), new HuntPick(state.WithRole(Role.Expert).Id, Role.Expert));

        state.Hunt!.Success.Should().BeTrue();
        state.Hunt.Guess.Should().Be(Role.Expert);
        state.Result!.Side.Should().Be(WinningSide.Killer);
    }

    [Fact]
    public void Hunt_OnlyWitness_RoleIsImplied_GuessIgnored()
    {
        var state = FinaleGame.ToVoting(players: 7);
        state.VoteAllRows(Right);
        state.VoteKiller(state.WithRole(Role.Detective));

        // Убийца не знает, есть ли в партии Эксперт, и назвал его — роль всё равно одна, засчитывается Свидетель.
        state.Run(state.WithRole(Role.Killer), new HuntPick(state.WithRole(Role.Witness).Id, Role.Expert));

        state.Hunt!.Guess.Should().Be(Role.Witness);
        state.Hunt.Success.Should().BeTrue();
    }

    [Fact]
    public void Blackmailer_Found_LosesWithoutClaim()
    {
        var state = FinaleGame.ToVoting(players: 8, FinaleGame.WithBlackmailer);
        var killer = state.WithRole(Role.Killer);
        state.VoteAllRows(Wrong);
        state.VoteKiller(state.WithRole(Role.Detective));
        state.Run(killer, new HuntPick(state.WithRole(Role.Detective).Id));

        state.Run(killer, new BlackmailerPick(state.WithRole(Role.Blackmailer).Id));

        state.Phase.Should().Be(Phase.AwardNomination);
        state.Result!.BlackmailerWon.Should().BeFalse();
        state.Result.Winners.Should().NotContain(state.WithRole(Role.Blackmailer).Id);
    }

    [Fact]
    public void Blackmailer_WhenCaseSolved_NoBlackmailerPhases()
    {
        var state = FinaleGame.ToVoting(players: 8, FinaleGame.WithBlackmailer);
        state.VoteAllRows(Right);
        state.VoteKiller(state.WithRole(Role.Detective));

        state.Run(state.WithRole(Role.Killer), new HuntPick(state.WithRole(Role.Detective).Id));

        state.Phase.Should().Be(Phase.AwardNomination);
        state.Result!.Side.Should().Be(WinningSide.Detectives);
    }

    [Theory]
    [InlineData(Right, WinningSide.Detectives)]
    [InlineData(Wrong, WinningSide.Nobody)]
    public void Cooperative_NoHunt_ResultRightAfterRows(int column, WinningSide side)
    {
        var state = FinaleGame.ToVoting(players: 3);

        state.VoteAllRows(column);

        state.Phase.Should().Be(Phase.AwardNomination);
        state.Result!.Side.Should().Be(side);
        state.Result.Winners.Should().HaveCount(side == WinningSide.Detectives ? 3 : 0);
    }

    [Fact]
    public void AfterResult_TruthAndRolesAreOpenToEveryone()
    {
        var state = FinaleGame.ToVoting(players: 7);
        var detective = state.WithRole(Role.Detective);
        state.VoteAllRows(Right);
        state.VoteKiller(detective);
        GameProjection.For(state, detective.Id).Truth.Should().BeNull();

        state.Run(state.WithRole(Role.Killer), new HuntPick(detective.Id));

        foreach (var view in new[] { GameProjection.For(state, detective.Id), GameProjection.For(state, null) })
        {
            view.Truth.Should().Equal(state.Truth!);
            view.Players.Should().OnlyContain(p => p.KnownRole != null);
            view.Finale!.Result!.Side.Should().Be(WinningSide.Detectives);
            view.Finale.Outcomes.Should().OnlyContain(o => o.Correct != null);
        }
    }

    [Fact]
    public void Awards_NominateVote_TopTwoWin()
    {
        var state = FinaleGame.ToAwards();
        var ps = state.Players.OrderBy(p => p.Seat).ToList();

        var self = () => state.Run(ps[0], new Nominate("Стальные яйца", ps[0].Id));
        self.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.Validation);

        state.Run(ps[0], new Nominate("Стальные яйца", ps[1].Id));
        state.Run(ps[1], new Nominate("стальные яйца", ps[2].Id));
        state.Run(ps[2], new Nominate("Стальные яйца", ps[1].Id));
        state.Run(ps[3], new Nominate("Шерлок", ps[4].Id));
        state.Run(ps[4], new Nominate(null, null));

        state.Phase.Should().Be(Phase.AwardVoting);
        state.Awards.Should().HaveCount(3);
        state.Awards[0].NominatedBy.Should().Equal(ps[0].Id, ps[2].Id);

        var own = () => state.Run(ps[0], new AwardVote(0));
        own.Should().Throw<GameRuleException>().Which.Code.Should().Be(GameRuleException.Codes.Validation);
        GameProjection.For(state, ps[3].Id).Finale!.Awards.Should().OnlyContain(a => a.Votes == null);

        state.Run(ps[0], new AwardVote(1));
        state.Run(ps[1], new AwardVote(2));
        state.Run(ps[2], new AwardVote(2));
        state.Run(ps[3], new AwardVote(0));
        state.Run(ps[4], new AwardVote(1));

        state.Phase.Should().Be(Phase.Finished);
        state.AwardWinners.Should().BeEquivalentTo(new[] { 1, 2 });
        var view = GameProjection.For(state, ps[3].Id).Finale!;
        view.Awards.Select(a => a.Votes).Should().Equal(1, 2, 2);
        view.Awards.Select(a => a.Won).Should().Equal(false, true, true);
    }

    [Fact]
    public void Awards_TieAtBoundary_StillOnlyTwo()
    {
        var state = FinaleGame.ToAwards();
        var ps = state.Players.OrderBy(p => p.Seat).ToList();
        for (var i = 0; i < 5; i++)
        {
            state.Run(ps[i], new Nominate($"N{i}", ps[(i + 1) % 5].Id));
        }

        for (var i = 0; i < 5; i++)
        {
            // Каждый голосует за выдвижение соседа: у всех по одному голосу.
            state.Run(ps[i], new AwardVote((i + 2) % 5));
        }

        state.AwardWinners.Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Awards_NoNominations_GameFinishes()
    {
        var state = FinaleGame.ToAwards();

        foreach (var p in state.Players.ToList())
        {
            state.Run(p, new Nominate(null, null));
        }

        state.Phase.Should().Be(Phase.Finished);
        state.AwardWinners.Should().BeEmpty();
    }

    [Fact]
    public void Likes_ToggleAndCount_NotSelf()
    {
        var state = FinaleGame.ToAwards();
        var ps = state.Players.OrderBy(p => p.Seat).ToList();

        state.Run(ps[0], new Like(ps[1].Id, true));
        state.Run(ps[0], new Like(ps[1].Id, true));
        state.Run(ps[2], new Like(ps[1].Id, true));
        state.Run(ps[2], new Like(ps[1].Id, false));

        var view = GameProjection.For(state, ps[0].Id).Finale!;
        view.Likes.Single(l => l.Player == ps[1].Id).Should().Be(new LikeCountView(ps[1].Id, 1, true));
        GameProjection.For(state, ps[0].Id).AllowedCommands.Should().Contain(nameof(Like)).And.Contain(nameof(Nominate));

        var self = () => state.Run(ps[0], new Like(ps[0].Id, true));
        self.Should().Throw<GameRuleException>();
    }

    [Fact]
    public void Timeouts_DriveWholeGameToFinished()
    {
        var state = TestGame.Create(players: 10, settings: new GameSettings { Rounds = 1 });
        var guard = 0;

        while (state.Phase != Phase.Finished && guard++ < 300)
        {
            GameEngine.Timeout(state);
        }

        state.Phase.Should().Be(Phase.Finished);
        state.Result.Should().NotBeNull();
        state.Hunt!.Success.Should().BeFalse();
        state.VoteOutcomes.Should().OnlyContain(o => o.ByLot);
    }
}

internal static class FinaleGame
{
    public static readonly GameSettings WithBlackmailer =
        new() { Rounds = 1, Roles = new RoleOptions(UseBlackmailer: true) };

    public static GameState ToVoting(int players, GameSettings? settings = null)
    {
        var state = TestGame.Create(players, settings ?? new GameSettings { Rounds = 1 });
        state.ToRound1();
        state.PlayRound();
        state.Phase.Should().Be(Phase.Voting);
        return state;
    }

    /// <summary>Кооператив на 5 игроков сразу после итогов.</summary>
    public static GameState ToAwards()
    {
        var state = ToVoting(players: 5, new GameSettings { Rounds = 1, Roles = new RoleOptions(KillerEnabled: false) });
        state.VoteAllRows(0);
        state.Phase.Should().Be(Phase.AwardNomination);
        return state;
    }

    public static void VoteRow(this GameState state, int column)
    {
        foreach (var p in state.Investigators.ToList())
        {
            state.Run(p, new CastVote(column, null));
        }
    }

    public static void VoteAllRows(this GameState state, int column)
    {
        while (state.CurrentVoteStage is { Kind: VoteStageKind.Row })
        {
            state.VoteRow(column);
        }
    }

    /// <summary>Все за подозреваемого, сам он воздерживается.</summary>
    public static void VoteKiller(this GameState state, PlayerState suspect)
    {
        state.CurrentVoteStage!.Kind.Should().Be(VoteStageKind.Killer);
        foreach (var p in state.EligibleVoters.ToList())
        {
            state.Run(p, p.Id == suspect.Id ? new CastVote(null, null) : new CastVote(null, suspect.Id));
        }
    }

    /// <summary>Первая половина голосует за a, вторая — за b.</summary>
    public static void VoteSplit(this GameState state, int a, int b)
    {
        var voters = state.Investigators.ToList();
        for (var i = 0; i < voters.Count; i++)
        {
            state.Run(voters[i], new CastVote(i < voters.Count / 2 ? a : b, null));
        }
    }

    public static void ReadyAll(this GameState state)
    {
        foreach (var p in state.Investigators.ToList())
        {
            state.Run(p, new ReadyRevote());
        }
    }
}
