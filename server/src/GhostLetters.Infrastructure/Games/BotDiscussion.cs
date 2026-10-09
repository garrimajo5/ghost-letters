using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using System.Text.RegularExpressions;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Только публичные реплики текущего обсуждения, в хронологическом порядке.</summary>
public sealed record DiscussionLine(Guid Author, string Text, IReadOnlyList<string> Cards, DateTimeOffset At);

/// <summary>Ограниченный обмен версиями, вопросами и ответами перед финалом.</summary>
public static class BotDiscussion
{
    public const int MaxMessages = 6;
    private static readonly Regex Who = Words(@"\bкто\b");

    private static readonly IReadOnlyDictionary<Category, Regex> RowWords = new Dictionary<Category, Regex>
    {
        [Category.Motive] = Words(@"\bмотив(?:а|у|ом|е|ы|ов|ам|ами|ах)?\b"),
        [Category.Place] = Words(@"\bмест(?:о|а|у|ом|е)\b"),
        [Category.Method] = Words(@"\bспособ(?:а|у|ом|е|ы|ов|ам|ами|ах)?\b"),
        [Category.Secret] = Words(@"\bтайн(?:а|ы|е|у|ой|ою|ам|ами|ах)?\b"),
    };

    private static Regex Words(string pattern) => new(pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static List<int> MentionedRows(PlayerView view, DiscussionLine message) =>
        Enumerable.Range(0, view.Board.Count)
            .Where(r => RowWords.TryGetValue(view.Board[r].Category, out var words) && words.IsMatch(message.Text)).ToList();

    /// <summary>Explicit row words take precedence over attached cards, which may contain the whole version.</summary>
    private static int? FocusRow(PlayerView view, DiscussionLine message)
    {
        var mentioned = MentionedRows(view, message);
        if (mentioned.Count > 0) return mentioned.Count == 1 ? mentioned[0] : null;
        var attached = Enumerable.Range(0, view.Board.Count)
            .Where(r => message.Cards.Any(view.Board[r].Cards.Contains)).ToList();
        return attached.Count == 1 ? attached[0] : null;
    }

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
                .OrderByDescending(h => tags.Similarity(h.Id, card, mind.Personality.Attention, mind.Personality.Details, mind.Personality.SecondaryMeanings)).FirstOrDefault();
            if (hint.Id is null || tags.Similarity(hint.Id, card, mind.Personality.Attention, mind.Personality.Details, mind.Personality.SecondaryMeanings) <= 0)
                return "связь с открытыми уликами слабая, пока предположение";
            var reason = tags.Explain(hint.Id, card, mind.Personality.Attention, mind.Personality.Details, mind.Personality.SecondaryMeanings);
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
        bool Addresses(DiscussionLine m, Guid target) => m.Text.StartsWith(Name(target) + ",", StringComparison.OrdinalIgnoreCase) ||
            m.Text.StartsWith(Name(target).Replace("Бот ", "") + ",", StringComparison.OrdinalIgnoreCase);
        bool Addressed(DiscussionLine m) => Addresses(m, me.Id);
        bool AddressedElsewhere(DiscussionLine m) => mind.Names.Keys.Any(id => id != me.Id && Addresses(m, id));
        // A question can arrive before this bot's opening summary. Do not lose it
        // merely because the summary was sent later; only an answer consumes it.
        var question = messages.LastOrDefault(m => m.Author != me.Id && m.Text.Contains('?') &&
            (Addressed(m) || (!AddressedElsewhere(m) && Who.IsMatch(m.Text) && m.Text.Contains("голос", StringComparison.OrdinalIgnoreCase))) &&
            !own.Any(o => o.At > m.At && o.Text.StartsWith(Name(m.Author) + ",", StringComparison.OrdinalIgnoreCase) &&
                (o.Text.Contains("отвечаю", StringComparison.OrdinalIgnoreCase) || o.Text.Contains("уточню", StringComparison.OrdinalIgnoreCase))));
        // Acknowledge the first answer to an actual outgoing question, not replies to
        // acknowledgements. Later public opinions still contribute to mind/plan.
        var answer = fresh.LastOrDefault(m => Addressed(m) && !m.Text.Contains('?') &&
            own.Any(q => q.Text.Contains('?') && q.At < m.At && Addresses(q, m.Author) &&
                !messages.Any(previous => previous.Author == m.Author && previous.At > q.At && previous.At < m.At &&
                    Addressed(previous) && !previous.Text.Contains('?'))));
        var incoming = question ?? answer;
        if (incoming is not null)
        {
            if (question is not null && question.Cards.Count == 0 &&
                (question.Text.Contains("за кого", StringComparison.OrdinalIgnoreCase) || question.Text.Contains("кого подозрева", StringComparison.OrdinalIgnoreCase)))
                return Line($"{Name(incoming.Author)}, отвечаю про подозрения.{suspicion}");
            var mentioned = MentionedRows(view, incoming);
            if (question is not null && mentioned.Count > 1)
            {
                // A direct question may name several rows. Answer each once, without
                // turning a whole-version attachment into a request about unrelated rows.
                var prefix = $"{Name(incoming.Author)}, отвечаю по названным рядам:";
                var lines = mentioned.Select(row =>
                {
                    var chosen = view.Board[row].Cards[plan[row]];
                    var offered = incoming.Cards.FirstOrDefault(view.Board[row].Cards.Contains);
                    var agreement = offered is null ? "мой выбор" : offered == chosen ? "наши версии совпадают" : "пока не согласен";
                    return $"• {Where(row, plan[row])} — {agreement}. {Argument(row)}.";
                });
                var response = prefix + "\n" + string.Join("\n", lines);
                if (response.Length > ChatService.MaxTextLength)
                    response = prefix + "\n" + string.Join("; ", mentioned.Select(row => Where(row, plan[row])));
                var selected = mentioned.Select(row => view.Board[row].Cards[plan[row]]).Take(ChatService.MaxCards).ToList();
                return (response, selected, selected.Select(_ => "думаю, эта").ToList());
            }
            var focus = FocusRow(view, incoming);
            if (focus is null)
            {
                if (question is null || incoming.Text.Contains("верси", StringComparison.OrdinalIgnoreCase) ||
                    incoming.Text.Contains("всем ряд", StringComparison.OrdinalIgnoreCase))
                    return Line(Summary($"{Name(incoming.Author)}, отвечаю по всей версии; вот мой выбор и основания:"));
                // Do not invent the subject of a vague question or attach an arbitrary first-row card.
                return ($"{Name(incoming.Author)}, уточню: ты спрашиваешь про какой ряд? Назови его или приложи карту — сравним улики.", [], []);
            }
            var row = focus.Value;
            var offered = incoming.Cards.FirstOrDefault(view.Board[row].Cards.Contains);
            var comparison = offered is null ? "вот мой текущий выбор" : offered == view.Board[row].Cards[plan[row]] ? "здесь наши версии совпадают" : "здесь я пока не согласен";
            var text = $"{Name(incoming.Author)}, {(question is null ? "сверил твой ответ с уликами" : "отвечаю про голосование")}: {comparison}. " +
                $"Сейчас выберу {Where(row, plan[row])}. {Argument(row)}. " +
                "Остальные ряды моей версии — на прикреплённых картах." + suspicion;
            return Line(text.Length <= ChatService.MaxTextLength ? text : $"{Name(incoming.Author)}, отвечаю: сейчас выберу {Where(row, plan[row])}. Версия основана на уликах всех раундов.", row);
        }
        if (!own.Any(m => m.Text.Contains('?')))
        {
            var others = view.Players.Where(p => p.Id != me.Id && !p.IsGhost && mind.Names.ContainsKey(p.Id)).OrderBy(p => p.Seat).ToList();
            var mySeat = view.Players.First(p => p.Id == me.Id).Seat;
            var followAffinity = mind.Personality.Social.Influence > 0 && rng.NextDouble() < mind.Personality.Social.Influence;
            var target = others.OrderByDescending(p => followAffinity ? mind.AffinityBias(p.Id) : 0)
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
