using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Вывод бота из заявлений стола — для речи («проверили трое, а чёрных двое…»).</summary>
/// <param name="Kind">consensus — карту отвергли несколько игроков, хоть один из них честен; contradiction — в ряду
/// «проверены» все карты: либо Призрак ошибся, либо кто-то врёт.</param>
public sealed record Deduction(string Kind, int Row, string? Card, Guid? Who, int? Round, double Strength, string Text);

/// <summary>Итог логики заявлений: убеждения о картах до и после заявлений, о чёрных и выводы для речи.</summary>
public sealed record ClaimLogicResult(
    IReadOnlyDictionary<string, double> Prior,
    IReadOnlyDictionary<string, double> Truth,
    IReadOnlyDictionary<Guid, double> Black,
    IReadOnlyList<Deduction> Deductions);

public static partial class BotPlayer
{
    /// <summary>Логика заявлений, как её видит бот (для тестов и логов); null — выключена или не к кому применить.</summary>
    public static ClaimLogicResult? Logic(PlayerView view, CardTags tags, BotMind? mind) =>
        view.Me is { Role: not Role.Ghost } me && view.Board.Count > 0 ? new Brain(view, me, new Random(0), tags, mind).Logic() : null;

    private sealed partial class Brain
    {
        private ClaimLogicResult? _logic;
        private Dictionary<Guid, double>? _baseBlack;
        private int _blackAmongUnknown;
        private bool _inLogic;

        /// <summary>Логика заявлений работает у мирных с памятью о чате; чёрные знают своих и так.</summary>
        private bool LogicOn => BotTuning.Current.ClaimLogic && !Classic && !KillerTeam && mind is not null;

        private sealed record CheckClaim(Guid Author, string Card, int Row, double S, int? Round);

        private List<ClaimedLetter>? _reports;
        private List<ClaimedLetter>? _reportedMisses;

        // Chat and board reports describe physical letters, independently of the proposed targets.
        private List<ClaimedLetter> PublicReports()
        {
            if (_reports is not null) return _reports;
            if (mind is null) return [];
            var players = view.Players.Where(p => !p.IsGhost && p.Id != me.Id).Select(p => p.Id).ToHashSet();
            var reports = mind.ClaimList
                .Concat(view.Table?.Claims.Select(c => new ClaimedLetter(c.Author, c.Card, c.Round)) ?? [])
                .Concat(mind.Opinions.Where(o => o.Public && o.IsCheck && o.SourceCard is not null)
                    .Select(o => new ClaimedLetter(o.Author, o.SourceCard!, o.Round ?? view.Round)));
            return _reports = reports.Where(c => players.Contains(c.Author) && c.Round > 0 && c.Round <= view.Round)
                .Distinct().ToList();
        }

        private List<ClaimedLetter> ReportedMisses()
        {
            if (_reportedMisses is not null) return _reportedMisses;
            if (mind is null) return [];
            var unavailable = view.Board.SelectMany(r => r.Cards).Concat(view.Hints.SelectMany(h => h.Cards))
                .Concat(me.Letters.Select(l => l.CardId)).Concat(me.Hand).Concat(me.Discarded ?? []).ToHashSet();
            return _reportedMisses = PublicReports().Where(c => !unavailable.Contains(c.Card)
                    && (c.Round < view.Round || view.Phase >= Phase.Refill))
                .GroupBy(c => c.Card)
                // Conflicting ownership is social evidence, not two independent negative checks.
                .Where(g => g.Count() == 1).Select(g => g.Single()).ToList();
        }

        private List<CheckClaim> CheckClaims()
        {
            var claims = new List<CheckClaim>();
            foreach (var report in ReportedMisses())
                for (var row = 0; row < view.Board.Count; row++)
                    foreach (var card in view.Board[row].Cards)
                    {
                        var match = VanishWeight(report.Card, card);
                        if (match <= 0) continue;
                        var clarity = 1 - Math.Exp(-4 * match);
                        if (match >= BotTuning.Current.VanishBar) clarity = Math.Max(clarity, Math.Clamp(BotTuning.Current.ClaimFloor, 0, 1));
                        var strength = clarity * OutshoneFactor(report.Card, report.Round, card) * Recall(report.Round, .6);
                        claims.Add(new(report.Author, card, row, strength, report.Round));
                    }
            return claims;
        }

