using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;

namespace GhostLetters.Infrastructure.Games;

/// <summary>
/// Общая теория бота: вероятность истины каждой карты, когда все подсказки читаются вместе.
/// <see cref="Before"/> — как бот видел ряды по отдельности, <see cref="Truth"/> — после общей теории;
/// <see cref="Best"/> — самая вероятная теория целиком (по карте в ряду), <see cref="Unexplained"/> — подсказки,
/// которые лучшая теория так и не объяснила.
/// </summary>
public sealed record HintTheory(
    IReadOnlyDictionary<string, double> Before,
    IReadOnlyDictionary<string, double> Truth,
    IReadOnlyList<string> Best,
    IReadOnlyList<string> Unexplained);

public static partial class BotPlayer
{
    /// <summary>Общая теория, как её видит бот (для тестов, бенчмарка и логов).</summary>
    public static HintTheory? Theory(PlayerView view, CardTags tags, BotMind? mind) =>
        view.Me is { Role: not Role.Ghost } me && view.Board.Count > 0 ? new Brain(view, me, new Random(0), tags, mind).Theory() : null;

    private static readonly Dictionary<object, HintTheory> TheoryCache = new();

    private sealed partial class Brain
    {
        private HintTheory? _theory;
        private bool _inTheory;

        private bool TheoryOn => BotTuning.Current.HintTheory && !Classic && view.Hints.Any(h => h.Cards.Count > 0);

        /// <summary>
        /// Боты 4.0: общая теория. Человек не читает подсказку для каждого ряда отдельно: он ищет такую раскладку
        /// (по карте в ряду), в которой каждая открытая Призраком карта на что-то указывает. Если ряд уже закрыт
        /// (корсет, шапка, кроссовки — свитер), то лыжи и нунчаки обязаны объяснять что-то другое; ванна, указывающая
        /// на уточку, туалет и пробирку, при закрытых способе и тайне ведёт к пробирке.
        ///
        /// Модель: бот перебирает все раскладки T (5⁴ = 625). Априори — его улики без подсказок (свои и чужие
        /// исчезнувшие письма, логика заявлений). Каждая подсказка h — выбор Призрака: P(h | T) = ε + среднее по
        /// рядам d_r(h, T_r), где d_r — насколько h выделяет T_r среди карт ряда r (softmax по сходству). Подсказка,
        /// не похожая ни на что в ряду, ничего о нём не говорит; подсказка, которую объясняет уже другой ряд, не тянет
        /// за собой третий («объяснение отнимает улику»). Итог — маргинальные вероятности карт.
        /// </summary>
        public HintTheory? Theory()
        {
            if (_theory is not null) return _theory;
            if (!TheoryOn || _inTheory) return null;
            // Теория меняется только с новой подсказкой, письмом или репликой: решения одного хода её не пересчитывают.
            var key = (view.GameId, me.Id, view.Round, view.Hints.Sum(h => h.Cards.Count), me.Letters.Count,
                mind?.Opinions.Count ?? 0, mind?.ClaimList.Count ?? 0, BotTuning.Current, P);
            lock (TheoryCache)
                if (TheoryCache.TryGetValue(key, out var cached)) return _theory = cached;
            _inTheory = true;
            try { _theory = ComputeTheory(); }
            finally { _inTheory = false; }
            lock (TheoryCache)
            {
                if (TheoryCache.Count > 4000) TheoryCache.Clear();
                TheoryCache[key] = _theory;
            }
            return _theory;
        }

        private HintTheory ComputeTheory()
        {
            var t = BotTuning.Current;
            var rows = view.Board.Count;
            var hintCards = view.Hints.SelectMany(h => h.Cards).Where(h => view.Board.All(b => !b.Cards.Contains(h))).ToList();
            var beta = Beta;

            // Как бот видел ряды до общей теории и что он знает без подсказок.
            var before = new Dictionary<string, double>();
            var logPrior = new double[rows][];
            for (var r = 0; r < rows; r++)
            {
                var cards = view.Board[r].Cards;
                var full = cards.Select(Evidence).ToArray();
                var noHints = cards.Select((c, i) => full[i] - hintCards.Sum(h => HintWeight(h, c))).ToArray();
                Normalize(full, beta, out var b);
                for (var i = 0; i < cards.Count; i++) before[cards[i]] = b[i];
                Normalize(noHints, beta, out var p);
                logPrior[r] = p.Select(x => Math.Log(Math.Max(1e-9, x))).ToArray();
            }

            // d[h][r][i]: насколько подсказка h выделяет карту i в ряду r.
            var d = hintCards.Select(h => Enumerable.Range(0, rows).Select(r =>
            {
                // То же тонкое чтение подсказки, что и без теории (HintWeight: фокус на лучшем совпадении, характер).
                var sims = view.Board[r].Cards.Select(c => t.TheoryFocus * HintWeight(h, c)).ToArray();
                Normalize(sims, 1, out var w);
                return w;
            }).ToArray()).ToArray();

            // Перебор всех раскладок (одометр по рядам).
            var sizes = view.Board.Select(b => b.Cards.Count).ToArray();
            var marg = sizes.Select(n => new double[n]).ToArray();
            var pick = new int[rows];
            var best = (LogP: double.NegativeInfinity, Pick: new int[rows]);
            var logs = new List<(double LogP, int[] Pick)>();
            var maxLog = double.NegativeInfinity;
            while (true)
            {
                var lp = 0.0;
                for (var r = 0; r < rows; r++) lp += logPrior[r][pick[r]];
                foreach (var dh in d)
                {
                    var sum = 0.0;
                    for (var r = 0; r < rows; r++) sum += dh[r][pick[r]];
                    lp += Math.Log(t.TheoryNoise + sum / rows);
                }
                logs.Add((lp, (int[])pick.Clone()));
                if (lp > maxLog) maxLog = lp;
                if (lp > best.LogP) best = (lp, (int[])pick.Clone());
                var k = rows - 1;
                while (k >= 0 && ++pick[k] == sizes[k]) pick[k--] = 0;
                if (k < 0) break;
            }
            var total = 0.0;
            foreach (var (lp, p) in logs)
            {
                var w = Math.Exp(lp - maxLog);
                total += w;
                for (var r = 0; r < rows; r++) marg[r][p[r]] += w;
            }
            var truth = new Dictionary<string, double>();
            for (var r = 0; r < rows; r++)
                for (var i = 0; i < sizes[r]; i++) truth[view.Board[r].Cards[i]] = marg[r][i] / total;

            // Какие подсказки лучшая теория не объясняет (ни один ряд не выделяет свою истину ею).
            var unexplained = hintCards.Where((h, j) => Enumerable.Range(0, rows).All(r => d[j][r][best.Pick[r]] < 1.5 / sizes[r])).ToList();
            return new HintTheory(before, truth, best.Pick.Select((i, r) => view.Board[r].Cards[i]).ToList(), unexplained);
        }

        private static void Normalize(double[] scores, double scale, out double[] probs)
        {
            var max = scores.Max();
            var e = scores.Select(x => Math.Exp(scale * (x - max))).ToArray();
            var sum = e.Sum();
            probs = e.Select(x => x / sum).ToArray();
        }
    }
}
