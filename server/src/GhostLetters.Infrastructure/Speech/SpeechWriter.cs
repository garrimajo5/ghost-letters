using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GhostLetters.Infrastructure.Speech;

/// <summary>Кто говорит: имя (без «Бот»), пара слов о характере и род по голосу.</summary>
public sealed record Speaker(string Name, string About, bool Male);

/// <summary>
/// Живые формулировки для озвучки через YandexGPT (OpenAI-совместимый API AI Studio). Нужны Speech:FolderId и ключ
/// с ролью ai.languageModels.user (Speech:LlmApiKey, иначе Speech:ApiKey). Модель лишь пересказывает уже решённое
/// ботом: в ответе должны остаться все названия карт, иначе берётся пересказ по правилам (<see cref="SpokenStyle"/>).
/// </summary>
public sealed partial class SpeechWriter
{
    public const string Endpoint = "https://ai.api.cloud.yandex.net/v1/chat/completions";

    private readonly HttpClient http;
    private readonly ILogger<SpeechWriter> logger;
    private readonly TimeProvider time;
    private readonly string? apiKey;
    private readonly string? folderId;
    private readonly string model;
    private readonly int dailyCalls;
    private readonly object gate = new();
    private DateOnly day;
    private int calls;

    public SpeechWriter(IConfiguration configuration, ILogger<SpeechWriter> logger, TimeProvider time, HttpMessageHandler? handler = null)
    {
        this.logger = logger;
        this.time = time;
        apiKey = (configuration["Speech:LlmApiKey"] is { Length: > 0 } own ? own : configuration["Speech:ApiKey"])?.Trim();
        folderId = configuration["Speech:FolderId"]?.Trim();
        model = configuration["Speech:LlmModel"] is { Length: > 0 } m ? m.Trim() : "yandexgpt-lite/latest";
        dailyCalls = configuration.GetValue("Speech:LlmDailyCalls", 3000);
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(8);
    }

    public bool Enabled => !string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(folderId);

    /// <summary>Модель пересказа (для кабинета).</summary>
    public string Model => model;

    /// <summary>Сколько пересказов принято сегодня и почему не удался последний (для кабинета).</summary>
    public int AcceptedToday { get { lock (gate) return accepted; } }

    public string? LastProblem { get; private set; }

    private int accepted;

    private void Done(string? accepted0, string? problem)
    {
        lock (gate)
        {
            if (accepted0 is not null) accepted++;
        }
        LastProblem = accepted0 is null ? problem : null;
    }

