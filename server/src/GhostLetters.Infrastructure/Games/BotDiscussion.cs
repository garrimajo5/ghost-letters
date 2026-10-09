using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Только публичные реплики текущего обсуждения, в хронологическом порядке.</summary>
public sealed record DiscussionLine(Guid Author, string Text, IReadOnlyList<string> Cards, DateTimeOffset At);

/// <summary>Ограниченный обмен версиями, вопросами и ответами перед финалом.</summary>
public static class BotDiscussion
{
    public const int MaxMessages = 6;

    public static bool ShouldWait(Guid botId, IReadOnlyList<DiscussionLine> messages, DateTimeOffset now)
    {
        var own = messages.Where(m => m.Author == botId).ToList();
        if (own.Count == 0) return true;
        if (own.Count >= MaxMessages || now - own[0].At >= TimeSpan.FromMinutes(2)) return false;
        return now - own[0].At < TimeSpan.FromSeconds(30) || now - messages[^1].At < TimeSpan.FromSeconds(12);
    }

    public static (string Text, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes)? Compose(
        PlayerView view, CardTags tags, BotMind mind, IReadOnlyList<DiscussionLine> messages, Random rng)
    {
        if (view.Me is not { Role: not Role.Ghost } me || view.Board.Count == 0) return null;
        var own = messages.Where(m => m.Author == me.Id).ToList();
        if (own.Count >= MaxMessages) return null;
        var plan = BotPlayer.Plan(view, tags, mind, rng);
        var cards = view.Board.Select((r, i) => r.Cards[plan[i]]).Take(ChatService.MaxCards).ToList();
        string Where(int r, int c) => $"{view.Board[r].Category switch { Category.Motive => "мотив", Category.Place => "место", Category.Method => "способ", _ => "тайна" }} {c + 1}";
        string Name(Guid id) => mind.Names.GetValueOrDefault(id, "Игрок");
        var suspicion = BotPlayer.DiscussionSuspicion(view, tags, mind, rng);
        if (string.IsNullOrWhiteSpace(suspicion)) suspicion = " По игрокам пока недостаточно оснований для уверенного обвинения.";
        string Argument(int row)
        {
            var card = view.Board[row].Cards[plan[row]];
            var hint = view.Hints.SelectMany(h => h.Cards.Select((id, i) => (Id: id, h.Round, Number: i + 1)))
                .OrderByDescending(h => tags.Similarity(h.Id, card, mind.Personality.Attention, mind.Personality.Details)).FirstOrDefault();
            if (hint.Id is null || tags.Similarity(hint.Id, card, mind.Personality.Attention, mind.Personality.Details) <= 0)
                return "связь с открытыми уликами слабая, пока предположение";
            var reason = tags.Explain(hint.Id, card, mind.Personality.Attention, mind.Personality.Details);
            return $"Подсказка р.{hint.Round} №{hint.Number}: {(reason.Length <= 120 ? reason : "сходство изображений")}";
        }
        (string Text, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes) Line(string text, int? focus = null) =>
            (text, focus is { } r ? cards.OrderByDescending(c => c == view.Board[r].Cards[plan[r]]).ToList() : cards,
                cards.Select(_ => "думаю, эта").ToList());
        string Overview(string prefix) => prefix + "\n" + string.Join("\n", view.Board.Select((_, r) =>
            $"• {Where(r, plan[r])} — {Argument(r)}.")) +
            (me.Letters.Any(l => l.Revealed == false) ? "\nУчёл и свои исчезнувшие письма: их вес зависит от того, насколько я доверяю такой проверке." : "");
        // Keep every row even with unusually long imported detail labels.
        string Summary(string prefix)
        {
            var text = Overview(prefix) + suspicion;
            return text.Length <= ChatService.MaxTextLength ? text : prefix + "\n" +
                string.Join("; ", view.Board.Select((_, r) => Where(r, plan[r]))) + ". Опираюсь на улики всех раундов.";
        }
        if (own.Count == 0) return Line(Summary("Собрал версию по всем открытым уликам за партию:"));

        var fresh = messages.Where(m => m.Author != me.Id && m.At > own[^1].At).ToList();
        var myName = Name(me.Id);
        bool Addressed(DiscussionLine m) => m.Text.StartsWith(myName + ",", StringComparison.OrdinalIgnoreCase) ||
            m.Text.StartsWith(myName.Replace("Бот ", "") + ",", StringComparison.OrdinalIgnoreCase);
        // A question can arrive before this bot's opening summary. Do not lose it
        // merely because the summary was sent later; only an answer consumes it.
        var question = messages.LastOrDefault(m => m.Author != me.Id && m.Text.Contains('?') &&
            (Addressed(m) || (m.Text.Contains("Кто", StringComparison.OrdinalIgnoreCase) && m.Text.Contains("голос", StringComparison.OrdinalIgnoreCase))) &&
            !own.Any(o => o.At > m.At && o.Text.StartsWith(Name(m.Author) + ",", StringComparison.OrdinalIgnoreCase) &&
                o.Text.Contains("отвечаю", StringComparison.OrdinalIgnoreCase)));
        var answer = fresh.LastOrDefault(m => Addressed(m) && !m.Text.Contains('?'));
        var incoming = question ?? answer;
        if (incoming is not null)
        {
            if (question is not null && question.Cards.Count == 0 &&
                (question.Text.Contains("за кого", StringComparison.OrdinalIgnoreCase) || question.Text.Contains("кого подозрева", StringComparison.OrdinalIgnoreCase)))
                return Line($"{Name(incoming.Author)}, отвечаю про подозрения.{suspicion}");
            var row = incoming.Cards.Select(c => Enumerable.Range(0, view.Board.Count)
                .FirstOrDefault(r => view.Board[r].Cards.Contains(c), -1)).FirstOrDefault(r => r >= 0, 0);
            var offered = incoming.Cards.FirstOrDefault(view.Board[row].Cards.Contains);
            var comparison = offered is null ? "вот мой текущий выбор" : offered == view.Board[row].Cards[plan[row]] ? "здесь наши версии совпадают" : "здесь я пока не согласен";
            var text = $"{Name(incoming.Author)}, {(question is null ? "сверил твой ответ с уликами" : "отвечаю про голосование")}: {comparison}. " +
                $"Сейчас выберу {Where(row, plan[row])}. {Argument(row)}. " +
                "Остальные ряды моей версии — на прикреплённых картах." + suspicion;
            return Line(text.Length <= ChatService.MaxTextLength ? text : $"{Name(incoming.Author)}, сейчас выберу {Where(row, plan[row])}. Версия основана на уликах всех раундов.", row);
        }
        if (!own.Any(m => m.Text.Contains('?')))
        {
            var others = view.Players.Where(p => p.Id != me.Id && !p.IsGhost && mind.Names.ContainsKey(p.Id)).OrderBy(p => p.Seat).ToList();
            var mySeat = view.Players.First(p => p.Id == me.Id).Seat;
            var target = others.OrderByDescending(p => mind.AffinityBias(p.Id))
                .ThenBy(p => p.Seat > mySeat ? 0 : 1).ThenBy(p => p.Seat).FirstOrDefault();
            if (target is null) return null;
            var row = me.Id.GetHashCode() % view.Board.Count;
            row = Math.Abs(row);
            return Line($"{Name(target.Id)}, за какую карту в ряду «{Where(row, plan[row]).Split(' ')[0]}» будешь голосовать? " +
                $"Я выбираю {Where(row, plan[row])}. {Argument(row)}. Какие улики поддерживают твою версию? А кого подозреваешь?", row);
        }
        if (fresh.Count > 0 && !own.Any(m => m.Text.StartsWith("Итог", StringComparison.Ordinal)))
            return Line(Summary("Итог после обсуждения; пока голосовал бы так:"));
        return null;
    }
}
