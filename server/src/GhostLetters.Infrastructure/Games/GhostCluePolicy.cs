using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;

namespace GhostLetters.Infrastructure.Games;

public static class GhostCluePolicy
{
    public sealed record Assessment(double TrueMatch, double FalseMatch, double Risk, bool Reveal)
    {
        /// <summary>Истина — среди сильных связей письма, а не побочная слабая ассоциация.</summary>
        public bool Clear => TrueMatch > 0 && TrueMatch >= BotTuning.Current.GhostClear * Math.Max(TrueMatch, FalseMatch);

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
        var reveal = readable >= .18 - .10 * risk && yes - no >= .06 - .60 * risk &&
            yes >= BotTuning.Current.GhostClear * Math.Max(yes, no);
        return new(yes, no, risk, reveal);
    }

    /// <summary>
    /// Какие письма открыть. Призрак примеряет каждое на стол: как изменится чтение всех рядов (подсказка
    /// «объясняется» самой похожей картой поля, слабее похожие почти не в счёт), и жадно добавляет
    /// письма, пока они делают истинные карты заметнее ложных. Осторожность по-прежнему решает, можно ли
    /// открыть сомнительное письмо; очень полезное для стола письмо открывается и у осторожного.
    /// </summary>
    /// <param name="letters">Карты — письма этого раунда (неоткрытые исчезнут); false — карты руки для зацепки.</param>
    public static IReadOnlyList<string> Choose(PlayerView view, CardTags tags, BotMind mind, IEnumerable<string> cards, bool letters = true)
    {
        if (view.Me?.Role != Role.Ghost || view.Truth is not { } truth) return [];
        var tuning = BotTuning.Current;
        if (tuning.GhostModel) return ModelChoose(view, tags, mind, cards.Distinct().ToList(), truth, letters);
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
                .Where(c => (c.Value.Reveal && c.Gain >= cost - tuning.GhostTolerance * risk * risk && c.Gain > -1e-9 - tuning.GhostTolerance * risk * risk) ||
                    (c.Gain >= .35 && c.Value.Clear))
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
            // Стол голодает: в прошлом раунде Призрак не открыл ни одного письма. Живой Призрак тогда открывает
            // лучшее из похожих на истину, даже если оно чуть двусмысленно, — иначе обсуждать нечего.
            var previous = view.Hints.Where(h => h.Round == view.Round - 1).ToList();
            var starving = view.Round > 1 && previous.Count > 0 && previous.All(h => h.Cards.Count == 0);
            var fallback = candidates
                .Select(c => (c.Card, c.Value, Gain: TableScore(view, truth, [.. shown, c.Card], all.Where(x => x != c.Card).ToList(), Sim, focus) - current))
                // Только письмо, которое на истину похоже сильнее, чем на любую ложную карту: двусмысленное не открываем.
                .Where(c => c.Value.TrueMatch >= .08 && c.Value.Clear && (!tuning.GhostFallbackStrict || c.Value.TrueMatch > c.Value.FalseMatch * (starving ? .8 : 1)) &&
                    c.Gain > -tuning.GhostFallback * (starving ? 1.5 : 1))
                .OrderByDescending(c => c.Gain).ThenByDescending(c => c.Value.TrueMatch - c.Value.FalseMatch)
                .FirstOrDefault();
            if (fallback.Card is not null) chosen.Add(fallback.Card);
        }

