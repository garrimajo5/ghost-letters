using GhostLetters.Domain.Roles;

namespace GhostLetters.Infrastructure.Games;

/// <summary>
/// Боты 3.0, этап 2: модель игроков — с какой вероятностью каждый за столом чёрный.
/// Улики против игрока (ложь о проверках, малая польза, споры с подсказками, обвинения стола, память) складываются
/// в «счёт», а счёт — в вероятность с учётом того, сколько чёрных за столом: если один почти наверняка Убийца,
/// подозрение к остальным падает. Известное (свои, Убийца для Свидетеля) учитывается сразу.
/// </summary>
public static partial class BotPlayer
{
    private sealed partial class Brain
    {
        private Dictionary<Guid, double>? _black;

        /// <summary>Сколько чёрных за столом по составу (без учёта опций лобби — их бот не видит).</summary>
        private int BlackCount()
        {
            var n = view.Players.Count;
            if (n < RoleTable.MinCompetitivePlayers) return 0;
            return 1 + (n >= 10 ? 2 : n >= 7 ? 1 : 0) + (n == 6 ? 1 : 0);
        }

        /// <summary>Вероятность, что игрок из команды Убийцы — по модели этого бота.</summary>
        private double Black(Guid id) => BlackOdds().GetValueOrDefault(id);

        private IReadOnlyDictionary<Guid, double> BlackOdds()
        {
            if (_black is not null) return _black;
            var result = new Dictionary<Guid, double>();
            var others = view.Players.Where(p => !p.IsGhost && p.Id != me.Id).ToList();
            var black = BlackCount() - (me.Role.IsKillerTeam() ? 1 : 0);
            var unknown = new List<Guid>();
            foreach (var p in others)
            {
                if (p.KnownRole is { } role) { result[p.Id] = role.IsKillerTeam() ? 1 : 0; if (role.IsKillerTeam()) black--; }
                else unknown.Add(p.Id);
            }

            // Свидетель знает Убийцу, но не Сообщников: Убийца — 1, остальные чёрные среди неизвестных.
            if (black <= 0 || unknown.Count == 0)
            {
                foreach (var id in unknown) result[id] = 0;
                return _black = result;
            }

            var target = Math.Min(black, unknown.Count - .05);
            // Пока считаем модель игроков, доверие к словам — по-старому (иначе круг: доверие → улики → модель).
            _computingBlack = true;
            Dictionary<Guid, double> score;
            try { score = unknown.ToDictionary(id => id, RoleEvidence); }
            finally { _computingBlack = false; }
            // Улики о картах дальше пересчитаем уже с доверием из модели.
            if (BotTuning.Current.UnifiedTrust) _evidence.Clear();
            // Сдвиг подбираем так, чтобы сумма вероятностей = числу чёрных среди неизвестных.
            double lo = -20, hi = 20;
            for (var i = 0; i < 50; i++)
            {
                var mid = (lo + hi) / 2;
                if (unknown.Sum(id => Logistic(1.3 * score[id] + mid)) > target) hi = mid; else lo = mid;
            }
            foreach (var id in unknown) result[id] = Logistic(1.3 * score[id] + lo);
            return _black = result;
        }

        /// <summary>
        /// Насколько игрок похож именно на Убийцу, а не на Сообщника. Сообщник вредит громко — врёт о проверках,
        /// отсекает истину, присваивает чужие письма; Убийца маскируется. Поэтому «громкие» улики при нескольких
        /// чёрных говорят скорее о Сообщнике, и в голосе за Убийцу их вес снижаем.
        /// </summary>
        private double Killer(Guid id)
        {
            if (view.Players.FirstOrDefault(p => p.Id == id)?.KnownRole is { } known) return known == Role.Killer ? 1 : 0;
            var black = Black(id);
            if (BlackCount() <= 1) return black;
            var loud = BotTuning.Current.LieDetect * LiedChecks(id) + ClaimConflict(id);
            return black * Math.Exp(-BotTuning.Current.AccompliceLoud * loud);
        }

