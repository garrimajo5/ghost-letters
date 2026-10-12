using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>Озвученная реплика: MP3 и его длительность.</summary>
public sealed record SpeechAudio(byte[] Mp3, int DurationMs);

/// <summary>
/// Синтез речи Yandex SpeechKit (REST API v3, MP3). Ключ — Speech:ApiKey (API-ключ сервисного аккаунта с ролью
/// ai.speechkit-tts.user); без ключа озвучки нет, боты пишут как раньше. Расход ограничен Speech:DailyCharLimit
/// символов в сутки; одинаковые реплики одним голосом синтезируются один раз (кэш в памяти).
/// </summary>
public sealed class SpeechKit
{
    public const string Endpoint = "https://tts.api.cloud.yandex.net/tts/v3/utteranceSynthesis";

    private readonly HttpClient http;
    private readonly ILogger<SpeechKit> logger;
    private readonly TimeProvider time;
    private readonly string? apiKey;
    private readonly string? folderId;
    private readonly int dailyLimit;
    private readonly ConcurrentDictionary<string, SpeechAudio> cache = new();
    private readonly object gate = new();
    private DateOnly day;
    private int spent;

    public SpeechKit(IConfiguration configuration, ILogger<SpeechKit> logger, TimeProvider time, HttpMessageHandler? handler = null)
    {
        this.logger = logger;
        this.time = time;
        apiKey = configuration["Speech:ApiKey"]?.Trim();
        folderId = configuration["Speech:FolderId"]?.Trim();
        dailyLimit = configuration.GetValue("Speech:DailyCharLimit", 300_000);
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(15);
    }

    public bool Enabled => !string.IsNullOrEmpty(apiKey);

    /// <summary>Сколько символов озвучено сегодня (UTC).</summary>
    public int SpentToday { get { lock (gate) return day == Today ? spent : 0; } }

    private DateOnly Today => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    /// <summary>Озвучить текст; null — озвучки нет (нет ключа, лимит, ошибка SpeechKit). Ошибки не бросает.</summary>
    public async Task<SpeechAudio?> SynthesizeAsync(string text, string voice, string? role, CancellationToken ct)
    {
        text = text.Trim();
        if (!Enabled || text.Length == 0) return null;
        var key = $"{voice}|{role}|{text}";
        if (cache.TryGetValue(key, out var hit)) return hit;
        lock (gate)
        {
            if (day != Today) (day, spent) = (Today, 0);
            if (spent + text.Length > dailyLimit)
            {
                logger.LogWarning("Озвучка: дневной лимит {Limit} символов исчерпан", dailyLimit);
                return null;
            }

            spent += text.Length;
        }

        try
        {
            using var audio = new MemoryStream();
            foreach (var chunk in SpeechText.Chunks(text))
            {
                var bytes = await RequestAsync(chunk, voice, role, ct);
                if (bytes is null) return null;
                audio.Write(bytes);
            }

            var mp3 = audio.ToArray();
            var duration = Mp3.DurationMs(mp3);
            if (duration <= 0) return null;
            var result = new SpeechAudio(mp3, duration);
            if (cache.Count > 500) cache.Clear();
            cache[key] = result;
            return result;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or FormatException)
        {
            if (ct.IsCancellationRequested) throw;
            logger.LogWarning(e, "Озвучка не удалась");
            return null;
        }
    }

    private async Task<byte[]?> RequestAsync(string text, string voice, string? role, CancellationToken ct)
    {
        var hints = new List<object> { new { voice } };
        if (role is not null) hints.Add(new { role });
        var body = JsonSerializer.Serialize(new
        {
            text,
            hints,
            outputAudioSpec = new { containerAudio = new { containerAudioType = "MP3" } },
            loudnessNormalizationType = "LUFS",
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Api-Key", apiKey);
        if (!string.IsNullOrEmpty(folderId)) request.Headers.Add("x-folder-id", folderId);
        using var response = await http.SendAsync(request, ct);
        var payload = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("SpeechKit ответил {Status}: {Body}", (int)response.StatusCode,
                Encoding.UTF8.GetString(payload, 0, Math.Min(payload.Length, 300)));
            return null;
        }

        return ReadAudio(payload);
    }

    /// <summary>Ответ — поток JSON-объектов {"result":{"audioChunk":{"data":"base64"}}}; склеиваем куски аудио.</summary>
    public static byte[] ReadAudio(byte[] payload)
    {
        using var audio = new MemoryStream();
        foreach (var json in TopLevelObjects(payload))
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("result", out var result)) root = result;
            if (root.TryGetProperty("audioChunk", out var chunk) && chunk.TryGetProperty("data", out var data))
                audio.Write(data.GetBytesFromBase64());
        }

        return audio.ToArray();
    }

    /// <summary>Объекты верхнего уровня подряд (по строкам или слитно) — по глубине скобок, строки учитываются.</summary>
    private static IEnumerable<ReadOnlyMemory<byte>> TopLevelObjects(byte[] payload)
    {
        int depth = 0, start = -1;
        bool inString = false, escaped = false;
        for (var i = 0; i < payload.Length; i++)
        {
            var b = payload[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (b == '\\') escaped = true;
                else if (b == '"') inString = false;
                continue;
            }

            if (b == '"') inString = true;
            else if (b == '{')
            {
                if (depth++ == 0) start = i;
            }
            else if (b == '}' && depth > 0 && --depth == 0)
            {
                yield return payload.AsMemory(start, i - start + 1);
            }
        }
    }
}

/// <summary>Длительность MP3 по заголовкам кадров (MPEG-1/2/2.5, Layer III), с пропуском ID3.</summary>
public static class Mp3
{
    private static readonly int[] V1Bitrates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] V2Bitrates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
    private static readonly int[] Rates = [44100, 48000, 32000];

    public static int DurationMs(ReadOnlySpan<byte> data)
    {
        var i = 0;
        double seconds = 0;
        while (i + 4 <= data.Length)
        {
            if (data[i] == 'I' && i + 10 <= data.Length && data[i + 1] == 'D' && data[i + 2] == '3')
            {
                var size = (data[i + 6] << 21) | (data[i + 7] << 14) | (data[i + 8] << 7) | data[i + 9];
                i += 10 + size;
                continue;
            }

            if (data[i] != 0xff || (data[i + 1] & 0xe0) != 0xe0)
            {
                i++;
                continue;
            }

            var version = (data[i + 1] >> 3) & 3; // 3 — MPEG-1, 2 — MPEG-2, 0 — MPEG-2.5
            var layer = (data[i + 1] >> 1) & 3; // 1 — Layer III
            var bitrateIndex = data[i + 2] >> 4;
            var rateIndex = (data[i + 2] >> 2) & 3;
            if (version == 1 || layer != 1 || bitrateIndex is 0 or 15 || rateIndex == 3)
            {
                i++;
                continue;
            }

            var mpeg1 = version == 3;
            var rate = Rates[rateIndex] / (mpeg1 ? 1 : version == 2 ? 2 : 4);
            var bitrate = (mpeg1 ? V1Bitrates : V2Bitrates)[bitrateIndex] * 1000;
            var samples = mpeg1 ? 1152 : 576;
            var padding = (data[i + 2] >> 1) & 1;
            var length = samples / 8 * bitrate / rate + padding;
            if (length < 4) { i++; continue; }
            seconds += (double)samples / rate;
            i += length;
        }

        return (int)Math.Round(seconds * 1000);
    }
}
