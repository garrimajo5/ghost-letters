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

        // Чёрные тоже читают стол теорией: так они вернее видят, во что верят мирные (и куда их уводить).
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
            // Ключ — то, от чего теория зависит: подсказки, мои письма, мнения и заявления стола.
            var key = (view.GameId, me.Id, view.Round, view.Hints.Sum(h => h.Cards.Count), me.Letters.Count(l => l.Revealed is not null),
                mind?.Opinions.Count ?? 0, (mind?.ClaimList.Count ?? 0) * 31 + (BotTuning.Current.GetHashCode() ^ P.GetHashCode()));
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

            // Вес каждой подсказки для каждой карты — один раз (дальше только арифметика).
            var hw = hintCards.Select(h => view.Board.Select(b => b.Cards.Select(c => HintWeight(h, c)).ToArray()).ToArray()).ToArray();

            // Как бот видел ряды до общей теории и что он знает без подсказок.
            var before = new Dictionary<string, double>();
            var logPrior = new double[rows][];
            for (var r = 0; r < rows; r++)
            {
                var cards = view.Board[r].Cards;
                var full = cards.Select(Evidence).ToArray();
                var row = r;
                var noHints = cards.Select((c, i) => full[i] - hw.Sum(h => h[row][i])).ToArray();
                Normalize(full, beta, out var b);
                for (var i = 0; i < cards.Count; i++) before[cards[i]] = b[i];
                Normalize(noHints, beta, out var p);
                logPrior[r] = p.Select(x => Math.Log(Math.Max(1e-9, x))).ToArray();
            }

            // d[h][r][i]: насколько подсказка h выделяет карту i в ряду r — тем же тонким чтением, что и без теории.
            var d = hw.Select(h => h.Select(rowWeights =>
            {
                Normalize(rowWeights.Select(x => t.TheoryFocus * x).ToArray(), 1, out var w);
                return w;
            }).ToArray()).ToArray();

            // Перебор всех раскладок (одометр по рядам): два прохода, без лишних выделений памяти.
            var sizes = view.Board.Select(b => b.Cards.Count).ToArray();
            var marg = sizes.Select(n => new double[n]).ToArray();
            var count = sizes.Aggregate(1, (a, n) => a * n);
            var logp = new double[count];
            var pick = new int[rows];
            var maxLog = double.NegativeInfinity;
            var bestIndex = 0;
            var noise = t.TheoryNoise;
            for (var n = 0; n < count; n++)
            {
                var lp = 0.0;
                for (var r = 0; r < rows; r++) lp += logPrior[r][pick[r]];
                for (var j = 0; j < d.Length; j++)
                {
                    var sum = 0.0;
                    var dh = d[j];
                    for (var r = 0; r < rows; r++) sum += dh[r][pick[r]];
                    lp += Math.Log(noise + sum / rows);
                }
                logp[n] = lp;
                if (lp > maxLog) { maxLog = lp; bestIndex = n; }
                for (var k = rows - 1; k >= 0 && ++pick[k] == sizes[k]; k--) pick[k] = 0;
            }
            var total = 0.0;
            Array.Clear(pick);
            var best = new int[rows];
            for (var n = 0; n < count; n++)
            {
                var w = Math.Exp(logp[n] - maxLog);
                total += w;
                for (var r = 0; r < rows; r++) marg[r][pick[r]] += w;
                if (n == bestIndex) Array.Copy(pick, best, rows);
                for (var k = rows - 1; k >= 0 && ++pick[k] == sizes[k]; k--) pick[k] = 0;
            }
            var truth = new Dictionary<string, double>();
            for (var r = 0; r < rows; r++)
                for (var i = 0; i < sizes[r]; i++) truth[view.Board[r].Cards[i]] = marg[r][i] / total;

            // Какие подсказки лучшая теория не объясняет (ни один ряд не выделяет свою истину ею).
            var unexplained = hintCards.Where((h, j) => Enumerable.Range(0, rows).All(r => d[j][r][best[r]] < 1.5 / sizes[r])).ToList();
            return new HintTheory(before, truth, best.Select((i, r) => view.Board[r].Cards[i]).ToList(), unexplained);
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