        private double BaseEvidence(string card) =>
            view.Hints.SelectMany(h => h.Cards).Sum(h => HintWeight(h, card))
            - NegativeWeight * me.Letters.Where(l => l.Revealed == false)
                .Sum(l => VanishWeight(l.CardId, card) * OutshoneFactor(l.CardId, l.Round, card));

        /// <summary>Резкость убеждений о ряде — та же, что в <see cref="RowBelief"/>.</summary>
        private double Beta => 2 + 4 * P.Strictness;

        /// <summary>
        /// Боты 4.0: логика заявлений. Бот перебирает все варианты «кто за столом чёрный» (с их правдоподобием из модели
        /// игроков) и для каждого — истину в рядах. Честный говорит «проверял — исчезло» о ложной карте (о истинной — только
        /// если Призрак ошибся, <see cref="BotTuning.GhostError"/>); лжец — о любой, а об истине даже охотнее (отсекает её).
        /// Отсюда сами собой выходят человеческие выводы: «проверили трое, чёрных двое — хоть один честен, верю» и «в ряду
        /// проверены все карты — либо Призрак ошибся, либо кто-то врёт, и скорее тот, кто и так под подозрением».
        /// </summary>
        public ClaimLogicResult? Logic()
        {
            if (_logic is not null) return _logic;
            if (!LogicOn || _inLogic) return null;
            if (_black is null) BlackOdds();
            if (_logic is not null) return _logic;
            _inLogic = true;
            try { return _logic = ComputeLogic(); }
            finally { _inLogic = false; }
        }

        private ClaimLogicResult ComputeLogic()
        {
            var t = BotTuning.Current;
            var basis = _baseBlack ?? _black ?? new Dictionary<Guid, double>();
            var others = view.Players.Where(p => !p.IsGhost && p.Id != me.Id).ToList();
            var knownBlack = others.Where(p => p.KnownRole is { } r && r.IsKillerTeam()).Select(p => p.Id).ToHashSet();
            var unknown = others.Where(p => p.KnownRole is null).Select(p => p.Id).ToList();
            var k = Math.Clamp(_baseBlack is null ? 0 : _blackAmongUnknown, 0, unknown.Count);
            var claims = CheckClaims().Where(c => !knownBlack.Contains(c.Author)).ToList();

            // Evaluate a report against the clues and our own letters before hearing it.
            // Otherwise an ambiguous hint can incriminate a player even though a different row explains it.
            var prior = new Dictionary<string, double>();
            if (claims.Count > 0 && TheoryOn)
                prior = new(ComputeTheory(BaseEvidence, includeIntent: false).Truth);
            else
                foreach (var row in view.Board)
                {
                    Normalize(row.Cards.Select(BaseEvidence).ToArray(), Beta, out var rowPrior);
                    for (var i = 0; i < rowPrior.Length; i++) prior[row.Cards[i]] = rowPrior[i];
                }

            // Никто ничего не проверял — логике нечего добавить (но повторные «письма» сказать стоит).
            if (claims.Count == 0)
                return new ClaimLogicResult(prior, prior, unknown.ToDictionary(id => id, id => basis.GetValueOrDefault(id)), ClaimDeductions(knownBlack));

            var odds = unknown.ToDictionary(id => id, id => Math.Clamp(basis.GetValueOrDefault(id, k / (double)Math.Max(1, unknown.Count)), .02, .98));
            var truth = prior.Keys.ToDictionary(c => c, _ => 0.0);
            var black = unknown.ToDictionary(id => id, _ => 0.0);
            var subsets = Subsets(unknown, k).ToList();
            // Заранее: по каждой карте — её заявления с логарифмами правдоподобия честного и лжеца.
            var byCard = claims.GroupBy(c => c.Card).ToDictionary(g => g.Key, g => g.Select(c =>
                (c.Author, Honest: Math.Log(1 - c.S * (1 - t.GhostError)), Liar: Math.Log(1 + t.LiarCut * c.S))).ToArray());
            var logPrior = prior.ToDictionary(x => x.Key, x => Math.Log(Math.Max(1e-9, x.Value)));
            var logZ = new double[subsets.Count];
            var rowPost = new List<Dictionary<string, double>>(subsets.Count);
            for (var b = 0; b < subsets.Count; b++)
            {
                var liars = subsets[b].Concat(knownBlack).ToHashSet();
                var z = subsets[b].Sum(id => Math.Log(odds[id] / (1 - odds[id])));
                var post = new Dictionary<string, double>();
                for (var r = 0; r < view.Board.Count; r++)
                {
                    var cards = view.Board[r].Cards;
                    var a = new double[cards.Count];
                    for (var i = 0; i < cards.Count; i++)
                    {
                        a[i] = logPrior[cards[i]];
                        if (byCard.TryGetValue(cards[i], out var said))
                            foreach (var c in said) a[i] += liars.Contains(c.Author) ? c.Liar : c.Honest;
                    }
                    var max = a.Max();
                    var lse = max + Math.Log(a.Sum(x => Math.Exp(x - max)));
                    z += lse;
                    for (var i = 0; i < cards.Count; i++) post[cards[i]] = Math.Exp(a[i] - lse);
                }
                logZ[b] = z;
                rowPost.Add(post);
            }

            var zMax = logZ.Length == 0 ? 0 : logZ.Max();
            var weights = logZ.Select(z => Math.Exp(z - zMax)).ToArray();
            var total = weights.Sum();
            for (var b = 0; b < subsets.Count; b++)
            {
                var w = weights[b] / total;
                foreach (var (card, p) in rowPost[b]) truth[card] += w * p;
                foreach (var id in subsets[b]) black[id] += w;
            }
            if (subsets.Count == 0) foreach (var card in prior.Keys) truth[card] = prior[card];

            return new ClaimLogicResult(prior, truth, black,
                [.. Deductions(claims, prior, truth, black, knownBlack, k + knownBlack.Count), .. ClaimDeductions(knownBlack)]);
        }

