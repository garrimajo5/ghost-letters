namespace GhostLetters.Infrastructure.Persistence.Entities;

/// <summary>Игрок.</summary>
public sealed class User
{
    public const int MinNicknameLength = 2;
    public const int MaxNicknameLength = 20;

    public Guid Id { get; set; }

    public string Nickname { get; set; } = string.Empty;

    /// <summary>Цвет заглушки-аватара, #RRGGBB.</summary>
    public string AvatarColor { get; set; } = "#7C6CF2";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>Бот для отладки: ходит сам, в рейтинг не попадает.</summary>
    public bool IsBot { get; set; }
}

public static class AuthProviders
{
    public const string Guest = "guest";
    public const string Google = "google";
    public const string Apple = "apple";

    /// <summary>Одноразовый код входа на другом устройстве (Subject — хэш кода, живёт 10 минут).</summary>
    public const string LinkCode = "link";
}

/// <summary>Способ входа: гость по deviceId, позже Google и Apple.</summary>
public sealed class AuthIdentity
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Provider { get; set; } = AuthProviders.Guest;

    /// <summary>deviceId для гостя или sub провайдера.</summary>
    public string Subject { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Refresh-токен. В базе только SHA-256 хеш; при обновлении токен меняется на новый.</summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Каким токеном заменён при ротации.</summary>
    public Guid? ReplacedById { get; set; }
}

public sealed class PushToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>android / ios.</summary>
    public string Platform { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}
