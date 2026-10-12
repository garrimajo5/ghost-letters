using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;

namespace GhostLetters.Infrastructure.Games;

public static class GhostCluePolicy
{
    public sealed record Assessment(double TrueMatch, double FalseMatch, double Risk, bool Reveal)
    {
        public string Reason => TrueMatch < .08 ? "связь с истиной слишком слабая" :
            FalseMatch >= TrueMatch ? "может сильнее увести к ложной карте" : "истинная карта читается яснее ложных";
    }

    public static Assessment Assess(PlayerView view, CardTags tags, BotMind mind, string letter)
    {
        if (view.Me?.Role != Role.Ghost || view.Truth is not { } truth) return new(0, 0, 0, false);
        var p = mind.Personality;
        var trueCards = view.Board.Select((r, i) => r.Cards[truth[i]]).ToHashSet();
        double Match(string card) => tags.Similarity(letter, card, p.Attention, p.Details, p.SecondaryMeanings);
        var yes = trueCards.Select(Match).DefaultIfEmpty().Max();
        var no = view.Board.SelectMany(r => r.Cards).Where(c => !trueCards.Contains(c)).Select(Match).DefaultIfEmpty().Max();
        // Only expressed public opinions, never somebody else's private plan.
        var publicOpinions = mind.Opinions.Where(o => !o.IsCheck && o.Strength > 0 &&
            view.Players.Any(player => player.Id == o.Author && !player.IsGhost &&
                !(player.KnownRole?.IsKillerTeam() ?? false))).ToList();
        var missing = trueCards.Count(c => !publicOpinions.Any(o => o.CardId == c)) / (double)Math.Max(1, trueCards.Count);
        var urgency = publicOpinions.Count == 0 ? 0 : missing * (view.TotalRounds == 1 && view.Round == 1 ? 1 : Math.Clamp((view.Round - 1.0) / Math.Max(1, view.TotalRounds - 1), 0, 1));
        var risk = Math.Clamp(p.Risk + (1 - p.Risk) * urgency * .8, 0, 1);
        var ranked = view.Board.SelectMany(r => r.Cards).OrderByDescending(Match).ToList();
        var breadth = Math.Max(1, (int)Math.Round(mind.TableBreadth));
        var readable = ranked.Take(breadth).Any(trueCards.Contains) ? yes : yes * .4;
        var reveal = readable >= .18 - .10 * risk && yes - no >= .06 - .60 * risk;
        return new(yes, no, risk, reveal);
    }

