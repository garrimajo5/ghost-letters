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

    // Log likelihood of replacing just this row in the best complete reading, relative to keeping it.
    internal IReadOnlyDictionary<string, double> VoteLogOdds { get; init; } = new Dictionary<string, double>();
}

/// <summary>
/// Чтение одной подсказки в лучшей версии. <see cref="Kind"/>: direct — прямо указывает на карту, confirm — повтор
/// (на ту же карту уже указала другая подсказка), combo — вместе с <see cref="With"/>, intent — так её назвал
/// отправитель, none — ничего не объясняет (Призрак ошибся или лучше не нашлось).
/// </summary>
public sealed record HintExplanation(string Hint, int Round, string? Card, string Kind, string? With, double Lift)
{
    public string? SecondCard { get; init; }
}

public static partial class BotPlayer
{
    /// <summary>Общая теория, как её видит бот (для тестов, бенчмарка и логов).</summary>
    public static HintTheory? Theory(PlayerView view, CardTags tags, BotMind? mind) =>
        view.Me is { Role: not Role.Ghost } me && view.Board.Count > 0 ? new Brain(view, me, new Random(0), tags, mind).Theory() : null;

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
        /// Бот перебирает все раскладки T (5⁴ = 625). Априори — исчезнувшие письма и заявления.
        /// Для каждой подсказки сравнивает объяснение одной целью и двумя целями из разных рядов, с поправкой на ошибку
        /// Призрака. Для расследования суммирует вероятности раскладок, для голоса сравнивает целые версии.
        /// </summary>
        public HintTheory? Theory()
        {
            if (_theory is not null) return _theory;
            if (!TheoryOn || _inTheory) return null;
            _inTheory = true;
            try
            {
                if (BotTuning.Current.UnifiedTrust && !Classic && !KillerTeam) BlackOdds();
                Logic();
                _theory = ComputeTheory(Evidence, includeIntent: true);
            }
            finally { _inTheory = false; }
            return _theory;
        }

