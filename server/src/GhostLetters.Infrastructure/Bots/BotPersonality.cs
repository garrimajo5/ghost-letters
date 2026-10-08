namespace GhostLetters.Infrastructure.Bots;

/// <summary>
/// Характер бота — шесть спектров (все 0…1). Задаётся админом в кабинете ботов, на каждую партию
/// чуть «плывёт» в пределах изменчивости.
/// </summary>
public sealed record BotPersonality
{
    /// <summary>Внимание к смыслу предмета (нож → оружие).</summary>
    public double Meaning { get; init; } = 0.5;

    /// <summary>Внимание к форме (длинное, круглое…).</summary>
    public double Shape { get; init; } = 0.25;

    /// <summary>Внимание к цвету.</summary>
    public double Color { get; init; } = 0.25;

    /// <summary>Выводы из того, что НЕ открыли: 0 — «не достали и ладно», 1 — «не достали — точно не оно».</summary>
    public double Negative { get; init; } = 0.5;

    /// <summary>Память о прошлых партиях с этими людьми: 0 — каждая игра с чистого листа, 1 — «был Убийцей — значит и сейчас».</summary>
    public double Memory { get; init; } = 0.3;

    /// <summary>Риск: 0 — никогда не врёт и не выдаёт себя, 1 — блефует и обвиняет в лоб.</summary>
    public double Risk { get; init; } = 0.5;

    /// <summary>Компромисс: 0 — не слушает даже Эксперта, 1 — договаривается даже с Убийцей.</summary>
    public double Compromise { get; init; } = 0.5;

    /// <summary>Изменчивость: 0 — всегда одинаковый, 1 — от партии к партии другой.</summary>
    public double Variability { get; init; } = 0.2;

    public static readonly BotPersonality Default = new();

    /// <summary>Доли внимания (смысл, форма, цвет) с суммой 1.</summary>
    public (double Meaning, double Shape, double Color) Attention
    {
        get
        {
            var m = Math.Max(0, Meaning);
            var s = Math.Max(0, Shape);
            var c = Math.Max(0, Color);
            var sum = m + s + c;
            return sum <= 0 ? (1 / 3.0, 1 / 3.0, 1 / 3.0) : (m / sum, s / sum, c / sum);
        }
    }

    /// <summary>Все значения в пределах 0…1.</summary>
    public BotPersonality Clamped() => new()
    {
        Meaning = Clamp(Meaning),
        Shape = Clamp(Shape),
        Color = Clamp(Color),
        Negative = Clamp(Negative),
        Memory = Clamp(Memory),
        Risk = Clamp(Risk),
        Compromise = Clamp(Compromise),
        Variability = Clamp(Variability),
    };

    /// <summary>
    /// Характер на конкретную партию: каждый спектр сдвигается на случайную величину до ±0.35 × изменчивость
    /// (одинаково для одной и той же партии — бот не «скачет» посреди игры).
    /// </summary>
    public BotPersonality ForGame(Guid gameId, Guid botId)
    {
        var p = Clamped();
        if (p.Variability <= 0)
        {
            return p;
        }

        var rng = new Random(HashCode.Combine(gameId, botId));
        double Drift(double v) => Clamp(v + (rng.NextDouble() * 2 - 1) * 0.35 * p.Variability);
        return p with
        {
            Meaning = Drift(p.Meaning),
            Shape = Drift(p.Shape),
            Color = Drift(p.Color),
            Negative = Drift(p.Negative),
            Memory = Drift(p.Memory),
            Risk = Drift(p.Risk),
            Compromise = Drift(p.Compromise),
        };
    }

    private static double Clamp(double v) => double.IsNaN(v) ? 0.5 : Math.Clamp(v, 0, 1);
}

/// <summary>Что бот помнит о соигроке по прошлым партиям с ним: сколько игр и кем тот был.</summary>
public sealed record PlayerHistory(int Games, int KillerTeam, int Informed)
{
    /// <summary>Как часто был в команде Убийцы, со сглаживанием (без игр — средняя доля ~0.25).</summary>
    public double KillerRate => (KillerTeam + 0.5) / (Games + 2.0);

    /// <summary>Как часто был Свидетелем или Экспертом.</summary>
    public double InformedRate => (Informed + 0.3) / (Games + 2.0);
}

/// <summary>
/// Всё, что бот знает помимо своей проекции партии: характер, память о людях, мнения из чата, имена.
/// </summary>
public sealed record BotMind(
    BotPersonality Personality,
    IReadOnlyDictionary<Guid, PlayerHistory> History,
    IReadOnlyList<ChatOpinion> Opinions,
    IReadOnlyDictionary<Guid, string> Names)
{
    public static readonly BotMind Neutral = new(BotPersonality.Default, new Dictionary<Guid, PlayerHistory>(), [], new Dictionary<Guid, string>());
}

/// <summary>Мнение из чата: автор показал карту поля с подписью («думаю, эта», «проверял эту»).</summary>
public sealed record ChatOpinion(Guid Author, string CardId, double Strength);

/// <summary>Готовые характеры — кабинет предлагает создать их одной кнопкой.</summary>
public static class BotPresets
{
    public static readonly IReadOnlyList<(string Name, string Color, string About, BotPersonality P)> All =
    [
        ("Пуаро", "#5C7C99", "Смысл прежде всего, упрям, мало врёт.",
            new BotPersonality { Meaning = 0.8, Shape = 0.1, Color = 0.1, Negative = 0.7, Memory = 0.4, Risk = 0.25, Compromise = 0.2, Variability = 0.1 }),
        ("Марпл", "#B370D9", "Помнит всех и всё, верит людям.",
            new BotPersonality { Meaning = 0.5, Shape = 0.2, Color = 0.3, Negative = 0.5, Memory = 0.9, Risk = 0.3, Compromise = 0.7, Variability = 0.15 }),
        ("Коломбо", "#E57F4F", "Смотрит на форму, обвиняет в лоб, рискует.",
            new BotPersonality { Meaning = 0.3, Shape = 0.5, Color = 0.2, Negative = 0.4, Memory = 0.5, Risk = 0.85, Compromise = 0.4, Variability = 0.3 }),
        ("Ватсон", "#4AA3DF", "Слушает большинство, не делает выводов из исчезнувшего.",
            new BotPersonality { Meaning = 0.45, Shape = 0.25, Color = 0.3, Negative = 0.15, Memory = 0.2, Risk = 0.35, Compromise = 0.9, Variability = 0.2 }),
        ("Фандорин", "#F2A541", "Видит цвета, холодный расчёт, каждый раз немного другой.",
            new BotPersonality { Meaning = 0.3, Shape = 0.2, Color = 0.5, Negative = 0.85, Memory = 0.3, Risk = 0.5, Compromise = 0.3, Variability = 0.6 }),
        ("Жеглов", "#3D6A99", "Блефует на любой роли, никому не верит.",
            new BotPersonality { Meaning = 0.6, Shape = 0.2, Color = 0.2, Negative = 0.5, Memory = 0.7, Risk = 0.95, Compromise = 0.05, Variability = 0.4 }),
    ];
}
