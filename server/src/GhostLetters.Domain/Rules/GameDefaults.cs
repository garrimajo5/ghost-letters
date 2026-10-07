namespace GhostLetters.Domain.Rules;

/// <summary>Значения по умолчанию из правил игры.</summary>
public static class GameDefaults
{
    public const int HandSize = 5;
    public const int DefaultColumns = 5;
    public const int DefaultColumnsWithCharacters = 6;
    public const int MinColumns = 4;
    public const int MaxColumns = 7;
    public const int RevoteLimit = 3;

    /// <summary>Раунды по числу игроков: 2–4 → 5, 5–7 → 4, 8–10 → 3, 11–12 → 2.</summary>
    public static int Rounds(int players) => players switch
    {
        < 2 or > 12 => throw new ArgumentOutOfRangeException(nameof(players), players, "Игроков должно быть от 2 до 12."),
        <= 4 => 5,
        <= 7 => 4,
        <= 10 => 3,
        _ => 2,
    };

    /// <summary>Сколько писем кладёт в ящик каждый игрок: вдвоём — по 2.</summary>
    public static int LettersPerPlayer(int players) => players == 2 ? 2 : 1;
}
