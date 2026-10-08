using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Domain.Rules;

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
    public static GameCommand? Decide(PlayerView view, Random rng, CardTags? tags = null)
    {
        var allowed = view.AllowedCommands.Where(c => !Passive.Contains(c)).ToList();
        if (allowed.Count == 0 || view.Me is not { } me)
        {
            return null;
        }

        var brain = new Brain(view, me, rng, tags ?? CardTags.Empty);
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
            nameof(HuntPick) => (brain.TeamTarget() ?? brain.HuntTarget()) is { } t ? new HuntPick(t, brain.TeamGuess(t) ?? brain.HuntGuess(t)) : null,
            nameof(BlackmailerPick) => (brain.TeamTarget() ?? brain.RandomOther()) is { } b ? new BlackmailerPick(b) : null,
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
    public static (string Text, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes)? Say(PlayerView view, Random rng, CardTags? tags = null)
    {
        if (view.Me is not { } me || me.Role == Role.Ghost || view.Board.Count == 0)
        {
            return null;
        }

        return new Brain(view, me, rng, tags ?? CardTags.Empty).Say();
    }

    private sealed class Brain(PlayerView view, MeView me, Random rng, CardTags tags)
    {
        private bool KillerTeam => me.Role.IsKillerTeam();

        private IReadOnlyList<int>? Truth => view.Truth;

        private string Card(int row, int column) => view.Board[row].Cards[column];

        private double Noise() => rng.NextDouble() * 1e-6;

        /// <summary>Насколько подсказки указывают на карту: сходство с открытыми письмами минус сходство с моими исчезнувшими.</summary>
        public double Evidence(string card)
        {
            var hints = view.Hints.SelectMany(h => h.Cards).Sum(h => tags.Similarity(h, card));
            var vanished = me.Letters.Where(l => l.Revealed == false).Sum(l => tags.Similarity(l.CardId, card));
            return hints - 0.6 * vanished;
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

                if (KillerTeam)
                {
                    // Уводим голоса: самая убедительная на вид, но ложная карта.
                    var fakes = candidates.Where(c => c != real).ToList();
                    if (fakes.Count > 0)
                    {
                        return fakes.OrderByDescending(c => Evidence(Card(stage.Row, c)) + Noise()).First();
                    }
                }
            }

            return BestColumn(stage.Row, candidates);
        }

        public Guid? SuspectVote(VoteStageView stage)
        {
            var candidates = stage.CandidateSuspects.Where(s => s != me.Id).ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            Role? Known(Guid id) => view.Players.FirstOrDefault(p => p.Id == id)?.KnownRole;

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
                return candidates.OrderByDescending(c => Contrarian(c) + rng.NextDouble() * 0.5).First();
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
            return score >= Hint ? card : null;
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
            var good = scored.Where(x => x.s >= 0.15).Select(x => x.l).ToList();
            if (good.Count == 0 && scored[0].s >= 0.08 && rng.Next(3) > 0)
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
            if (KillerTeam && Truth is { } truth)
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

            return me.Hand.OrderByDescending(h => tags.Similarity(h, target) + Noise()).Take(count).ToList();
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
        public IReadOnlyList<int> TruthColumns() => view.Board.Select((r, i) =>
        {
            var offered = Team.Where(s => s.Columns is { } c && c.Count > i).Select(s => s.Columns![i]).ToList();
            return offered.Count > 0
                ? offered.GroupBy(c => c).OrderByDescending(g => g.Count()).ThenBy(_ => rng.Next()).First().Key
                : rng.Next(r.Cards.Count);
        }).ToList();

        /// <summary>Игрок, на которого чаще указывали Сообщники.</summary>
        public Guid? TeamTarget() => Team.Where(s => s.Target is not null)
            .GroupBy(s => s.Target!.Value).OrderByDescending(g => g.Count()).ThenBy(_ => rng.Next()).FirstOrDefault()?.Key;

        public Role? TeamGuess(Guid target) => Team.Where(s => s.Target == target && s.Guess is not null)
            .GroupBy(s => s.Guess!.Value).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;

        /// <summary>Сообщник подсказывает один раз за фазу: ночью — случайные карты, на охоте — своего подозреваемого.</summary>
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
                                        + RowAccuracy(c) + Noise())
                .First();
        }

        /// <summary>Тот, кто почти всегда голосовал за истинные карты, похож на Эксперта.</summary>
        public Role HuntGuess(Guid target) => RowAccuracy(target) >= 0.75 ? Role.Expert : Role.Witness;

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
            if (KillerTeam && Truth is { } truth)
            {
                target = rng.Next(view.Board.Count);
                var fakes = Enumerable.Range(0, view.Board[target].Cards.Count).Where(c => c != truth[target]).ToList();
                column = fakes.OrderByDescending(c => Evidence(Card(target, c)) + Noise()).First();
            }
            else
            {
                target = Enumerable.Range(0, view.Board.Count).OrderByDescending(r => Certainty(r) + Noise()).First();
                column = Truth is { } t && me.Role == Role.Expert && rng.Next(3) > 0 ? t[target] : BestColumn(target, null);
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
            var opinion = lines[rng.Next(lines.Length)];
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
                .Select(x => (x, s: tags.Similarity(letter, Card(x.Row, x.Column))))
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
            if (liar && letter.Revealed != true && others.Count > 0 && rng.Next(5) < 3)
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
                .MaxBy(x => tags.Similarity(card, Card(x.r, x.c)) + Noise());

        private List<string> TruthCards(IReadOnlyList<int> truth) => truth.Select((c, r) => Card(r, c)).ToList();

        private double Best(string card, IEnumerable<string> others) => others.Select(o => tags.Similarity(card, o)).DefaultIfEmpty(0).Max();
    }
}
