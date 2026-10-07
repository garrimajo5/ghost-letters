using GhostLetters.Domain.Roles;
using GhostLetters.Domain.Rules;

namespace GhostLetters.Domain.Game;

/// <summary>Развязка: голосование, аресты, охота, Шантажист, итоги и награды.</summary>
public static partial class GameEngine
{
    public const int MaxAwardCodeLength = 40;
    public const int AwardsPerGame = 2;

    private static bool ExecuteFinale(GameState state, PlayerState actor, GameCommand command, List<GameEvent> events)
    {
        switch (command)
        {
            case CastVote vote:
                RequirePhase(state, Phase.Voting);
                RequireInvestigator(actor);
                ApplyVote(state, actor, vote, events);
                return true;
            case ReadyRevote:
                RequirePhase(state, Phase.VoteTie);
                RequireInvestigator(actor);
                if (state.Done.Add(actor.Id))
                {
                    events.Add(new GameEvent("ReadyRevote", actor.Id));
                }

                if (state.Investigators.All(p => state.Done.Contains(p.Id)))
                {
                    SetPhase(state, Phase.Voting, events);
                }

                return true;
            case HuntPick hunt:
                RequirePhase(state, Phase.Hunt);
                RequireKiller(actor);
                ApplyHunt(state, RequireTarget(state, actor, hunt.Target), hunt.Guess, events);
                return true;
            case BlackmailerPick pick:
                RequirePhase(state, Phase.BlackmailerHunt);
                RequireKiller(actor);
                ApplyBlackmailerPick(state, RequireTarget(state, actor, pick.Target), events);
                return true;
            case NameTruth claim:
                RequirePhase(state, Phase.BlackmailerClaim);
                if (actor.Role != Role.Blackmailer)
                {
                    throw GameRuleException.NotAllowed("Улики называет Шантажист.");
                }

                ValidateColumns(state, claim.Columns);
                ApplyClaim(state, claim.Columns.ToList(), events);
                return true;
            case Like like:
                if (state.Phase is not (Phase.AwardNomination or Phase.AwardVoting or Phase.Finished))
                {
                    throw GameRuleException.WrongPhase(Phase.AwardNomination, state.Phase);
                }

                ApplyLike(state, actor, like, events);
                return true;
            case Nominate nominate:
                RequirePhase(state, Phase.AwardNomination);
                ApplyNomination(state, actor, nominate, events);
                return true;
            case AwardVote awardVote:
                RequirePhase(state, Phase.AwardVoting);
                ApplyAwardVote(state, actor, awardVote.Entry, events);
                return true;
            default:
                return false;
        }
    }

    private static void TimeoutFinale(GameState state, Random rng, List<GameEvent> events)
    {
        switch (state.Phase)
        {
            case Phase.Voting:
                foreach (var p in state.Investigators.Where(x => !state.Done.Contains(x.Id)).ToList())
                {
                    ApplyVote(state, p, new CastVote(null, null), events);
                }

                break;
            case Phase.VoteTie:
                SetPhase(state, Phase.Voting, events);
                break;
            case Phase.Hunt:
                state.Hunt = new HuntResult();
                events.Add(new GameEvent("HuntMissed"));
                NextFinaleStep(state, events);
                break;
            case Phase.BlackmailerHunt:
                state.BlackmailerFound = false;
                events.Add(new GameEvent("BlackmailerNotFound"));
                NextFinaleStep(state, events);
                break;
            case Phase.BlackmailerClaim:
                ApplyClaim(state, [], events);
                break;
            case Phase.AwardNomination:
                FinishNominations(state, events);
                break;
            case Phase.AwardVoting:
                FinishAwards(state, rng, events);
                break;
            default:
                break;
        }
    }

    // ---------- Голосование ----------

    private static void StartVoting(GameState state, List<GameEvent> events)
    {
        state.SpeakingOrder = [];
        state.SpeakerIndex = 0;
        state.FloorGrantedTo = null;
        state.RaisedHands.Clear();

        var columns = Enumerable.Range(0, state.Settings.Columns).ToList();
        for (var row = 0; row < state.Board.Count; row++)
        {
            state.VoteStages.Add(new VoteStage { Kind = VoteStageKind.Row, Row = row, CandidateColumns = columns.ToList() });
        }

        if (state.HasKiller)
        {
            state.VoteStages.Add(KillerStage(state));
        }

        state.VoteStageIndex = 0;
        SetPhase(state, Phase.Voting, events);
    }

