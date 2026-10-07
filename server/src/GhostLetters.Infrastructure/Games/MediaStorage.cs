using Microsoft.Extensions.Configuration;

namespace GhostLetters.Infrastructure.Games;

/// <summary>Хранилище файлов голосовых. Сейчас — папка на диске (Media:StoragePath), позже можно S3.</summary>
public interface IMediaStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken ct);

    Stream? Open(string key);
}

public sealed class FileMediaStorage(IConfiguration configuration) : IMediaStorage
{
    private readonly string _root = Path.GetFullPath(configuration["Media:StoragePath"] is { Length: > 0 } path
        ? path
        : Path.Combine(AppContext.BaseDirectory, "data", "media"));

    public async Task SaveAsync(string key, Stream content, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        await using var file = File.Create(PathFor(key));
        await content.CopyToAsync(file, ct);
    }

    public Stream? Open(string key)
    {
        var path = PathFor(key);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    /// <summary>Ключ — только из id, без путей от клиента.</summary>
    private string PathFor(string key) =>
        key.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            ? Path.Combine(_root, key)
            : throw new ArgumentException("Недопустимый ключ файла.", nameof(key));
}
