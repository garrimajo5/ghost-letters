using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;
using System.Text.RegularExpressions;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Только публичные реплики текущего обсуждения, в хронологическом порядке.</summary>
public sealed record DiscussionLine(Guid Author, string Text, IReadOnlyList<string> Cards, DateTimeOffset At, int? DurationMs = null, IReadOnlyList<string>? Notes = null);

/// <summary>Ограниченный обмен версиями, вопросами и ответами перед финалом.</summary>
public static class BotDiscussion
{
    public const int MaxMessages = 6;
    private static readonly Regex Who = Words(@"\bкто\b");

    public static TimeSpan SpeechTime(string text, int? durationMs = null) => TimeSpan.FromSeconds(
        Math.Max(Math.Clamp(2 + text.Length / 14.0, 4, 45), Math.Clamp((durationMs ?? 0) / 1000.0, 0, 60)));

    // A public story can differ from a killer's private vote. Decide the mask
    // once per game/player, without exposing that decision in public text.
    public static IReadOnlyList<int> PublicPlan(PlayerView view, CardTags tags, BotMind mind)
    {
        var calm = view.Me?.Role.IsKillerTeam() == true
            ? view with { Truth = null, Me = view.Me with { Role = Role.Detective } } : view;
        var plan = BotPlayer.Plan(calm, tags, mind, new Random(0)).ToArray();
        if (view.Me?.Role.IsKillerTeam() == true && view.Truth is { } truth && plan.Length > 0 &&
            view.Me.Id.ToByteArray()[0] / 255.0 < mind.Personality.Risk)
        {
            var row = view.Me.Id.ToByteArray()[1] % plan.Length;
            var options = Enumerable.Range(0, view.Board[row].Cards.Count).Where(c => c != truth[row]).ToList();
            if (options.Count > 0) plan[row] = options.OrderByDescending(c => BotPlayer.Evidence(calm, tags, mind, view.Board[row].Cards[c])).First();
        }
        return plan;
    }

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
        if (now - messages[^1].At < SpeechTime(messages[^1].Text, messages[^1].DurationMs)) return true;
        if (own.Count >= MaxMessages || now - own[0].At >= TimeSpan.FromMinutes(2)) return false;
        return now - own[0].At < TimeSpan.FromSeconds(30) || now - messages[^1].At < TimeSpan.FromSeconds(12);
    }

    public static (string Text, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes)? Compose(
        PlayerView view, CardTags tags, BotMind mind, IReadOnlyList<DiscussionLine> messages, Random rng)
    {
        if (view.Me is not { Role: not Role.Ghost } me || view.Board.Count == 0) return null;
        var own = messages.Where(m => m.Author == me.Id).ToList();
        if (own.Count >= MaxMessages) return null;
        var plan = PublicPlan(view, tags, mind);
        var cards = view.Board.Select((r, i) => r.Cards[plan[i]]).Take(ChatService.MaxCards).ToList();
        string Where(int r, int c) => $"{view.Board[r].Category switch { Category.Motive => "мотив", Category.Place => "место", Category.Method => "способ", _ => "тайна" }} {c + 1}";
        string Name(Guid id) => mind.Names.GetValueOrDefault(id, "Игрок");
        string Revision(int row)
        {
            // Read only this bot's previous public choices. A multi-card comparison
            // within one row does not establish a definite previous preference.
            var previous = own.AsEnumerable().Reverse()
                .Select(m => m.Cards.Where(view.Board[row].Cards.Contains).Distinct().ToList())
                .FirstOrDefault(matches => matches.Count == 1);
            if (previous is null || previous[0] == view.Board[row].Cards[plan[row]]) return "";
            var column = view.Board[row].Cards.ToList().IndexOf(previous[0]);
            return $"Пересмотрел выбор: {Where(row, column)} → {Where(row, plan[row])}. ";
        }
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
            $"• {Revision(r)}{Where(r, plan[r])} — {Argument(r)}.")) +
            (me.Letters.Any(l => l.Revealed == false)
                ? mind.Personality.Negative > 0
                    ? "\nУчёл и свои исчезнувшие письма: их вес зависит от того, насколько я доверяю такой проверке."
                    : "\nИсчезновение своих писем не считаю доводом против карт: смотрю на другие улики."
                : "");
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
        var pending = messages.Where(m => m.Author != me.Id && (m.Text.Contains('?') ||
            (m.Text.Contains("верси", StringComparison.OrdinalIgnoreCase) &&
             new[] { "назови", "покажи", "повтори", "расскажи" }.Any(word => m.Text.Contains(word, StringComparison.OrdinalIgnoreCase)))) &&
            (Addressed(m) || (!AddressedElsewhere(m) && Who.IsMatch(m.Text) && m.Text.Contains("голос", StringComparison.OrdinalIgnoreCase)))).ToList();
        // Each reply consumes just the latest pending question from that player at
        // the time of the reply, not every earlier question by the same author.
        foreach (var response in own.Where(o => o.Text.Contains("отвечаю", StringComparison.OrdinalIgnoreCase) ||
                     o.Text.Contains("уточню", StringComparison.OrdinalIgnoreCase) ||
                     o.Text.Contains("договорились", StringComparison.OrdinalIgnoreCase)))
        {
            var answered = pending.LastOrDefault(q => q.At < response.At &&
                response.Text.StartsWith(Name(q.Author) + ",", StringComparison.OrdinalIgnoreCase));
            if (answered is not null) pending.Remove(answered);
        }
        var question = pending.LastOrDefault();
        // Acknowledge the first answer to an actual outgoing question, not replies to
        // acknowledgements. Later public opinions still contribute to mind/plan.
        var answer = fresh.LastOrDefault(m => Addressed(m) && !m.Text.Contains('?') &&
            own.Any(q => q.Text.Contains('?') && q.At < m.At && Addresses(q, m.Author) &&
                !messages.Any(previous => previous.Author == m.Author && previous.At > q.At && previous.At < m.At &&
                    Addressed(previous) && !previous.Text.Contains('?'))));
        var incoming = question ?? answer;
        if (incoming is not null)
        {
            if (question?.Text.Contains("Предлагаю голосовать вместе", StringComparison.Ordinal) == true)
            {
                var shared = question.Cards.Intersect(cards).ToList();
                if (shared.Count > 0)
                    return ($"{Name(question.Author)}, договорились поддержать эти карты. По остальным пунктам ещё сверю улики." + suspicion,
                        shared, shared.Select(_ => "думаю, эта").ToList());
                return Line($"{Name(question.Author)}, пока не договорились: мои улики ведут к другим картам. Показываю свою версию." + suspicion);
            }
            var mentioned = MentionedRows(view, incoming);
            var asksSuspect = question is not null &&
                (question.Text.Contains("за кого", StringComparison.OrdinalIgnoreCase) || question.Text.Contains("кого подозрева", StringComparison.OrdinalIgnoreCase));
            // Attachments supply context, not a replacement for an explicit question about players.
            if (asksSuspect && mentioned.Count == 0)
                return Line($"{Name(incoming.Author)}, отвечаю про подозрения.{suspicion}");
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
                    return $"• {Revision(row)}{Where(row, plan[row])} — {agreement}. {Argument(row)}.";
                });
                var response = prefix + "\n" + string.Join("\n", lines) + (asksSuspect ? suspicion : "");
                if (response.Length > ChatService.MaxTextLength)
                    response = prefix + "\n" + string.Join("; ", mentioned.Select(row => Where(row, plan[row]))) +
                        (asksSuspect ? suspicion : "");
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
                $"{Revision(row)}Сейчас выберу {Where(row, plan[row])}. {Argument(row)}. " +
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
        if (own.Count >= 2 && !own.Any(m => m.Text.Contains("Предлагаю голосовать вместе", StringComparison.Ordinal)))
        {
            var ally = messages.Where(m => m.Author != me.Id && mind.Names.ContainsKey(m.Author) &&
                    view.Players.Any(p => p.Id == m.Author && !p.IsGhost &&
                        (me.Role.IsKillerTeam() || p.KnownRole is null || !p.KnownRole.Value.IsKillerTeam())))
                .GroupBy(m => m.Author).Select(g => g.Last())
                .Select(m => (Message: m, Shared: m.Cards.Intersect(cards).ToList()))
                .Where(x => x.Shared.Count > 0)
                .OrderByDescending(x => x.Shared.Count + mind.AffinityBias(x.Message.Author) * .5).FirstOrDefault();
            if (ally.Message is not null)
            {
                var sharedSuspicion = suspicion.Contains($"Подозреваю, что {Name(ally.Message.Author)} из", StringComparison.Ordinal)
                    ? " По подозрениям у нас могут быть разногласия." : suspicion;
                return ($"{Name(ally.Message.Author)}, наши версии здесь совпадают. Предлагаю голосовать вместе за эти карты; " +
                    "если появится новая улика, пересмотрим договорённость." + sharedSuspicion +
                    (sharedSuspicion.Contains("Подозреваю", StringComparison.Ordinal) ? " Предлагаю вместе голосовать и против этого подозреваемого." : "") + " Согласен?",
                    ally.Shared, ally.Shared.Select(_ => "думаю, эта").ToList());
            }
        }
        if (fresh.Count > 0 && !own.Any(m => m.Text.StartsWith("Итог", StringComparison.Ordinal)))
            return Line(Summary("Итог после обсуждения; пока голосовал бы так:"));
        return null;
    }
}
