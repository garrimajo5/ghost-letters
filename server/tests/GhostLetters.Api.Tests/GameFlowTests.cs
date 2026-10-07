using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Games;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GhostLetters.Api.Tests;

[Collection(DbCollection.Name)]
public sealed class GameFlowTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Views_ArePrivate_EachPlayerSeesOnlyOwnRoleAndGhost()
    {
        var game = await GameHarness.StartAsync(_factory, players: 5);

        foreach (var p in game.Players)
        {
            var view = await p.ViewAsync(game.GameId);
            view.Str("phase").Should().Be("RoleReveal");
            var role = view.GetProperty("me").Str("role");
            var known = view.GetProperty("players").EnumerateArray()
                .Where(x => x.GetProperty("knownRole").ValueKind != JsonValueKind.Null)
                .Select(x => x.Id("id")).ToList();

            known.Should().Contain(p.Id);
            if (role == "Detective")
            {
                known.Should().HaveCount(2, "детектив знает только себя и Призрака");
            }

            view.GetProperty("truth").ValueKind.Should().Be(JsonValueKind.Null);
        }

        var stranger = await TestPlayer.LoginAsync(_factory, "Чужой");
        (await stranger.GetAsync($"/api/v1/games/{game.GameId}/view", HttpStatusCode.Forbidden)).Code().Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Commands_AdvanceGame_WithVersionAndIdempotency()
    {
        var game = await GameHarness.StartAsync(_factory, players: 4);
        var gid = game.GameId;
        var first = game.Players[0];

        var r1 = await first.CommandAsync(gid, "AckRole", expectedVersion: 0, clientCommandId: "ack-1");
        r1.GetProperty("version").GetInt32().Should().Be(1);
        r1.GetProperty("duplicate").GetBoolean().Should().BeFalse();

        var repeat = await first.CommandAsync(gid, "AckRole", expectedVersion: 0, clientCommandId: "ack-1");
        repeat.GetProperty("version").GetInt32().Should().Be(1);
        repeat.GetProperty("duplicate").GetBoolean().Should().BeTrue();

        (await game.Players[1].CommandAsync(gid, "AckRole", expectedVersion: 0, expected: HttpStatusCode.Conflict))
            .Code().Should().Be("VERSION_CONFLICT");

        (await first.CommandAsync(gid, "RevealHints", new { cardIds = Array.Empty<string>() }, expected: HttpStatusCode.Conflict))
            .Code().Should().Be("PHASE_MISMATCH");
        (await first.CommandAsync(gid, "NoSuchCommand", expected: HttpStatusCode.BadRequest)).Code().Should().Be("VALIDATION");

        foreach (var p in game.Players.Skip(1))
        {
            await p.CommandAsync(gid, "AckRole");
        }

        (await first.ViewAsync(gid)).Str("phase").Should().Be("Night");

        var killer = await game.WithRoleAsync("Killer");
        var detective = await game.WithRoleAsync("Detective");
        (await detective.CommandAsync(gid, "ChooseTruth", new { columns = new[] { 0, 1, 2, 3 } }, expected: HttpStatusCode.Forbidden))
            .Code().Should().Be("NOT_ALLOWED");
        await killer.CommandAsync(gid, "ChooseTruth", new { columns = new[] { 0, 1, 2, 3 } });

        var view = await detective.ViewAsync(gid);
        view.Str("phase").Should().Be("FirstClue");
        view.GetProperty("me").GetProperty("hand").GetArrayLength().Should().Be(5);

        // Событие выбора улик — личное для Убийцы.
        var killerEvents = await killer.GetAsync($"/api/v1/games/{gid}/events");
        var detectiveEvents = await detective.GetAsync($"/api/v1/games/{gid}/events");
        killerEvents.EnumerateArray().Select(e => e.Str("type")).Should().Contain("TruthChosen");
        detectiveEvents.EnumerateArray().Select(e => e.Str("type")).Should().NotContain("TruthChosen").And.Contain("HandsDealt");
    }

    [Fact]
    public async Task TableScreen_SeesGame_ButCannotAct()
    {
        var game = await GameHarness.StartAsync(_factory, players: 4);
        var table = await TestPlayer.LoginAsync(_factory, "Экран");
        await table.PostAsync($"/api/v1/lobbies/{game.Code}/join", new { mode = "table" });

        var view = await table.ViewAsync(game.GameId);
        view.GetProperty("me").ValueKind.Should().Be(JsonValueKind.Null);
        view.GetProperty("allowedCommands").GetArrayLength().Should().Be(0);
        (await table.CommandAsync(game.GameId, "AckRole", expected: HttpStatusCode.Forbidden)).Code().Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Timer_MakesMovesWhenDeadlinePasses()
    {
        var game = await GameHarness.StartAsync(_factory, players: 4);

        // База общая для всех тестов, поэтому проверяем свою партию, а не число сдвинутых.
        await game.ExpireAsync(TimeSpan.FromSeconds(5));
        (await game.StateAsync()).Phase.Should().Be("RoleReveal", "до дедлайна ещё далеко");
        (await game.ExpireAsync(TimeSpan.FromSeconds(30))).Should().BeGreaterThanOrEqualTo(1);

        var (phase, version, _) = await game.StateAsync();
        phase.Should().Be("Night");
        version.Should().Be(1);
        var events = await game.Host.GetAsync($"/api/v1/games/{game.GameId}/events");
        events.EnumerateArray().Select(e => e.Str("type")).Should().Contain("Timeout");
    }

    [Fact]
    public async Task WholeGame_OnTimeouts_FinishesAndReopensLobby()
    {
        var game = await GameHarness.StartAsync(_factory, players: 7, new LobbySettings { Rounds = 1 });

        for (var i = 0; i < 300 && (await game.StateAsync()).Status == "active"; i++)
        {
            await game.ExpireAsync();
        }

        var (phase, _, status) = await game.StateAsync();
        status.Should().Be("finished");
        phase.Should().Be("Finished");

        var row = await _factory.WithDbAsync(db => db.Games.AsNoTracking().SingleAsync(g => g.Id == game.GameId));
        row.Result.Should().NotBeNull();
        row.PhaseDeadline.Should().BeNull();

        var lobby = await game.Host.GetAsync($"/api/v1/lobbies/{game.Code}");
        lobby.Str("status").Should().Be("open");

        // После итогов всем видны роли и истина.
        var view = await game.Players[3].ViewAsync(game.GameId);
        view.GetProperty("truth").GetArrayLength().Should().Be(4);
        view.GetProperty("finale").GetProperty("result").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task StateSnapshot_SurvivesJsonRoundTrip_ThroughWholeGame()
    {
        var ids = Enumerable.Range(1, 10).Select(_ => Guid.NewGuid()).ToList();
        var deck = Enumerable.Range(1, 300).Select(i => $"orig_{i:0000}").ToList();
        var state = GameEngine.Create(Guid.NewGuid(), ids,
            new GameSettings { Rounds = 1, Roles = new Domain.Roles.RoleOptions(UseBlackmailer: true) }, deck, 7);

        for (var i = 0; i < 400 && state.Phase != Phase.Finished; i++)
        {
            var json = GameJson.Serialize(state);
            var copy = GameJson.Deserialize<GameState>(json);
            GameJson.Serialize(copy).Should().Be(json);

            // Дальше играем копией: если что-то не сериализуется, партия разойдётся.
            state = copy;
            GameEngine.Timeout(state);
        }

        state.Phase.Should().Be(Phase.Finished);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SignalR_SubscriberReceivesOwnViewAfterOthersMove()
    {
        var game = await GameHarness.StartAsync(_factory, players: 4);
        var watcher = game.Players[1];
        await using var connection = Connect(watcher.Token);
        var views = Channel.CreateUnbounded<JsonElement>();
        connection.On<JsonElement>("GameView", v => views.Writer.TryWrite(v));
        await connection.StartAsync();

        var snapshot = await connection.InvokeAsync<JsonElement>("SubscribeGame", game.GameId);
        snapshot.GetProperty("view").GetProperty("me").Id("id").Should().Be(watcher.Id);

        await using var actor = Connect(game.Players[2].Token);
        await actor.StartAsync();
        var result = await actor.InvokeAsync<JsonElement>("Command", game.GameId, "AckRole", null, null, "hub-1");
        result.GetProperty("version").GetInt32().Should().Be(1);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pushed = await views.Reader.ReadAsync(timeout.Token);
        pushed.GetProperty("view").GetProperty("version").GetInt32().Should().Be(1);
        pushed.GetProperty("view").GetProperty("me").Id("id").Should().Be(watcher.Id);
        pushed.GetProperty("deadline").ValueKind.Should().Be(JsonValueKind.String);
        snapshot.GetProperty("roster").GetArrayLength().Should().Be(4);
        snapshot.GetProperty("roster")[0].Str("nickname").Should().Be("Игрок 1");

        var payload = JsonSerializer.SerializeToElement(new { columns = new[] { 0, 0, 0, 0 } });
        var error = async () => await actor.InvokeAsync<JsonElement>("Command", game.GameId, "ChooseTruth", payload, null, null);
        (await error.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain("PHASE_MISMATCH");
    }

    private HubConnection Connect(string token) => new HubConnectionBuilder()
        .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/play"), o =>
        {
            o.Transports = HttpTransportType.LongPolling;
            o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            o.AccessTokenProvider = () => Task.FromResult<string?>(token);
        })
        .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
        .Build();
}
