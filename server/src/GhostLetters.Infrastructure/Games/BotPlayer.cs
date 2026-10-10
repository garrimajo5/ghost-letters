using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Domain.Rules;
using GhostLetters.Infrastructure.Bots;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace GhostLetters.Infrastructure.Games;

/// <summary>
/// Бот играет по своей проекции (без подглядывания) и по тегам карт — «видит», что на них нарисовано:
/// Призрак открывает письма, похожие на истину; детективы шлют письма под проверяемую улику и голосуют
/// за карты, на которые указывают подсказки; Убийца путает следы и охотится на того, кто слишком уверенно
/// голосовал против него. Без тегов (<see cref="CardTags.Empty"/>) ходы почти случайные.
/// Не лайкает, не поднимает руку и не номинирует.
/// </summary>
public static class BotPlayer
{
    private static readonly HashSet<string> Passive = [nameof(Like), nameof(RaiseHand), nameof(GiveFloor)];

    /// <summary>Порог, с которого письмо «похоже» на улику.</summary>
    private const double Hint = 0.2;

    /// <summary>Ход бота или null, если ходить не нужно.</summary>
    public static GameCommand? Decide(PlayerView view, Random rng, CardTags? tags = null, BotMind? mind = null)
    {
        var allowed = view.AllowedCommands.Where(c => !Passive.Contains(c)).ToList();
        if (allowed.Count == 0 || view.Me is not { } me)
        {
            return null;
        }

        var brain = new Brain(view, me, rng, tags ?? CardTags.Empty, mind);
        var type = allowed.Contains(nameof(Discard)) ? nameof(Discard) : allowed[rng.Next(allowed.Count)];
        var stage = view.Finale?.CurrentStage;

        return type switch
        {
            nameof(AckRole) => new AckRole(),
            nameof(ChooseTruth) => new ChooseTruth(brain.TruthColumns()),
            nameof(TeamSuggest) => brain.Suggestion(),
            nameof(NameTruth) => new NameTruth(view.Board.Select((_, r) => brain.BestColumn(r, null)).ToList()),
            nameof(GiveFirstClue) => new GiveFirstClue(brain.FirstClue()),
            nameof(SendLetter) => new SendLetter(brain.Letters(GameDefaults.LettersPerPlayer(view.Players.Count))),
            nameof(RevealHints) => new RevealHints(brain.HintsToReveal()),
            nameof(Discard) => new Discard(brain.CardToDiscard()),
            nameof(EndTurn) => new EndTurn(),
            nameof(ReadyNextRound) => new ReadyNextRound(),
            nameof(ReadyRevote) => new ReadyRevote(),
            nameof(CastVote) when stage is { Kind: VoteStageKind.Row } => new CastVote(brain.RowVote(stage), null),
            nameof(CastVote) when stage is not null => new CastVote(null, brain.SuspectVote(stage)),
            nameof(CastVote) => new CastVote(null, null),
            nameof(HuntPick) => (brain.ListensToTeam() ? brain.TeamTarget() ?? brain.HuntTarget() : brain.HuntTarget()) is { } t
                ? new HuntPick(t, brain.TeamGuess(t) ?? brain.HuntGuess(t))
                : null,
            nameof(BlackmailerPick) => (brain.ListensToTeam() ? brain.TeamTarget() ?? brain.RandomOther() : brain.RandomOther()) is { } b
                ? new BlackmailerPick(b)
                : null,
            nameof(Nominate) => brain.Nomination(),
            nameof(AwardVote) => new AwardVote(brain.AwardChoice()),
            _ => null,
        };
    }

    /// <summary>Насколько подсказки указывают на карту глазами этого бота (для тестов и отладки).</summary>
    public static double Evidence(PlayerView view, CardTags tags, BotMind? mind, string card) =>
        view.Me is { } me ? new Brain(view, me, new Random(0), tags, mind).Evidence(card) : 0;

    /// <summary>Версия по всем рядам: те же улики, характер и мнения, что при голосовании.</summary>
    public static IReadOnlyList<int> Plan(PlayerView view, CardTags tags, BotMind? mind, Random rng) =>
        view.Me is { } me ? view.Board.Select((row, r) => new Brain(view, me, rng, tags, mind)
            .RowVote(new VoteStageView(0, VoteStageKind.Row, r, 1, Enumerable.Range(0, row.Cards.Count).ToList(), [])) ?? 0).ToList() : [];

    public static string DiscussionSuspicion(PlayerView view, CardTags tags, BotMind mind, Random rng) =>
        view.Me is { } me ? new Brain(view, me, rng, tags, mind).Accusation() : "";

    public static (string Text, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes)? Reply(
        PlayerView view, Random rng, CardTags tags, BotMind? mind) =>
        view.Me is { Role: not Role.Ghost } me ? new Brain(view, me, rng, tags, mind).Reply() : null;

    /// <summary>
    /// Реплика бота в обсуждении: что он думает об одном ряду, с упоминанием карты; если отправлял письмо —
    /// показывает его («кидал эту») и карты поля, которые им проверял (их может быть несколько).
    /// Команда Убийцы так же уверенно указывает на ложную карту. Призрак молчит.
    /// </summary>
    public static (string Text, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes)? Say(PlayerView view, Random rng, CardTags? tags = null, BotMind? mind = null)
    {
        if (view.Me is not { } me || me.Role == Role.Ghost || view.Board.Count == 0)
        {
            return null;
        }

        return new Brain(view, me, rng, tags ?? CardTags.Empty, mind).Say();
    }

