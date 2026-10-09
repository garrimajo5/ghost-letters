using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Перенос состояния домена в строку games и обратно.</summary>
public static class GameStore
{
    public static GameState Read(Game game) => GameJson.Deserialize<GameState>(game.State);

    /// <summary>
    /// Записать снимок. Дедлайн пересчитывается, когда началась новая фаза или ход.
    /// Один человек с ботами (<paramref name="solo"/>) — без таймеров: боты всегда ждут человека.
    /// </summary>
    public static void Write(Game game, GameState state, LobbySettings settings, DateTimeOffset now, bool phaseChanged, bool solo = false)
    {
        game.State = GameJson.Serialize(state);
        game.Version = state.Version;
        game.Phase = state.Phase.ToString();
        if (solo)
        {
            game.PhaseDeadline = null;
        }
        else if (phaseChanged)
        {
            game.PhaseDeadline = settings.TimerFor(state) is { } timer ? now + timer : null;
        }

        if (state.Phase == Phase.Finished && game.Status == GameStatuses.Active)
        {
            game.Status = GameStatuses.Finished;
            game.FinishedAt = now;
            game.PhaseDeadline = null;
        }

        if (state.Result is not null)
        {
            game.Result = GameJson.Serialize(state.Result);
        }
    }

    /// <summary>Партия одного человека с ботами (людей среди игроков не больше одного).</summary>
    public static async Task<bool> IsSoloAsync(GhostLettersDbContext db, IReadOnlyCollection<Guid> players, CancellationToken ct) =>
        await db.Users.AsNoTracking().CountAsync(u => players.Contains(u.Id) && !u.IsBot, ct) <= 1;

    /// <summary>Отпечаток «шага» партии: если он изменился — нужен новый таймер.</summary>
    public static string StepKey(GameState state) =>
        $"{state.Phase}|{state.Round}|{state.EffectiveDiscussion}|{state.SpeakerIndex}|{state.VoteStageIndex}|{state.CurrentVoteStage?.Attempt}";
}