    /// <summary>
    /// Какие письма открыть. Призрак примеряет каждое на стол: как изменится чтение всех рядов (подсказка
    /// «объясняется» самой похожей картой поля, слабее похожие почти не в счёт), и жадно добавляет
    /// письма, пока они делают истинные карты заметнее ложных. Осторожность по-прежнему решает, можно ли
    /// открыть сомнительное письмо; очень полезное для стола письмо открывается и у осторожного.
    /// </summary>
    public static IReadOnlyList<string> Choose(PlayerView view, CardTags tags, BotMind mind, IEnumerable<string> cards)
    {
        if (view.Me?.Role != Role.Ghost || view.Truth is not { } truth) return [];
        var tuning = BotTuning.Current;
        if (!tuning.GhostPlans)
            return cards.Select(c => (Card: c, Value: Assess(view, tags, mind, c))).Where(x => x.Value.Reveal)
                .OrderByDescending(x => x.Value.TrueMatch - x.Value.FalseMatch * (1 - x.Value.Risk)).Select(x => x.Card).ToList();
        var p = mind.Personality;
        var candidates = cards.Distinct().Select(c => (Card: c, Value: Assess(view, tags, mind, c))).ToList();
        if (candidates.Count == 0) return [];
        var memo = new Dictionary<(string, string), double>();
        double Sim(string a, string b) => memo.TryGetValue((a, b), out var v) ? v
            : memo[(a, b)] = tuning.GhostTableDefault ? tags.Similarity(a, b) : tags.Similarity(a, b, p.Attention, p.Details, p.SecondaryMeanings);
        var shown = view.Hints.SelectMany(h => h.Cards).ToList();
        var all = candidates.Select(c => c.Card).ToList();
        // Как широко стол читает подсказку: строгий стол «объясняет» письмо одной картой, широкий — несколькими.
        var breadth = tuning.GhostBreadth ? Math.Clamp(mind.TableBreadth, 1, 4) : 1;
        var focus = 4 + 8 * (1 - (breadth - 1) / 3);
        var current = TableScore(view, truth, shown, all, Sim, focus);
        var risk = candidates[0].Value.Risk;
        var chosen = new List<string>();
        var cost = .03 * (1 - risk);
        while (true)
        {
            var best = candidates.Where(c => !chosen.Contains(c.Card))
                .Select(c => (c.Card, c.Value, Gain: TableScore(view, truth, [.. shown, .. chosen, c.Card], all.Where(x => x != c.Card && !chosen.Contains(x)).ToList(), Sim, focus) - current))
                .Where(c => (c.Value.Reveal && c.Gain >= cost - tuning.GhostTolerance * risk * risk && c.Gain > -1e-9 - tuning.GhostTolerance * risk * risk) || c.Gain >= .35)
                .OrderByDescending(c => c.Gain).ThenByDescending(c => c.Value.TrueMatch - c.Value.FalseMatch)
                .FirstOrDefault();
            if (best.Card is null) break;
            chosen.Add(best.Card);
            current += best.Gain;
        }

        // Ничего не открыть — стол остаётся без единой улики, обсуждать нечего. Если ни одно письмо не помогает,
        // но лучшее почти не вредит и всё же похоже на истину, Призрак открывает его (зацепку — всегда).
        if (chosen.Count == 0 && tuning.GhostFallback > 0)
        {
            var fallback = candidates
                .Select(c => (c.Card, c.Value, Gain: TableScore(view, truth, [.. shown, c.Card], all.Where(x => x != c.Card).ToList(), Sim, focus) - current))
                .Where(c => c.Value.TrueMatch >= .08 && c.Gain > -tuning.GhostFallback)
                .OrderByDescending(c => c.Gain).ThenByDescending(c => c.Value.TrueMatch - c.Value.FalseMatch)
                .FirstOrDefault();
            if (fallback.Card is not null) chosen.Add(fallback.Card);
        }

        return chosen;
    }

    /// <summary>
    /// Насколько ясно стол прочтёт истину: сумма по рядам отрыва истины от лучшей ложной. Неоткрытые письма
    /// тоже читаются — отправитель отодвигается от похожих на них карт, поэтому прятать письмо, указывающее
    /// на истину, вредно.
    /// </summary>
    public static double TableScore(PlayerView view, IReadOnlyList<int> truth, IReadOnlyList<string> hints,
        IReadOnlyList<string> hidden, Func<string, string, double> sim, double focus = 8)
    {
        var board = view.Board.SelectMany(r => r.Cards).ToList();
        var evidence = board.ToDictionary(c => c, _ => 0.0);
        foreach (var hint in hints)
        {
            var scores = board.Where(c => c != hint).Select(c => (Card: c, S: sim(hint, c))).ToList();
            var top = scores.Select(x => x.S).DefaultIfEmpty(0).Max();
            foreach (var (card, s) in scores) evidence[card] += Read(s, top, focus);
        }

        foreach (var letter in hidden)
        {
            var scores = board.Where(c => c != letter).Select(c => (Card: c, S: sim(letter, c))).ToList();
            var top = scores.Select(x => x.S).DefaultIfEmpty(0).Max();
            foreach (var (card, s) in scores) evidence[card] -= .3 * Read(s, top, focus);
        }

        return view.Board.Select((row, r) =>
        {
            var real = evidence[row.Cards[truth[r]]];
            var fake = row.Cards.Where((_, c) => c != truth[r]).Select(c => evidence[c]).DefaultIfEmpty(0).Max();
            return Math.Tanh((real - fake) / .4);
        }).Sum();
    }

    /// <summary>
    /// Вклад подсказки в карту: сходство, ослабленное тем, насколько карта уступает самой похожей карте поля.
    /// Игрок «объясняет» письмо лучшим совпадением — остальные похожие почти не в счёт.
    /// </summary>
    public static double Read(double similarity, double top, double focus = 8) =>
        similarity <= 0 ? 0 : similarity * Math.Exp(-focus * Math.Max(0, top - similarity));
}
