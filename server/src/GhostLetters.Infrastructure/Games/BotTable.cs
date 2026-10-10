using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Уже сказанный ботом шаг хода мысли за этот раунд (канал table).</summary>
public sealed record BotThought(DateTimeOffset At, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes);

/// <summary>Один шаг бота у доски улик: мысль вслух и, возможно, изменения доски.</summary>
public sealed record BotTableStep(string Thought, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes, IReadOnlyList<TableOp> Ops);

/// <summary>
/// Бот у доски улик «думает вслух» (экран D02): смотрит на подсказку → тянет нить, называет своё исчезнувшее
/// письмо и вычёркивает похожую карту, соглашается или спорит с чужими нитями, закрепляет версию булавками.
/// Читает только то, что бот и так знает: свою проекцию партии, свою версию (<see cref="BotPlayer.Plan"/>) и доску.
/// Команда Убийцы раскладывает ту же публичную версию, что называет вслух, и не раскрывает свои письма.
/// </summary>
public static class BotTable
{
    public const int MaxStepsPerRound = 10;
    public const int MaxLinksPerRound = 3;
    public const int MaxEndorsementsPerRound = 2;
    public static readonly TimeSpan Gap = TimeSpan.FromSeconds(6);
    private const double LinkThreshold = .15;
    private const double CheckThreshold = .25;

    /// <summary>Версия бота меняется от новых сведений, а не от такта к такту.</summary>
    public static Random Random(GameState state, Guid botId) =>
        new(unchecked(state.Seed * 397 ^ botId.GetHashCode() * 31 + state.Round));

    public static bool Due(IReadOnlyList<BotThought> recent, DateTimeOffset now) =>
        recent.Count == 0 || now - recent[^1].At >= Gap;

