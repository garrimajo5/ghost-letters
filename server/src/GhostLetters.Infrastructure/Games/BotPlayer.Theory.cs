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
    IReadOnlyList<string> Unexplained)
{
    /// <summary>Как лучшая версия читает каждую подсказку: на какую истину она указывает и почему.</summary>
    public IReadOnlyList<HintExplanation> Explanations { get; init; } = [];
}

/// <summary>
/// Чтение одной подсказки в лучшей версии. <see cref="Kind"/>: direct — прямо указывает на карту, confirm — повтор
/// (на ту же карту уже указала другая подсказка), combo — вместе с <see cref="With"/>, intent — так её назвал
/// отправитель, none — ничего не объясняет (Призрак ошибся или лучше не нашлось).
/// </summary>
public sealed record HintExplanation(string Hint, int Round, string? Card, string Kind, string? With, double Lift);

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
            var onBoard = view.Board.SelectMany(b => b.Cards).ToHashSet();
            var hintList = view.Hints.SelectMany(h => h.Cards.Select(c => (Card: c, h.Round))).Where(h => !onBoard.Contains(h.Card)).ToList();
            var hintCards = hintList.Select(h => h.Card).ToList();
            var beta = Beta;
            var sizes = view.Board.Select(b => b.Cards.Count).ToArray();

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

            // Замысел: «кидал корсет на свитер», и корсет открыли — Призрак, вероятно, подтвердил свитер.
            // Доля «ради неё» — по доверию к автору: чёрному о замысле верят мало.
            var intent = new string?[hintCards.Count];
            if (t.TheoryVersions && t.TheoryIntent > 0 && mind is not null)
                for (var j = 0; j < hintCards.Count; j++)
                {
                    var claims = mind.Opinions.Where(o => o.IsCheck && o.SourceCard == hintCards[j] && onBoard.Contains(o.CardId))
                        .GroupBy(o => o.CardId)
                        .Select(g => (Card: g.Key, Trust: g.Max(o => o.Author == me.Id ? 1 : Trust(o.Author)))).ToList();
                    foreach (var (card, trust) in claims)
                    {
                        var r = Enumerable.Range(0, rows).First(x => view.Board[x].Cards.Contains(card));
                        var i = view.Board[r].Cards.ToList().IndexOf(card);
                        var w = Math.Clamp(t.TheoryIntent * trust / claims.Count, 0, .9);
                        if (w <= 0) continue;
                        for (var k = 0; k < sizes[r]; k++) d[j][r][k] = (1 - w) * d[j][r][k] + (k == i ? w : 0);
                        intent[j] = card;
                    }
                }

            // «Подъём»: во сколько раз подсказка выделяет карту против равного выбора (1 — ничего не говорит о ряде).
            var lift = d.Select(h => h.Select((row, r) => row.Select(x => x * sizes[r]).ToArray()).ToArray()).ToArray();

            // Пары подсказок одного раунда: обе понемногу указывают на одну карту (сапоги + телевизор — беговая дорожка).
            var combo = new double[hintCards.Count][][];
            var comboWith = new int[hintCards.Count][][];
            for (var j = 0; j < hintCards.Count; j++)
            {
                combo[j] = sizes.Select(n => new double[n]).ToArray();
                comboWith[j] = sizes.Select(n => Enumerable.Repeat(-1, n).ToArray()).ToArray();
                if (!t.TheoryVersions || t.TheoryCombo <= 0) continue;
                for (var k = 0; k < hintCards.Count; k++)
                {
                    if (k == j || hintList[k].Round != hintList[j].Round) continue;
                    for (var r = 0; r < rows; r++)
                        for (var i = 0; i < sizes[r]; i++)
                        {
                            var c = t.TheoryCombo * Math.Sqrt(lift[j][r][i] * lift[k][r][i]);
                            if (c > combo[j][r][i]) { combo[j][r][i] = c; comboWith[j][r][i] = k; }
                        }
                }
            }

            // Одна подсказка в версии T: прежде — среднее по рядам (подсказка «тянет» все похожие истины сразу);
            // версии — объясняет лучший ряд (прямо или парой), остальные ряды лишь чуть добавляют (редкое «на всё сразу»).
            var noise = t.TheoryNoise;
            var versions = t.TheoryVersions;
            var multi = t.TheoryMulti;
            double HintLike(int j, int[] pick)
            {
                if (!versions)
                {
                    var sum = 0.0;
                    for (var r = 0; r < rows; r++) sum += d[j][r][pick[r]];
                    return noise + sum / rows;
                }
                double best = 0, extra = 0;
                for (var r = 0; r < rows; r++)
                {
                    var l = Math.Max(lift[j][r][pick[r]], combo[j][r][pick[r]]);
                    if (l > best) { extra += Math.Max(0, best - 1); best = l; }
                    else extra += Math.Max(0, l - 1);
                }
                return noise * rows + best + multi * extra;
            }

            // Перебор всех раскладок (одометр по рядам): два прохода, без лишних выделений памяти.
            var marg = sizes.Select(n => new double[n]).ToArray();
            var count = sizes.Aggregate(1, (a, n) => a * n);
            var logp = new double[count];
            var pick = new int[rows];
            var maxLog = double.NegativeInfinity;
            var bestIndex = 0;
            for (var n = 0; n < count; n++)
            {
                var lp = 0.0;
                for (var r = 0; r < rows; r++) lp += logPrior[r][pick[r]];
                for (var j = 0; j < d.Length; j++) lp += Math.Log(HintLike(j, pick));
                logp[n] = lp;
                if (lp > maxLog) { maxLog = lp; bestIndex = n; }
                for (var k = rows - 1; k >= 0 && ++pick[k] == sizes[k]; k--) pick[k] = 0;
            }
            var total = 0.0;
            Array.Clear(pick);
            var bestPick = new int[rows];
            for (var n = 0; n < count; n++)
            {
                var w = Math.Exp(logp[n] - maxLog);
                total += w;
                for (var r = 0; r < rows; r++) marg[r][pick[r]] += w;
                if (n == bestIndex) Array.Copy(pick, bestPick, rows);
                for (var k = rows - 1; k >= 0 && ++pick[k] == sizes[k]; k--) pick[k] = 0;
            }
            var truth = new Dictionary<string, double>();
            for (var r = 0; r < rows; r++)
                for (var i = 0; i < sizes[r]; i++) truth[view.Board[r].Cards[i]] = marg[r][i] / total;

            // Как лучшая версия читает каждую подсказку.
            var explanations = new List<HintExplanation>();
            var explained = new HashSet<string>();
            for (var j = 0; j < hintCards.Count; j++)
            {
                var (bestRow, bestLift, viaCombo) = (-1, 1.5, false);
                for (var r = 0; r < rows; r++)
                {
                    var direct = lift[j][r][bestPick[r]];
                    var pair = combo[j][r][bestPick[r]];
                    var l = Math.Max(direct, versions ? pair : 0);
                    if (l > bestLift) (bestRow, bestLift, viaCombo) = (r, l, pair > direct);
                }
                if (bestRow < 0)
                {
                    explanations.Add(new HintExplanation(hintCards[j], hintList[j].Round, null, "none", null, 1));
                    continue;
                }
                var card = view.Board[bestRow].Cards[bestPick[bestRow]];
                var kind = viaCombo ? "combo" : intent[j] == card ? "intent" : explained.Contains(card) ? "confirm" : "direct";
                var with = viaCombo && comboWith[j][bestRow][bestPick[bestRow]] is >= 0 and var k ? hintCards[k] : null;
                explanations.Add(new HintExplanation(hintCards[j], hintList[j].Round, card, kind, with, bestLift));
                explained.Add(card);
            }
            var unexplained = explanations.Where(e => e.Kind == "none").Select(e => e.Hint).ToList();
            return new HintTheory(before, truth, bestPick.Select((i, r) => view.Board[r].Cards[i]).ToList(), unexplained)
                { Explanations = explanations };
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