        private bool _computingBlack;

        /// <summary>
        /// Боты 4.0, шаг 2: насколько верить словам игрока — из той же модели игроков, по которой бот подозревает
        /// и голосует (1 — как случайному игроку за столом, 0 — точно чёрный, выше 1 не бывает). Раньше доверие
        /// считалось отдельной формулой и расходилось с подозрениями.
        /// </summary>
        private double Trust(Guid id)
        {
            if (!BotTuning.Current.UnifiedTrust || Classic || KillerTeam || _computingBlack)
                return HistoryTrust(id) * DistrustFactor(id);
            if (Known(id) is { } known) return known.IsKillerTeam() ? 0 : 1;
            var unknown = view.Players.Count(p => !p.IsGhost && p.Id != me.Id && p.KnownRole is null);
            var prior = Math.Min(.9, Math.Max(0, BlackCount() - (me.Role.IsKillerTeam() ? 1 : 0)) / (double)Math.Max(1, unknown));
            // Память о прошлых партиях — предел сверху: «он всегда был Убийцей» не перевешивается тем, что за столом трое.
            return Math.Min(HistoryTrust(id), Math.Clamp((1 - Black(id)) / (1 - prior), 0, 1));
        }

        private static double Logistic(double x) => 1 / (1 + Math.Exp(-x));

        /// <summary>Улики против игрока одним числом (0 — ничего против него нет).</summary>
        private double RoleEvidence(Guid id) => Suspicion(id) + (KillerTeam ? 0 : .6 * Useless(id));

        /// <summary>
        /// Малая польза: игрок «проверял» карты рядов, которые уже прояснила подсказка прошлых раундов.
        /// Так чёрные изображают работу, не помогая столу.
        /// </summary>
        private double Useless(Guid id)
        {
            if (Classic || mind is null) return 0;
            var settled = new Dictionary<int, int>(); // ряд → раунд, с которого он ясен
            foreach (var hint in view.Hints)
                foreach (var h in hint.Cards)
                {
                    var best = view.Board.SelectMany(r => r.Cards).Where(c => c != h).OrderByDescending(c => Sim(h, c)).Take(2).ToList();
                    if (best.Count < 2 || Sim(h, best[0]) - Sim(h, best[1]) <= .1) continue;
                    var row = Position(best[0]).Row;
                    if (row >= 0 && (!settled.TryGetValue(row, out var at) || hint.Round < at)) settled[row] = hint.Round;
                }

            var useless = mind.Opinions.Where(o => o.Author == id && o.IsCheck && o.Round is { } r &&
                Position(o.CardId).Row is var row && row >= 0 && settled.TryGetValue(row, out var at) && at < r).Count();
            return Math.Min(2, .5 * useless);
        }

        /// <summary>Самая весомая причина подозрения — для реплики «из чёрных: …».</summary>
        private string RoleReason(Guid id)
        {
            var reasons = new List<(double Weight, string Text)>
            {
                (BotTuning.Current.LieDetect * LiedChecks(id), "отверг карту, на которую потом указала подсказка"),
                (ClaimConflict(id), "назвал своим письмо, которое было не его"),
                (.6 * Useless(id), "проверяет уже ясные ряды — пользы от него мало"),
                ((0.5 + P.Strictness) * ChatContrarian(id), "его версия хуже объясняет открытые подсказки, чем соседние карты"),
                (Contrarian(id), "его голоса расходятся с моим чтением подсказок"),
                (1.5 * Accused(id), "его подозревают и другие"),
                (3 * PastSuspicion(id), "уже бывал Убийцей"),
            };
            var top = reasons.OrderByDescending(r => r.Weight).First();
            return top.Weight > 0 ? top.Text : "пока прямой улики нет, только ощущение";
        }
    }
}