    private sealed class Brain(PlayerView view, MeView me, Random rng, CardTags tags, BotMind? mind)
    {
        // One immutable role projection per decision: never share scores between players or turns.
        private readonly Dictionary<(string, string), double> _similarities = new();
        private readonly Dictionary<string, double> _evidence = new();
        private readonly Dictionary<string, Dictionary<string, int>> _ranks = new();
        private readonly Dictionary<(int Row, int Column), ulong> _cardPreferences = new();

        // ---------- Характер ----------
        // Без характера (mind == null) бот играет «классически» — как до появления индивидуальностей.
        private bool Classic => mind is null;

        private BotPersonality P => mind?.Personality ?? BotPersonality.Default;

        /// <summary>Похожесть карт глазами этого бота: внимание к смыслу, форме и цвету.</summary>
        private double Sim(string a, string b)
        {
            if (_similarities.TryGetValue((a, b), out var score)) return score;
            return _similarities[(a, b)] = Classic ? tags.Similarity(a, b) : tags.Similarity(a, b, P.Attention, P.Details, P.SecondaryMeanings);
        }

        /// <summary>Насколько «не открыли моё письмо» отталкивает от похожих карт.</summary>
        private double NegativeWeight => Classic ? 0.6 : 1.6 * P.Negative;

        /// <summary>Призрак: с какой похожести письмо — подсказка. Рискованный открывает и сомнительные.</summary>
        private double ClueThreshold => Classic ? Hint : 0.28 - 0.16 * P.Risk;

        private double RevealThreshold => Classic ? 0.15 : 0.22 - 0.14 * P.Risk;

        private double RevealFallback => Classic ? 0.08 : 0.12 - 0.08 * P.Risk;

        /// <summary>Чёрные: как часто врут о своём письме, голосуют и пишут под ложную карту.</summary>
        private double LieChance => Classic ? 0.6 : 0.1 + 0.8 * P.Risk;

        private double FakeChance => Classic ? 1 : 0.25 + 0.75 * P.Risk;

        /// <summary>Сколько карт поля бот «проверяет» одним письмом и читает за одной подсказкой (строгий — 1, широкий — 4).</summary>
        private int MyBreadth => Classic ? 3 : P.CheckBreadth;

        /// <summary>Место карты среди карт поля по похожести на письмо (0 — самая похожая).</summary>
        private int Rank(string letter, string card)
        {
            // Match Checked's stable ordering, including ties. Otherwise a strict
            // reader checks the first card but the Ghost expects every tied card.
            if (!_ranks.TryGetValue(letter, out var ranks))
            {
                ranks = view.Board.SelectMany(r => r.Cards).Where(c => c != letter)
                    .OrderByDescending(c => Sim(letter, c)).Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
                _ranks[letter] = ranks;
            }
            return ranks.GetValueOrDefault(card, ranks.Count);
        }

        /// <summary>
        /// Сколько подсказка говорит о карте. Строгий читает подсказку только как «одну из самых похожих карт»:
        /// всё, что дальше его ширины, почти не в счёт; широкий видит связь со многим.
        /// </summary>
        private double HintWeight(string hint, string card)
        {
            var s = Sim(hint, card);
            if (Classic || s <= 0)
            {
                return s;
            }

            return Rank(hint, card) < MyBreadth ? s : s * (1 - P.Strictness);
        }

        /// <summary>
        /// Призрак: насколько письмо укажет на истинную улику. Призрак-бот слушает обсуждение — знает, сколько карт
        /// стол «проверяет» одним письмом. Если истинная улика не среди стольких самых похожих карт поля,
        /// игроки её за письмом не увидят — письмо почти бесполезно.
        /// </summary>
        private double GhostScore(string letter, IReadOnlyList<int> truth)
        {
            var truthCards = TruthCards(truth);
            var best = Best(letter, truthCards);
            if (Classic)
            {
                return best;
            }

            var breadth = Math.Max(1, (int)Math.Round(mind!.TableBreadth));
            return truthCards.Any(t => Sim(letter, t) > 0 && Rank(letter, t) < breadth) ? best : best * 0.4;
        }

        private Role? Known(Guid id) => view.Players.FirstOrDefault(p => p.Id == id)?.KnownRole;

        private string? NameOf(Guid id) => mind?.Names.GetValueOrDefault(id);

        /// <summary>
        /// Память: насколько игрок подозрителен по прошлым партиям с этим ботом (0 — как все, до +1.5).
        /// Доля партий в команде Убийцы сверх средней (~25%), умноженная на спектр памяти.
        /// </summary>
        private double PastSuspicion(Guid id) =>
            Classic || mind!.History.GetValueOrDefault(id) is not { Games: > 0 } h ? 0 : P.Memory * Math.Max(0, h.KillerRate - 0.25) * 2;

        // PastSuspicion can exceed one. Distrust may discard a statement, never reverse its meaning.
        private double HistoryTrust(Guid id) => Math.Clamp(1 - PastSuspicion(id) * (1 - P.Compromise), 0, 1);

        private double PastInformed(Guid id) =>
            Classic || mind!.History.GetValueOrDefault(id) is not { Games: > 0 } h ? 0 : P.Memory * Math.Max(0, h.InformedRate - 0.15) * 2;

