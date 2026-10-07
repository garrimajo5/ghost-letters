using GhostLetters.Infrastructure.Persistence.Entities;

namespace GhostLetters.Infrastructure.Auth;

public sealed record GuestLoginRequest(string DeviceId, string Nickname, string? AvatarColor);

public sealed record RefreshRequest(string RefreshToken);

public sealed record UpdateProfileRequest(string? Nickname, string? AvatarColor);

public sealed record UserDto(Guid Id, string Nickname, string AvatarColor, DateTimeOffset CreatedAt)
{
    public static UserDto From(User user) => new(user.Id, user.Nickname, user.AvatarColor, user.CreatedAt);
}

public sealed record AuthResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, UserDto User);

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
