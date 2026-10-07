using System.Net;
using System.Text.Json;
using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Games;

namespace GhostLetters.Api.Tests;

[Collection(DbCollection.Name)]
public sealed class LobbyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Create_Join_Ready_Start()
    {
        var host = await TestPlayer.LoginAsync(_factory, "Хост");
        var lobby = await host.PostAsync("/api/v1/lobbies", new { title = "Пятница" });
        var code = lobby.Str("code");
        var lobbyId = lobby.Id("id");
        code.Should().MatchRegex("^[A-Z2-9]{6}$");
        lobby.Str("status").Should().Be("open");

        var guests = new List<TestPlayer>();
        for (var i = 0; i < 3; i++)
        {
            var p = await TestPlayer.LoginAsync(_factory, $"Гость {i}");
            await p.PostAsync($"/api/v1/lobbies/{code.ToLowerInvariant()}/join", new { mode = "player" });
            guests.Add(p);
        }

        var again = await guests[0].PostAsync($"/api/v1/lobbies/{code}/join", new { mode = "player" });
        var members = again.GetProperty("members").EnumerateArray().ToList();
        members.Select(m => m.GetProperty("seat").GetInt32()).Should().Equal(0, 1, 2, 3);
        members[0].Id("userId").Should().Be(host.Id);

        var notReady = await host.PostAsync($"/api/v1/lobbies/{lobbyId}/start", null, HttpStatusCode.BadRequest);
        notReady.Code().Should().Be("VALIDATION");

        var byGuest = await guests[0].PostAsync($"/api/v1/lobbies/{lobbyId}/start", null, HttpStatusCode.Forbidden);
        byGuest.Code().Should().Be("FORBIDDEN");

        foreach (var g in guests)
        {
            await g.PostAsync($"/api/v1/lobbies/{lobbyId}/ready", new { ready = true });
        }

        var started = await host.PostAsync($"/api/v1/lobbies/{lobbyId}/start", null);
        var gameId = started.Id("gameId");

        var after = await host.GetAsync($"/api/v1/lobbies/{code}");
        after.Str("status").Should().Be("in_game");
        after.Id("currentGameId").Should().Be(gameId);

        var late = await TestPlayer.LoginAsync(_factory, "Опоздал");
        (await late.PostAsync($"/api/v1/lobbies/{code}/join", new { mode = "player" }, HttpStatusCode.Conflict))
            .Code().Should().Be("GAME_IN_PROGRESS");
        await late.PostAsync($"/api/v1/lobbies/{code}/join", new { mode = "table" });
        (await late.ViewAsync(gameId)).GetProperty("me").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Settings_OnlyHost_AndValidated()
    {
        var host = await TestPlayer.LoginAsync(_factory, "Хост");
        var guest = await TestPlayer.LoginAsync(_factory, "Гость");
        var lobby = await host.PostAsync("/api/v1/lobbies", new { });
        var id = lobby.Id("id");
        await guest.PostAsync($"/api/v1/lobbies/{lobby.Str("code")}/join", new { });

        var settings = new LobbySettings { Columns = 6, Discussion = DiscussionMode.FreeChat, CardSets = ["original"] };
        (await guest.PutAsync($"/api/v1/lobbies/{id}/settings", settings, HttpStatusCode.Forbidden)).Code().Should().Be("FORBIDDEN");
        (await host.PutAsync($"/api/v1/lobbies/{id}/settings", settings with { Columns = 9 }, HttpStatusCode.BadRequest))
            .Code().Should().Be("VALIDATION");
        (await host.PutAsync($"/api/v1/lobbies/{id}/settings", settings with { CardSets = ["nope"] }))
            .GetProperty("settings").GetProperty("cardSets")[0].GetString().Should().Be("nope", "наборы проверяются при старте");

        var updated = await host.PutAsync($"/api/v1/lobbies/{id}/settings", settings);
        updated.GetProperty("settings").GetProperty("columns").GetInt32().Should().Be(6);
        updated.GetProperty("settings").Str("discussion").Should().Be("FreeChat");
    }

    [Fact]
    public async Task HostLeaves_NextPlayerAroundTheTableBecomesHost_LastLeaveCloses()
    {
        var players = new List<TestPlayer>();
        for (var i = 0; i < 3; i++)
        {
            players.Add(await TestPlayer.LoginAsync(_factory, $"Игрок {i}"));
        }

        var lobby = await players[0].PostAsync("/api/v1/lobbies", new { });
        var code = lobby.Str("code");
        var id = lobby.Id("id");
        await players[1].PostAsync($"/api/v1/lobbies/{code}/join", new { });
        await players[2].PostAsync($"/api/v1/lobbies/{code}/join", new { });

        await players[0].PostAsync($"/api/v1/lobbies/{id}/leave", null, HttpStatusCode.NoContent);

        var after = await players[1].GetAsync($"/api/v1/lobbies/{code}");
        after.Id("hostUserId").Should().Be(players[1].Id);
        after.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("seat").GetInt32()).Should().Equal(0, 1);

        await players[2].PostAsync($"/api/v1/lobbies/{id}/leave", null, HttpStatusCode.NoContent);
        await players[1].PostAsync($"/api/v1/lobbies/{id}/leave", null, HttpStatusCode.NoContent);
        (await players[1].GetAsync($"/api/v1/lobbies/{code}", HttpStatusCode.NotFound)).Code().Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Kick_OnlyByHost()
    {
        var host = await TestPlayer.LoginAsync(_factory, "Хост");
        var a = await TestPlayer.LoginAsync(_factory, "Аня");
        var b = await TestPlayer.LoginAsync(_factory, "Боря");
        var lobby = await host.PostAsync("/api/v1/lobbies", new { });
        var id = lobby.Id("id");
        await a.PostAsync($"/api/v1/lobbies/{lobby.Str("code")}/join", new { });
        await b.PostAsync($"/api/v1/lobbies/{lobby.Str("code")}/join", new { });

        await a.PostAsync($"/api/v1/lobbies/{id}/kick", new { userId = b.Id }, HttpStatusCode.Forbidden);
        await host.PostAsync($"/api/v1/lobbies/{id}/kick", new { userId = b.Id }, HttpStatusCode.NoContent);

        var after = await host.GetAsync($"/api/v1/lobbies/{lobby.Str("code")}");
        after.GetProperty("members").EnumerateArray().Select(m => m.Id("userId")).Should().Equal(host.Id, a.Id);
    }

    [Fact]
    public async Task Join_TwelvePlayersMax()
    {
        var host = await TestPlayer.LoginAsync(_factory, "Хост");
        var code = (await host.PostAsync("/api/v1/lobbies", new { })).Str("code");
        for (var i = 1; i < 12; i++)
        {
            await (await TestPlayer.LoginAsync(_factory, $"Игрок {i}")).PostAsync($"/api/v1/lobbies/{code}/join", new { });
        }

        var extra = await TestPlayer.LoginAsync(_factory, "Лишний");
        (await extra.PostAsync($"/api/v1/lobbies/{code}/join", new { }, HttpStatusCode.Conflict)).Code().Should().Be("LOBBY_FULL");
        await extra.PostAsync($"/api/v1/lobbies/{code}/join", new { mode = "table" });
    }

    [Fact]
    public async Task InGame_OnlyTempoAndTimersCanChange()
    {
        var game = await GameHarness.StartAsync(_factory, players: 4);
        var current = await game.Host.GetAsync($"/api/v1/lobbies/{game.Code}");
        var settings = current.GetProperty("settings").Deserialize<LobbySettings>(GameJson.Options)!;

        (await game.Host.PutAsync($"/api/v1/lobbies/{game.LobbyId}/settings", settings with { Columns = 6 }, HttpStatusCode.Conflict))
            .Code().Should().Be("GAME_IN_PROGRESS");

        await game.Host.PutAsync($"/api/v1/lobbies/{game.LobbyId}/settings",
            settings with { Timers = new PhaseTimers { Mailbox = 120 } });

        var stored = await _factory.WithDbAsync(db => Task.FromResult(db.Games.Single(g => g.Id == game.GameId).Settings));
        GameJson.Deserialize<LobbySettings>(stored).Timers.Mailbox.Should().Be(120);

        // Раунды тоже можно менять: игра на 4 игроков — 5 раундов по правилам, уменьшаем до 2.
        await game.Host.PutAsync($"/api/v1/lobbies/{game.LobbyId}/settings", settings with { Rounds = 2 });
        (await game.Host.ViewAsync(game.GameId)).GetProperty("totalRounds").GetInt32().Should().Be(2);

        (await game.Host.PutAsync($"/api/v1/lobbies/{game.LobbyId}/settings", settings with { Rounds = 9 }, HttpStatusCode.BadRequest))
            .Code().Should().Be("VALIDATION");
    }
}