    private static VoteStage KillerStage(GameState state) => new()
    {
        Kind = VoteStageKind.Killer,
        CandidateSuspects = state.Investigators.Where(p => !state.Arrested.Contains(p.Id)).Select(p => p.Id).ToList(),
    };

    private static void ApplyVote(GameState state, PlayerState voter, CastVote vote, List<GameEvent> events)
    {
        if (state.Done.Contains(voter.Id))
        {
            throw GameRuleException.NotAllowed("Голос на этом этапе уже отдан.");
        }

        var stage = state.CurrentVoteStage!;
        var abstain = vote.Column is null && vote.Suspect is null;
        if (!abstain)
        {
            if (stage.Kind == VoteStageKind.Row)
            {
                if (vote.Suspect is not null || vote.Column is not { } column || !stage.CandidateColumns.Contains(column))
                {
                    throw GameRuleException.Validation("Выберите одну из карт этого ряда.");
                }
            }
            else
            {
                if (vote.Column is not null || vote.Suspect is not { } suspect || !stage.CandidateSuspects.Contains(suspect))
                {
                    throw GameRuleException.Validation("Выберите одного из подозреваемых.");
                }

                if (suspect == voter.Id)
                {
                    throw GameRuleException.Validation("Нельзя голосовать за себя.");
                }
            }
        }

        state.PendingVotes.Add(new VoteRecord
        {
            Stage = state.VoteStageIndex,
            Attempt = stage.Attempt,
            Voter = voter.Id,
            Column = vote.Column,
            Suspect = vote.Suspect,
        });
        state.Done.Add(voter.Id);
        events.Add(new GameEvent(abstain ? "VoteAbstained" : "VoteCast", voter.Id));

        if (state.Investigators.All(p => state.Done.Contains(p.Id)))
        {
            TallyVotes(state, events);
        }
    }

    private static void TallyVotes(GameState state, List<GameEvent> events)
    {
        var stage = state.CurrentVoteStage!;
        var votes = state.PendingVotes.OrderBy(v => state.Player(v.Voter).Seat).ToList();
        state.VoteRecords.AddRange(votes);
        state.PendingVotes.Clear();
        events.Add(new GameEvent("VotesOpened", Detail: $"{state.VoteStageIndex}:{stage.Attempt}"));

        if (stage.Kind == VoteStageKind.Row)
        {
            var leaders = Leaders(stage.CandidateColumns, votes.Where(v => v.Column is not null).Select(v => v.Column!.Value));
            if (Decide(state, stage, leaders, events) is { } decided)
            {
                ResolveRow(state, stage, decided.Choice, decided.ByLot, events);
            }
        }
        else
        {
            var leaders = Leaders(stage.CandidateSuspects, votes.Where(v => v.Suspect is not null).Select(v => v.Suspect!.Value));
            if (Decide(state, stage, leaders, events) is { } decided)
            {
                ResolveArrest(state, decided.Choice, decided.ByLot, events);
            }
        }
    }

    /// <summary>Варианты с наибольшим числом голосов, в порядке кандидатов. Нет голосов — ничья всех.</summary>
    private static List<T> Leaders<T>(IReadOnlyList<T> candidates, IEnumerable<T> votes)
        where T : notnull
    {
        var counts = votes.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        if (counts.Count == 0)
        {
            return candidates.ToList();
        }

        var max = counts.Values.Max();
        return candidates.Where(c => counts.GetValueOrDefault(c) == max).ToList();
    }

