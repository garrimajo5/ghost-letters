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
            nameof(ChooseTruth) => new ChooseTruth(view.Board.Select(r => rng.Next(r.Cards.Count)).ToList()),
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
            nameof(HuntPick) => brain.HuntTarget() is { } t ? new HuntPick(t, brain.HuntGuess(t)) : null,
            nameof(BlackmailerPick) => brain.RandomOther() is { } b ? new BlackmailerPick(b) : null,
            nameof(Nominate) => new Nominate(null, null),
            nameof(AwardVote) => new AwardVote(null),
            _ => null,
        };
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
            // Без тегов похожесть не оценить — открываем наугад одно-два письма.
            if (Truth is not { } truth || mailbox.Count == 0 || tags.Count == 0)
            {
                return mailbox.OrderBy(_ => rng.Next()).Take(rng.Next(1, 3)).ToList();
            }

            var scored = mailbox.Select(l => (l, s: Best(l, TruthCards(truth)) + Noise())).OrderByDescending(x => x.s).ToList();
            var good = scored.Where(x => x.s >= Hint).Take(3).Select(x => x.l).ToList();
            if (good.Count == 0 && scored[0].s >= Hint / 2)
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

        private List<string> TruthCards(IReadOnlyList<int> truth) => truth.Select((c, r) => Card(r, c)).ToList();

        private double Best(string card, IEnumerable<string> others) => others.Select(o => tags.Similarity(card, o)).DefaultIfEmpty(0).Max();
    }
}
