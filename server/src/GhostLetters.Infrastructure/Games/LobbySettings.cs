using System.Text.Json.Serialization;
using GhostLetters.Application;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Domain.Rules;

namespace GhostLetters.Infrastructure.Games;

public static class Tempos
{
    /// <summary>Живая партия: таймеры в секундах.</summary>
    public const string Live = "live";

    /// <summary>Походовая: на каждую фазу — часы.</summary>
    public const string TurnBased = "turn";
}

/// <summary>Время на фазы живой партии, в секундах.</summary>
public sealed class PhaseTimers
{
    public const int Min = 5;
    public const int Max = 3600;

    public int RoleReveal { get; init; } = 30;

    public int Night { get; init; } = 60;

    public int FirstClue { get; init; } = 60;

    public int Mailbox { get; init; } = 60;

    public int GhostPick { get; init; } = 90;

    public int Refill { get; init; } = 30;

    /// <summary>Ход говорящего в режиме рации.</summary>
    public int SpeakerTurn { get; init; } = 60;

    /// <summary>Свободное обсуждение — на весь раунд.</summary>
    public int FreeDiscussion { get; init; } = 180;

    public int Voting { get; init; } = 60;

    public int VoteTie { get; init; } = 60;

    /// <summary>Охота и решения Шантажиста.</summary>
    public int Finale { get; init; } = 90;

    public int Awards { get; init; } = 90;

    [JsonIgnore]
    public IEnumerable<int> All =>
    [
        RoleReveal, Night, FirstClue, Mailbox, GhostPick, Refill, SpeakerTurn, FreeDiscussion, Voting, VoteTie, Finale, Awards,
    ];
}

/// <summary>Настройки лобби: правила партии, темп, таймеры и наборы карт.</summary>
public sealed record LobbySettings
{
    public const int MaxTurnHours = 72;

    public bool UseSecretRow { get; init; } = true;

    public int Columns { get; init; } = GameDefaults.DefaultColumns;

    public int? Rounds { get; init; }

    public int HandSize { get; init; } = GameDefaults.HandSize;

    public RoleOptions Roles { get; init; } = RoleOptions.Default;

    public DiscussionMode Discussion { get; init; } = DiscussionMode.Radio;

    public string Tempo { get; init; } = Tempos.Live;

    /// <summary>Часы на фазу в походовой партии.</summary>
    public int TurnHours { get; init; } = 24;

    public PhaseTimers Timers { get; init; } = new();

    /// <summary>Наборы карт; по умолчанию — все четыре (761 карта).</summary>
    public IReadOnlyList<string> CardSets { get; init; } = AllCardSets;

    public static readonly IReadOnlyList<string> AllCardSets = ["original", "mailbox", "ritual", "mirror"];

    /// <summary>Рейтинговая или обычная партия.</summary>
    public bool Ranked { get; init; } = true;

    /// <summary>Назначенный хостом Призрак (id игрока лобби); null — по жребию. Если игрок ушёл — тоже по жребию.</summary>
    public Guid? GhostUserId { get; init; }

    public GameSettings ToGameSettings() => new()
    {
        UseSecretRow = UseSecretRow,
        Columns = Columns,
        Rounds = Rounds,
        HandSize = HandSize,
        Roles = Roles ?? RoleOptions.Default,
        Discussion = Discussion,
        GhostPlayerId = GhostUserId,
        Ranked = Ranked,
    };

    /// <summary>Проверка без числа игроков: оно известно только при старте.</summary>
    public void Validate()
    {
        if (Tempo is not (Tempos.Live or Tempos.TurnBased))
        {
            throw AppException.Validation("Темп — live или turn.");
        }

        if (TurnHours is < 1 or > MaxTurnHours)
        {
            throw AppException.Validation($"Часов на ход — от 1 до {MaxTurnHours}.");
        }

        if (Timers is null || Timers.All.Any(t => t is < PhaseTimers.Min or > PhaseTimers.Max))
        {
            throw AppException.Validation($"Таймеры фаз — от {PhaseTimers.Min} до {PhaseTimers.Max} секунд.");
        }

        if (Roles is { ExtraAccomplices: < 0 or > RoleOptions.MaxExtraAccomplices })
        {
            throw AppException.Validation($"Сообщников вместо Детективов — от 0 до {RoleOptions.MaxExtraAccomplices}.");
        }

        if (CardSets is null || CardSets.Count == 0)
        {
            throw AppException.Validation("Выберите хотя бы один набор карт.");
        }

        try
        {
            // Размер колоды проверяется при старте; здесь — только поле, руки и раунды.
            ToGameSettings().Validate(players: 4, deckSize: int.MaxValue);
        }
        catch (GameRuleException e)
        {
            throw AppException.Validation(e.Message);
        }
    }

    /// <summary>Сколько даётся на текущую фазу; null — без таймера.</summary>
    public TimeSpan? TimerFor(GameState state)
    {
        if (state.Phase == Phase.Finished)
        {
            return null;
        }

        if (Tempo == Tempos.TurnBased)
        {
            return TimeSpan.FromHours(TurnHours);
        }

        var t = Timers ?? new PhaseTimers();
        var seconds = state.Phase switch
        {
            Phase.RoleReveal => t.RoleReveal,
            Phase.Night => t.Night,
            Phase.FirstClue => t.FirstClue,
            Phase.Mailbox => t.Mailbox,
            Phase.GhostPick => t.GhostPick,
            Phase.Refill => t.Refill,
            Phase.Discussion => state.Settings.Discussion == DiscussionMode.Radio ? t.SpeakerTurn : t.FreeDiscussion,
            Phase.Voting => t.Voting,
            Phase.VoteTie => t.VoteTie,
            Phase.AwardNomination or Phase.AwardVoting => t.Awards,
            _ => t.Finale,
        };
        return TimeSpan.FromSeconds(seconds);
    }
}
