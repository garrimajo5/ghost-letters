using GhostLetters.Domain.Roles;

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

/// <summary>Голос на текущем этапе: столбец для ряда или подозреваемый для ареста. Оба null — воздержаться.</summary>
public sealed record CastVote(int? Column, Guid? Suspect) : GameCommand;

/// <summary>Ничья обсуждена, игрок готов переголосовать.</summary>
public sealed record ReadyRevote : GameCommand;

/// <summary>
/// Убийца указывает, кого считает Свидетелем или Экспертом.
/// Если в игре обе роли, нужно назвать и роль (Guess) — засчитывается только точное попадание.
/// </summary>
public sealed record HuntPick(Guid Target, Role? Guess = null) : GameCommand;

/// <summary>
/// Подсказка Сообщника Убийце (видна только команде Убийцы): ночью — карта в каждом ряду (Columns)
/// и/или карты, которые лучше не брать (Avoid); на охоте — игрок (Target) и, если в игре и Свидетель,
/// и Эксперт, роль (Guess). Решает всё равно Убийца.
/// </summary>
public sealed record TeamSuggest(IReadOnlyList<int>? Columns = null, Guid? Target = null, Role? Guess = null,
    IReadOnlyList<BoardCellRef>? Avoid = null) : GameCommand;

/// <summary>Клетка поля: ряд и столбец.</summary>
public sealed record BoardCellRef(int Row, int Column);

/// <summary>Убийца указывает, кого считает Шантажистом.</summary>
public sealed record BlackmailerPick(Guid Target) : GameCommand;

/// <summary>Шантажист называет истинные улики: столбец для каждого ряда.</summary>
public sealed record NameTruth(IReadOnlyList<int> Columns) : GameCommand;

/// <summary>Поставить или снять лайк игроку.</summary>
public sealed record Like(Guid To, bool On) : GameCommand;

/// <summary>Выдвинуть игрока на ачивку; Code == null — пропустить.</summary>
public sealed record Nominate(string? Code, Guid? Nominee) : GameCommand;

/// <summary>Голос за выдвижение по его номеру; null — пропустить.</summary>
public sealed record AwardVote(int? Entry) : GameCommand;

/// <summary>
/// Одна операция на доске улик. Какие поля нужны, зависит от Kind:
/// Link — SourceKind, Source, Target, Stance, Reason?; Unlink/Endorse/Dispute/Clear — Thread;
/// Pin/Unpin/Check/Uncheck — Target (карта поля); Claim — Round и Source (карта, которую игрок называет своим письмом).
/// </summary>
public sealed record TableOp(
    TableOpKind Kind,
    TableSourceKind? SourceKind = null,
    string? Source = null,
    string? Target = null,
    TableStance? Stance = null,
    string? Reason = null,
    int? Thread = null,
    int? Round = null);

/// <summary>Пачка изменений на доске улик от одного игрока (до 6 операций).</summary>
public sealed record TablePost(IReadOnlyList<TableOp> Ops) : GameCommand;
