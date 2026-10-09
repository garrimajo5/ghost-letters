using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using GhostLetters.Infrastructure.Games;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace GhostLetters.Api.Tests;

[Collection(DbCollection.Name)]
public sealed class SpectatorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task SignalR_SpectatorGetsPublicUpdatesAndChat_AfterReconnectToo()
    {
        var game = await GameHarness.StartAsync(_factory, 7);
        var watcher = await TestPlayer.LoginAsync(_factory, "Зритель онлайн");
        await watcher.PostAsync($"/api/v1/lobbies/{game.Code}/join", new { mode = "spectator" });
        await new GameDriver(game).RunUntilAsync(p => p == "Mailbox");
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/play"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(watcher.Token);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        var views = Channel.CreateUnbounded<JsonElement>();
        var messages = Channel.CreateUnbounded<JsonElement>();
        connection.On<JsonElement>("GameView", v => views.Writer.TryWrite(v));
        connection.On<JsonElement>("ChatMessage", m => messages.Writer.TryWrite(m));
        await connection.StartAsync();
        AssertPublic((await connection.InvokeAsync<JsonElement>("SubscribeGame", game.GameId)).GetProperty("view"));
        var detective = await game.WithRoleAsync("Detective");
        var hand = (await detective.ViewAsync(game.GameId)).GetProperty("me").GetProperty("hand");
        await detective.CommandAsync(game.GameId, "SendLetter", new { cardIds = new[] { hand[0].GetString() } });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        AssertPublic((await views.Reader.ReadAsync(timeout.Token)).GetProperty("view"));

        var killer = await game.WithRoleAsync("Killer");
        var chatUrl = $"/api/v1/games/{game.GameId}/chat";
        await killer.PostAsync(chatUrl, new { channel = "killer_team", text = "Секрет" });
        await detective.PostAsync(chatUrl, new { text = "Открытая версия" });
        var message = await messages.Reader.ReadAsync(timeout.Token);
        message.Str("channel").Should().Be("public");
        message.Str("text").Should().Be("Открытая версия");
        var command = async () => await connection.InvokeAsync<JsonElement>("Command", game.GameId, "AckRole", null, null, null);
        (await command.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain("FORBIDDEN");
        await connection.StopAsync();
        await connection.StartAsync();
        AssertPublic((await connection.InvokeAsync<JsonElement>("SubscribeGame", game.GameId)).GetProperty("view"));
        await connection.InvokeAsync("UnsubscribeGame", game.GameId);
    }

    [Fact]
    public async Task Stranger_DiscoversAndJoinsRunningGame_WithoutSecretsOrActions()
    {
        var game = await GameHarness.StartAsync(_factory, 7, new LobbySettings { Rounds = 1 });
        var spectator = await TestPlayer.LoginAsync(_factory, "Зритель");
        var url = $"/api/v1/games/{game.GameId}";
        await spectator.GetAsync($"{url}/view", HttpStatusCode.Forbidden);

        var listed = await spectator.GetAsync("/api/v1/lobbies/watchable");
        var item = listed.EnumerateArray().Single(g => g.Id("gameId") == game.GameId);
        item.Str("code").Should().Be(game.Code);
        item.GetProperty("players").GetInt32().Should().Be(7);
        item.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("code", "title", "gameId", "phase", "players");
        (await game.Host.GetAsync("/api/v1/lobbies/watchable")).EnumerateArray()
            .Should().NotContain(g => g.Id("gameId") == game.GameId);

        var lobby = await spectator.PostAsync($"/api/v1/lobbies/{game.Code}/join", new { mode = "spectator" });
        lobby.GetProperty("members").EnumerateArray().Single(m => m.Id("userId") == spectator.Id).Str("mode").Should().Be("spectator");
        await spectator.PostAsync($"/api/v1/lobbies/{game.Code}/join", new { mode = "spectator" });
        await spectator.PostAsync($"/api/v1/lobbies/{game.Code}/join", new { mode = "player" }, HttpStatusCode.Conflict);
        await game.Host.PostAsync($"/api/v1/lobbies/{game.Code}/join", new { mode = "spectator" }, HttpStatusCode.Conflict);

        var driver = new GameDriver(game);
        await driver.RunUntilAsync(p => p == "GhostPick");
        var view = await spectator.ViewAsync(game.GameId);
        AssertPublic(view);
        var detective = await game.WithRoleAsync("Detective");
        var detectiveView = await detective.ViewAsync(game.GameId);
        view.GetProperty("board").GetRawText().Should().Be(detectiveView.GetProperty("board").GetRawText());
        view.GetProperty("hints").GetRawText().Should().Be(detectiveView.GetProperty("hints").GetRawText());
        view.GetProperty("players").GetArrayLength().Should().Be(7);

        await spectator.PostAsync($"{url}/commands", new { type = "AckRole" }, HttpStatusCode.Forbidden);
        await spectator.PostAsync($"{url}/chat", new { text = "Голосуйте!" }, HttpStatusCode.Forbidden);
        await spectator.PostAsync($"/api/v1/lobbies/{game.LobbyId}/ready", new { ready = true }, HttpStatusCode.Forbidden);
        await spectator.GetAsync($"{url}/notes", HttpStatusCode.Forbidden);
        await spectator.GetAsync($"{url}/marks", HttpStatusCode.Forbidden);
        await spectator.GetAsync($"{url}/summary", HttpStatusCode.Conflict);

        var publicEvents = await spectator.GetAsync($"{url}/events");
        // Exact match with the table projection also covers private role/deal/letter events.
        var table = await TestPlayer.LoginAsync(_factory, "Стол");
        await table.PostAsync($"/api/v1/lobbies/{game.Code}/join", new { mode = "table" });
        publicEvents.GetRawText().Should().Be((await table.GetAsync($"{url}/events")).GetRawText());
        (await table.ViewAsync(game.GameId)).GetRawText().Should().Be(view.GetRawText());

        await driver.RunUntilAsync(p => p == "Discussion");
        AssertPublic(await spectator.ViewAsync(game.GameId));
        await spectator.PostAsync($"/api/v1/lobbies/{game.LobbyId}/leave", null, HttpStatusCode.NoContent);
        await spectator.GetAsync($"{url}/view", HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Spectator_PublicChatAndVoiceOnly_EvenWithPrivateMediaId()
    {
        var game = await GameHarness.StartAsync(_factory, 4);
        var watcher = await TestPlayer.LoginAsync(_factory, "Наблюдатель");
        await watcher.PostAsync($"/api/v1/lobbies/{game.Code}/join", new { mode = "spectator" });
        var killer = await game.WithRoleAsync("Killer");
        var detective = await game.WithRoleAsync("Detective");
        var url = $"/api/v1/games/{game.GameId}/chat";
        await detective.PostAsync(url, new { text = "Публичная версия" });
        await killer.PostAsync(url, new { channel = "killer_team", text = "Тайный план" });
        (await watcher.GetAsync(url)).EnumerateArray().Select(m => m.Str("channel")).Should().OnlyContain(c => c == "public");

        foreach (var channel in new[] { "public", "killer_team" })
        {
            using var form = new MultipartFormDataContent();
            var payload = new byte[4096];
            payload[0] = 0xff; payload[1] = 0xf1;
            var bytes = new ByteArrayContent(payload);
            bytes.Headers.ContentType = new MediaTypeHeaderValue("audio/aac");
            form.Add(bytes, "file", "voice.aac");
            form.Add(new StringContent("1000"), "durationMs");
            var upload = await killer.Client.PostAsync("/api/v1/media", form);
            upload.StatusCode.Should().Be(HttpStatusCode.OK);
            var media = JsonDocument.Parse(await upload.Content.ReadAsStringAsync()).RootElement.Id("mediaId");
            _factory.Time.Advance(TimeSpan.FromSeconds(2));
            await killer.PostAsync(url, new { channel, mediaId = media });
            (await watcher.Client.GetAsync($"/api/v1/media/{media}")).StatusCode.Should()
                .Be(channel == "public" ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Spectator_InLobby_DoesNotNeedReadyOrReceiveRoleOrStats()
    {
        var host = await TestPlayer.LoginAsync(_factory, "Хост");
        var player = await TestPlayer.LoginAsync(_factory, "Игрок");
        var watcher = await TestPlayer.LoginAsync(_factory, "Зритель");
        var lobby = await host.PostAsync("/api/v1/lobbies", new { settings = new LobbySettings { Rounds = 1 } });
        var code = lobby.Str("code");
        var id = lobby.Id("id");
        await player.PostAsync($"/api/v1/lobbies/{code}/join", new { mode = "player" });
        await watcher.PostAsync($"/api/v1/lobbies/{code}/join", new { mode = "spectator" });
        await player.PostAsync($"/api/v1/lobbies/{id}/ready", new { ready = true });
        var gameId = (await host.PostAsync($"/api/v1/lobbies/{id}/start", null)).Id("gameId");
        (await watcher.ViewAsync(gameId)).GetProperty("players").GetArrayLength().Should().Be(2);
        var game = GameHarness.Existing(_factory, [host, player], id, code, gameId);
        await new GameDriver(game).RunUntilAsync(p => p == "Finished");
        var profile = await watcher.GetAsync($"/api/v1/users/{watcher.Id}/profile");
        profile.GetProperty("stats").GetProperty("games").GetInt32().Should().Be(0);
        (await watcher.GetAsync("/api/v1/lobbies/watchable")).EnumerateArray().Should().NotContain(g => g.Id("gameId") == gameId);
    }

    private static void AssertPublic(JsonElement view)
    {
        view.GetProperty("me").ValueKind.Should().Be(JsonValueKind.Null);
        view.GetProperty("truth").ValueKind.Should().Be(JsonValueKind.Null);
        view.GetProperty("mailboxForGhost").ValueKind.Should().Be(JsonValueKind.Null);
        view.GetProperty("teamSuggestions").ValueKind.Should().Be(JsonValueKind.Null);
        view.GetProperty("allowedCommands").GetArrayLength().Should().Be(0);
        foreach (var player in view.GetProperty("players").EnumerateArray())
        {
            if (player.GetProperty("isGhost").GetBoolean()) player.Str("knownRole").Should().Be("Ghost");
            else player.GetProperty("knownRole").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }
}
