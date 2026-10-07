using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Persistence.Entities;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Перенос состояния домена в строку games и обратно.</summary>
public static class GameStore
{
    public static GameState Read(Game game) => GameJson.Deserialize<GameState>(game.State);

    /// <summary>Записать снимок. Дедлайн пересчитывается, когда началась новая фаза или ход.</summary>
    public static void Write(Game game, GameState state, LobbySettings settings, DateTimeOffset now, bool phaseChanged)
    {
        game.State = GameJson.Serialize(state);
        game.Version = state.Version;
        game.Phase = state.Phase.ToString();
        if (phaseChanged)
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

    /// <summary>Отпечаток «шага» партии: если он изменился — нужен новый таймер.</summary>
    public static string StepKey(GameState state) =>
        $"{state.Phase}|{state.Round}|{state.SpeakerIndex}|{state.VoteStageIndex}|{state.CurrentVoteStage?.Attempt}";
}
