using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Domain.Rules;

namespace GhostLetters.Infrastructure.Games;

/// <summary>
/// Простой бот для отладки: делает случайный допустимый ход по своей проекции (без подглядывания).
/// Не лайкает, не поднимает руку и не номинирует. Настоящие ИИ-боты — этап 7.
/// </summary>
public static class BotPlayer
{
    private static readonly HashSet<string> Passive = [nameof(Like), nameof(RaiseHand), nameof(GiveFloor)];

    /// <summary>Ход бота или null, если ходить не нужно.</summary>
    public static GameCommand? Decide(PlayerView view, Random rng)
    {
        var allowed = view.AllowedCommands.Where(c => !Passive.Contains(c)).ToList();
        if (allowed.Count == 0 || view.Me is not { } me)
        {
            return null;
        }

        var type = allowed.Contains(nameof(Discard)) ? nameof(Discard) : allowed[rng.Next(allowed.Count)];
        var columns = view.Board.Count == 0 ? 5 : view.Board[0].Cards.Count;
        var others = view.Players.Where(p => !p.IsGhost && p.Id != me.Id).Select(p => p.Id).ToList();
        var stage = view.Finale?.CurrentStage;

        return type switch
        {
            nameof(AckRole) => new AckRole(),
            nameof(ChooseTruth) => new ChooseTruth(view.Board.Select(_ => rng.Next(columns)).ToList()),
            nameof(NameTruth) => new NameTruth(view.Board.Select(_ => rng.Next(columns)).ToList()),
            nameof(GiveFirstClue) => new GiveFirstClue(rng.Next(2) == 0 || me.Hand.Count == 0 ? null : me.Hand[rng.Next(me.Hand.Count)]),
            nameof(SendLetter) => new SendLetter(me.Hand.OrderBy(_ => rng.Next())
                .Take(GameDefaults.LettersPerPlayer(view.Players.Count)).ToList()),
            nameof(RevealHints) => new RevealHints((view.MailboxForGhost ?? []).OrderBy(_ => rng.Next()).Take(rng.Next(1, 3)).ToList()),
            nameof(Discard) => new Discard(rng.Next(3) == 0 && me.Hand.Count > 0 ? me.Hand[rng.Next(me.Hand.Count)] : null),
            nameof(EndTurn) => new EndTurn(),
            nameof(ReadyNextRound) => new ReadyNextRound(),
            nameof(ReadyRevote) => new ReadyRevote(),
            nameof(CastVote) when stage is { Kind: VoteStageKind.Row } =>
                new CastVote(stage.CandidateColumns[rng.Next(stage.CandidateColumns.Count)], null),
            nameof(CastVote) => stage?.CandidateSuspects.Where(s => s != me.Id).ToList() is { Count: > 0 } suspects
                ? new CastVote(null, suspects[rng.Next(suspects.Count)])
                : new CastVote(null, null),
            nameof(HuntPick) => others.Count == 0 ? null : new HuntPick(others[rng.Next(others.Count)], Role.Witness),
            nameof(BlackmailerPick) => others.Count == 0 ? null : new BlackmailerPick(others[rng.Next(others.Count)]),
            nameof(Nominate) => new Nominate(null, null),
            nameof(AwardVote) => new AwardVote(null),
            _ => null,
        };
    }
}