        /// <summary>Все наборы из <paramref name="k"/> игроков.</summary>
        private static IEnumerable<IReadOnlyList<Guid>> Subsets(IReadOnlyList<Guid> items, int k)
        {
            if (k == 0) { yield return []; yield break; }
            var idx = Enumerable.Range(0, k).ToArray();
            while (true)
            {
                yield return idx.Select(i => items[i]).ToList();
                var j = k - 1;
                while (j >= 0 && idx[j] == items.Count - k + j) j--;
                if (j < 0) yield break;
                idx[j]++;
                for (var i = j + 1; i < k; i++) idx[i] = idx[i - 1] + 1;
            }
        }

        private string RowWord(int row) => view.Board[row].Category switch
        {
            Category.Motive => "мотивов", Category.Place => "мест", Category.Method => "способов", _ => "тайн",
        };

        private static string Times(int n) => n switch { 2 => "двое", 3 => "трое", 4 => "четверо", 5 => "пятеро", _ => $"{n} игроков" };

        private static string Blacks(int n) => n switch { 1 => "один чёрный", 2 => "двое чёрных", 3 => "трое чёрных", _ => $"{n} чёрных" };

        private List<Deduction> Deductions(List<CheckClaim> claims, Dictionary<string, double> prior, Dictionary<string, double> truth,
            Dictionary<Guid, double> black, HashSet<Guid> knownBlack, int blacks)
        {
            var result = new List<Deduction>();
            // «Проверили несколько — хоть один честен»: карта, в которую ещё можно было верить, после слов стола отпала.
            foreach (var g in claims.Where(c => c.S >= .3 && !knownBlack.Contains(c.Author)).GroupBy(c => c.Card))
            {
                var authors = g.Select(c => c.Author).Distinct().Count();
                if (authors < 2 || prior[g.Key] < .12 || truth[g.Key] > Math.Min(.2, prior[g.Key] / 2)) continue;
                var name = CardName(g.Key);
                var text = authors > blacks && blacks > 0
                    ? $"{Cap(name)} проверили {Times(authors)}, а у нас {Blacks(blacks)} — хоть один говорил честно. Если Призрак понял письмо так же, эту карту я бы отодвинул."
                    : $"{Cap(name)} проверили {Times(authors)} — вряд ли все они чёрные, так что {name} я вычёркиваю.";
                result.Add(new Deduction("consensus", g.First().Row, g.Key, null, g.Max(c => c.Round), prior[g.Key] - truth[g.Key], text));
            }

            // «Проверены все карты ряда»: так не бывает — либо Призрак ошибся, либо кто-то врёт.
            for (var r = 0; r < view.Board.Count; r++)
            {
                var inRow = claims.Where(c => c.Row == r && c.S >= .3).ToList();
                if (view.Board[r].Cards.Any(card => inRow.All(c => c.Card != card))) continue;
                var suspect = inRow.Select(c => c.Author).Distinct()
                    .Select(id => (Id: id, Black: knownBlack.Contains(id) ? 1 : black.GetValueOrDefault(id))).MaxBy(x => x.Black);
                var who = suspect.Black >= .5 ? NameOf(suspect.Id) : null;
                var text = $"Все карты {RowWord(r)} уже кто-то проверил, а истина среди них есть — так не бывает: либо Призрак ошибся, либо кто-то врёт. " +
                    (who is not null ? $"Скорее врёт {who}." : "Скорее Призрак не понял чьё-то письмо.");
                result.Add(new Deduction("contradiction", r, null, who is not null ? suspect.Id : null, inRow.Max(c => c.Round), 1, text));
            }
            return result;
        }