        /// <summary>
        /// Мнение стола о карте: кто показал её в чате («думаю, эта» — 1, «проверял эту» — 0.5), с доверием к автору.
        /// Компромисс решает, слушать ли и кого: низкий — никого, высокий — даже известную команду Убийцы.
        /// </summary>
        private double Social(string card)
        {
            if (Classic)
            {
                return 0;
            }

            double sum = 0;
            foreach (var o in mind!.Opinions.Where(o => o.CardId == card && o.Author != me.Id && !o.IsCheck))
            {
                var trust = Known(o.Author) is { } r && r.IsKillerTeam() && !KillerTeam ? P.Compromise * 0.5 : 1;
                trust *= HistoryTrust(o.Author);
                trust /= 1 + ChatContrarian(o.Author) * (1 - P.Compromise * 0.5);
                sum += o.Strength * trust * mind.TrustMultiplier(o.Author);
            }

            return sum;
        }

        /// <summary>
        /// Насколько стол обвиняет игрока: сумма обвинений из чата с доверием к их авторам. Сговорчивый
        /// прислушивается сильнее, упрямый — почти не слушает; слова известной команды Убийцы — со скидкой.
        /// </summary>
        private double Accused(Guid id)
        {
            if (Classic)
            {
                return 0;
            }

            double sum = 0;
            foreach (var a in mind!.AccusationList.Where(a => a.Target == id && a.Author != me.Id))
            {
                var trust = Known(a.Author) is { } r && r.IsKillerTeam() && !KillerTeam ? 0.3 * P.Compromise : 1;
                trust *= HistoryTrust(a.Author);
                sum += a.Strength * trust * mind.TrustMultiplier(a.Author);
            }

            return sum * (0.05 + 0.95 * P.Compromise);
        }

        /// <summary>
        /// Подозрительное поведение в чате: игрок советует карты, которые плохо сходятся с подсказками
        /// (по моей оценке; Эксперт и команда Убийцы сверяют с истиной). Чем больше — тем подозрительнее.
        /// </summary>
        private double ChatContrarian(Guid id)
        {
            if (Classic)
            {
                return 0;
            }

            var count = 0.0;
            foreach (var o in mind!.Opinions.Where(o => o.Author == id && !o.IsCheck && o.Strength != 0))
            {
                var (row, column) = Position(o.CardId);
                if (row < 0)
                {
                    continue;
                }

                if (Truth is { } truth && (me.Role == Role.Expert || KillerTeam))
                {
                    var supportsTruth = truth[row] == column;
                    if (supportsTruth != (o.Strength > 0)) count += Math.Abs(o.Strength);
                    continue;
                }

                var best = view.Board[row].Cards.Max(Evidence);
                var score = Evidence(o.CardId);
                // Rejecting one of several equally plausible cards is legitimate disagreement.
                // A negative opinion is evidence against its author only for a clear leader.
                var rejectsClearLeader = o.Strength < 0 && score > 0.15 &&
                    score - view.Board[row].Cards.Where(c => c != o.CardId).Select(Evidence).DefaultIfEmpty(0).Max() > 0.15;
                if ((o.Strength > 0 && best - score > 0.15) || rejectsClearLeader)
                {
                    count += Math.Abs(o.Strength) * 0.6;
                }
            }

            return count;
        }

        private (int Row, int Column) Position(string card)
        {
            for (var r = 0; r < view.Board.Count; r++)
            {
                for (var c = 0; c < view.Board[r].Cards.Count; c++)
                {
                    if (view.Board[r].Cards[c] == card)
                    {
                        return (r, c);
                    }
                }
            }

            return (-1, -1);
        }

        /// <summary>Итоговое подозрение к игроку: память, чужие обвинения и странные советы в чате.</summary>
        private double Suspicion(Guid id) => 3 * PastSuspicion(id) + 1.5 * Accused(id) +
            (0.5 + P.Strictness) * ChatContrarian(id) + Contrarian(id) - .25 * (mind?.AffinityBias(id) ?? 0);

        private string CardName(string card)
        {
            var (r, c) = Position(card);
            if (r < 0) return "письмо";
            var category = view.Board[r].Category switch {
                Category.Motive => "мотив", Category.Place => "место", Category.Method => "способ", _ => "тайна" };
            return $"{category} {c + 1}";
        }

        private string Connection(string from, string to) => tags.Explain(from, to, P.Attention, Classic ? 0 : P.Details, P.SecondaryMeanings);

        private string BehaviourReason(Guid id) => ChatContrarian(id) > 0
            ? "его версия хуже объясняет открытые подсказки, чем соседние карты"
            : Contrarian(id) > 0 ? "его голоса расходятся с моим чтением подсказок"
            : "пока опираюсь на подозрения стола и прошлые встречи, прямой улики нет";

        public (string Text, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes)? Reply()
        {
            if (Classic) return null;
            var candidates = view.Players.Where(p => p.Id != me.Id && !p.IsGhost).ToList();
            var ally = candidates.Where(p => Known(p.Id) is not { } role || !role.IsKillerTeam())
                .Select(p => (Player: p, Opinion: mind!.Opinions.FirstOrDefault(o => o.Author == p.Id && !o.IsCheck &&
                    o.Strength > 0 && Evidence(o.CardId) > 0.1)))
                .Where(x => x.Opinion is not null && ChatContrarian(x.Player.Id) == 0 && Suspicion(x.Player.Id) < 0.6)
                .OrderByDescending(x => Evidence(x.Opinion!.CardId)).FirstOrDefault();
            if (ally.Opinion is null || NameOf(ally.Player.Id) is not { } name) return null;
            var card = ally.Opinion.CardId;
            var text = $"{name}, твоя версия «{CardName(card)}» сходится с подсказками; пока считаю тебя мирным. ";
            var suspect = candidates.Where(p => p.Id != ally.Player.Id && (Known(p.Id) is null || Known(p.Id)!.Value.IsKillerTeam()))
                .OrderByDescending(p => Suspicion(p.Id)).FirstOrDefault();
            if (suspect is not null && Suspicion(suspect.Id) >= 1.3 - 0.9 * P.Risk && NameOf(suspect.Id) is { } target)
                text += $"Подозреваю, что {target} из чёрных: {BehaviourReason(suspect.Id)}. Предлагаю вместе голосовать против него.";
            else
                text += $"Предлагаю поддержать «{CardName(card)}». Какие ещё улики подтверждают нашу версию?";
            return (text, new[] { card }, new[] { "думаю, эта" });
        }

