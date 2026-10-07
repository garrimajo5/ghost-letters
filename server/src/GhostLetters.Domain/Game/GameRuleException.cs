namespace GhostLetters.Domain.Game;

/// <summary>Нарушение правил: команда не в той фазе, не от того игрока и т. п.</summary>
public sealed class GameRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static class Codes
    {
        public const string Validation = "VALIDATION";
        public const string PhaseMismatch = "PHASE_MISMATCH";
        public const string NotAllowed = "NOT_ALLOWED";
        public const string NotYourTurn = "NOT_YOUR_TURN";
        public const string UnknownPlayer = "UNKNOWN_PLAYER";
    }

    internal static GameRuleException Validation(string message) => new(Codes.Validation, message);

    internal static GameRuleException NotAllowed(string message) => new(Codes.NotAllowed, message);

    internal static GameRuleException NotYourTurn(string message) => new(Codes.NotYourTurn, message);

    internal static GameRuleException WrongPhase(Phase expected, Phase actual) =>
        new(Codes.PhaseMismatch, $"Команда доступна в фазе {expected}, сейчас {actual}.");
}
