using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GhostLetters.Infrastructure.Voice;

/// <summary>Подключение к голосовой комнате партии.</summary>
public sealed record VoiceAccess(string Url, string Token, string Room, bool CanSpeak);

/// <summary>
/// Живой голосовой чат партии через LiveKit (свой сервер рядом с API). Комната — партия; говорить можно только тому,
/// у кого слово: рация или дали слово, в свободном обсуждении и голосовании — всем, кроме Призрака; после итогов — всем.
/// Право говорить сервер выставляет сам (UpdateParticipant), клиенту доверять не нужно.
/// Настройки: LiveKit:ApiKey, LiveKit:ApiSecret, LiveKit:PublicUrl (wss://…), LiveKit:InternalUrl (http://livekit:7880).
/// </summary>
public sealed class VoiceRooms
{
    private readonly HttpClient http;
    private readonly ILogger<VoiceRooms> logger;
    private readonly TimeProvider time;
    private readonly string? apiKey;
    private readonly string? apiSecret;
    private readonly string? publicUrl;
    private readonly string? internalUrl;
    private readonly ConcurrentDictionary<Guid, string> lastSpeakers = new();

    public VoiceRooms(IConfiguration configuration, ILogger<VoiceRooms> logger, TimeProvider time, HttpMessageHandler? handler = null)
    {
        this.logger = logger;
        this.time = time;
        apiKey = configuration["LiveKit:ApiKey"]?.Trim();
        apiSecret = configuration["LiveKit:ApiSecret"]?.Trim();
        publicUrl = configuration["LiveKit:PublicUrl"]?.Trim().TrimEnd('/');
        internalUrl = (configuration["LiveKit:InternalUrl"] ?? "http://livekit:7880").Trim().TrimEnd('/');
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(5);
    }

    public bool Enabled => !string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret) && !string.IsNullOrEmpty(publicUrl);

    public static string RoomOf(Guid gameId) => "game-" + gameId.ToString("N");

    /// <summary>Может ли игрок сейчас говорить вслух.</summary>
    public static bool MaySpeak(GameState state, Guid userId)
    {
        var player = state.Players.FirstOrDefault(p => p.Id == userId);
        if (player is null) return false;
        if (state.Phase is Phase.Finished or Phase.AwardNomination or Phase.AwardVoting || state.Result is not null) return true;
        // Призрак до итогов ничего не сообщает, кроме карт.
        if (player.Role == Role.Ghost) return false;
        return state.Phase switch
        {
            Phase.Discussion => state.EffectiveDiscussion == DiscussionMode.FreeChat ||
                                state.CurrentSpeaker == userId || state.FloorGrantedTo == userId,
            Phase.Voting or Phase.VoteTie => true,
            _ => false,
        };
    }

    /// <summary>Доступ к комнате: игрокам — слушать всегда, говорить — когда можно; зрителям — только слушать.</summary>
    public VoiceAccess Access(GameState state, Guid userId, string name)
    {
        var room = RoomOf(state.Id);
        var speak = MaySpeak(state, userId);
        var token = Token(userId.ToString(), name, new Dictionary<string, object>
        {
            ["roomJoin"] = true, ["room"] = room, ["canSubscribe"] = true, ["canPublish"] = speak,
            ["canPublishData"] = false, ["canPublishSources"] = new[] { "microphone" },
        }, TimeSpan.FromHours(6));
        return new VoiceAccess(publicUrl!, token, room, speak);
    }

    /// <summary>Новое состояние партии: выставить право говорить тем, у кого оно поменялось.</summary>
    public async Task SyncAsync(GameState state, CancellationToken ct)
    {
        if (!Enabled) return;
        var speakers = state.Players.Where(p => MaySpeak(state, p.Id)).Select(p => p.Id).Order().ToList();
        var key = string.Join(",", speakers);
        var previous = lastSpeakers.TryGetValue(state.Id, out var last) ? last : null;
        if (previous == key) return;
        lastSpeakers[state.Id] = key;
        var before = previous is null ? null : previous.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToHashSet();
        var room = RoomOf(state.Id);
        foreach (var player in state.Players)
        {
            var speak = speakers.Contains(player.Id);
            if (before is not null && before.Contains(player.Id) == speak) continue;
            await UpdateAsync(room, player.Id.ToString(), speak, ct);
        }

        if (state.Phase == Phase.Finished && state.Result is not null && lastSpeakers.Count > 1000) lastSpeakers.Clear();
    }

    private async Task UpdateAsync(string room, string identity, bool canPublish, CancellationToken ct)
    {
        var admin = Token("ghost-letters-server", "server", new Dictionary<string, object> { ["roomAdmin"] = true, ["room"] = room },
            TimeSpan.FromMinutes(5));
        var body = JsonSerializer.Serialize(new
        {
            room, identity,
            permission = new { canSubscribe = true, canPublish, canPublishData = false, canPublishSources = new[] { 2 } },
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{internalUrl}/twirp/livekit.RoomService/UpdateParticipant")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        try
        {
            using var response = await http.SendAsync(request, ct);
            // Игрока нет в комнате (не включал голос) — ничего страшного: при входе он получит права в токене.
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
                logger.LogDebug("LiveKit UpdateParticipant {Identity}: {Status}", identity, (int)response.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            logger.LogDebug(e, "LiveKit недоступен");
        }
    }

    /// <summary>JWT для LiveKit (HS256): iss — ключ API, sub — участник, video — права.</summary>
    public string Token(string identity, string name, IReadOnlyDictionary<string, object> video, TimeSpan lifetime)
    {
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = B64(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = apiKey ?? "", ["sub"] = identity, ["name"] = name, ["nbf"] = now - 10, ["exp"] = now + (long)lifetime.TotalSeconds,
            ["video"] = video,
        }));
        var signature = B64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(apiSecret ?? ""), Encoding.ASCII.GetBytes(header + "." + payload)));
        return header + "." + payload + "." + signature;
    }

    private static string B64(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