    /// <summary>Пересказ реплики; null — модель недоступна или ответ не прошёл проверку.</summary>
    public async Task<string?> RephraseAsync(string spoken, IReadOnlyCollection<string> cardNames, Speaker speaker, CancellationToken ct)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(spoken)) return null;
        lock (gate)
        {
            var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            if (day != today) (day, calls, accepted) = (today, 0, 0);
            if (calls >= dailyCalls) return null;
            calls++;
        }

        var limit = Math.Min(SpeechText.MaxLength, spoken.Length + 80);
        var system = new StringBuilder()
            .Append("Ты озвучиваешь реплику игрока за столом детективной настольной игры «Письма призрака». ")
            .Append($"Говорит {(speaker.Male ? "мужчина" : "женщина")} по имени {speaker.Name}")
            .Append(string.IsNullOrWhiteSpace(speaker.About) ? ". " : $"; характер: {speaker.About.Trim()}. ")
            .Append("Эту реплику прочитает синтезатор речи. Перепиши её так, чтобы она звучала живо и с верной интонацией, ")
            .Append("как говорит человек за столом: в своём характере, от первого лица. Держись близко к исходной реплике — её же видят в чате: ")
            .Append("тот же порядок мыслей и почти те же слова, можно добавить короткие разговорные связки («ну», «так», «слушайте», «хм»). ")
            .Append("Интонацию задавай пунктуацией: запятые и тире — паузы, многоточие — раздумье, «!» — уверенность или эмоция, «?» — только для настоящего вопроса. ")
            .Append("Длинные предложения дели на короткие. Сохрани смысл, все факты, обращения по имени и род слов (кто «права», кто «прав»). ")
            .Append("Названия карт оставь узнаваемыми (падеж менять можно). Не добавляй новых карт, улик, имён и выводов. ")
            .Append("Не упоминай игру, правила и что ты ИИ. ")
            .Append($"Не длиннее {limit} символов. Без списков, кавычек, эмодзи, латиницы и разметки. Ответь только текстом реплики.")
            .ToString();
        var user = (cardNames.Count > 0 ? "Карты: " + string.Join("; ", cardNames) + "\n" : "") + "Реплика: " + spoken;
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model = $"gpt://{folderId}/{model}",
                messages = new[] { new { role = "system", content = system }, new { role = "user", content = user } },
                temperature = 0.9,
                max_tokens = 300,
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Api-Key", apiKey);
            request.Headers.Add("OpenAI-Project", folderId);
            using var response = await http.SendAsync(request, ct);
            var payload = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("YandexGPT ответил {Status}: {Body}", (int)response.StatusCode, payload.Length > 300 ? payload[..300] : payload);
                Done(null, $"YandexGPT ответил {(int)response.StatusCode}: {(payload.Length > 160 ? payload[..160] : payload)}");
                return null;
            }

            using var doc = JsonDocument.Parse(payload);
            var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            var result = Accept(text, spoken, cardNames, limit);
            Done(result, $"ответ не прошёл проверку: «{(text is { Length: > 120 } t ? t[..120] + "…" : text)}»");
            return result;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            if (ct.IsCancellationRequested) throw;
            logger.LogWarning(e, "Пересказ реплики не удался");
            Done(null, e.GetType().Name + ": " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// Можно ли так назвать карту за столом (по её названиям и признакам). true/false — ответ модели; null — модель недоступна.
    /// </summary>
    public async Task<bool?> JudgeAsync(string phrase, IReadOnlyCollection<string> names, IReadOnlyCollection<string> features, CancellationToken ct)
    {
        if (!Enabled) return null;
        lock (gate)
        {
            var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            if (day != today) (day, calls) = (today, 0);
            if (calls >= dailyCalls) return null;
            calls++;
        }

        var system = "Ты судья в настольной игре с картинками. Игрок назвал картинку своим словом. Реши, честное ли это название: " +
                     "похоже ли оно на то, что изображено, или на очевидную ассоциацию. Ответь одним словом: да или нет.";
        var user = $"Картинка: {string.Join(", ", names)}. Признаки: {string.Join(", ", features.Take(12))}. Игрок назвал её: «{phrase}».";
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model = $"gpt://{folderId}/{model}",
                messages = new[] { new { role = "system", content = system }, new { role = "user", content = user } },
                temperature = 0.0,
                max_tokens = 5,
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new AuthenticationHeaderValue("Api-Key", apiKey);
            request.Headers.Add("OpenAI-Project", folderId);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var answer = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim().ToLowerInvariant() ?? "";
            return answer.StartsWith("да", StringComparison.Ordinal) ? true : answer.StartsWith("нет", StringComparison.Ordinal) ? false : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            if (ct.IsCancellationRequested) throw;
            return null;
        }
    }

    [GeneratedRegex(@"[A-Za-z*#_`\[\]<>]")]
    private static partial Regex Foreign();

    /// <summary>Ответ модели годится, если в нём есть все карты (по основе слов), он не длиннее нормы и без разметки.</summary>
    public static string? Accept(string? text, string spoken, IReadOnlyCollection<string> cardNames, int limit)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var clean = Regex.Replace(text.Trim().Trim('"', '«', '»'), @"\s+", " ");
        if (clean.Length < 8 || clean.Length > limit || Foreign().IsMatch(clean)) return null;
        var lower = clean.ToLowerInvariant();
        foreach (var name in cardNames)
        {
            // «клетка» → «клетку»: сверяем основы слов названия (без двух последних букв, не короче 3).
            var words = name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3);
            if (!words.All(w => lower.Contains(w.Length > 4 ? w[..^2] : w[..3]))) return null;
        }

        return clean;
    }
}