    public static BotTableStep? Next(PlayerView view, CardTags tags, BotMind? mind, IReadOnlyList<int> plan, IReadOnlyList<BotThought> recent)
    {
        if (view.Me is not { } me || me.Role == Role.Ghost || view.Table is not { CanPost: true } table) return null;
        if (plan.Count < view.Board.Count) return null;
        var p = mind?.Personality ?? BotPersonality.Default;
        double Sim(string a, string b) => tags.Similarity(a, b, p.Attention, p.Details, p.SecondaryMeanings);
        string Name(Guid id) => mind?.Names.GetValueOrDefault(id) ?? "игроком";
        int Count(string note) => recent.Count(t => t.Notes.Contains(note));
        var last = recent.Count > 0 ? recent[^1] : null;

        (int Row, int Column)? Cell(string card)
        {
            for (var r = 0; r < view.Board.Count; r++)
            {
                for (var c = 0; c < view.Board[r].Cards.Count; c++)
                    if (view.Board[r].Cards[c] == card) return (r, c);
            }
            return null;
        }

        string Where(string card) => Cell(card) is { } cell ? $"{CategoryWord(view.Board[cell.Row].Category)} {cell.Column + 1}" : "карта";

        string Source(TableSourceKind kind, string card)
        {
            if (kind == TableSourceKind.Letter)
                return table.Claims.FirstOrDefault(c => c.Card == card) is { } claim ? $"письмо р.{claim.Round}" : "письмо";
            var hint = view.Hints.FirstOrDefault(h => h.Cards.Contains(card));
            return hint is null ? "подсказка" : hint.Round == 0 ? "зацепка" : $"р.{hint.Round}";
        }

        string Target(int row) => view.Board[row].Cards[plan[row]];

        BotTableStep? Pins()
        {
            var cards = new List<string>();
            for (var r = 0; r < view.Board.Count; r++)
            {
                var pin = table.Pins.FirstOrDefault(x => x.Author == me.Id && x.Row == r);
                if (pin is null || pin.Column != plan[r]) cards.Add(Target(r));
            }
            if (cards.Count == 0) return null;
            cards = cards.Take(Math.Min(ChatService.MaxCards, GameEngine.MaxTableOps)).ToList();
            var text = (table.PinsOnly ? "Переставляю булавки: " : "Закрепляю версию: ") + string.Join(", ", cards.Select(Where)) + ".";
            return new(text, cards, cards.Select(_ => "версия").ToList(),
                cards.Select(c => new TableOp(TableOpKind.Pin, Target: c)).ToList());
        }

        // В голосовании — только булавки, без лимита шагов; последний шаг раунда всегда оставляем на версию.
        if (table.PinsOnly) return Pins();
        if (recent.Count >= MaxStepsPerRound) return null;
        if (recent.Count >= MaxStepsPerRound - 1) return Pins();

        BotTableStep? Thread()
        {
            if (Count("нить") >= MaxLinksPerRound ||
                table.Threads.Count(t => t.Author == me.Id) >= GameEngine.MaxThreadsPerPlayer - 2) return null;
            var hints = view.Hints.SelectMany(h => h.Cards).Distinct().ToList();
            if (hints.Count == 0) return null;
            var best = Enumerable.Range(0, view.Board.Count)
                .Select(Target)
                .Where(target => !table.Threads.Any(t => t.Author == me.Id && t.Target == target))
                .Select(target => hints.Select(h => (Hint: h, Target: target, Score: Sim(h, target)))
                    .OrderByDescending(x => x.Score).ThenBy(x => x.Hint, StringComparer.Ordinal).First())
                .Where(x => x.Score >= LinkThreshold)
                .OrderByDescending(x => x.Score).ThenBy(x => x.Target, StringComparer.Ordinal)
                .FirstOrDefault();
            if (best.Hint is null) return null;
            var (hint, target, _) = best;
            if (last is not null && last.Notes.Contains("смотрю") && last.Cards.SequenceEqual([hint, target]))
            {
                var reason = tags.ReasonChip(hint, target, p.Attention, p.SecondaryMeanings);
                var why = reason switch { "предмет" => ": общий предмет", "форма" => ": похожая форма", "цвет" => ": тот же цвет", _ => "" };
                return new($"Подсказка {Source(TableSourceKind.Hint, hint)} — {Where(target)}{why}. Тяну нить «за».",
                    [hint, target], ["улика", "нить"],
                    [new TableOp(TableOpKind.Link, TableSourceKind.Hint, hint, target, TableStance.For, reason)]);
            }
            return new($"Смотрю на подсказку {Source(TableSourceKind.Hint, hint)} и сравниваю с картой «{Where(target)}»…",
                [hint, target], ["улика", "смотрю"], []);
        }

        BotTableStep? Letters()
        {
            // Команда Убийцы не называет свои письма: так она не выдаёт, что знает ответ.
            if (me.Role.IsKillerTeam()) return null;
            foreach (var letter in me.Letters.Where(l => l.Revealed == false).OrderBy(l => l.Round))
            {
                if (!table.Claims.Any(c => c.Author == me.Id && c.Round == letter.Round))
                    return new($"Моё письмо р.{letter.Round} Призрак не открыл — оно исчезло. Кладу его на доску.",
                        [letter.CardId], ["письмо"],
                        [new TableOp(TableOpKind.Claim, Source: letter.CardId, Round: letter.Round)]);
                var closest = view.Board.SelectMany(r => r.Cards)
                    .Select(c => (Card: c, Score: Sim(letter.CardId, c)))
                    .OrderByDescending(x => x.Score).ThenBy(x => x.Card, StringComparer.Ordinal).First();
                if (closest.Score < CheckThreshold || table.Checks.Any(k => k.Author == me.Id && k.Card == closest.Card)) continue;
                return new($"Письмо р.{letter.Round} похоже на «{Where(closest.Card)}», а оно исчезло. Вычёркиваю.",
                    [letter.CardId, closest.Card], ["письмо", "проверил"],
                    [new TableOp(TableOpKind.Check, Target: closest.Card)]);
            }
            return null;
        }

        BotTableStep? Social()
        {
            foreach (var t in table.Threads.OrderBy(t => t.Id))
            {
                if (t.Author == me.Id || t.EndorsedBy.Contains(me.Id) || t.DisputedBy.Contains(me.Id) ||
                    t.Stance != TableStance.For || Cell(t.Target) is not { } cell) continue;
                var source = Source(t.SourceKind, t.Source);
                if (plan[cell.Row] == cell.Column)
                {
                    if (Count("согласен") >= MaxEndorsementsPerRound) continue;
                    return new($"Согласен с {Name(t.Author)}: {source} — это {Where(t.Target)}.",
                        [t.Source, t.Target], ["улика", "согласен"], [new TableOp(TableOpKind.Endorse, Thread: t.Id)]);
                }

                var mine = Target(cell.Row);
                if (Count("оспорил") >= 1 ||
                    BotPlayer.Evidence(view, tags, mind, t.Target) > BotPlayer.Evidence(view, tags, mind, mine) - .1) continue;
                return new($"Не согласен с {Name(t.Author)}: по-моему, это скорее {Where(mine)}, а не {Where(t.Target)}.",
                    [t.Source, t.Target, mine], ["улика", "оспорил", "думаю, эта"],
                    [new TableOp(TableOpKind.Dispute, Thread: t.Id)]);
            }
            return null;
        }

        // Начатый «смотрю» доводим до нити; первая нить — до писем, как в прототипе D02.
        if (last is not null && last.Notes.Contains("смотрю") && Thread() is { } link) return link;
        if (Count("нить") == 0 && Thread() is { } first) return first;
        return Letters() ?? Thread() ?? Social() ?? Pins();
    }

    private static string CategoryWord(Category category) => category switch
    {
        Category.Motive => "мотив",
        Category.Place => "место",
        Category.Method => "способ",
        _ => "тайна",
    };
}