        private HintTheory ComputeTheory(Func<string, double> evidence, bool includeIntent)
        {
            var t = BotTuning.Current;
            var rows = view.Board.Count;
            var onBoard = view.Board.SelectMany(b => b.Cards).ToHashSet();
            var hintList = view.Hints.OrderBy(h => h.Round).SelectMany(h => h.Cards.Select(c => (Card: c, h.Round)))
                .Where(h => !onBoard.Contains(h.Card)).ToList();
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
                var full = cards.Select(evidence).ToArray();
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
            // Отношение подсказки к ряду: ряд, где она ни на что не похожа, её не объясняет (не «равный выбор», а ноль).
            var rel = hw.Select(h =>
            {
                var top = h.Select(row => row.Length == 0 ? 0 : Math.Max(0, row.Max())).ToArray();
                var all = top.DefaultIfEmpty(0).Max();
                return top.Select(x => all <= 0 ? 1 : t.TheoryRowFloor + (1 - t.TheoryRowFloor) * x / all).ToArray();
            }).ToArray();
            if (includeIntent && t.TheoryVersions && t.TheoryIntent > 0 && mind is not null)
                for (var j = 0; j < hintCards.Count; j++)
                {
                    var claims = mind.Opinions.Where(o => o.IsCheck && o.SourceCard == hintCards[j] && onBoard.Contains(o.CardId))
                        .GroupBy(o => o.CardId)
                        // Доверие без теории (история, обвинения, отвергнутые истины): Trust внутри теории зависел бы от порядка кэша.
                        .Select(g => (Card: g.Key, Trust: g.Max(o => o.Author == me.Id ? 1 : HistoryTrust(o.Author) * DistrustFactor(o.Author)))).ToList();
                    foreach (var (card, trust) in claims)
                    {
                        var r = Enumerable.Range(0, rows).First(x => view.Board[x].Cards.Contains(card));
                        var i = view.Board[r].Cards.ToList().IndexOf(card);
                        var w = Math.Clamp(t.TheoryIntent * trust / claims.Count, 0, .9);
                        if (w <= 0) continue;
                        for (var k = 0; k < sizes[r]; k++) d[j][r][k] = (1 - w) * d[j][r][k] + (k == i ? w : 0);
                        rel[j][r] = Math.Max(rel[j][r], w);
                        intent[j] = card;
                    }
                }

            // «Подъём»: во сколько раз подсказка выделяет карту против равного выбора (1 — ничего не говорит о ряде).
            var lift = d.Select((h, j) => h.Select((row, r) => row.Select(x => x * sizes[r] * rel[j][r]).ToArray()).ToArray()).ToArray();

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

            // One letter may deliberately link two subjects. Compare that interpretation with the
            // strongest single reading. Surface matches and a sender's claim cannot create a second target.
            var semanticLift = !t.TheoryVersions || t.TheoryMulti <= 0 ? [] : hintCards.Select(hint => view.Board.Select((row, r) =>
            {
                var supports = row.Cards.Select(c => _reading.Kind(hint, c) == CardTags.LinkKind.Meaning
                    ? _reading.Support(hint, c, Focus) : 0).ToArray();
                Normalize(supports, t.TheoryFocus, out var probs);
                var relative = supports.Max() / Math.Max(1e-9, HintTop(hint));
                var relevance = t.TheoryRowFloor + (1 - t.TheoryRowFloor) * relative;
                return supports.Select((s, i) => s > 0 ? probs[i] * sizes[r] * relevance : 0).ToArray();
            }).ToArray()).ToArray();
            // Related clues often repeat a target. Discount the two-target interpretation by the
            // support already available for each of its cards. A single confirmation keeps its full lift.
            var previous = sizes.Select(n => Enumerable.Repeat(1.0, n).ToArray()).ToArray();
            foreach (var round in Enumerable.Range(0, semanticLift.Length).GroupBy(j => hintList[j].Round).OrderBy(g => g.Key))
                for (var r = 0; r < rows; r++)
                    for (var i = 0; i < sizes[r]; i++)
                    {
                        var strongest = previous[r][i];
                        foreach (var j in round)
                        {
                            var current = semanticLift[j][r][i];
                            semanticLift[j][r][i] = current / Math.Sqrt(previous[r][i]);
                            strongest = Math.Max(strongest, current);
                        }
                        previous[r][i] = strongest;
                    }
            var noise = t.TheoryNoise;
            var versions = t.TheoryVersions;
            var multi = t.TheoryMulti;
            (double Score, int First, int Second) ReadVersion(int j, int[] pick)
            {
                var best = 0.0;
                int directRow = -1, first = -1, second = -1;
                double strongest = 0, next = 0;
                for (var r = 0; r < rows; r++)
                {
                    var i = pick[r];
                    var single = Math.Max(lift[j][r][i], combo[j][r][i]);
                    if (single > best) (best, directRow) = (single, r);
                    var meaning = semanticLift.Length == 0 ? 0 : semanticLift[j][r][i];
                    if (meaning > strongest)
                    {
                        (next, second) = (strongest, first);
                        (strongest, first) = (meaning, r);
                    }
                    else if (meaning > next) (next, second) = (meaning, r);
                }
                var paired = multi * strongest * next;
                return next > 1 && paired > best ? (paired, first, second) : (best, directRow, -1);
            }

            double HintLike(int j, int[] pick)
            {
                if (!versions)
                {
                    var sum = 0.0;
                    for (var r = 0; r < rows; r++) sum += d[j][r][pick[r]];
                    return noise + sum / rows;
                }
                return noise * rows + ReadVersion(j, pick).Score;
            }

            // Перебор всех раскладок (одометр по рядам): два прохода, без лишних выделений памяти.
            var marg = sizes.Select(n => new double[n]).ToArray();
            var count = sizes.Aggregate(1, (a, n) => a * n);
            var logp = new double[count];
            var pick = new int[rows];
            var maxLog = double.NegativeInfinity;
            var bestIndex = 0;
            // Unconstrained rows can tie exactly. Choose one representative jointly and consistently,
            // using the independent evidence and the bot's usual tie preference on those rows.
            var tieRanks = sizes.Select(n => new int[n]).ToArray();
            for (var r = 0; r < rows; r++)
            {
                var rank = 0;
                foreach (var i in Enumerable.Range(0, sizes[r]).OrderByDescending(i => before[view.Board[r].Cards[i]])
                             .ThenBy(i => CardPreference(r, i)))
                    tieRanks[r][i] = rank++;
            }
            var bestRank = int.MaxValue;
            for (var n = 0; n < count; n++)
            {
                var lp = 0.0;
                var rank = 0;
                for (var r = 0; r < rows; r++)
                {
                    lp += logPrior[r][pick[r]];
                    rank += tieRanks[r][pick[r]];
                }
                for (var j = 0; j < d.Length; j++) lp += Math.Log(HintLike(j, pick));
                logp[n] = lp;
                if (lp > maxLog || lp == maxLog && rank < bestRank) (maxLog, bestIndex, bestRank) = (lp, n, rank);
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
            var voteLogOdds = new Dictionary<string, double>();
            var stride = 1;
            for (var r = rows - 1; r >= 0; r--)
            {
                for (var i = 0; i < sizes[r]; i++)
                {
                    var card = view.Board[r].Cards[i];
                    truth[card] = marg[r][i] / total;
                    if (versions) voteLogOdds[card] = logp[bestIndex + (i - bestPick[r]) * stride] - maxLog;
                }
                stride *= sizes[r];
            }

            // Как лучшая версия читает каждую подсказку.
            var explanations = new List<HintExplanation>();
            var explained = new HashSet<string>();
            for (var j = 0; j < hintCards.Count; j++)
            {
                var (bestLift, bestRow, secondRow) = ReadVersion(j, bestPick);
                if (bestLift <= 1.5 || bestRow < 0)
                {
                    explanations.Add(new HintExplanation(hintCards[j], hintList[j].Round, null, "none", null, 1));
                    continue;
                }
                var card = view.Board[bestRow].Cards[bestPick[bestRow]];
                var secondCard = secondRow >= 0 ? view.Board[secondRow].Cards[bestPick[secondRow]] : null;
                var viaCombo = secondCard is null && combo[j][bestRow][bestPick[bestRow]] > lift[j][bestRow][bestPick[bestRow]];
                var repeated = explained.Contains(card) && (secondCard is null || explained.Contains(secondCard));
                var kind = viaCombo ? "combo" : secondCard is null && intent[j] == card ? "intent" : repeated ? "confirm" : "direct";
                var with = viaCombo && comboWith[j][bestRow][bestPick[bestRow]] is >= 0 and var k ? hintCards[k] : null;
                explanations.Add(new HintExplanation(hintCards[j], hintList[j].Round, card, kind, with, bestLift) { SecondCard = secondCard });
                explained.Add(card);
                if (secondCard is not null) explained.Add(secondCard);
            }
            var unexplained = explanations.Where(e => e.Kind == "none").Select(e => e.Hint).ToList();
            return new HintTheory(before, truth, bestPick.Select((i, r) => view.Board[r].Cards[i]).ToList(), unexplained)
                { Explanations = explanations, VoteLogOdds = voteLogOdds };
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
