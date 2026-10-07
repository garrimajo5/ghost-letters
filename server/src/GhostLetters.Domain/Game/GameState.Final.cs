using GhostLetters.Domain.Roles;

namespace GhostLetters.Domain.Game;

/// <summary>Этап финального голосования: один ряд улик или арест Убийцы.</summary>
public sealed class VoteStage
{
    public VoteStageKind Kind { get; init; }

    /// <summary>Номер ряда для голосования по уликам; -1 для ареста.</summary>
    public int Row { get; init; } = -1;

    /// <summary>1 — первое голосование, дальше переголосования.</summary>
    public int Attempt { get; set; } = 1;

    public List<int> CandidateColumns { get; set; } = [];

    public List<Guid> CandidateSuspects { get; set; } = [];
}

/// <summary>Голос игрока. Оба поля null — воздержался.</summary>
public sealed class VoteRecord
{
    public int Stage { get; init; }

    public int Attempt { get; init; }

    public Guid Voter { get; init; }

    public int? Column { get; init; }

    public Guid? Suspect { get; init; }
}

/// <summary>Чем закончился этап голосования.</summary>
public sealed class VoteOutcome
{
    public int Stage { get; init; }

    public VoteStageKind Kind { get; init; }

    public int Row { get; init; }

    public int? Column { get; init; }

    public Guid? Suspect { get; init; }

    /// <summary>Улика истинная / арестован настоящий Убийца.</summary>
    public bool Correct { get; init; }

    /// <summary>Решено жребием после лимита переголосований.</summary>
    public bool ByLot { get; init; }

    /// <summary>Роль арестованного, которую раскрывают сразу: Убийца, Сообщник или Подражатель.</summary>
    public Role? RevealedRole { get; init; }
}

public sealed class HuntResult
{
    public Guid? Target { get; init; }

    public Role? Guess { get; init; }

    public bool Success { get; init; }
}

public enum WinningSide
{
    Nobody,
    Detectives,
    Killer,

    /// <summary>Шантажист назвал истину и не был найден — Убийца и Сообщники проигрывают.</summary>
    Blackmailer,
}

public sealed class GameResult
{
    public bool Solved { get; init; }

    public int CorrectRows { get; init; }

    public bool KillerCaught { get; init; }

    public WinningSide Side { get; init; }

    public bool ImitatorWon { get; init; }

    public bool BlackmailerWon { get; init; }

    public List<Guid> Winners { get; init; } = [];
}

public sealed class LikeRecord
{
    public Guid From { get; init; }

    public Guid To { get; init; }
}

/// <summary>Выдвижение игрока на ачивку. Одинаковые выдвижения разных игроков объединяются.</summary>
public sealed class AwardEntry
{
    public string Code { get; init; } = string.Empty;

    public Guid Nominee { get; init; }

    public List<Guid> NominatedBy { get; init; } = [];

    public List<Guid> Voters { get; init; } = [];
}

public sealed partial class GameState
{
    public List<VoteStage> VoteStages { get; init; } = [];

    public int VoteStageIndex { get; set; }

    /// <summary>Голоса текущего этапа — скрыты, пока не проголосуют все.</summary>
    public List<VoteRecord> PendingVotes { get; init; } = [];

    /// <summary>Подсчитанные голоса — открытая информация.</summary>
    public List<VoteRecord> VoteRecords { get; init; } = [];

    public List<VoteOutcome> VoteOutcomes { get; init; } = [];

    public List<Guid> Arrested { get; init; } = [];

    /// <summary>Дело раскрыто по итогам голосования (до охоты); null — голосование не закончено.</summary>
    public bool? Solved { get; set; }

    public HuntResult? Hunt { get; set; }

    public bool? BlackmailerFound { get; set; }

    /// <summary>Улики, названные Шантажистом; пустой список — не назвал.</summary>
    public List<int>? BlackmailerClaim { get; set; }

    public GameResult? Result { get; set; }

    public List<LikeRecord> Likes { get; init; } = [];

    public List<AwardEntry> Awards { get; init; } = [];

    /// <summary>Индексы выдвижений, получивших ачивки (не больше двух).</summary>
    public List<int> AwardWinners { get; init; } = [];

    public VoteStage? CurrentVoteStage =>
        Phase is Phase.Voting or Phase.VoteTie && VoteStageIndex < VoteStages.Count ? VoteStages[VoteStageIndex] : null;
}
