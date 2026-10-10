using GhostLetters.Infrastructure.Persistence.Entities;

namespace GhostLetters.Infrastructure.Auth;

public sealed record GuestLoginRequest(string DeviceId, string Nickname, string? AvatarColor, string? RefreshToken = null, RecoveryKeyRequest? Recovery = null);

public sealed record RecoverySecret(string Kind, string? Word = null, string[]? Cards = null);
public sealed record RecoveryKeyRequest(string Login, RecoverySecret Key, RecoverySecret? CurrentKey = null);
public sealed record KeyLoginRequest(string Login, RecoverySecret Key);
public sealed record RecoveryInfo(string? Login, string? Kind);

public sealed record RefreshRequest(string RefreshToken);

/// <summary>Вход на новом устройстве по коду со старого: устройство привязывается к тому же игроку.</summary>
public sealed record LinkLoginRequest(string DeviceId, string Code);

public sealed record LinkCodeResponse(string Code, DateTimeOffset ExpiresAt);

public sealed record UpdateProfileRequest(string? Nickname, string? AvatarColor);

public sealed record UserDto(Guid Id, string Nickname, string AvatarColor, DateTimeOffset CreatedAt, Guid? AvatarId = null)
{
    public static UserDto From(User user) => new(user.Id, user.Nickname, user.AvatarColor, user.CreatedAt, user.AvatarMediaId);
}

public sealed record AuthResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, UserDto User);

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
