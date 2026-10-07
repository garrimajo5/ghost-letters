using GhostLetters.Domain.Roles;
using GhostLetters.Domain.Rules;

namespace GhostLetters.Domain.Game;

/// <summary>Настройки партии, снимок из лобби на момент старта.</summary>
public sealed record GameSettings
{
    /// <summary>Четвёртый ряд «Тайна».</summary>
    public bool UseSecretRow { get; init; } = true;

    /// <summary>Сколько карт в каждом ряду.</summary>
    public int Columns { get; init; } = GameDefaults.DefaultColumns;

    /// <summary>Число раундов; null — по таблице из правил.</summary>
    public int? Rounds { get; init; }

    public int HandSize { get; init; } = GameDefaults.HandSize;

    public RoleOptions Roles { get; init; } = RoleOptions.Default;

    public DiscussionMode Discussion { get; init; } = DiscussionMode.Radio;

    public IReadOnlyList<Category> Categories => UseSecretRow
        ? [Category.Motive, Category.Place, Category.Method, Category.Secret]
        : [Category.Motive, Category.Place, Category.Method];

    public int RoundsFor(int players) => Rounds ?? GameDefaults.Rounds(players);

    /// <summary>Минимум карт в колоде: поле + руки + по письму от каждого на раунд.</summary>
    public int MinDeckSize(int players) => Categories.Count * Columns + players * HandSize + players * GameDefaults.LettersPerPlayer(players);

    public void Validate(int players, int deckSize)
    {
        if (Columns is < GameDefaults.MinColumns or > GameDefaults.MaxColumns)
        {
            throw GameRuleException.Validation($"Карт в ряду должно быть от {GameDefaults.MinColumns} до {GameDefaults.MaxColumns}.");
        }

        if (HandSize is < 4 or > 7)
        {
            throw GameRuleException.Validation("Карт на руке должно быть от 4 до 7.");
        }

        if (Rounds is < 1 or > 5)
        {
            throw GameRuleException.Validation("Раундов должно быть от 1 до 5.");
        }

        if (deckSize < MinDeckSize(players))
        {
            throw GameRuleException.Validation($"В колоде {deckSize} карт, нужно хотя бы {MinDeckSize(players)}.");
        }
    }
}