        return chosen;
    }

    /// <summary>
    /// Боты 3.0: Призрак примеряет письма на модель стола (<see cref="TableModel"/>). Стол видит открытые подсказки
    /// и узнаёт, что остальные письма исчезли. Призрак жадно открывает письма, пока истина становится вероятнее
    /// (сумма log-вероятностей истинных карт) — минимум открытий нормален. Письмо, после которого ряд, где стол
    /// уже верно видел истину, уходит к ложной карте, не открывается, если оно не прибавляет верных рядов в целом.
    /// Осторожность (риск характера и срочность к концу партии) снижает порог: под конец Призрак рискует и
    /// дальней подсказкой — если она хоть немного поднимает истину, которую никто не нашёл.
    /// </summary>
    private static IReadOnlyList<string> ModelChoose(PlayerView view, CardTags tags, BotMind mind, IReadOnlyList<string> cards,
        IReadOnlyList<int> truth, bool letters)
    {
        if (cards.Count == 0) return [];
        var tuning = BotTuning.Current;
        var memo = new Dictionary<(string, string), double>();
        double Sim(string a, string b) => memo.TryGetValue((a, b), out var v) ? v : memo[(a, b)] = tags.Similarity(a, b);
        var breadth = Math.Clamp(mind.TableBreadth, 1, 4);
        var focus = 4 + 8 * (1 - (breadth - 1) / 3);
        var table = TableModel.Uniform(view.Board.Select(r => (IReadOnlyList<string>)r.Cards).ToList(), Sim, focus);
        foreach (var hint in view.Hints.SelectMany(h => h.Cards)) table = table.With(hint, true);

        TableModel After(IReadOnlyCollection<string> open)
        {
            var t = table;
            foreach (var c in open) t = t.With(c, true);
            if (letters)
                foreach (var c in cards.Where(c => !open.Contains(c))) t = t.With(c, false, tuning.GhostModelVanish);
            return t;
        }

        var late = view.TotalRounds <= 1 ? 1 : Math.Clamp((view.Round - 1.0) / (view.TotalRounds - 1), 0, 1);
        var risk = Math.Clamp(mind.Personality.Risk + (1 - mind.Personality.Risk) * late * .8, 0, 1);
        var cost = tuning.GhostModelCost * (1 - risk);
        // Письмо, которое сильнее указывает на ложную карту, чем на любую истинную, не открываем вовсе:
        // стол «объясняет» подсказку лучшим совпадением, и уводить его потом дорого (в крайнем случае — 50/50).
        bool Honest(string letter)
        {
            var w = table.Pointing(letter);
            double t = 0, f = 0;
            for (var r = 0; r < w.Length; r++)
                for (var c = 0; c < w[r].Length; c++)
                    if (c == truth[r]) t = Math.Max(t, w[r][c]); else f = Math.Max(f, w[r][c]);
            return t > 0 && t >= f * tuning.GhostModelHonest;
        }

        var chosen = new List<string>();
        var current = After(chosen);
        var score = current.TruthScore(truth);
        var right = current.RowsRight(truth);
        while (true)
        {
            var best = cards.Where(c => !chosen.Contains(c) && Honest(c)).Select(c =>
            {
                var next = After([.. chosen, c]);
                var misleads = Enumerable.Range(0, truth.Count).Any(r => current.Best(r) == truth[r] && next.Best(r) != truth[r]);
                return (Card: c, Next: next, Gain: next.TruthScore(truth) - score, Right: next.RowsRight(truth), Misleads: misleads);
            })
                .Where(x => x.Gain > cost && (!x.Misleads || x.Right > right))
                .OrderByDescending(x => x.Gain).FirstOrDefault();
            if (best.Card is null) break;
            chosen.Add(best.Card);
            current = best.Next;
            score += best.Gain;
            right = best.Right;
        }

        return chosen;
    }

    /// <summary>
    /// Зацепка, когда <see cref="Choose"/> ничего не выбрал: карта руки, которая на истину похожа заметнее всего
    /// относительно ложных (сходство с истиной не ниже .08). Нет такой — зацепки нет.
    /// </summary>
    public static string? FirstClueFallback(PlayerView view, CardTags tags, BotMind mind, IEnumerable<string> hand)
    {
        if (!BotTuning.Current.FirstClueAlways) return null;
        return hand.Distinct().Select(c => (Card: c, Value: Assess(view, tags, mind, c)))
            .Where(x => x.Value.TrueMatch >= .08)
            .OrderByDescending(x => x.Value.TrueMatch - x.Value.FalseMatch).ThenByDescending(x => x.Value.TrueMatch)
            .Select(x => x.Card).FirstOrDefault();
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
