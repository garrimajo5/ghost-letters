namespace GhostLetters.Infrastructure.Persistence.Entities;

/// <summary>Характер бота из кабинета админа (сам бот — обычный игрок с IsBot).</summary>
public sealed class BotProfile
{
    public Guid UserId { get; set; }

    /// <summary>Пара слов о характере — видна хосту при выборе бота в лобби.</summary>
    public string Social { get; set; } = "{}";

    public string About { get; set; } = string.Empty;

    public double Meaning { get; set; } = 0.5;

    public double Shape { get; set; } = 0.25;

    public double Color { get; set; } = 0.25;

    public double Negative { get; set; } = 0.5;

    public double Memory { get; set; } = 0.3;

    public double Risk { get; set; } = 0.5;

    public double Compromise { get; set; } = 0.5;

    /// <summary>Строгость ассоциаций: сколько карт поля бот «проверяет» одним письмом.</summary>
    public double Strictness { get; set; } = 0.5;

    public double Details { get; set; } = 0.25;
    public double SecondaryMeanings { get; set; } = 0.35;

    public double Variability { get; set; } = 0.2;

    /// <summary>Выключенного бота не предлагают в лобби, но его история и рейтинг сохраняются.</summary>
    public bool Enabled { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; }
}
