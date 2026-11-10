using GhostLetters.Domain.Roles;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Состояние бота внутри партии (Боты 4.0, шаг 5): всё — от 0 до 1.</summary>
/// <param name="Confidence">Уверенность в версии: средняя вероятность лидера по рядам.</param>
/// <param name="Anxiety">Тревога: насколько стол подозревает меня.</param>
/// <param name="Excitement">Азарт: как близко финал.</param>
public sealed record BotAffect(double Confidence, double Anxiety, double Excitement);

public static partial class BotPlayer
{
    private sealed partial class Brain
    {
        private BotAffect? _affect;

        /// <summary>
        /// Состояние складывается из того, что бот уже думает: уверенность — из убеждений о рядах, тревога — из
        /// обвинений в мой адрес, азарт — из близости финала. Обида — отдельно, к конкретному игроку (<see cref="Grudge"/>).
        /// </summary>
        public BotAffect Affect()
        {
            if (_affect is not null) return _affect;
            var confidence = view.Board.Count == 0 ? 0 : Enumerable.Range(0, view.Board.Count).Average(r => RowBelief(r).Values.Max());
            var anxiety = Math.Clamp(Accused(me.Id) / Math.Max(.1, P.Compromise), 0, 1);
            var excitement = view.TotalRounds <= 1 ? 1 : Math.Clamp((view.Round - 1) / (double)(view.TotalRounds - 1), 0, 1);
            return _affect = new BotAffect(confidence, anxiety, excitement);
        }

        /// <summary>
        /// Есть ли в реплике новое: карта, которую я ещё не называл, проверка своего письма («кидал / проверял»),
        /// обвинение. Иначе это повтор — люди в таком случае обычно молчат.
        /// </summary>
        public bool HasNews((string Text, IReadOnlyList<string> Cards, IReadOnlyList<string> Notes) line)
        {
            if (Classic || mind is null) return true;
            if (line.Text.Contains("Убийц", StringComparison.Ordinal) || line.Text.Contains("чёрн", StringComparison.Ordinal)) return true;
            if (line.Notes.Any(n => n.StartsWith("кидал", StringComparison.Ordinal) || n.StartsWith("проверял", StringComparison.Ordinal))) return true;
            var said = mind.OwnSaid.Select(o => o.CardId).ToHashSet();
            return line.Cards.Count == 0 || line.Cards.Any(c => !said.Contains(c));
        }

        /// <summary>Шанс промолчать без повода: дотошный (детали) и встревоженный говорят чаще.</summary>
        public double SilenceChance() => Math.Clamp(BotTuning.Current.SilenceBase - .5 * P.Details - .3 * Affect().Anxiety, 0, 1);

        /// <summary>Обида на игрока: он назвал своим моё письмо или отверг карту, на которую потом указала подсказка.</summary>
        private double Grudge(Guid id) => KillerTeam ? 0 : Math.Clamp(.5 * ClaimConflict(id) + .3 * LiedChecks(id), 0, 1);

        /// <summary>
        /// Порог «сказать вслух, что игрок чёрный» с поправкой на состояние: обида и азарт его снижают
        /// (к концу и после обмана говорят смелее), тревога — повышает (когда подозревают тебя, нападать рискованно).
        /// </summary>
        private double AffectThreshold(Guid id) => !BotTuning.Current.Affect || Classic ? 0
            : -.15 * Grudge(id) - .1 * Affect().Excitement + .1 * Affect().Anxiety;

        /// <summary>Температура мягкого выбора: неуверенный мечется сильнее, уверенный — твёрже.</summary>
        private double AffectTemperature() => !BotTuning.Current.Affect || Classic ? 1 : 1.5 - Affect().Confidence;
    }
}
