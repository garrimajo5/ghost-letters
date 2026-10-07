using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GhostLetters.Application;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GhostLetters.Infrastructure.Auth;

/// <summary>Вход гостем, выдача и ротация токенов, выход.</summary>
public sealed class AuthService(GhostLettersDbContext db, IOptions<JwtOptions> options, TimeProvider time)
{
    private readonly JwtOptions _jwt = options.Value;

    /// <summary>Гость входит по deviceId; с того же устройства — тот же игрок.</summary>
    public async Task<AuthResponse> GuestAsync(GuestLoginRequest request, CancellationToken ct)
    {
        var deviceId = ProfileRules.DeviceId(request.DeviceId);
        var nickname = ProfileRules.Nickname(request.Nickname);
        var color = request.AvatarColor is null ? null : ProfileRules.AvatarColor(request.AvatarColor);
        var now = time.GetUtcNow();

        var identity = await db.AuthIdentities
            .FirstOrDefaultAsync(i => i.Provider == AuthProviders.Guest && i.Subject == deviceId, ct);

        User user;
        if (identity is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Nickname = nickname,
                AvatarColor = color ?? ProfileRules.Palette[Random.Shared.Next(ProfileRules.Palette.Length)],
                CreatedAt = now,
                LastSeenAt = now,
            };
            db.Users.Add(user);
            db.AuthIdentities.Add(new AuthIdentity
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Provider = AuthProviders.Guest,
                Subject = deviceId,
                CreatedAt = now,
            });
            db.Stats.Add(new UserStats { UserId = user.Id });
        }
        else
        {
            user = await db.Users.SingleAsync(u => u.Id == identity.UserId, ct);
            user.LastSeenAt = now;
        }

        var tokens = Issue(user, now);
        await db.SaveChangesAsync(ct);
        return new AuthResponse(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken, UserDto.From(user));
    }

    /// <summary>
    /// Новая пара токенов, старый refresh отзывается. Повторное использование отозванного токена
    /// означает утечку — отзываются все сессии игрока.
    /// </summary>
    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var stored = await FindAsync(refreshToken, ct) ?? throw AppException.Unauthorized("Сессия не найдена.");

        if (stored.RevokedAt is not null)
        {
            await RevokeAllAsync(stored.UserId, now, ct);
            throw AppException.Unauthorized("Сессия отозвана. Войдите заново.");
        }

        if (stored.ExpiresAt <= now)
        {
            throw AppException.Unauthorized("Сессия истекла. Войдите заново.");
        }

        var user = await db.Users.SingleAsync(u => u.Id == stored.UserId, ct);
        user.LastSeenAt = now;
        var tokens = Issue(user, now, out var replacement);
        stored.RevokedAt = now;
        stored.ReplacedById = replacement.Id;
        await db.SaveChangesAsync(ct);
        return new AuthResponse(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken, UserDto.From(user));
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var stored = await FindAsync(refreshToken, ct);
        if (stored is { RevokedAt: null })
        {
            stored.RevokedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private TokenPair Issue(User user, DateTimeOffset now) => Issue(user, now, out _);

    private TokenPair Issue(User user, DateTimeOffset now, out RefreshToken stored)
    {
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);
        var access = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", user.Id.ToString()),
                new Claim("name", user.Nickname),
            }),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(JwtSetup.SigningKey(_jwt), SecurityAlgorithms.HmacSha256),
        });

        var refresh = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        stored = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = Hash(refresh),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_jwt.RefreshTokenDays),
        };
        db.RefreshTokens.Add(stored);
        return new TokenPair(access, expires, refresh);
    }

    private Task<RefreshToken?> FindAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw AppException.Validation("Нужен refreshToken.");
        }

        var hash = Hash(refreshToken);
        return db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
    }

    private async Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var token in active)
        {
            token.RevokedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
