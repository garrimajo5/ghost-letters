using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Domain.Rules;
using GhostLetters.Infrastructure.Bots;

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
        // ---------- Характер ----------
        // Без характера (mind == null) бот играет «классически» — как до появления индивидуальностей.
        private bool Classic => mind is null;

        private BotPersonality P => mind?.Personality ?? BotPersonality.Default;

        /// <summary>Похожесть карт глазами этого бота: внимание к смыслу, форме и цвету.</summary>
        private double Sim(string a, string b) => Classic ? tags.Similarity(a, b) : tags.Similarity(a, b, P.Attention);

        /// <summary>Насколько «не открыли моё письмо» отталкивает от похожих карт.</summary>
        private double NegativeWeight => Classic ? 0.6 : 1.6 * P.Negative;

        /// <summary>Призрак: с какой похожести письмо — подсказка. Рискованный открывает и сомнительные.</summary>
        private double ClueThreshold => Classic ? Hint : 0.28 - 0.16 * P.Risk;

        private double RevealThreshold => Classic ? 0.15 : 0.22 - 0.14 * P.Risk;

        private double RevealFallback => Classic ? 0.08 : 0.12 - 0.08 * P.Risk;

        /// <summary>Чёрные: как часто врут о своём письме, голосуют и пишут под ложную карту.</summary>
        private double LieChance => Classic ? 0.6 : 0.1 + 0.8 * P.Risk;

        private double FakeChance => Classic ? 1 : 0.25 + 0.75 * P.Risk;

        private Role? Known(Guid id) => view.Players.FirstOrDefault(p => p.Id == id)?.KnownRole;

        private string? NameOf(Guid id) => mind?.Names.GetValueOrDefault(id);

        /// <summary>
        /// Память: насколько игрок подозрителен по прошлым партиям с этим ботом (0 — как все, до +1).
        /// Доля партий в команде Убийцы сверх средней (~25%), умноженная на спектр памяти.
        /// </summary>
        private double PastSuspicion(Guid id) =>
            Classic || mind!.History.GetValueOrDefault(id) is not { Games: > 0 } h ? 0 : P.Memory * Math.Max(0, h.KillerRate - 0.25) * 2;

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
            foreach (var o in mind!.Opinions.Where(o => o.CardId == card && o.Author != me.Id))
            {
                var trust = Known(o.Author) is { } r && r.IsKillerTeam() && !KillerTeam ? P.Compromise * 0.5 : 1;
                trust *= 1 - PastSuspicion(o.Author) * (1 - P.Compromise);
                sum += o.Strength * trust;
            }

            return sum;
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
            var c = Classic ? 0 : P.Compromise * (social.Max() > 0 ? 1 : 0);
            return (1 - c) * Norm(own[i], own) + c * Norm(social[i], social);
        }

        private bool KillerTeam => me.Role.IsKillerTeam();

        private IReadOnlyList<int>? Truth => view.Truth;

        private string Card(int row, int column) => view.Board[row].Cards[column];

        private double Noise() => rng.NextDouble() * 1e-6;

        /// <summary>Насколько подсказки указывают на карту: сходство с открытыми письмами минус сходство с моими исчезнувшими.</summary>
        public double Evidence(string card)
        {
            var hints = view.Hints.SelectMany(h => h.Cards).Sum(h => Sim(h, card));
            var vanished = me.Letters.Where(l => l.Revealed == false).Sum(l => Sim(l.CardId, card));
            return hints - NegativeWeight * vanished;
        }

        /// <summary>Лучший столбец ряда по подсказкам (среди кандидатов, если заданы).</summary>
        public int BestColumn(int row, IReadOnlyList<int>? candidates)
        {
            var columns = candidates ?? Enumerable.Range(0, view.Board[row].Cards.Count).ToList();
            return columns.OrderByDescending(c => Evidence(Card(row, c)) + Noise()).First();
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
                        return fakes.OrderByDescending(c => Evidence(Card(stage.Row, c)) + Noise()).First();
                    }
                }
            }

            if (Classic)
            {
                return BestColumn(stage.Row, candidates);
            }

            return candidates.OrderByDescending(c => Blend(stage.Row, c, candidates) + Noise()).First();
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

                var unknown = candidates.Where(c => Known(c) is null || Known(c)!.Value.IsKillerTeam()).ToList();
                if (unknown.Count > 0)
                {
                    candidates = unknown;
                }

                // Подозреваем того, кто чаще других голосовал против карт, на которые указывают подсказки.
                // Память добавляет подозрение тем, кто часто бывал Убийцей в прошлых партиях с ботом.
                return candidates.OrderByDescending(c => Contrarian(c) + rng.NextDouble() * 0.5 + 3 * PastSuspicion(c)).First();
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

            var (card, score) = me.Hand.Select(h => (h, Best(h, TruthCards(truth)))).MaxBy(x => x.Item2 + Noise());
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
            var scored = mailbox.Select(l => (l, s: Best(l, TruthCards(truth)) + Noise())).OrderByDescending(x => x.s).ToList();
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

            return me.Hand.OrderByDescending(h => Sim(h, target) + Noise()).Take(count).ToList();
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
                                        + RowAccuracy(c) + 2 * PastInformed(c) + Noise())
                .First();
        }

        /// <summary>Тот, кто почти всегда голосовал за истинные карты, похож на Эксперта.</summary>
        public Role HuntGuess(Guid target) => view.HuntRoles is [var only]
            ? only
            : RowAccuracy(target) >= 0.75 ? Role.Expert : Role.Witness;

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
        private string Accusation()
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

            if (!KillerTeam && me.Role != Role.Ghost)
            {
                var suspect = view.Players.Where(p => p.Id != me.Id && !p.IsGhost)
                    .Select(p => (p.Id, s: PastSuspicion(p.Id))).OrderByDescending(x => x.s).FirstOrDefault();
                if (suspect.s > 0.25 && rng.NextDouble() < P.Risk && NameOf(suspect.Id) is { } who)
                {
                    return $" И помните: {who} уже бывал Убийцей.";
                }
            }

            return "";
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
            var opinion = Tone(lines[rng.Next(lines.Length)]) + Accusation();
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
                notes.Add("проверял эту");
            }

            if (!cards.Contains(card) && cards.Count < ChatService.MaxCards)
            {
                cards.Add(card);
                notes.Add("думаю, эта");
            }

            var checkedText = checkedCards.Count == 0 ? "" : $" Проверял: {string.Join(", ", checkedCards.Select(x => Where(x.Row, x.Column)))}.";
            return ($"{claimText}{checkedText} {opinion}", cards, notes);
        }

        /// <summary>Карты поля, на которые похоже письмо: до трёх самых похожих (без тегов — ни одной).</summary>
        private List<(int Row, int Column)> Checked(string letter) =>
            Enumerable.Range(0, view.Board.Count)
                .SelectMany(r => Enumerable.Range(0, view.Board[r].Cards.Count).Select(c => (Row: r, Column: c)))
                .Select(x => (x, s: Sim(letter, Card(x.Row, x.Column))))
                .Where(x => x.s > 0 && Card(x.x.Row, x.x.Column) != letter)
                .OrderByDescending(x => x.s)
                .Take(3)
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
