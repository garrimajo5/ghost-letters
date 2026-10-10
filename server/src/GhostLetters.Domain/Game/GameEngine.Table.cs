using System.Text.RegularExpressions;
using GhostLetters.Domain.Roles;

namespace GhostLetters.Domain.Game;

/// <summary>Доска улик: нити от подсказок и писем к картам поля, булавки версий, проверки и заявления о письмах.</summary>
public static partial class GameEngine
{
    public const int MaxTableOps = 6;
    public const int MaxThreadsPerPlayer = 30;
    public const int MaxReasonLength = 60;

    /// <summary>Можно ли сейчас менять доску (кроме булавок в голосовании).</summary>
    public static bool TableOpen(GameState state) =>
        state.Result is null && state.Phase is Phase.FirstClue or Phase.Mailbox or Phase.GhostPick or Phase.Refill
            or Phase.Discussion or Phase.VoteTie;

    /// <summary>В голосовании можно только переставлять булавки — они подсказывают выбор.</summary>
    public static bool TablePinsOnly(GameState state) => state.Result is null && state.Phase == Phase.Voting;

    public static bool CanPostTable(GameState state, PlayerState player) =>
        player.Role != Role.Ghost && (TableOpen(state) || TablePinsOnly(state));

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,64}$")]
    private static partial Regex CardIdPattern();

    private static void ApplyTablePost(GameState state, PlayerState actor, TablePost post, List<GameEvent> events)
    {
        if (actor.Role == Role.Ghost)
        {
            throw GameRuleException.NotAllowed("Призрак видит доску, но ничего на неё не кладёт.");
        }

        if (!TableOpen(state) && !TablePinsOnly(state))
        {
            throw GameRuleException.NotAllowed("Сейчас доску улик менять нельзя.");
        }

        var ops = post.Ops ?? [];
        if (ops.Count is 0 or > MaxTableOps)
        {
            throw GameRuleException.Validation($"В одном посте от 1 до {MaxTableOps} изменений.");
        }

        foreach (var op in ops)
        {
            if (TablePinsOnly(state) && op.Kind is not (TableOpKind.Pin or TableOpKind.Unpin))
            {
                throw GameRuleException.NotAllowed("Во время голосования можно только переставить булавку.");
            }

            ApplyTableOp(state, actor, op);
        }

        events.Add(new GameEvent("TablePosted", actor.Id, Detail: ops.Count.ToString()));
    }

    private static void ApplyTableOp(GameState state, PlayerState actor, TableOp op)
    {
        var table = state.Table;
        switch (op.Kind)
        {
            case TableOpKind.Link:
                Link(state, actor, op);
                break;
            case TableOpKind.Unlink:
            {
                var thread = Thread(table, op.Thread);
                if (thread.Author != actor.Id)
                {
                    throw GameRuleException.NotAllowed("Убрать можно только свою нить.");
                }

                table.Threads.Remove(thread);
                break;
            }
            case TableOpKind.Endorse or TableOpKind.Dispute:
            {
                var thread = Thread(table, op.Thread);
                if (thread.Author == actor.Id)
                {
                    throw GameRuleException.Validation("Со своей нитью соглашаться не нужно.");
                }

                thread.EndorsedBy.Remove(actor.Id);
                thread.DisputedBy.Remove(actor.Id);
                (op.Kind == TableOpKind.Endorse ? thread.EndorsedBy : thread.DisputedBy).Add(actor.Id);
                break;
            }
            case TableOpKind.Clear:
            {
                var thread = Thread(table, op.Thread);
                thread.EndorsedBy.Remove(actor.Id);
                thread.DisputedBy.Remove(actor.Id);
                break;
            }
            case TableOpKind.Pin:
            {
                var (row, column) = BoardCell(state, op.Target);
                table.Pins.RemoveAll(p => p.Author == actor.Id && p.Row == row);
                table.Pins.Add(new TablePin { Author = actor.Id, Row = row, Column = column });
                break;
            }
            case TableOpKind.Unpin:
            {
                var (row, _) = BoardCell(state, op.Target);
                table.Pins.RemoveAll(p => p.Author == actor.Id && p.Row == row);
                break;
            }
            case TableOpKind.Check:
            {
                var (row, column) = BoardCell(state, op.Target);
                var card = state.Board[row].Cards[column];
                if (!table.Checks.Any(c => c.Author == actor.Id && c.Card == card))
                {
                    table.Checks.Add(new TableCheck { Author = actor.Id, Card = card });
                }

                break;
            }
            case TableOpKind.Uncheck:
                BoardCell(state, op.Target);
                table.Checks.RemoveAll(c => c.Author == actor.Id && c.Card == op.Target);
                break;
            case TableOpKind.Claim:
            {
                if (op.Round is not { } round || round < 1 || round > state.Round)
                {
                    throw GameRuleException.Validation("Письмо можно назвать только за уже начавшийся раунд.");
                }

                if (op.Source is not { } card || !CardIdPattern().IsMatch(card))
                {
                    throw GameRuleException.Validation("Назовите карту письма.");
                }

                var claim = table.Claims.FirstOrDefault(c => c.Author == actor.Id && c.Round == round);
                if (claim is null)
                {
                    table.Claims.Add(new LetterClaim { Author = actor.Id, Round = round, Card = card });
                }
                else
                {
                    claim.Card = card;
                }

                break;
            }
            default:
                throw GameRuleException.Validation("Неизвестное изменение доски.");
        }
    }

    private static void Link(GameState state, PlayerState actor, TableOp op)
    {
        var table = state.Table;
        if (op.SourceKind is not { } kind || op.Source is not { } source || op.Stance is not { } stance)
        {
            throw GameRuleException.Validation("Для нити нужны улика, карта поля и «за» или «против».");
        }

        var known = kind switch
        {
            TableSourceKind.Hint => state.Hints.Any(h => h.Cards.Contains(source)),
            TableSourceKind.Letter => table.Claims.Any(c => c.Card == source),
            _ => false,
        };
        if (!known)
        {
            throw GameRuleException.Validation(kind == TableSourceKind.Hint
                ? "Нить тянется только от открытой подсказки Призрака."
                : "Нить тянется только от письма, которое кто-то назвал своим.");
        }

        var (row, column) = BoardCell(state, op.Target);
        var target = state.Board[row].Cards[column];
        var reason = op.Reason?.Trim();
        if (reason is { Length: > MaxReasonLength })
        {
            throw GameRuleException.Validation($"Причина — не длиннее {MaxReasonLength} символов.");
        }

        reason = string.IsNullOrEmpty(reason) ? null : reason;
        var existing = table.Threads.FirstOrDefault(t => t.Author == actor.Id && t.SourceKind == kind && t.Source == source && t.Target == target);
        if (existing is not null)
        {
            if (existing.Stance != stance)
            {
                // Нить сменила смысл — прежнее согласие других к ней больше не относится.
                existing.EndorsedBy.Clear();
                existing.DisputedBy.Clear();
            }

            existing.Stance = stance;
            existing.Reason = reason;
            existing.Round = state.Round;
            existing.Version = state.Version + 1;
            return;
        }

        if (table.Threads.Count(t => t.Author == actor.Id) >= MaxThreadsPerPlayer)
        {
            throw GameRuleException.Validation($"У игрока не больше {MaxThreadsPerPlayer} нитей — уберите старые.");
        }

        table.Threads.Add(new TableThread
        {
            Id = table.NextThreadId++,
            Author = actor.Id,
            Round = state.Round,
            SourceKind = kind,
            Source = source,
            Target = target,
            Stance = stance,
            Reason = reason,
            Version = state.Version + 1,
        });
    }

    private static TableThread Thread(TableState table, int? id) =>
        table.Threads.FirstOrDefault(t => t.Id == id) ?? throw GameRuleException.Validation("Такой нити на доске нет.");

    /// <summary>Ряд и столбец карты поля; бросает, если карты на поле нет.</summary>
    public static (int Row, int Column) BoardCell(GameState state, string? card)
    {
        for (var r = 0; r < state.Board.Count; r++)
        {
            var c = card is null ? -1 : state.Board[r].Cards.IndexOf(card);
            if (c >= 0)
            {
                return (r, c);
            }
        }

        throw GameRuleException.Validation("Такой карты на поле нет.");
    }
}
