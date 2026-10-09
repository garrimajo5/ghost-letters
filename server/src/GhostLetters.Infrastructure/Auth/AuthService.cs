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

    /// <summary>Регистрация гостя. Идентификатор устройства сам по себе не доказывает владение аккаунтом.</summary>
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
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
                throw AppException.Unauthorized("Для этого аккаунта нужна действующая сессия или код с другого устройства.");
            var proof = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(
                t => t.TokenHash == Hash(request.RefreshToken), ct);
            if (proof?.UserId != identity.UserId)
                throw AppException.Unauthorized("Сессия не принадлежит этому аккаунту.");
            return await RefreshAsync(request.RefreshToken, ct);
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
        if (string.IsNullOrWhiteSpace(refreshToken)) throw AppException.Validation("Нужен refreshToken.");
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize rotation and revocation for the whole account across API processes.
        var hash = Hash(refreshToken);
        var owner = await db.RefreshTokens.AsNoTracking().Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.UserId).FirstOrDefaultAsync(ct)
            ?? throw AppException.Unauthorized("Сессия не найдена.");
        await LockAccountAsync(owner, ct);
        var stored = await FindAsync(refreshToken, ct) ?? throw AppException.Unauthorized("Сессия не найдена.");
        await db.Entry(stored).ReloadAsync(ct);

        if (stored.RevokedAt is not null)
        {
            await RevokeAllAsync(stored.UserId, now, ct);
            await transaction.CommitAsync(ct);
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
        await transaction.CommitAsync(ct);
        return new AuthResponse(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken, UserDto.From(user));
    }

    /// <summary>Сколько живёт код входа на другом устройстве.</summary>
    public static readonly TimeSpan LinkCodeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Без похожих символов (0/O, 1/I/L): код диктуют голосом и вводят руками.</summary>
    private const string LinkAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>Новый код входа для игрока; прежние неиспользованные коды отзываются.</summary>
    public async Task<LinkCodeResponse> CreateLinkCodeAsync(Guid userId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var old = await db.AuthIdentities.Where(i => i.Provider == AuthProviders.LinkCode && i.UserId == userId).ToListAsync(ct);
        db.AuthIdentities.RemoveRange(old);

        var code = new string(Enumerable.Range(0, 8).Select(_ => LinkAlphabet[RandomNumberGenerator.GetInt32(LinkAlphabet.Length)]).ToArray());
        db.AuthIdentities.Add(new AuthIdentity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = AuthProviders.LinkCode,
            Subject = Hash(code),
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        return new LinkCodeResponse(code, now + LinkCodeLifetime);
    }

    /// <summary>
    /// Вход по коду: это устройство с этого момента входит как владелец кода. Если на устройстве был
    /// другой гость, устройство переходит к владельцу кода (прежний гость остаётся в истории партий).
    /// </summary>
    public async Task<AuthResponse> LinkAsync(LinkLoginRequest request, CancellationToken ct)
    {
        var deviceId = ProfileRules.DeviceId(request.DeviceId);
        var code = NormalizeCode(request.Code);
        var now = time.GetUtcNow();
        var hash = Hash(code);
        var link = await db.AuthIdentities.FirstOrDefaultAsync(i => i.Provider == AuthProviders.LinkCode && i.Subject == hash, ct);
        if (link is null || link.CreatedAt + LinkCodeLifetime < now)
        {
            throw AppException.Validation("Код не подошёл или устарел. Получите новый на другом устройстве.");
        }

        db.AuthIdentities.Remove(link);
        var device = await db.AuthIdentities.FirstOrDefaultAsync(i => i.Provider == AuthProviders.Guest && i.Subject == deviceId, ct);
        if (device is null)
        {
            db.AuthIdentities.Add(new AuthIdentity
            {
                Id = Guid.NewGuid(),
                UserId = link.UserId,
                Provider = AuthProviders.Guest,
                Subject = deviceId,
                CreatedAt = now,
            });
        }
        else
        {
            device.UserId = link.UserId;
        }

        var user = await db.Users.SingleAsync(u => u.Id == link.UserId, ct);
        user.LastSeenAt = now;
        var tokens = Issue(user, now);
        await db.SaveChangesAsync(ct);
        return new AuthResponse(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken, UserDto.From(user));
    }

    /// <summary>Код вводят как угодно: строчными, с пробелами и дефисами.</summary>
    public static string NormalizeCode(string? code)
    {
        var clean = new string((code ?? string.Empty).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (clean.Length != 8)
        {
            throw AppException.Validation("Код — 8 букв и цифр.");
        }

        return clean;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var stored = await FindAsync(refreshToken, ct);
        if (stored is not null)
        {
            await LockAccountAsync(stored.UserId, ct);
            await db.Entry(stored).ReloadAsync(ct);
        }
        if (stored is { RevokedAt: null })
        {
            stored.RevokedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        else if (stored?.ReplacedById is not null)
        {
            // A refresh won the race with logout. Do not leave its replacement usable.
            await RevokeAllAsync(stored.UserId, time.GetUtcNow(), ct);
        }
        await transaction.CommitAsync(ct);
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
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    private Task LockAccountAsync(Guid userId, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtextextended({userId.ToString()}, 1))", ct);
}