        /// <summary>
        /// Выводы из заявлений «кидал эту»: одну и ту же карту игрок назвал своим письмом в разных раундах —
        /// точно врёт (брошенное письмо в руку не возвращается); одну карту назвали своим письмом двое —
        /// кто-то из них врёт. Подозрение это уже даёт (<see cref="ClaimConflict"/>), а тут — реплика вслух.
        /// </summary>
        private List<Deduction> ClaimDeductions(HashSet<Guid> knownBlack)
        {
            var result = new List<Deduction>();
            if (mind is null) return result;
            var claims = PublicReports();
            string CardLabel(string card) => tags.NameOf(card) ?? CardName(card);
            foreach (var g in claims.GroupBy(c => (c.Author, c.Card)))
            {
                var rounds = g.Select(c => c.Round).Distinct().OrderBy(r => r).ToList();
                if (rounds.Count < 2 || knownBlack.Contains(g.Key.Author)) continue;
                var name = CardLabel(g.Key.Card);
                var who = NameOf(g.Key.Author);
                var text = who is null
                    ? $"«{Cap(name)}» кто-то назвал своим письмом дважды — в раундах {rounds[0]} и {rounds[^1]}. Одно письмо дважды не отправишь: врут."
                    : $"{who}, «{name}» ты уже называл своим письмом в раунде {rounds[0]} — одно письмо дважды не отправишь. Значит, врёшь.";
                result.Add(new Deduction("repeat", Position(g.Key.Card).Row, g.Key.Card, g.Key.Author, rounds[^1], 3, text));
            }
            foreach (var g in claims.GroupBy(c => c.Card))
            {
                var authors = g.Select(c => c.Author).Distinct().ToList();
                if (authors.Count < 2 || authors.All(knownBlack.Contains)) continue;
                var names = authors.Select(a => NameOf(a) ?? "кто-то ещё").ToList();
                var both = names.Count == 2 ? $"{names[0]} и {names[1]}" : string.Join(", ", names);
                var text = $"«{Cap(CardLabel(g.Key))}» назвали своим письмом и {both} — кто-то из них врёт.";
                result.Add(new Deduction("conflict", Position(g.Key).Row, g.Key, null, g.Max(c => c.Round), 1.2, text));
            }
            return result;
        }

        public double Strictness => P.Strictness;

        public string? Name(Guid id) => NameOf(id);

        private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

        /// <summary>Вывод, который стоит сказать сейчас: появился в этом раунде (новое заявление), самый весомый.</summary>
        public Deduction? FreshDeduction() =>
            BotTuning.Current.DeduceSpeech && Logic() is { } logic
                ? logic.Deductions.Where(d => d.Round == view.Round).OrderByDescending(d => d.Strength).FirstOrDefault()
                : null;
    }
}
