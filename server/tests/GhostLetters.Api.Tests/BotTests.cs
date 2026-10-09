using System.Net;
using GhostLetters.Infrastructure.Games;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Api.Tests;

[Collection(DbCollection.Name)]
public sealed class BotTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Host_AddsBots_OnlyHost_BotsAreReady()
    {
        var host = await TestPlayer.LoginAsync(_factory, "Хост");
        var guest = await TestPlayer.LoginAsync(_factory, "Гость");
        var lobby = await host.PostAsync("/api/v1/lobbies", new { });
        var id = lobby.Id("id");
        await guest.PostAsync($"/api/v1/lobbies/{lobby.Str("code")}/join", new { });

        (await guest.PostAsync($"/api/v1/lobbies/{id}/bots", null, HttpStatusCode.Forbidden)).Code().Should().Be("FORBIDDEN");
        await host.PostAsync($"/api/v1/lobbies/{id}/bots", null);
        var after = await host.PostAsync($"/api/v1/lobbies/{id}/bots", null);

        var bots = after.GetProperty("members").EnumerateArray().Where(m => m.GetProperty("isBot").GetBoolean()).ToList();
        bots.Should().HaveCount(2);
        bots.Should().OnlyContain(b => b.GetProperty("isReady").GetBoolean());
        bots.Select(b => b.Str("nickname")).Should().OnlyHaveUniqueItems().And.OnlyContain(n => n.StartsWith("Бот"));
        after.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("seat").GetInt32()).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public async Task Bots_OneNameIsOnePlayer_AcrossLobbies()
    {
        var host = await TestPlayer.LoginAsync(_factory, "Хост");
        for (var lobbyNo = 0; lobbyNo < 2; lobbyNo++)
        {
            var lobby = await host.PostAsync("/api/v1/lobbies", new { settings = new LobbySettings() });
            await host.PostAsync($"/api/v1/lobbies/{lobby.Id("id")}/bots", null);
            await host.PostAsync($"/api/v1/lobbies/{lobby.Id("id")}/bots", null);
        }

        // Одно имя — один бот: во втором лобби те же боты, а не новые одноимённые игроки.
        var duplicates = await _factory.WithDbAsync(db => db.Users.Where(u => u.IsBot)
            .GroupBy(u => u.Nickname).Where(g => g.Count() > 1).Select(g => g.Key).ToListAsync());
        duplicates.Should().BeEmpty();
    }

    [Fact]
    public async Task OneHumanAndBots_PlayWholeGame()
    {
        var host = await TestPlayer.LoginAsync(_factory, "Хост");
        var lobby = await host.PostAsync("/api/v1/lobbies", new { settings = new LobbySettings { Rounds = 1 } });
        var id = lobby.Id("id");
        for (var i = 0; i < 6; i++)
        {
            await host.PostAsync($"/api/v1/lobbies/{id}/bots", null);
        }

        var gameId = (await host.PostAsync($"/api/v1/lobbies/{id}/start", null)).Id("gameId");
        var game = GameHarness.Existing(_factory, [host], id, lobby.Str("code"), gameId);
        var driver = new GameDriver(game);
        var botMoves = 0;
        var deadlines = new HashSet<DateTimeOffset?>();

        for (var i = 0; i < 2000 && (await game.StateAsync()).Status == "active"; i++)
        {
            deadlines.Add(await _factory.WithDbAsync(db => db.Games.AsNoTracking().Where(g => g.Id == gameId).Select(g => g.PhaseDeadline).SingleAsync()));
            var moved = await _factory.WithServiceAsync<BotService, int>(s => s.TickAsync(CancellationToken.None));
            botMoves += moved;
            if (moved == 0 && !await driver.StepAsync())
            {
                // Ни боты, ни человек не ходят — значит ждём таймер (например, ход Призрака-человека уже сделан).
                await game.ExpireAsync();
            }
        }

        (await game.StateAsync()).Status.Should().Be("finished");
        deadlines.Should().Equal(new DateTimeOffset?[] { null }, "один человек с ботами — без таймеров, боты ждут");
        var reopened = await host.GetAsync($"/api/v1/lobbies/{lobby.Str("code")}");
        reopened.GetProperty("members").EnumerateArray().Where(m => m.GetProperty("isBot").GetBoolean())
            .Should().OnlyContain(m => m.GetProperty("isReady").GetBoolean(), "боты готовы к реваншу");
        botMoves.Should().BeGreaterThan(30);

        // Рейтинговая партия с ботами меняет рейтинг всем, ботам тоже; статистика человека учтена.
        var history = await _factory.WithDbAsync(db => db.RatingHistoryRecords.AsNoTracking().Where(h => h.GameId == gameId).ToListAsync());
        history.Should().NotBeEmpty();
        history.Should().Contain(h => h.UserId == host.Id);
        var profile = await host.GetAsync($"/api/v1/users/{host.Id}/profile");
        profile.GetProperty("stats").GetProperty("games").GetInt32().Should().Be(1);

        var board = await host.GetAsync("/api/v1/leaderboard?limit=100");
        board.EnumerateArray().Select(r => r.GetProperty("user").Str("nickname")).Should().NotContain(n => n.StartsWith("Бот"));

        // Галочка «показать ботов»: боты в таблице, с пометкой.
        var withBots = await host.GetAsync("/api/v1/leaderboard?limit=100&bots=true");
        withBots.EnumerateArray().Where(r => r.GetProperty("isBot").GetBoolean())
            .Select(r => r.GetProperty("user").Str("nickname")).Should().NotBeEmpty().And.OnlyContain(n => n.StartsWith("Бот"));
    }

    [Fact]
    public void BotPlayer_PicksValidMovesForEveryPhase()
    {
        var ids = Enumerable.Range(1, 8).Select(_ => Guid.NewGuid()).ToList();
        var deck = Enumerable.Range(1, 300).Select(i => $"orig_{i:0000}").ToList();
        var state = Domain.Game.GameEngine.Create(Guid.NewGuid(), ids,
            new Domain.Game.GameSettings { Rounds = 1, Roles = new Domain.Roles.RoleOptions(UseBlackmailer: true) }, deck, 11);
        var rng = new Random(5);

        for (var i = 0; i < 2000 && state.Phase != Domain.Game.Phase.Finished; i++)
        {
            var moved = false;
            foreach (var p in state.Players)
            {
                var command = BotPlayer.Decide(Domain.Game.GameProjection.For(state, p.Id), rng);
                if (command is null)
                {
                    continue;
                }

                try
                {
                    Domain.Game.GameEngine.Execute(state, p.Id, command);
                }
                catch (Domain.Game.GameRuleException) when (command is Domain.Game.HuntPick hunt)
                {
                    Domain.Game.GameEngine.Execute(state, p.Id, hunt with { Guess = Domain.Roles.Role.Expert });
                }

                moved = true;
                break;
            }

            moved.Should().BeTrue($"в фазе {state.Phase} кто-то из ботов должен ходить");
        }

        state.Phase.Should().Be(Domain.Game.Phase.Finished);
    }
}
