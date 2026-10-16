namespace GhostLetters.Infrastructure.Speech;

/// <summary>Настроение, с которым бот говорит: из того, что с ним происходит в партии.</summary>
public enum SpeechMood
{
    /// <summary>Ровно, без особых эмоций.</summary>
    Calm,

    /// <summary>Улики сходятся — говорит твёрдо.</summary>
    Confident,

    /// <summary>Письмо открыли, версия подтверждается — радуется.</summary>
    Excited,

    /// <summary>Улик мало, ряды неясны — сомневается.</summary>
    Doubtful,

    /// <summary>Письма исчезают одно за другим — досадует.</summary>
    Frustrated,

    /// <summary>Кого-то всерьёз подозревает — говорит жёстче.</summary>
    Suspicious,

    /// <summary>Его самого обвиняют — защищается.</summary>
    Defensive,
}

/// <summary>Что бот хочет сказать — одна мысль реплики.</summary>
public enum ThoughtKind
{
    Defense,
    ClaimLost,
    ClaimOpen,
    ClaimWait,
    Check,
    CheckNothing,
    LostConclusion,
    LostIgnored,
    OpenConclusion,
    Agree,
    Disagree,
    Version,
    VersionSure,
    VersionDoubt,
    Tentative,
    Exclude,
    Weak,
    Link,
    Whole,
    Accuse,
    NoClues,

    /// <summary>Доверяю игроку: «пока считаю тебя мирным».</summary>
    Trust,

    /// <summary>Зову поддержать карту: «давайте вместе за мотив 2».</summary>
    Support,

    /// <summary>Зову голосовать против подозреваемого.</summary>
    VoteAgainst,
}

/// <summary>
/// Одна мысль. Карты — ссылками, как их видит стол («место 4» в именительном, проверенные — списком),
/// подсказка — «Подсказка р.1 №2» или «Зацепка», игрок — имя без «Бот ».
/// </summary>
/// <param name="Round">Для согласия и спора: раунд, в котором игрок это сказал (раньше текущего — так и говорим).</param>
/// <param name="Strength">Для обвинения: 1 — Убийца, .6 — из чёрных, .4 — присмотрелся бы.</param>
public sealed record Thought(
    ThoughtKind Kind,
    string? Card = null,
    string? Who = null,
    string? Why = null,
    string? Hint = null,
    IReadOnlyList<string>? Cards = null,
    int? Round = null,
    double Strength = 0);

/// <summary>Положение за столом, как его видит бот.</summary>
/// <param name="Agreeing">Сколько игроков вслух за ту же карту, что и бот, в этом ряду.</param>
/// <param name="Disagreeing">Сколько вслух за другую карту в этом ряду.</param>
public sealed record TableSituation(int Round, int TotalRounds, int Hints, int Agreeing, int Disagreeing)
{
    public bool LastRound => TotalRounds > 0 && Round >= TotalRounds;

    public bool Consensus => Agreeing >= 2 && Disagreeing == 0;

    public bool Contested => Disagreeing >= 1 && Disagreeing >= Agreeing;
}

/// <summary>
/// Что бот решил сказать, до слов: настроение, мысли по порядку и положение за столом. По этому
/// <see cref="SpeechComposer"/> подбирает фразы — своего персонажа, выученные у людей или общие.
/// </summary>
public sealed record SpeechIntent(SpeechMood Mood, IReadOnlyList<Thought> Thoughts, TableSituation Situation)
{
    public SpeechIntent With(Thought first) => this with { Thoughts = [first, .. Thoughts] };
}
