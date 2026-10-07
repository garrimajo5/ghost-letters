namespace GhostLetters.Domain.Game;

/// <summary>Намерение игрока. Сервер проверяет его правилами и применяет к состоянию.</summary>
public abstract record GameCommand;

/// <summary>Игрок посмотрел свою роль.</summary>
public sealed record AckRole : GameCommand;

/// <summary>Истинные улики: номер столбца для каждого ряда по порядку.</summary>
public sealed record ChooseTruth(IReadOnlyList<int> Columns) : GameCommand;

/// <summary>Первая зацепка Призрака; null — без зацепки.</summary>
public sealed record GiveFirstClue(string? CardId) : GameCommand;

/// <summary>Отправить письмо (1 карту, вдвоём — 2) в почтовый ящик.</summary>
public sealed record SendLetter(IReadOnlyList<string> CardIds) : GameCommand;

/// <summary>Призрак открывает выбранные письма как подсказки, остальные исчезают.</summary>
public sealed record RevealHints(IReadOnlyList<string> CardIds) : GameCommand;

/// <summary>Сбросить карту (null — оставить руку) и добрать до полной руки.</summary>
public sealed record Discard(string? CardId) : GameCommand;

/// <summary>Говорящий заканчивает слово, рация уходит дальше.</summary>
public sealed record EndTurn : GameCommand;

/// <summary>Говорящий даёт слово другому игроку до конца своего хода.</summary>
public sealed record GiveFloor(Guid To) : GameCommand;

public sealed record RaiseHand(bool Raised) : GameCommand;

/// <summary>Свободный чат: игрок готов к следующему раунду.</summary>
public sealed record ReadyNextRound : GameCommand;

/// <summary>Событие партии для журнала и рассылки. OnlyFor — личное событие.</summary>
public sealed record GameEvent(string Type, Guid? Actor = null, Guid? OnlyFor = null, string? Detail = null);
