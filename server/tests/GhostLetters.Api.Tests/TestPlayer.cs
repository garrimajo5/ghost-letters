using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GhostLetters.Infrastructure.Auth;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

/// <summary>Игрок в тестах: свой HttpClient с токеном.</summary>
public sealed class TestPlayer
{
    private TestPlayer(HttpClient client, AuthResponse auth)
    {
        Client = client;
        Auth = auth;
    }

    public HttpClient Client { get; }

    public AuthResponse Auth { get; }

    public Guid Id => Auth.User.Id;

    public string Token => Auth.AccessToken;

    public static async Task<TestPlayer> LoginAsync(DbApiFactory factory, string nickname)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/guest",
            new GuestLoginRequest($"device-{Guid.NewGuid():N}", nickname, null));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return new TestPlayer(client, auth);
    }

    public async Task<JsonElement> PostAsync(string url, object? body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await Client.PostAsJsonAsync(url, body ?? new { }, GameJson.Options);
        return await ReadAsync(response, expected);
    }

    public async Task<JsonElement> PutAsync(string url, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await Client.PutAsJsonAsync(url, body, GameJson.Options);
        return await ReadAsync(response, expected);
    }

    public async Task<JsonElement> GetAsync(string url, HttpStatusCode expected = HttpStatusCode.OK) =>
        await ReadAsync(await Client.GetAsync(url), expected);

    /// <summary>Отправить игровую команду; ожидаемый код ответа — expected.</summary>
    public Task<JsonElement> CommandAsync(Guid gameId, string type, object? payload = null, int? expectedVersion = null,
        string? clientCommandId = null, HttpStatusCode expected = HttpStatusCode.OK) =>
        PostAsync($"/api/v1/games/{gameId}/commands",
            new { type, payload = payload ?? new { }, expectedVersion, clientCommandId }, expected);

    public async Task<JsonElement> ViewAsync(Guid gameId) =>
        (await GetAsync($"/api/v1/games/{gameId}/view")).GetProperty("view");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
}

public static class JsonExtensions
{
    public static string Str(this JsonElement e, string name) => e.GetProperty(name).GetString()!;

    public static Guid Id(this JsonElement e, string name) => e.GetProperty(name).GetGuid();

    public static string Code(this JsonElement problem) => problem.Str("code");
}
