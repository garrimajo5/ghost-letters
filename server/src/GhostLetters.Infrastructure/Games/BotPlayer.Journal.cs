using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Bots;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Чего касается улика: истины в рядах или роли игрока.</summary>
public enum ClueKind
{
    /// <summary>Открытая подсказка Призрака.</summary>
    Hint,

    /// <summary>Моё письмо исчезло — похожие карты скорее ложные.</summary>
    MyVanished,

    /// <summary>Чужое письмо исчезло (со слов автора).</summary>
    TheirVanished,

    /// <summary>Отверг карту, на которую потом указала подсказка.</summary>
    Lied,

    /// <summary>Назвал своим чужое письмо.</summary>
    ClaimConflict,

    /// <summary>Версия хуже объясняет подсказки, чем соседние карты.</summary>
    ChatAgainstHints,

    /// <summary>Голоса расходятся с моим чтением подсказок.</summary>
    VotesAgainstHints,

    /// <summary>«Проверяет» уже ясные ряды.</summary>
    Useless,

    /// <summary>Его обвиняют другие.</summary>
    Accused,

    /// <summary>Память о прошлых партиях.</summary>
    Memory,

    /// <summary>Отношения: симпатия снижает подозрение.</summary>
    Affinity,
}

/// <summary>
/// Боты 4.0, шаг 1: одна улика в журнале бота. <see cref="Cards"/> — насколько улика говорит «эта карта истинна»
/// (минус — «ложна»); <see cref="Roles"/> — насколько говорит «этот игрок чёрный». Сумма по журналу даёт ровно то же,
/// что прежние <c>Evidence</c> и счёт подозрения: журнал пока только объясняет решения, а не меняет их.
/// </summary>
/// <param name="Salience">Заметность 0…1: ложь и разоблачение — ярко, рутина — тускло (для памяти и внимания, шаг 4).</param>
/// <param name="Reason">Человеческая причина — для логов и речи.</param>
public sealed record Clue(
    ClueKind Kind,
    Guid? Source,
    int? Round,
    double Salience,
    IReadOnlyDictionary<string, double> Cards,
    IReadOnlyDictionary<Guid, double> Roles,
    string Reason);

/// <summary>Журнал улик бота и что из него следует (для тестов и логов).</summary>
public sealed record BotJournal(
    IReadOnlyList<Clue> Clues,
    IReadOnlyDictionary<string, double> Evidence,
    IReadOnlyDictionary<Guid, double> Suspicion)
{
    /// <summary>Убеждения о рядах: вероятность, что карта истинна (в каждом ряду сумма — 1).</summary>
    public IReadOnlyDictionary<string, double> Truth { get; init; } = new Dictionary<string, double>();

    /// <summary>Убеждения об игроках: вероятность, что игрок из команды Убийцы.</summary>
    public IReadOnlyDictionary<Guid, double> Black { get; init; } = new Dictionary<Guid, double>();

    /// <summary>Насколько бот верит словам игрока (1 — как случайному, 0 — не верит).</summary>
    public IReadOnlyDictionary<Guid, double> Trust { get; init; } = new Dictionary<Guid, double>();

    /// <summary>Состояние бота внутри партии.</summary>
    public BotAffect? Affect { get; init; }

    /// <summary>Поддержка карты по журналу: сумма улик.</summary>
    public double CardSupport(string card) => Clues.Sum(c => c.Cards.GetValueOrDefault(card));

    /// <summary>Подозрение к игроку по журналу: сумма улик.</summary>
    public double RoleSupport(Guid player) => Clues.Sum(c => c.Roles.GetValueOrDefault(player));
}

public static partial class BotPlayer
{
    /// <summary>
    /// Боты 4.0, шаг 1: журнал улик бота — всё, на чём он строит мнение о рядах и игроках, с причинами.
    /// Пока журнал только объясняет прежние решения (сумма улик = Evidence и счёт подозрения).
    /// </summary>
    public static BotJournal? Journal(PlayerView view, CardTags? tags = null, BotMind? mind = null)
    {
        if (view.Me is not { Role: not Role.Ghost } me || view.Board.Count == 0) return null;
        var brain = new Brain(view, me, new Random(0), tags ?? CardTags.Empty, mind);
        var clues = brain.Clues();
        var cards = view.Board.SelectMany(r => r.Cards).ToDictionary(c => c, brain.Evidence);
        var players = view.Players.Where(p => !p.IsGhost && p.Id != me.Id).ToDictionary(p => p.Id, p => brain.RoleScore(p.Id));
        var others = view.Players.Where(p => !p.IsGhost && p.Id != me.Id).Select(p => p.Id).ToList();
        return new BotJournal(clues, cards, players)
        {
            Truth = view.Board.SelectMany((_, r) => brain.RowBelief(r)).ToDictionary(x => x.Key, x => x.Value),
            Black = others.ToDictionary(id => id, brain.BlackOf),
            Trust = others.ToDictionary(id => id, brain.TrustOf),
            Affect = brain.Affect(),
        };
    }

