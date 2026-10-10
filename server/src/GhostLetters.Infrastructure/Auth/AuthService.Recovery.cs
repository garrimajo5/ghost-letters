using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GhostLetters.Application;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Auth;

public sealed partial class AuthService
{
    private const string KeyError = "Логин или ключ не подошёл. После пяти ошибок подождите 15 минут.";

    public async Task<RecoveryInfo> RecoveryInfoAsync(Guid userId, CancellationToken ct)
    {
        var key = await db.RecoveryCredentials.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        return new(key?.Login, key?.Kind);
    }

    public async Task<AuthResponse> KeyLoginAsync(KeyLoginRequest request, CancellationToken ct)
    {
        var login = NormalizeLogin(request.Login);
        var secret = CanonicalSecret(request.Key);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var owner = await db.RecoveryCredentials.Where(x => x.Login == login).Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct);
        if (owner is null)
        {
            // Do comparable work for an unknown login; never disclose whether it exists.
            _ = Derive(secret, new byte[16], 600_000);
            throw AppException.Unauthorized(KeyError);
        }
        await LockAccountAsync(owner.Value, ct);
        var key = await db.RecoveryCredentials.SingleAsync(x => x.UserId == owner, ct);
        if (key.Login != login) throw AppException.Unauthorized(KeyError);
        await VerifyKeyAsync(key, secret, ct);
        var user = await db.Users.SingleAsync(x => x.Id == owner, ct);
        user.LastSeenAt = time.GetUtcNow();
        var tokens = Issue(user, user.LastSeenAt);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken, UserDto.From(user));
    }

    public async Task<AuthResponse> SetRecoveryAsync(Guid userId, RecoveryKeyRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAccountAsync(userId, ct);
        var old = await db.RecoveryCredentials.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (old is not null)
        {
            if (request.CurrentKey is null) throw AppException.Validation("Подтвердите прежний ключ.");
            await VerifyKeyAsync(old, CanonicalSecret(request.CurrentKey), ct, changing: true);
        }
        var replacement = await NewCredentialAsync(userId, request, ct);
        if (old is null) db.RecoveryCredentials.Add(replacement);
        else db.Entry(old).CurrentValues.SetValues(replacement);
        var now = time.GetUtcNow();
        await RevokeAllAsync(userId, now, ct);
        // Invalidate outstanding one-time login codes too.
        await db.AuthIdentities.Where(x => x.UserId == userId && x.Provider == AuthProviders.LinkCode).ExecuteDeleteAsync(ct);
        var user = await db.Users.SingleAsync(x => x.Id == userId, ct);
        var tokens = Issue(user, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken, UserDto.From(user));
    }

    private async Task<RecoveryCredential> NewCredentialAsync(Guid userId, RecoveryKeyRequest request, CancellationToken ct)
    {
        var login = NormalizeLogin(request.Login);
        var secret = CanonicalSecret(request.Key);
        if (request.Key.Kind == "cards")
        {
            var cards = request.Key.Cards!;
            if (await db.Cards.CountAsync(x => cards.Contains(x.ImageKey), ct) != 3)
                throw AppException.Validation("Выберите три карты из каталога.");
        }
        // Serialize competing registrations/renames of the same login across API instances.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({login}, 2))", ct);
        if (await db.RecoveryCredentials.AnyAsync(x => x.Login == login && x.UserId != userId, ct))
            throw AppException.Validation("Этот логин занят. Выберите другой.");
        var salt = RandomNumberGenerator.GetBytes(16);
        return new RecoveryCredential
        {
            UserId = userId, Login = login, Kind = request.Key.Kind,
            Salt = Convert.ToBase64String(salt), KeyHash = Convert.ToBase64String(Derive(secret, salt, 600_000)),
        };
    }

    private async Task VerifyKeyAsync(RecoveryCredential key, string secret, CancellationToken ct, bool changing = false)
    {
        var now = time.GetUtcNow();
        if (key.LockedUntil > now) throw changing ? AppException.Forbidden(KeyError) : AppException.Unauthorized(KeyError);
        if (key.LockedUntil is not null) { key.FailedAttempts = 0; key.LockedUntil = null; }
        var valid = CryptographicOperations.FixedTimeEquals(
            Derive(secret, Convert.FromBase64String(key.Salt), key.Iterations), Convert.FromBase64String(key.KeyHash));
        if (!valid)
        {
            key.FailedAttempts++;
            if (key.FailedAttempts >= 5) key.LockedUntil = now.AddMinutes(15);
            await db.SaveChangesAsync(ct);
            // Persist failed attempts even though the caller returns an authentication error.
            await db.Database.CurrentTransaction!.CommitAsync(ct);
            throw changing ? AppException.Forbidden(KeyError) : AppException.Unauthorized(KeyError);
        }
        key.FailedAttempts = 0;
        key.LockedUntil = null;
    }

    internal static string NormalizeLogin(string? login)
    {
        var value = (login ?? "").Trim().ToLowerInvariant();
        if (!Regex.IsMatch(value, "^[a-z0-9_-]{3,32}$", RegexOptions.CultureInvariant))
            throw AppException.Validation("Логин: 3–32 латинские буквы, цифры, дефис или подчёркивание.");
        return value;
    }

    internal static string CanonicalSecret(RecoverySecret? key)
    {
        if (key?.Kind == "word" && key.Word is { Length: >= 5 and <= 128 } word && !string.IsNullOrWhiteSpace(word))
            return "word:" + word.Normalize(NormalizationForm.FormC);
        if (key?.Kind == "cards" && key.Cards is { Length: 3 } cards && cards.Distinct(StringComparer.Ordinal).Count() == 3
            && cards.All(x => x is { Length: > 0 and <= 100 } && Regex.IsMatch(x, "^[a-zA-Z0-9_-]+$")))
            return "cards:" + string.Join(",", cards);
        throw AppException.Validation("Ключ — слово от 5 до 128 символов или три разные карты по порядку.");
    }

    private static byte[] Derive(string secret, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, iterations, HashAlgorithmName.SHA256, 32);
}
