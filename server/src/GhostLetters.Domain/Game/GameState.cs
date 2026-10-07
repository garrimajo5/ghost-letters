using GhostLetters.Domain.Roles;

namespace GhostLetters.Domain.Game;

public sealed class PlayerState
{
    public Guid Id { get; init; }

    public int Seat { get; init; }

    public Role Role { get; set; }

    public List<string> Hand { get; init; } = [];

    public bool Acknowledged { get; set; }
}

public sealed class BoardRow
{
    public Category Category { get; init; }

    public List<string> Cards { get; init; } = [];
}

/// <summary>Подсказки одного раунда (0 — первая зацепка). Пустой список — «в этом раунде ничего».</summary>
public sealed class HintGroup
{
    public int Round { get; init; }

    public List<string> Cards { get; init; } = [];
}

public sealed class Letter
{
    public string CardId { get; init; } = string.Empty;

    public Guid From { get; init; }
}

/// <summary>История писем — для итогов партии и повтора раундов.</summary>
public sealed class LetterRecord
{
    public int Round { get; init; }

    public Guid From { get; init; }

    public string CardId { get; init; } = string.Empty;

    public bool Revealed { get; set; }
}

/// <summary>Полное состояние партии. Хранится на сервере целиком, клиентам уходят только проекции.</summary>
public sealed class GameState
{
    public Guid Id { get; init; }

    public int Seed { get; init; }

    public int Version { get; set; }

    public GameSettings Settings { get; init; } = new();

    public int TotalRounds { get; init; }

    public List<PlayerState> Players { get; init; } = [];

    public List<BoardRow> Board { get; init; } = [];

    /// <summary>Номер столбца истинной улики для каждого ряда; null — ещё не выбраны.</summary>
    public List<int>? Truth { get; set; }

    public List<string> Deck { get; init; } = [];

    public List<string> DiscardPile { get; init; } = [];

    public List<string> Vanished { get; init; } = [];

    public List<HintGroup> Hints { get; init; } = [];

    public List<Letter> Mailbox { get; init; } = [];

    public List<LetterRecord> Letters { get; init; } = [];

    public Phase Phase { get; set; }

    /// <summary>0 — до первого раунда, далее 1..TotalRounds.</summary>
    public int Round { get; set; }

    /// <summary>Кто уже сделал ход в текущей фазе (письмо, сброс, готовность).</summary>
    public HashSet<Guid> Done { get; init; } = [];

    public Guid? RadioHolder { get; set; }

    public List<Guid> SpeakingOrder { get; set; } = [];

    public int SpeakerIndex { get; set; }

    public Guid? FloorGrantedTo { get; set; }

    public HashSet<Guid> RaisedHands { get; init; } = [];

    public PlayerState Ghost => Players.Single(p => p.Role == Role.Ghost);

    public bool HasKiller => Players.Any(p => p.Role == Role.Killer);

    public Guid? CurrentSpeaker =>
        Phase == Phase.Discussion && SpeakerIndex < SpeakingOrder.Count ? SpeakingOrder[SpeakerIndex] : null;

    public PlayerState Player(Guid id) =>
        Players.FirstOrDefault(p => p.Id == id)
        ?? throw new GameRuleException(GameRuleException.Codes.UnknownPlayer, "Игрок не участвует в партии.");

    /// <summary>Игроки, кроме Призрака, по местам.</summary>
    public IEnumerable<PlayerState> Investigators => Players.Where(p => p.Role != Role.Ghost).OrderBy(p => p.Seat);
}