        /// <summary>Своя оценка карт ряда, смешанная с мнением стола в доле компромисса.</summary>
        private double Blend(int row, int column, IReadOnlyList<int> columns)
        {
            var own = columns.Select(c => Evidence(Card(row, c))).ToList();
            var social = columns.Select(c => Social(Card(row, c))).ToList();
            double Norm(double v, List<double> all)
            {
                var min = all.Min();
                var max = all.Max();
                return max - min < 1e-9 ? 0 : (v - min) / (max - min);
            }

            var i = columns.ToList().IndexOf(column);
            var c = Classic ? 0 : P.Compromise * (social.Any(s => Math.Abs(s) > 1e-9) ? 1 : 0);
            return (1 - c) * Norm(own[i], own) + c * Norm(social[i], social);
        }

        private bool KillerTeam => me.Role.IsKillerTeam();

        private IReadOnlyList<int>? Truth => view.Truth;

        private string Card(int row, int column) => view.Board[row].Cards[column];

        private double Noise() => rng.NextDouble() * 1e-6;

        // Resolve exact ties consistently across discussion, voting and process restarts.
        // Only public game/card identity and this bot's identity enter the preference.
        private ulong CardPreference(int row, int column)
        {
            if (_cardPreferences.TryGetValue((row, column), out var preference)) return preference;
            var key = $"card-tie-v1:{view.GameId:N}:{me.Id:N}:{row}:{Card(row, column)}";
            return _cardPreferences[(row, column)] = BinaryPrimitives.ReadUInt64LittleEndian(
                SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        }

        /// <summary>Насколько подсказки указывают на карту: сходство с открытыми письмами минус сходство с моими исчезнувшими.</summary>
        public double Evidence(string card)
        {
            if (_evidence.TryGetValue(card, out var score)) return score;
            var hints = view.Hints.SelectMany(h => h.Cards).Sum(h => HintWeight(h, card));
            var vanished = me.Letters.Where(l => l.Revealed == false).Sum(l => Sim(l.CardId, card));
            return _evidence[card] = hints - NegativeWeight * vanished;
        }

        /// <summary>Лучший столбец ряда по подсказкам (среди кандидатов, если заданы).</summary>
        public int BestColumn(int row, IReadOnlyList<int>? candidates)
        {
            var columns = candidates ?? Enumerable.Range(0, view.Board[row].Cards.Count).ToList();
            return columns.OrderByDescending(c => Evidence(Card(row, c))).ThenBy(c => CardPreference(row, c)).ThenBy(c => c).First();
        }

        public int? RowVote(VoteStageView stage)
        {
            var candidates = stage.CandidateColumns;
            if (candidates.Count == 0)
            {
                return null;
            }

            if (Truth is { } truth)
            {
                var real = truth[stage.Row];
                if (!KillerTeam && candidates.Contains(real))
                {
                    return real;
                }

                // Уводим голоса: самая убедительная на вид, но ложная карта. Осторожный иногда голосует
                // «как все», чтобы не выделяться.
                if (KillerTeam && (Classic || rng.NextDouble() < FakeChance))
                {
                    var fakes = candidates.Where(c => c != real).ToList();
                    if (fakes.Count > 0)
                    {
                        return fakes.OrderByDescending(c => Evidence(Card(stage.Row, c))).ThenBy(c => CardPreference(stage.Row, c)).ThenBy(c => c).First();
                    }
                }
            }

            if (Classic)
            {
                return BestColumn(stage.Row, candidates);
            }

            return candidates.OrderByDescending(c => Blend(stage.Row, c, candidates)).ThenBy(c => CardPreference(stage.Row, c)).ThenBy(c => c).First();
        }

        public Guid? SuspectVote(VoteStageView stage)
        {
            var candidates = stage.CandidateSuspects.Where(s => s != me.Id).ToList();
            if (candidates.Count == 0)
            {
                return null;
            }


            // Шантажисту, как и команде Убийцы, раскрытое дело невыгодно.
            if (!KillerTeam && me.Role != Role.Blackmailer)
            {
                // Свидетель (и все, кто знает Убийцу) голосует за него; остальные — не за тех, чья невиновность известна.
                var killer = candidates.FirstOrDefault(c => Known(c) == Role.Killer);
                if (killer != Guid.Empty)
                {
                    return killer;
                }

                // Призрак в списке кандидатов означает «Убийцы нет» (Озон на четверых).
                var unknown = candidates.Where(c => Known(c) is null || Known(c) == Role.Ghost || Known(c)!.Value.IsKillerTeam()).ToList();
                if (unknown.Count > 0)
                {
                    candidates = unknown;
                }

                // Подозреваем того, кто чаще других голосовал против карт, на которые указывают подсказки.
                // Память добавляет подозрение тем, кто часто бывал Убийцей в прошлых партиях с ботом.
                // Подозрения: память о прошлых партиях, обвинения стола и странные советы в чате.
                return candidates.OrderByDescending(c => rng.NextDouble() * 0.5 + Suspicion(c)).First();
            }
            else
            {
                var outsiders = candidates.Where(c => Known(c) is not { } r || !r.IsKillerTeam()).ToList();
                if (outsiders.Count > 0)
                {
                    candidates = outsiders;
                }
            }

            return candidates[rng.Next(candidates.Count)];
        }

        public string? FirstClue()
        {
            if (Truth is not { } truth || me.Hand.Count == 0)
            {
                return null;
            }

            if (tags.Count == 0)
            {
                return rng.Next(2) == 0 ? null : me.Hand[rng.Next(me.Hand.Count)];
            }

            var (card, score) = me.Hand.Select(h => (h, GhostScore(h, truth))).MaxBy(x => x.Item2 + Noise());
            return score >= ClueThreshold ? card : null;
        }

        public IReadOnlyList<string> HintsToReveal()
        {
            var mailbox = view.MailboxForGhost ?? [];
            // Без тегов похожесть не оценить — открываем наугад от нуля до всех писем.
            if (Truth is not { } truth || mailbox.Count == 0 || tags.Count == 0)
            {
                return mailbox.OrderBy(_ => rng.Next()).Take(rng.Next(0, mailbox.Count + 1)).ToList();
            }

            // Открываем все письма, похожие на истинные улики (их может быть сколько угодно — или ни одного).
            var scored = mailbox.Select(l => (l, s: GhostScore(l, truth) + Noise())).OrderByDescending(x => x.s).ToList();
            var good = scored.Where(x => x.s >= RevealThreshold).Select(x => x.l).ToList();
            if (good.Count == 0 && scored[0].s >= RevealFallback && rng.Next(3) > 0)
            {
                good.Add(scored[0].l);
            }

            return good;
        }

        public IReadOnlyList<string> Letters(int count)
        {
            if (me.Hand.Count == 0)
            {
                return [];
            }

            string target;
            if (KillerTeam && Truth is { } truth && (Classic || rng.NextDouble() < FakeChance))
            {
                // Письмо под ложную улику — Призрак его не откроет, но остальным покажется, что её проверяли.
                var row = rng.Next(view.Board.Count);
                var fakes = Enumerable.Range(0, view.Board[row].Cards.Count).Where(c => c != truth[row]).ToList();
                target = Card(row, fakes[rng.Next(fakes.Count)]);
            }
            else
            {
                // Проверяем самый неясный ряд: его лучшую (или случайную) карту.
                var row = Enumerable.Range(0, view.Board.Count).OrderBy(r => Certainty(r) + Noise()).First();
                target = Card(row, Truth is { } t && me.Role == Role.Expert ? t[row] : BestColumn(row, null));
            }

            // Строгий выбирает письмо, похожее на цель и ни на что больше на поле, — чтобы проверить ровно её.
            var board = view.Board.SelectMany(r => r.Cards).Where(c => c != target).ToList();
            double Pick(string h) => Classic ? Sim(h, target) : Sim(h, target) - 0.6 * P.Strictness * Best(h, board);
            return me.Hand.OrderByDescending(h => Pick(h) + Noise()).Take(count).ToList();
        }

        public string? CardToDiscard()
        {
            if (me.Hand.Count == 0)
            {
                return null;
            }

            var board = view.Board.SelectMany(r => r.Cards).ToList();
            var (card, score) = me.Hand.Select(h => (h, Best(h, board))).MinBy(x => x.Item2 + Noise());
            return score < 0.1 && rng.Next(2) == 0 ? card : null;
        }

        private IReadOnlyList<TeamSuggestionView> Team => view.TeamSuggestions ?? [];

        /// <summary>Ночью Убийца берёт в каждом ряду карту, которую чаще предлагали Сообщники; без подсказок — случайную.</summary>
        public IReadOnlyList<int> TruthColumns()
        {
            var listen = ListensToTeam();
            return view.Board.Select((r, i) =>
        {
            var offered = listen ? Team.Where(s => s.Columns is { } c && c.Count > i).Select(s => s.Columns![i]).ToList() : [];
            return offered.Count > 0
                ? offered.GroupBy(c => c).OrderByDescending(g => g.Count()).ThenBy(_ => rng.Next()).First().Key
                : rng.Next(r.Cards.Count);
        }).ToList();
        }

        /// <summary>Игрок, на которого чаще указывали Сообщники.</summary>
        public Guid? TeamTarget() => Team.Where(s => s.Target is not null)
            .GroupBy(s => s.Target!.Value).OrderByDescending(g => g.Count()).ThenBy(_ => rng.Next()).FirstOrDefault()?.Key;

        public Role? TeamGuess(Guid target) => Team.Where(s => s.Target == target && s.Guess is not null)
            .GroupBy(s => s.Guess!.Value).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;

        /// <summary>Сообщник подсказывает один раз за фазу: ночью — случайные карты, на охоте — своего подозреваемого.</summary>
        /// <summary>Убийца слушает Сообщников с вероятностью по компромиссу (классический — всегда).</summary>
        public bool ListensToTeam() => Classic || Team.Count == 0 || rng.NextDouble() < 0.3 + 0.7 * P.Compromise;

        public TeamSuggest? Suggestion()
        {
            if (Team.Any(s => s.From == me.Id))
            {
                return null;
            }

            return view.Phase switch
            {
                Phase.Night => new TeamSuggest(Columns: view.Board.Select(r => rng.Next(r.Cards.Count)).ToList()),
                Phase.Hunt => HuntTarget() is { } t ? new TeamSuggest(Target: t, Guess: HuntGuess(t)) : null,
                Phase.BlackmailerHunt => RandomOther() is { } b ? new TeamSuggest(Target: b) : null,
                _ => null,
            };
        }

        /// <summary>Охота: тот, кто голосовал против команды Убийцы, — скорее всего Свидетель или Эксперт.</summary>
        public Guid? HuntTarget()
        {
            var team = view.Players.Where(p => p.Id == me.Id || (p.KnownRole is { } r && r.IsKillerTeam())).Select(p => p.Id).ToHashSet();
            var candidates = view.Players.Where(p => !p.IsGhost && !team.Contains(p.Id)).Select(p => p.Id).ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            var votes = view.Finale?.Votes ?? [];
            return candidates
                .OrderByDescending(c => votes.Count(v => v.Voter == c && v.Suspect is { } s && team.Contains(s)) * 2
                                        + RowAccuracy(c) + 2 * PastInformed(c) + 3 * AccusedTeam(c, team)
                                        + (ClaimedHuntRole(c) is null ? 0 : 4) + Noise())
                .First();
        }

        /// <summary>Сколько игрок обвинял в чате команду Убийцы — меткий обвинитель похож на Свидетеля.</summary>
        private double AccusedTeam(Guid player, HashSet<Guid> team) =>
            Classic ? 0 : mind!.AccusationList.Where(a => a.Author == player && team.Contains(a.Target)).Sum(a => a.Strength);

        /// <summary>Тот, кто почти всегда голосовал за истинные карты, похож на Эксперта.</summary>
        private Role? ClaimedHuntRole(Guid target) => mind?.RoleClaims is { } claims
            && claims.TryGetValue(target, out var role) && view.HuntRoles?.Contains(role) == true ? role : null;

        public Role HuntGuess(Guid target) => view.HuntRoles is [var only]
            ? only
            : ClaimedHuntRole(target) ?? (RowAccuracy(target) >= 0.75 ? Role.Expert : Role.Witness);

        public Guid? RandomOther()
        {
            var others = view.Players
                .Where(p => !p.IsGhost && p.Id != me.Id && !(p.KnownRole is { } r && r.IsKillerTeam()))
                .Select(p => p.Id).ToList();
            return others.Count == 0 ? null : others[rng.Next(others.Count)];
        }

        /// <summary>Сколько раз игрок голосовал в рядах не за ту карту, которую считаю лучшей я.</summary>
        private double Contrarian(Guid player)
        {
            if (view.Finale is not { } finale)
            {
                return 0;
            }

            var rows = finale.Outcomes.Where(o => o.Kind == VoteStageKind.Row).ToDictionary(o => o.Stage, o => o.Row);
            return finale.Votes
                .Where(v => v.Voter == player && v.Column is not null && rows.ContainsKey(v.Stage))
                .Count(v => v.Column != BestColumn(rows[v.Stage], null));
        }

        private double RowAccuracy(Guid player)
        {
            if (Truth is not { } truth || view.Finale is not { } finale)
            {
                return 0;
            }

            var rows = finale.Votes.Where(v => v.Voter == player && v.Column is not null).ToList();
            if (rows.Count == 0)
            {
                return 0;
            }

            var stages = finale.Outcomes.Where(o => o.Kind == VoteStageKind.Row).ToDictionary(o => o.Stage, o => o.Row);
            var hits = rows.Count(v => stages.TryGetValue(v.Stage, out var row) && truth[row] == v.Column);
            return hits / (double)rows.Count;
        }

        private double Certainty(int row)
        {
            var scores = view.Board[row].Cards.Select(Evidence).OrderByDescending(s => s).ToList();
            return scores.Count < 2 ? 0 : scores[0] - scores[1];
        }

        /// <summary>Манера речи: осторожный сомневается, рисковый говорит уверенно.</summary>
        private string Tone(string line)
        {
            if (Classic)
            {
                return line;
            }

            if (P.Risk < 0.3 && rng.Next(2) == 0)
            {
                return "Не уверен, но " + char.ToLowerInvariant(line[0]) + line[1..];
            }

            return P.Risk > 0.75 && rng.Next(2) == 0 ? line.TrimEnd('.', '?') + " — уверен." : line;
        }

        /// <summary>
        /// Обвинение в чате. Свидетель знает Убийцу: рисковый называет его прямо, средний намекает,
        /// осторожный молчит (иначе его вычислят на охоте). Детектив с сильной памятью
        /// вспоминает, кто часто бывал Убийцей.
        /// </summary>
        public string Accusation()
        {
            if (Classic)
            {
                return "";
            }

            if (me.Role == Role.Witness && view.Players.FirstOrDefault(p => p.KnownRole == Role.Killer && p.Id != me.Id) is { } killer
                && NameOf(killer.Id) is { } name)
            {
                if (P.Risk > 0.7 && rng.NextDouble() < P.Risk)
                {
                    return $" Мне кажется, Убийца — {name}.";
                }

                if (P.Risk > 0.4 && rng.NextDouble() < P.Risk)
                {
                    return $" Я бы присмотрелся к {name}.";
                }

                return "";
            }

            if (me.Role == Role.Ghost)
            {
                return "";
            }

            var others = view.Players.Where(p => p.Id != me.Id && !p.IsGhost).ToList();
            if (KillerTeam)
            {
                // Чёрные переводят стрелки: рисковый обвиняет того, кто обвинял его команду (или случайного «белого»).
                if (rng.NextDouble() >= P.Risk * 0.7)
                {
                    return "";
                }

                var team = others.Where(p => p.KnownRole is { } r && r.IsKillerTeam()).Select(p => p.Id).Append(me.Id).ToHashSet();
                var target = others.Where(p => !team.Contains(p.Id))
                    .OrderByDescending(p => mind!.AccusationList.Where(a => a.Author == p.Id && team.Contains(a.Target)).Sum(a => a.Strength) + rng.NextDouble() * 0.3)
                    .FirstOrDefault();
                return target is not null && NameOf(target.Id) is { } framed ? $" Подозреваю, что {framed} из чёрных." : "";
            }

            // Белые: говорят о подозрениях, если они достаточно сильные; смелый — раньше, осторожный — только при уверенности.
            var top = others.Where(p => p.KnownRole is not { } r || r.IsKillerTeam())
                .Select(p => (p.Id, s: Suspicion(p.Id))).OrderByDescending(x => x.s).FirstOrDefault();
            if (top.Id == Guid.Empty || NameOf(top.Id) is not { } who || top.s < 1.3 - 0.9 * P.Risk)
            {
                return "";
            }

            if (top.s > 2)
            {
                return $" Думаю, Убийца — {who}: {BehaviourReason(top.Id)}.";
            }

            return PastSuspicion(top.Id) > 0.25 && rng.Next(2) == 0
                ? $" Подозреваю {who}: уже бывал Убийцей."
                : $" Подозреваю, что {who} из чёрных: {BehaviourReason(top.Id)}.";
        }

        public (string Text, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes) Say()
        {
            static string Name(Category c) => c switch
            {
                Category.Motive => "Мотив",
                Category.Place => "Место",
                Category.Method => "Способ",
                _ => "Тайна",
            };

            var (claimText, claimCard) = LetterClaim();
            int target;
            int column;
            if (KillerTeam && Truth is { } truth && (Classic || rng.NextDouble() < FakeChance))
            {
                target = rng.Next(view.Board.Count);
                var fakes = Enumerable.Range(0, view.Board[target].Cards.Count).Where(c => c != truth[target]).ToList();
                column = fakes.OrderByDescending(c => Evidence(Card(target, c)) + Noise()).First();
            }
            else
            {
                target = Enumerable.Range(0, view.Board.Count).OrderByDescending(r => Certainty(r) + Noise()).First();
                // Эксперт знает истину: осторожный редко её выдаёт (его ищут на охоте), рисковый — почти всегда.
                column = Truth is { } t && me.Role == Role.Expert && (Classic ? rng.Next(3) > 0 : rng.NextDouble() < 0.2 + 0.7 * P.Risk)
                    ? t[target]
                    : BestColumn(target, null);
            }

            var card = Card(target, column);
            string Where(int r, int c) => $"{Name(view.Board[r].Category).ToLowerInvariant()} {c + 1}";
            var lines = Evidence(card) > 0
                ? new[]
                {
                    $"Подсказки похожи на эту: {Name(view.Board[target].Category)} — карта {column + 1}.",
                    $"Ставлю на {Name(view.Board[target].Category).ToLowerInvariant()}, карта {column + 1}.",
                    $"По-моему, {Name(view.Board[target].Category)}: {column + 1}. Кто против?",
                }
                : new[]
                {
                    $"Пока не ясно. Проверил бы {Name(view.Board[target].Category).ToLowerInvariant()} — карту {column + 1}.",
                    $"Есть идея про {Name(view.Board[target].Category).ToLowerInvariant()}: карта {column + 1}?",
                };
            var opinion = "Версия: " + Tone(lines[rng.Next(lines.Length)]);
            var links = view.Hints.SelectMany(h => h.Cards.Select((id, i) => (Id: id, h.Round, Number: i + 1)))
                .Where(h => HintWeight(h.Id, card) > 0.01).OrderByDescending(h => HintWeight(h.Id, card)).Take(2)
                .Select(h => $"Подсказка р.{h.Round} №{h.Number} → {Where(target, column)} ({Connection(h.Id, card)}).");
            opinion += "\n" + string.Join("\n", links);
            if (!links.Any()) opinion += "Связь с открытыми уликами слабая — это только предположение.";
            if (!Classic && view.Board.Count > 1)
            {
                var version = new List<string>();
                for (var r = 0; r < view.Board.Count; r++)
                {
                    var options = Enumerable.Range(0, view.Board[r].Cards.Count)
                        .Where(c => !KillerTeam || Truth is null || Truth[r] != c).ToList();
                    if (options.Count == 0) continue;
                    var best = BestColumn(r, options);
                    if (Evidence(Card(r, best)) > 0.05) version.Add(Where(r, best));
                }
                if (version.Count > 1) opinion += "\nСвязная версия: " + string.Join("; ", version) + ".";
            }
            opinion += Accusation();
            if (claimCard is null)
            {
                return (opinion, new List<string> { card }, new List<string> { "думаю, эта" });
            }

            // Письмом проверяют сразу несколько карт поля — те, на которые оно похоже.
            var checkedCards = Checked(claimCard);
            var cards = new List<string> { claimCard };
            var notes = new List<string> { "кидал эту" };
            foreach (var (r, c) in checkedCards)
            {
                cards.Add(Card(r, c));
                notes.Add(!Classic && Card(r, c) == card ? "проверял эту; думаю, эта" : "проверял эту");
            }

            if (!cards.Contains(card) && cards.Count < ChatService.MaxCards)
            {
                cards.Add(card);
                notes.Add("думаю, эта");
            }

            var checkedText = checkedCards.Count == 0 ? "Проверял: явной связи с полем нет." :
                $"Проверял: {string.Join(", ", checkedCards.Select(x => Where(x.Row, x.Column)))}.\n" +
                string.Join("\n", checkedCards.Select(x => $"• {Where(x.Row, x.Column)} — {Connection(claimCard, Card(x.Row, x.Column))}."));
            var vanished = claimText.Contains("исчезла", StringComparison.Ordinal);
            var conclusion = vanished
                ? checkedCards.Count == 0 ? "Письмо исчезло, но явных связей с картами поля я не вижу."
                    : NegativeWeight > 0 ? "Письмо исчезло: это ослабляет эти версии, но не исключает карты — другие улики могут перевесить."
                        : "Письмо исчезло, но я не считаю это исключением карт."
                : claimText.Contains("открыл", StringComparison.Ordinal)
                    ? checkedCards.Count > 0 ? "Открытие письма усиливает эти связи." : "Письмо открыто, но явных связей с картами поля я не вижу."
                    : "Пока жду результат проверки.";
            // Лимит чата соблюдаем по законченным строкам, не обрывая объяснение посреди слова.
            var sections = new[] { claimText, checkedText, conclusion, opinion };
            var text = string.Join("\n", sections);
            if (text.Length > ChatService.MaxTextLength)
                text = string.Join("\n", new[] { claimText, checkedText, conclusion, $"Версия: {Where(target, column)}." });
            return (text, cards, notes);
        }

        /// <summary>
        /// Карты поля, на которые похоже письмо: столько самых похожих, какова ширина бота
        /// (строгий — одна, широкий — до четырёх; без характера — три; без тегов — ни одной).
        /// </summary>
        private List<(int Row, int Column)> Checked(string letter) =>
            Enumerable.Range(0, view.Board.Count)
                .SelectMany(r => Enumerable.Range(0, view.Board[r].Cards.Count).Select(c => (Row: r, Column: c)))
                .Select(x => (x, s: Sim(letter, Card(x.Row, x.Column))))
                .Where(x => x.s > 0 && Card(x.x.Row, x.x.Column) != letter)
                .OrderByDescending(x => x.s)
                .Take(Math.Min(MyBreadth, ChatService.MaxCards - 1))
                .Select(x => x.x)
                .ToList();

        /// <summary>
        /// Что бот говорит о своём письме этого раунда. По правилам можно рассказывать, что отправил;
        /// чёрные (команда Убийцы, Шантажист, Подражатель) иногда врут — выдают чужую открытую подсказку за свою.
        /// </summary>
        private (string Text, string? Card) LetterClaim()
        {
            var letter = me.Letters.LastOrDefault(l => l.Round == view.Round) ?? me.Letters.LastOrDefault();
            if (letter is null)
            {
                return ("", null);
            }

            var liar = me.Role is Role.Killer or Role.Accomplice or Role.Blackmailer or Role.Imitator;
            var others = view.Hints.Where(h => h.Round == letter.Round).SelectMany(h => h.Cards).Where(c => c != letter.CardId).ToList();
            if (liar && letter.Revealed != true && others.Count > 0 && (Classic ? rng.Next(5) < 3 : rng.NextDouble() < LieChance))
            {
                return ("Я отправлял вот эту — и она открылась!", others[rng.Next(others.Count)]);
            }

            return letter.Revealed switch
            {
                true => ("Моё письмо — вот это, оно открылось.", letter.CardId),
                false => ("Отправлял вот эту — исчезла.", letter.CardId),
                null => ("Отправил вот эту, ждём Призрака.", letter.CardId),
            };
        }

        private static readonly string[] Awards = ["sherlock", "best_liar", "steel_balls", "ghost_whisperer"];

        /// <summary>Выдвижение: в половине случаев — случайная ачивка случайному игроку (не себе), иначе пропуск.</summary>
        public Nominate Nomination()
        {
            var others = view.Players.Where(p => p.Id != me.Id).ToList();
            if (others.Count == 0 || rng.Next(2) == 0)
            {
                return new Nominate(null, null);
            }

            var nominee = others[rng.Next(others.Count)];
            var code = nominee.IsGhost ? "ghost_whisperer" : Awards[rng.Next(Awards.Length - 1)];
            return new Nominate(code, nominee.Id);
        }

        /// <summary>Голос за чужое выдвижение не себя; нечего выбрать — пропуск.</summary>
        public int? AwardChoice()
        {
            var entries = (view.Finale?.Awards ?? []).Where(a => !a.MineNomination && a.Nominee != me.Id).ToList();
            return entries.Count == 0 ? null : entries[rng.Next(entries.Count)].Index;
        }

        /// <summary>Карта поля, больше всего похожая на данную.</summary>
        private (int Row, int Column) Closest(string card) =>
            Enumerable.Range(0, view.Board.Count)
                .SelectMany(r => Enumerable.Range(0, view.Board[r].Cards.Count).Select(c => (r, c)))
                .MaxBy(x => Sim(card, Card(x.r, x.c)) + Noise());

        private List<string> TruthCards(IReadOnlyList<int> truth) => truth.Select((c, r) => Card(r, c)).ToList();

        private double Best(string card, IEnumerable<string> others) => others.Select(o => Sim(card, o)).DefaultIfEmpty(0).Max();
    }
}
