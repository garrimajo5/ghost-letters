using System.Net;
using System.Net.Http.Json;
using GhostLetters.Domain.Game;
using GhostLetters.Domain.Roles;
using GhostLetters.Infrastructure.Auth;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GhostLetters.Api.Tests;

/// <summary>
/// Надёжность под нагрузкой: бот из кабинета сидит во многих партиях сразу, партии кончаются одновременно,
/// одна сломанная партия не мешает остальным.
/// </summary>
[Collection(DbCollection.Name)]
public sealed class ReliabilityTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task SamePlayerFinishingSeveralGamesAtOnce_CountsEveryGame()
    {
        var bot = await TestPlayer.LoginAsync(_factory, "Общий");
        var ghost = await TestPlayer.LoginAsync(_factory, "Призрак");
        const int games = 8;

        await Task.WhenAll(Enumerable.Range(0, games).Select(_ => Task.Run(async () =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GhostLettersDbContext>();
            var recorder = scope.ServiceProvider.GetRequiredService<GameRecorder>();
            var state = new GameState
            {
                Settings = new GameSettings { Ranked = false },
                Players =
                [
                    new PlayerState { Id = bot.Id, Seat = 0, Role = Role.Detective },
                    new PlayerState { Id = ghost.Id, Seat = 1, Role = Role.Ghost },
                ],
                Result = new GameResult { Side = WinningSide.Detectives, Winners = [bot.Id, ghost.Id] },
            };

            await using var transaction = await db.Database.BeginTransactionAsync();
            await recorder.RecordAsync(new Game { Id = Guid.NewGuid() }, state, hadResult: false, Phase.Finished,
                DateTimeOffset.UtcNow, CancellationToken.None);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        })));

        var stats = await _factory.WithDbAsync(db => db.Stats.AsNoTracking().SingleAsync(s => s.UserId == bot.Id));
        stats.Games.Should().Be(games, "каждая партия засчитана, ни одна не затёрла другую");
        stats.Wins.Should().Be(games);
    }

    [Fact]
    public async Task BrokenGame_DoesNotStopBotsInOtherGames()
    {
        var host = await TestPlayer.LoginAsync(_factory, "Хост");
        var broken = await StartWithBotsAsync(host);
        var healthy = await StartWithBotsAsync(host);
        await _factory.WithDbAsync(db =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE games SET state = '{{\"Players\": 5}}'::jsonb WHERE id = {broken}"));
        try
        {
            var before = await VersionAsync(healthy);
            for (var i = 0; i < 5 && await VersionAsync(healthy) == before; i++)
            {
                await _factory.WithServiceAsync<BotService, int>(s => s.TickAsync(CancellationToken.None));
            }

            (await VersionAsync(healthy)).Should().BeGreaterThan(before, "боты в исправной партии ходят, хотя другая партия сломана");
        }
        finally
        {
            await _factory.WithDbAsync(db =>
                db.Database.ExecuteSqlInterpolatedAsync($"UPDATE games SET status = {GameStatuses.Finished} WHERE id = {broken}"));
        }
    }

    [Fact]
    public async Task BotMemory_IsReadOncePerGame_AndRefreshedLater()
    {
        var admin = await TestPlayer.LoginAsync(_factory, "Лида");
        _factory.Services.GetRequiredService<IConfiguration>()["Admin:UserIds"] = admin.Id.ToString();
        var spectra = BotSpectra.From(new BotPersonality { Memory = 1, Variability = 0 });
        var botId = (await admin.PostAsync("/api/v1/admin/bots", new { nickname = "Помнящий", spectra })).Id("id");
        var state = GameEngine.Create(Guid.NewGuid(), [botId, admin.Id], new GameSettings(),
            Enumerable.Range(1, 300).Select(i => $"c{i:000}").ToList(), 7);

        await AddFinishedGameAsync(botId, admin.Id);
        (await MindAsync(state, botId)).History[admin.Id].Games.Should().Be(1);

        // Новая законченная партия не перечитывается на каждом такте...
        await AddFinishedGameAsync(botId, admin.Id);
        (await MindAsync(state, botId)).History[admin.Id].Games.Should().Be(1);

        // ...но через 10 минут память обновляется.
        _factory.Time.Advance(BotService.MemoryCacheLifetime + TimeSpan.FromSeconds(1));
        (await MindAsync(state, botId)).History[admin.Id].Games.Should().Be(2);
    }

    [Fact]
    public async Task LinkCode_EnteredOnTwoDevicesAtOnce_OneWins_OtherGetsValidationError()
    {
        var owner = await TestPlayer.LoginAsync(_factory, "Ватсон");
        var code = await owner.PostAsync("/api/v1/auth/link-code", null);
        using var client = _factory.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            client.PostAsJsonAsync("/api/v1/auth/link", new LinkLoginRequest($"device-{Guid.NewGuid()}", code.Str("code")))));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, "код одноразовый");
        responses.Where(r => r.StatusCode != HttpStatusCode.OK).Should()
            .OnlyContain(r => r.StatusCode == HttpStatusCode.BadRequest, "одновременный вход — понятная ошибка, а не 500");
    }

    private async Task<Guid> StartWithBotsAsync(TestPlayer host)
    {
        var lobby = await host.PostAsync("/api/v1/lobbies", new { settings = new LobbySettings { Rounds = 1 } });
        var id = lobby.Id("id");
        for (var i = 0; i < 4; i++)
        {
            await host.PostAsync($"/api/v1/lobbies/{id}/bots", null);
        }

        return (await host.PostAsync($"/api/v1/lobbies/{id}/start", null)).Id("gameId");
    }

    private Task<int> VersionAsync(Guid gameId) =>
        _factory.WithDbAsync(db => db.Games.AsNoTracking().Where(g => g.Id == gameId).Select(g => g.Version).SingleAsync());

    private async Task<BotMind> MindAsync(GameState state, Guid botId) =>
        (await _factory.WithServiceAsync<BotService, BotMind?>(s => s.MindAsync(state, botId, CancellationToken.None)))!;

    private Task<int> AddFinishedGameAsync(Guid botId, Guid playerId) => _factory.WithDbAsync(db =>
    {
        var id = Guid.NewGuid();
        var at = _factory.Time.GetUtcNow().AddDays(-1);
        db.Games.Add(new Game { Id = id, StartedAt = at, FinishedAt = at, Status = GameStatuses.Finished });
        db.GamePlayers.Add(new GamePlayer { GameId = id, UserId = botId, Seat = 0, Role = "Detective" });
        db.GamePlayers.Add(new GamePlayer { GameId = id, UserId = playerId, Seat = 1, Role = "Detective" });
        return db.SaveChangesAsync();
    });
}
