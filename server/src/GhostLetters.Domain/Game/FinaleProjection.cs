using GhostLetters.Domain.Roles;

namespace GhostLetters.Domain.Game;

public sealed record VoteStageView(
    int Index,
    VoteStageKind Kind,
    int Row,
    int Attempt,
    IReadOnlyList<int> CandidateColumns,
    IReadOnlyList<Guid> CandidateSuspects);

public sealed record VoteRecordView(int Stage, int Attempt, Guid Voter, int? Column, Guid? Suspect);

/// <summary>Итог этапа. Correct по рядам скрыт (null) до объявления результата.</summary>
public sealed record VoteOutcomeView(
    int Stage,
    VoteStageKind Kind,
    int Row,
    int? Column,
    Guid? Suspect,
    bool? Correct,
    bool ByLot,
    Role? RevealedRole);

public sealed record MyVoteView(int? Column, Guid? Suspect);

public sealed record HuntView(Guid? Target, Role? Guess, bool Success);

public sealed record ResultView(
    bool Solved,
    int CorrectRows,
    bool KillerCaught,
    WinningSide Side,
    bool ImitatorWon,
    bool BlackmailerWon,
    IReadOnlyList<Guid> Winners,
    IReadOnlyList<int>? BlackmailerClaim);

/// <summary>Выдвижение на ачивку. Число голосов видно только после завершения.</summary>
public sealed record AwardEntryView(int Index, string Code, Guid Nominee, int NominatedByCount, bool MineNomination, int? Votes, bool Won);

public sealed record LikeCountView(Guid Player, int Count, bool LikedByMe);

/// <summary>Голосование, развязка и награды глазами игрока.</summary>
public sealed record FinaleView(
    VoteStageView? CurrentStage,
    int StagesTotal,
    MyVoteView? MyVote,
    IReadOnlyList<VoteRecordView> Votes,
    IReadOnlyList<VoteOutcomeView> Outcomes,
    IReadOnlyList<Guid> Arrested,
    HuntView? Hunt,
    bool? BlackmailerFound,
    ResultView? Result,
    IReadOnlyList<AwardEntryView> Awards,
    IReadOnlyList<LikeCountView> Likes);

public static class FinaleProjection
{
    public static FinaleView? For(GameState state, PlayerState? viewer)
    {
        if (state.VoteStages.Count == 0)
        {
            return null;
        }

        var stage = state.CurrentVoteStage;
        var stageView = stage is null
            ? null
            : new VoteStageView(state.VoteStageIndex, stage.Kind, stage.Row, stage.Attempt,
                stage.CandidateColumns.ToList(), stage.CandidateSuspects.ToList());

        var mine = viewer is null ? null : state.PendingVotes.FirstOrDefault(v => v.Voter == viewer.Id);
        var finished = state.Result is not null;

        var outcomes = state.VoteOutcomes.Select(o => new VoteOutcomeView(
                o.Stage,
                o.Kind,
                o.Row,
                o.Column,
                o.Suspect,
                o.Kind == VoteStageKind.Killer || finished ? (bool?)o.Correct : null,
                o.ByLot,
                finished && o.Suspect is { } s ? (Role?)state.Player(s).Role : o.RevealedRole))
            .ToList();

        var result = state.Result is { } r
            ? new ResultView(r.Solved, r.CorrectRows, r.KillerCaught, r.Side, r.ImitatorWon, r.BlackmailerWon,
                r.Winners.ToList(), state.BlackmailerClaim?.ToList())
            : null;

        var awardsDone = state.Phase == Phase.Finished;
        var awards = state.Awards.Select((a, i) => new AwardEntryView(
                i,
                a.Code,
                a.Nominee,
                a.NominatedBy.Count,
                viewer is not null && a.NominatedBy.Contains(viewer.Id),
                awardsDone ? (int?)a.Voters.Count : null,
                state.AwardWinners.Contains(i)))
            .ToList();

        var likes = state.Players.OrderBy(p => p.Seat)
            .Select(p => new LikeCountView(
                p.Id,
                state.Likes.Count(l => l.To == p.Id),
                viewer is not null && state.Likes.Any(l => l.From == viewer.Id && l.To == p.Id)))
            .ToList();

        return new FinaleView(
            stageView,
            state.VoteStages.Count,
            mine is null ? null : new MyVoteView(mine.Column, mine.Suspect),
            state.VoteRecords.Select(v => new VoteRecordView(v.Stage, v.Attempt, v.Voter, v.Column, v.Suspect)).ToList(),
            outcomes,
            state.Arrested.ToList(),
            state.Hunt is { } h ? new HuntView(h.Target, h.Guess, h.Success) : null,
            state.BlackmailerFound,
            result,
            awards,
            likes);
    }

    internal static void AddAllowedCommands(GameState state, PlayerState viewer, bool done, List<string> list)
    {
        var investigator = viewer.Role != Role.Ghost;
        switch (state.Phase)
        {
            case Phase.Voting when investigator && !done:
                list.Add(nameof(CastVote));
                break;
            case Phase.VoteTie when investigator && !done:
                list.Add(nameof(ReadyRevote));
                break;
            case Phase.Hunt when viewer.Role == Role.Killer:
                list.Add(nameof(HuntPick));
                break;
            case Phase.BlackmailerHunt when viewer.Role == Role.Killer:
                list.Add(nameof(BlackmailerPick));
                break;
            case Phase.BlackmailerClaim when viewer.Role == Role.Blackmailer:
                list.Add(nameof(NameTruth));
                break;
            case Phase.AwardNomination:
                list.Add(nameof(Like));
                if (!done)
                {
                    list.Add(nameof(Nominate));
                }

                break;
            case Phase.AwardVoting:
                list.Add(nameof(Like));
                if (!done)
                {
                    list.Add(nameof(AwardVote));
                }

                break;
            case Phase.Finished:
                list.Add(nameof(Like));
                break;
            default:
                break;
        }
    }
}