    /// <summary>Один лидер — решено; ничья — переголосование или жребий после лимита.</summary>
    private static (T Choice, bool ByLot)? Decide<T>(GameState state, VoteStage stage, List<T> leaders, List<GameEvent> events)
        where T : notnull
    {
        if (leaders.Count == 1)
        {
            return (leaders[0], false);
        }

        if (stage.Attempt > GameDefaults.RevoteLimit)
        {
            var lot = leaders[Rng(state).Next(leaders.Count)];
            events.Add(new GameEvent("DecidedByLot", Detail: lot.ToString()));
            return (lot, true);
        }

        stage.Attempt++;
        if (stage.Kind == VoteStageKind.Row)
        {
            stage.CandidateColumns = leaders.Cast<int>().ToList();
        }
        else
        {
            stage.CandidateSuspects = leaders.Cast<Guid>().ToList();
        }

        events.Add(new GameEvent("VoteTie", Detail: stage.Attempt.ToString()));
        SetPhase(state, Phase.VoteTie, events);
        return null;
    }

    private static void ResolveRow(GameState state, VoteStage stage, int column, bool byLot, List<GameEvent> events)
    {
        state.VoteOutcomes.Add(new VoteOutcome
        {
            Stage = state.VoteStageIndex,
            Kind = VoteStageKind.Row,
            Row = stage.Row,
            Column = column,
            Correct = state.Truth![stage.Row] == column,
            ByLot = byLot,
        });
        events.Add(new GameEvent("RowChosen", Detail: $"{stage.Row}:{column}"));
        NextVoteStage(state, events);
    }

    private static void ResolveArrest(GameState state, Guid suspectId, bool byLot, List<GameEvent> events)
    {
        var suspect = state.Player(suspectId);
        state.Arrested.Add(suspect.Id);
        var revealed = suspect.Role is Role.Killer or Role.Accomplice or Role.Imitator ? suspect.Role : (Role?)null;
        state.VoteOutcomes.Add(new VoteOutcome
        {
            Stage = state.VoteStageIndex,
            Kind = VoteStageKind.Killer,
            Row = -1,
            Suspect = suspect.Id,
            Correct = suspect.Role == Role.Killer,
            ByLot = byLot,
            RevealedRole = revealed,
        });
        events.Add(new GameEvent("Arrested", Detail: suspect.Id.ToString()));

        // Арестован Сообщник — доарест среди оставшихся.
        if (suspect.Role == Role.Accomplice)
        {
            var again = KillerStage(state);
            if (again.CandidateSuspects.Count > 1)
            {
                state.VoteStages.Add(again);
                events.Add(new GameEvent("ExtraArrest"));
            }
        }

        NextVoteStage(state, events);
    }

    private static void NextVoteStage(GameState state, List<GameEvent> events)
    {
        state.VoteStageIndex++;
        if (state.VoteStageIndex < state.VoteStages.Count)
        {
            SetPhase(state, Phase.Voting, events);
            return;
        }

        var rows = state.VoteOutcomes.Where(o => o.Kind == VoteStageKind.Row).ToList();
        var correct = rows.Count(o => o.Correct);
        var killerCaught = state.VoteOutcomes.Any(o => o.Kind == VoteStageKind.Killer && o.Correct);
        state.Solved = correct == state.Board.Count || (state.HasKiller && killerCaught && correct == state.Board.Count - 1);
        events.Add(new GameEvent(state.Solved == true ? "CaseSolved" : "CaseUnsolved"));
        NextFinaleStep(state, events);
    }

    // ---------- Охота и Шантажист ----------

    private static void NextFinaleStep(GameState state, List<GameEvent> events)
    {
        var roles = state.Players.Select(p => p.Role).ToList();
        if (state.Hunt is null && state.HasKiller && (roles.Contains(Role.Witness) || roles.Contains(Role.Expert)))
        {
            SetPhase(state, Phase.Hunt, events);
        }
        else if (state.Solved == false && state.BlackmailerFound is null && roles.Contains(Role.Blackmailer))
        {
            SetPhase(state, Phase.BlackmailerHunt, events);
        }
        else if (state.BlackmailerFound == false && state.BlackmailerClaim is null)
        {
            SetPhase(state, Phase.BlackmailerClaim, events);
        }
        else
        {
            FinishGame(state, events);
        }
    }

