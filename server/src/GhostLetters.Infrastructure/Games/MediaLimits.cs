using GhostLetters.Application;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Bounded reads and database-wide quotas, shared by avatars and voice uploads.</summary>
public sealed class MediaLimits(GhostLettersDbContext db, IMediaStorage storage, IConfiguration config,
    TimeProvider time, ILogger<MediaLimits> logger)
{
    public static async Task<MemoryStream> ReadAsync(Stream input, long declaredLength, long maximum, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        try
        {
            var chunk = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(chunk, ct)) != 0)
            {
                if (buffer.Length + count > maximum) throw AppException.Validation("Файл слишком большой.");
                await buffer.WriteAsync(chunk.AsMemory(0, count), ct);
            }
            if (buffer.Length != declaredLength || buffer.Length == 0)
                throw AppException.Validation("Неверная длина файла.");
            buffer.Position = 0;
            return buffer;
        }
        catch { buffer.Dispose(); throw; }
    }

    public async Task SaveAsync(Media media, Stream content, Action<Media>? attach, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // One quota decision across all API replicas; never race SUM with another upload.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74293821)", ct);
        var total = await db.MediaFiles.SumAsync(m => (long?)m.SizeBytes, ct) ?? 0;
        var mine = await db.MediaFiles.Where(m => m.OwnerId == media.OwnerId).SumAsync(m => (long?)m.SizeBytes, ct) ?? 0;
        var pending = await db.MediaFiles.CountAsync(m => m.OwnerId == media.OwnerId &&
            !db.ChatMessages.Any(c => c.MediaId == m.Id) && !db.Users.Any(u => u.AvatarMediaId == m.Id), ct);
        if (total + media.SizeBytes > config.GetValue("Media:TotalQuotaBytes", 2L * 1024 * 1024 * 1024) ||
            mine + media.SizeBytes > config.GetValue("Media:UserQuotaBytes", 100L * 1024 * 1024) || pending >= 5)
            throw new AppException("MEDIA_QUOTA", "Лимит хранения файлов исчерпан. Попробуйте позже.", 429);
        try
        {
            await storage.SaveAsync(media.StorageKey, content, ct);
            db.MediaFiles.Add(media);
            attach?.Invoke(media);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            DeleteFile(media.StorageKey);
            throw;
        }
    }

    public async Task<int> CleanupAsync(CancellationToken ct)
    {
        var cutoff = time.GetUtcNow().AddHours(-24);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74293821)", ct);
        var unused = await db.MediaFiles.Where(m => m.CreatedAt < cutoff &&
            !db.ChatMessages.Any(c => c.MediaId == m.Id) && !db.Users.Any(u => u.AvatarMediaId == m.Id))
            .OrderBy(m => m.CreatedAt).Take(500).ToListAsync(ct);
        db.MediaFiles.RemoveRange(unused);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        foreach (var file in unused) DeleteFile(file.StorageKey);
        return unused.Count;
    }

    private void DeleteFile(string key)
    {
        try { storage.Delete(key); }
        catch (IOException e) { logger.LogError(e, "Не удалось удалить файл {Key}", key); }
    }

    // Check the actual container, never serve arbitrary text as user-supplied MIME.
    public static bool IsVoice(ReadOnlySpan<byte> data, string type)
    {
        if (data.Length < 16) return false;
        return type switch
        {
            "audio/aac" => data[0] == 0xff && (data[1] & 0xf6) == 0xf0,
            "audio/mpeg" => data[..3].SequenceEqual("ID3"u8) ||
                (data[0] == 0xff && (data[1] & 0xe0) == 0xe0 && (data[1] & 6) != 0),
            "audio/mp4" or "audio/m4a" or "audio/x-m4a" => data.Slice(4, 4).SequenceEqual("ftyp"u8),
            "audio/ogg" => data[..4].SequenceEqual("OggS"u8) && data[4] == 0,
            "audio/webm" => data[..4].SequenceEqual(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }),
            "audio/wav" => data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WAVE"u8),
            _ => false,
        };
    }
}
