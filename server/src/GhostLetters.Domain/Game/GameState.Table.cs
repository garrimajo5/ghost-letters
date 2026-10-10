namespace GhostLetters.Domain.Game;

/// <summary>Откуда тянется нить: подсказка Призрака или заявленное игроком письмо.</summary>
public enum TableSourceKind
{
    Hint,
    Letter,
}

/// <summary>Нить говорит «эта карта поля подходит» или «не подходит».</summary>
public enum TableStance
{
    For,
    Against,
}

/// <summary>Что делает одна операция поста на доске.</summary>
public enum TableOpKind
{
    /// <summary>Протянуть нить (или поменять свою нить между теми же картами).</summary>
    Link,
    /// <summary>Убрать свою нить.</summary>
    Unlink,
    /// <summary>Булавка «моя версия по ряду» на карте поля; в ряду одна, новая заменяет прежнюю.</summary>
    Pin,
    /// <summary>Снять булавку с ряда карты.</summary>
    Unpin,
    /// <summary>«Проверял эту карту своим письмом, оно исчезло».</summary>
    Check,
    Uncheck,
    /// <summary>«В раунде N я отправлял эту карту» — слова игрока, блеф возможен.</summary>
    Claim,
    /// <summary>Согласиться с чужой нитью.</summary>
    Endorse,
    /// <summary>Не согласиться с чужой нитью.</summary>
    Dispute,
    /// <summary>Снять своё согласие или несогласие.</summary>
    Clear,
}

/// <summary>Доска улик: общие для всех нити, булавки, проверки и заявления о письмах.</summary>
public sealed class TableState
{
    public int NextThreadId { get; set; } = 1;

    public List<TableThread> Threads { get; init; } = [];

    public List<TablePin> Pins { get; init; } = [];

    public List<TableCheck> Checks { get; init; } = [];

    public List<LetterClaim> Claims { get; init; } = [];
}

public sealed class TableThread
{
    public int Id { get; init; }

    public Guid Author { get; init; }

    public int Round { get; set; }

    public TableSourceKind SourceKind { get; init; }

    /// <summary>Карта подсказки или заявленного письма.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Карта поля.</summary>
    public string Target { get; init; } = string.Empty;

    public TableStance Stance { get; set; }

    public string? Reason { get; set; }

    public int Version { get; set; }

    public List<Guid> EndorsedBy { get; init; } = [];

    public List<Guid> DisputedBy { get; init; } = [];
}

public sealed class TablePin
{
    public Guid Author { get; init; }

    public int Row { get; init; }

    public int Column { get; set; }
}

public sealed class TableCheck
{
    public Guid Author { get; init; }

    public string Card { get; init; } = string.Empty;
}

public sealed class LetterClaim
{
    public Guid Author { get; init; }

    public int Round { get; init; }

    public string Card { get; set; } = string.Empty;
}

public sealed partial class GameState
{
    /// <summary>Доска улик — открыта всем, включая Призрака, зрителей и экран стола.</summary>
    public TableState Table { get; init; } = new();
}