    private static void ApplyHunt(GameState state, PlayerState target, Role? guess, List<GameEvent> events)
    {
        var hunted = state.Players.Select(p => p.Role).Where(r => r is Role.Witness or Role.Expert).Distinct().ToList();
        if (guess is not null && !hunted.Contains(guess.Value))
        {
            throw GameRuleException.Validation("Можно назвать только роль Свидетеля или Эксперта, которая есть в игре.");
        }

        // Только одна из ролей в игре — роль очевидна; обе — Убийца должен угадать и роль.
        if (hunted.Count > 1 && guess is null)
        {
            throw GameRuleException.Validation("В игре и Свидетель, и Эксперт — назовите роль.");
        }

        var role = guess ?? hunted[0];
        var success = target.Role == role;
        state.Hunt = new HuntResult { Target = target.Id, Guess = role, Success = success };
        events.Add(new GameEvent(success ? "HuntSucceeded" : "HuntMissed", Detail: target.Id.ToString()));
        NextFinaleStep(state, events);
    }

    private static void ApplyBlackmailerPick(GameState state, PlayerState target, List<GameEvent> events)
    {
        state.BlackmailerFound = target.Role == Role.Blackmailer;
        events.Add(new GameEvent(state.BlackmailerFound == true ? "BlackmailerFound" : "BlackmailerNotFound",
            Detail: target.Id.ToString()));
        NextFinaleStep(state, events);
    }

    private static void ApplyClaim(GameState state, List<int> columns, List<GameEvent> events)
    {
        state.BlackmailerClaim = columns;
        events.Add(new GameEvent("BlackmailerClaimed"));
        NextFinaleStep(state, events);
    }

    private static void FinishGame(GameState state, List<GameEvent> events)
    {
        var rows = state.VoteOutcomes.Where(o => o.Kind == VoteStageKind.Row).ToList();
        var killerCaught = state.VoteOutcomes.Any(o => o.Kind == VoteStageKind.Killer && o.Correct);
        var solved = state.Solved == true;
        var huntWon = state.Hunt?.Success == true;
        var imitatorWon = state.Arrested.Any(id => state.Player(id).Role == Role.Imitator);
        var blackmailerWon = !solved && state.BlackmailerFound == false &&
                             state.BlackmailerClaim is { } claim && claim.SequenceEqual(state.Truth!);

        var side = solved && !huntWon ? WinningSide.Detectives
            : blackmailerWon ? WinningSide.Blackmailer
            : state.HasKiller ? WinningSide.Killer
            : WinningSide.Nobody;

        bool SideWins(PlayerState p) => side switch
        {
            WinningSide.Detectives => p.Role.IsDetectiveTeam(),
            WinningSide.Killer => p.Role == Role.Killer || (p.Role == Role.Accomplice && !state.Arrested.Contains(p.Id)),
            _ => false,
        };

        var winners = state.Players.Where(p =>
            SideWins(p) || (imitatorWon && p.Role == Role.Imitator) || (side == WinningSide.Blackmailer && p.Role == Role.Blackmailer));

        state.Result = new GameResult
        {
            Solved = solved,
            CorrectRows = rows.Count(o => o.Correct),
            KillerCaught = killerCaught,
            Side = side,
            ImitatorWon = imitatorWon,
            BlackmailerWon = blackmailerWon,
            Winners = winners.OrderBy(p => p.Seat).Select(p => p.Id).ToList(),
        };
        events.Add(new GameEvent("GameResult", Detail: side.ToString()));
        SetPhase(state, Phase.AwardNomination, events);
    }

    // ---------- Лайки и ачивки ----------

    private static void ApplyLike(GameState state, PlayerState actor, Like like, List<GameEvent> events)
    {
        var to = state.Player(like.To);
        if (to.Id == actor.Id)
        {
            throw GameRuleException.Validation("Нельзя лайкнуть себя.");
        }

        var existing = state.Likes.FirstOrDefault(l => l.From == actor.Id && l.To == to.Id);
        if (like.On && existing is null)
        {
            state.Likes.Add(new LikeRecord { From = actor.Id, To = to.Id });
            events.Add(new GameEvent("Liked", actor.Id, Detail: to.Id.ToString()));
        }
        else if (!like.On && existing is not null)
        {
            state.Likes.Remove(existing);
            events.Add(new GameEvent("Unliked", actor.Id, Detail: to.Id.ToString()));
        }
    }

