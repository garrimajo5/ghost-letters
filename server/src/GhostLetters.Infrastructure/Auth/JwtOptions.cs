namespace GhostLetters.Infrastructure.Auth;

/// <summary>Настройки токенов, секция Jwt.</summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "ghost-letters";

    public string Audience { get; set; } = "ghost-letters-app";

    /// <summary>Ключ подписи HS256, не короче 32 символов. В продакшене — только из секретов.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 60;
}
