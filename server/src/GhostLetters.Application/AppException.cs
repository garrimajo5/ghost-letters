namespace GhostLetters.Application;

/// <summary>Ошибка для клиента: код из контракта API и HTTP-статус. Отдаётся как problem+json.</summary>
public sealed class AppException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;

    public int Status { get; } = status;

    public static class Codes
    {
        public const string Validation = "VALIDATION";
        public const string Unauthorized = "UNAUTHORIZED";
        public const string NotFound = "NOT_FOUND";
        public const string Forbidden = "FORBIDDEN";
        public const string Conflict = "CONFLICT";
        public const string LobbyFull = "LOBBY_FULL";
        public const string GameInProgress = "GAME_IN_PROGRESS";
        public const string VersionConflict = "VERSION_CONFLICT";
    }

    public static AppException Validation(string message) => new(Codes.Validation, message, 400);

    public static AppException Unauthorized(string message) => new(Codes.Unauthorized, message, 401);

    public static AppException NotFound(string message) => new(Codes.NotFound, message, 404);

    public static AppException Forbidden(string message) => new(Codes.Forbidden, message, 403);

    public static AppException Conflict(string code, string message) => new(code, message, 409);
}