    private static void ApplyNomination(GameState state, PlayerState actor, Nominate nominate, List<GameEvent> events)
    {
        if (state.Done.Contains(actor.Id))
        {
            throw GameRuleException.NotAllowed("Выдвижение уже сделано.");
        }

        var code = nominate.Code?.Trim();
        if (!string.IsNullOrEmpty(code))
        {
            if (code.Length > MaxAwardCodeLength)
            {
                throw GameRuleException.Validation($"Название номинации — до {MaxAwardCodeLength} символов.");
            }

            if (nominate.Nominee is not { } nomineeId)
            {
                throw GameRuleException.Validation("Выберите игрока.");
            }

            var nominee = state.Player(nomineeId);
            if (nominee.Id == actor.Id)
            {
                throw GameRuleException.Validation("Нельзя выдвинуть себя.");
            }

            var entry = state.Awards.FirstOrDefault(a =>
                a.Nominee == nominee.Id && string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                entry = new AwardEntry { Code = code, Nominee = nominee.Id };
                state.Awards.Add(entry);
            }

            entry.NominatedBy.Add(actor.Id);
            events.Add(new GameEvent("Nominated", actor.Id, Detail: state.Awards.IndexOf(entry).ToString()));
        }
        else
        {
            events.Add(new GameEvent("NominationSkipped", actor.Id));
        }

        state.Done.Add(actor.Id);
        if (state.Players.All(p => state.Done.Contains(p.Id)))
        {
            FinishNominations(state, events);
        }
    }

    private static void FinishNominations(GameState state, List<GameEvent> events) =>
        SetPhase(state, state.Awards.Count == 0 ? Phase.Finished : Phase.AwardVoting, events);

    private static void ApplyAwardVote(GameState state, PlayerState actor, int? index, List<GameEvent> events)
    {
        if (state.Done.Contains(actor.Id))
        {
            throw GameRuleException.NotAllowed("Голос уже отдан.");
        }

        if (index is { } i)
        {
            if (i < 0 || i >= state.Awards.Count)
            {
                throw GameRuleException.Validation("Такого выдвижения нет.");
            }

            var entry = state.Awards[i];
            if (entry.NominatedBy.Contains(actor.Id) || entry.Nominee == actor.Id)
            {
                throw GameRuleException.Validation("Нельзя голосовать за своё выдвижение или за себя.");
            }

            entry.Voters.Add(actor.Id);
        }

        state.Done.Add(actor.Id);
        events.Add(new GameEvent(index is null ? "AwardVoteSkipped" : "AwardVoted", actor.Id));
        if (state.Players.All(p => state.Done.Contains(p.Id)))
        {
            FinishAwards(state, Rng(state), events);
        }
    }

    /// <summary>Ачивки получают два выдвижения с наибольшим числом голосов; ничья на границе — жребий.</summary>
    private static void FinishAwards(GameState state, Random rng, List<GameEvent> events)
    {
        var winners = state.Awards
            .Select((entry, index) => (index, votes: entry.Voters.Count, lot: rng.Next()))
            .Where(x => x.votes > 0)
            .OrderByDescending(x => x.votes)
            .ThenBy(x => x.lot)
            .Take(AwardsPerGame)
            .Select(x => x.index)
            .ToList();

        state.AwardWinners.AddRange(winners);
        events.Add(new GameEvent("AwardsGranted", Detail: string.Join(",", winners)));
        SetPhase(state, Phase.Finished, events);
    }

    // ---------- Проверки ----------

    private static void RequireKiller(PlayerState actor)
    {
        if (actor.Role != Role.Killer)
        {
            throw GameRuleException.NotAllowed("Это решение Убийцы.");
        }
    }

    private static PlayerState RequireTarget(GameState state, PlayerState actor, Guid targetId)
    {
        var target = state.Player(targetId);
        if (target.Role == Role.Ghost || target.Id == actor.Id)
        {
            throw GameRuleException.Validation("Укажите другого игрока, кроме Призрака.");
        }

        return target;
    }

    private static void ValidateColumns(GameState state, IReadOnlyList<int> columns)
    {
        if (columns.Count != state.Board.Count || columns.Any(c => c < 0 || c >= state.Settings.Columns))
        {
            throw GameRuleException.Validation($"Нужно назвать по одной улике в каждом из {state.Board.Count} рядов.");
        }
    }
}
