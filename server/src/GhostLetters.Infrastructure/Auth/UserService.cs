using GhostLetters.Application;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Infrastructure.Auth;

/// <summary>Свой профиль: просмотр, смена ника и цвета.</summary>
public sealed class UserService(GhostLettersDbContext db, IMediaStorage storage, MediaLimits mediaLimits, TimeProvider time)
{
    public async Task<UserDto> GetAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw AppException.NotFound("Игрок не найден.");
        return UserDto.From(user);
    }

    public async Task<UserDto> UpdateAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw AppException.NotFound("Игрок не найден.");

        if (request.Nickname is not null)
        {
            user.Nickname = ProfileRules.Nickname(request.Nickname);
        }

        if (request.AvatarColor is not null)
        {
            user.AvatarColor = ProfileRules.AvatarColor(request.AvatarColor);
        }

        await db.SaveChangesAsync(ct);
        return UserDto.From(user);
    }

    /// <summary>Аватарка — до 1 МБ (клиент заранее уменьшает до 512 px).</summary>
    public const long MaxAvatarBytes = 1024 * 1024;

    /// <summary>Загрузить свою аватарку: JPEG, PNG или WebP — проверяется по содержимому, а не по имени.</summary>
    public async Task<UserDto> SetAvatarAsync(Guid userId, Stream content, long length, CancellationToken ct)
    {
        if (length is <= 0 or > MaxAvatarBytes)
        {
            throw AppException.Validation("Картинка — до 1 МБ.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw AppException.NotFound("Игрок не найден.");

        using var buffer = await MediaLimits.ReadAsync(content, length, MaxAvatarBytes, ct);
        var contentType = ImageType(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))
                          ?? throw AppException.Validation("Нужна картинка JPEG, PNG или WebP.");

        var media = new Media
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            ContentType = contentType,
            SizeBytes = buffer.Length,
            CreatedAt = time.GetUtcNow(),
        };
        media.StorageKey = media.Id.ToString("N");
        buffer.Position = 0;
        await mediaLimits.SaveAsync(media, buffer, saved => user.AvatarMediaId = saved.Id, ct);
        return UserDto.From(user);
    }

    public async Task<UserDto> RemoveAvatarAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw AppException.NotFound("Игрок не найден.");
        user.AvatarMediaId = null;
        await db.SaveChangesAsync(ct);
        return UserDto.From(user);
    }

    /// <summary>Аватарку видят все (как ник и цвет), поэтому отдаётся без входа; id не угадать.</summary>
    public async Task<(Stream Content, string ContentType)> OpenAvatarAsync(Guid mediaId, CancellationToken ct)
    {
        var isAvatar = await db.Users.AsNoTracking().AnyAsync(u => u.AvatarMediaId == mediaId, ct);
        var media = isAvatar ? await db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mediaId, ct) : null;
        var stream = media is null ? null : storage.Open(media.StorageKey);
        return stream is null || media is null
            ? throw AppException.NotFound("Аватарка не найдена.")
            : (stream, media.ContentType);
    }

    public static string? ImageType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