    private sealed partial class Brain
    {
        /// <summary>
        /// Боты 4.0, шаг 2: убеждение о ряде — вероятность истины каждой карты (softmax по уликам; строгий
        /// сосредоточен на лидере, мягкий держит в уме и соседей).
        /// </summary>
        public IReadOnlyDictionary<string, double> RowBelief(int row)
        {
            var focus = 2 + 4 * P.Strictness;
            var cards = view.Board[row].Cards;
            var top = cards.Max(Evidence);
            var weights = cards.Select(c => Math.Exp(focus * (Evidence(c) - top))).ToList();
            var sum = weights.Sum();
            return cards.Select((c, i) => (c, weights[i] / sum)).ToDictionary(x => x.c, x => x.Item2);
        }

        public double BlackOf(Guid id) => Black(id);

        public double TrustOf(Guid id) => Trust(id);

        /// <summary>Счёт подозрения, как его видит модель игроков (для журнала и тестов).</summary>
        public double RoleScore(Guid id) => RoleEvidence(id);

        public IReadOnlyList<Clue> Clues()
        {
            var clues = new List<Clue>();
            var board = view.Board.SelectMany(r => r.Cards).ToList();
            var none = new Dictionary<Guid, double>();
            var noCards = new Dictionary<string, double>();

            Dictionary<string, double> OnBoard(Func<string, double> support) =>
                board.Select(c => (c, support(c))).Where(x => x.Item2 != 0).ToDictionary(x => x.c, x => x.Item2);

            // Подсказки: каждая открытая карта — отдельная улика за похожие карты.
            foreach (var group in view.Hints)
                foreach (var hint in group.Cards)
                {
                    var support = OnBoard(c => HintWeight(hint, c));
                    if (support.Count == 0) continue;
                    var top = support.MaxBy(x => x.Value).Key;
                    clues.Add(new Clue(ClueKind.Hint, view.Players.FirstOrDefault(p => p.IsGhost)?.Id, group.Round, .7, support, none,
                        $"{(group.Round == 0 ? "зацепка" : $"подсказка раунда {group.Round}")} похожа на {CardName(top)}"));
                }

            // Мои исчезнувшие письма — против похожих карт.
            foreach (var letter in me.Letters.Where(l => l.Revealed == false))
            {
                var support = OnBoard(c => -NegativeWeight * (Classic || !BotTuning.Current.VanishedFocus ? Sim(letter.CardId, c)
                    : GhostCluePolicy.Read(Sim(letter.CardId, c), HintTop(letter.CardId), Focus)) * OutshoneFactor(letter.CardId, letter.Round, c));
                if (support.Count == 0) continue;
                var top = support.MinBy(x => x.Value).Key;
                clues.Add(new Clue(ClueKind.MyVanished, me.Id, letter.Round, .8, support, none, $"моё письмо исчезло — {CardName(top)} вряд ли истина"));
            }

            // Чужие исчезнувшие письма — со слов автора, с доверием к нему.
            var vanished = OnBoard(c => -NegativeWeight * OthersVanished(c));
            if (vanished.Count > 0)
                clues.Add(new Clue(ClueKind.TheirVanished, null, null, .5, vanished, none, "письма других, которые не открыли"));

            // Улики против игроков — по одной на вид, чтобы у каждой была своя причина.
            foreach (var p in view.Players.Where(p => !p.IsGhost && p.Id != me.Id))
            {
                void Add(ClueKind kind, double weight, double salience, string reason)
                {
                    if (weight == 0) return;
                    clues.Add(new Clue(kind, p.Id, null, salience, noCards, new Dictionary<Guid, double> { [p.Id] = weight }, reason));
                }

                if (!KillerTeam)
                {
                    Add(ClueKind.Lied, BotTuning.Current.LieDetect * LiedChecks(p.Id), .9, "отверг карту, на которую потом указала подсказка");
                    Add(ClueKind.ClaimConflict, ClaimConflict(p.Id), 1, "назвал своим письмо, которое было не его");
                    Add(ClueKind.Useless, .6 * Useless(p.Id), .4, "проверяет уже ясные ряды");
                }

                Add(ClueKind.ChatAgainstHints, (0.5 + P.Strictness) * ChatContrarian(p.Id), .5, "его версия хуже объясняет подсказки");
                Add(ClueKind.VotesAgainstHints, Contrarian(p.Id), .6, "его голоса расходятся с подсказками");
                Add(ClueKind.Accused, 1.5 * Accused(p.Id), .5, "его подозревают другие");
                Add(ClueKind.Memory, 3 * PastSuspicion(p.Id), .3, "память о прошлых партиях");
                Add(ClueKind.Affinity, -.25 * (mind?.AffinityBias(p.Id) ?? 0), .2, "отношения с ним");
            }

            return clues;
        }
    }
}
