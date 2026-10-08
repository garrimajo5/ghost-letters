using System.Text.Json;
using GhostLetters.Infrastructure.Games;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Api.Tests;

/// <summary>Целая партия живыми командами через API: от лобби до итогов, статистики, рейтинга и ачивок.</summary>
[Collection(DbCollection.Name)]
public sealed class EndToEndTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task CooperativeGame_DoesNotChangeRating()
    {
        var game = await GameHarness.StartAsync(_factory, players: 3, new LobbySettings { Rounds = 1 });
        var driver = new GameDriver(game);

        await driver.RunUntilAsync(p => p == "Finished");

        var history = await _factory.WithDbAsync(db => db.RatingHistoryRecords.AsNoTracking().Where(h => h.GameId == game.GameId).ToListAsync());
        history.Should().BeEmpty("в кооперативе нет соперника — рейтинг не меняется");
    }

    [Fact]
    public async Task CasualGame_CountsStats_ButNotRating()
    {
        var game = await GameHarness.StartAsync(_factory, players: 4, new LobbySettings { Rounds = 1, Ranked = false });
        var driver = new GameDriver(game);

        await driver.RunUntilAsync(p => p == "Finished");

        var history = await _factory.WithDbAsync(db => db.RatingHistoryRecords.CountAsync(h => h.GameId == game.GameId));
        history.Should().Be(0, "обычная партия рейтинг не меняет");
        var profile = await game.Host.GetAsync($"/api/v1/users/{game.Host.Id}/profile");
        profile.GetProperty("stats").GetProperty("games").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task SevenPlayers_FullGame_ResultsStatsRatingAchievements()
    {
        var game = await GameHarness.StartAsync(_factory, players: 7, new LobbySettings { Rounds = 1 });
        var driver = new GameDriver(game);

        await driver.RunUntilAsync(p => p == "AwardNomination");

        // Лайк во время наград попадает в профиль.
        await game.Players[2].CommandAsync(game.GameId, "Like", new { to = game.Players[3].Id, on = true });

        await driver.RunUntilAsync(p => p == "Finished");
        driver.Steps.Should().BeGreaterThan(40);

        var summary = await game.Host.GetAsync($"/api/v1/games/{game.GameId}/summary");
        var players = summary.GetProperty("players").EnumerateArray().ToList();
        players.Should().HaveCount(7);
        players.Select(x => x.Str("role")).Should().Contain(new[] { "Ghost", "Killer", "Accomplice", "Witness" });
        summary.GetProperty("truth").EnumerateArray().Select(x => x.GetInt32()).Should().Equal(0, 0, 0, 0);
        summary.GetProperty("letters").GetArrayLength().Should().Be(7);
        var result = summary.GetProperty("finale").GetProperty("result");
        result.GetProperty("correctRows").GetInt32().Should().Be(4, "все голосовали за нулевой столбец — истину");
        result.GetProperty("solved").GetBoolean().Should().BeTrue();

        var winners = players.Where(x => x.GetProperty("won").GetBoolean()).Select(x => x.Id("id")).ToHashSet();
        winners.Should().NotBeEmpty();

        foreach (var p in game.Players)
        {
            var profile = await p.GetAsync($"/api/v1/users/{p.Id}/profile");
            var stats = profile.GetProperty("stats");
            stats.GetProperty("games").GetInt32().Should().Be(1);
            stats.GetProperty("wins").GetInt32().Should().Be(winners.Contains(p.Id) ? 1 : 0);
        }

        // Рейтинг: у всех, кроме Подражателя/Шантажиста (их тут нет), есть запись истории, сумма изменений ~0.
        var history = await _factory.WithDbAsync(db => db.RatingHistoryRecords.AsNoTracking()
            .Where(h => h.GameId == game.GameId).ToListAsync());
        history.Should().HaveCount(7);
        history.Select(h => h.RatingAfter - h.Delta).Should().OnlyContain(r => r == 1000);
        history.Where(h => winners.Contains(h.UserId)).Should().OnlyContain(h => h.Delta > 0);

        // Ачивка: игрок 1 выдвинул игрока 2, за него проголосовали остальные.
        var nominee = await game.Players[1].GetAsync($"/api/v1/users/{game.Players[1].Id}/profile");
        var achievement = nominee.GetProperty("achievements").EnumerateArray().Single();
        achievement.Str("code").Should().Be(GameDriver.AwardCode);
        achievement.Str("title").Should().Be("Стальные яйца");

        var liked = await game.Players[3].GetAsync($"/api/v1/users/{game.Players[3].Id}/profile");
        liked.GetProperty("stats").GetProperty("likesReceived").GetInt32().Should().Be(1);

        var votes = await _factory.WithDbAsync(db => db.Votes.CountAsync(v => v.GameId == game.GameId));
        votes.Should().BeGreaterThanOrEqualTo(6 * 5);

        var board = await game.Host.GetAsync("/api/v1/leaderboard");
        board.EnumerateArray().Select(r => r.GetProperty("user").Id("id")).Should().Contain(game.Players.Select(p => p.Id));

        var mine = await game.Host.GetAsync("/api/v1/me/games?status=finished");
        var row = mine.EnumerateArray().Single(g => g.Id("gameId") == game.GameId);
        row.GetProperty("yourTurn").GetBoolean().Should().BeFalse();

        (await game.Host.GetAsync($"/api/v1/lobbies/{game.Code}")).Str("status").Should().Be("open");
    }

    [Fact]
    public async Task Summary_BeforeResult_IsNotAvailable_AndMyGamesShowsTurn()
    {
        var game = await GameHarness.StartAsync(_factory, players: 4);

        (await game.Host.GetAsync($"/api/v1/games/{game.GameId}/summary", System.Net.HttpStatusCode.Conflict))
            .Code().Should().Be("GAME_IN_PROGRESS");

        var mine = await game.Host.GetAsync("/api/v1/me/games?status=active");
        mine.EnumerateArray().Single(g => g.Id("gameId") == game.GameId).GetProperty("yourTurn").GetBoolean()
            .Should().BeTrue("в начале каждый должен посмотреть роль");
        await game.Host.GetAsync("/api/v1/me/games?status=weird", System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public void Elo_TeamDelta_IsSymmetricAndFavoursUnderdog()
    {
        GameRecorder.EloDelta(1000, 1000, Domain.Game.WinningSide.Detectives).Should().Be((16, -16));
        GameRecorder.EloDelta(1000, 1000, Domain.Game.WinningSide.Killer).Should().Be((-16, 16));
        GameRecorder.EloDelta(1000, 1000, Domain.Game.WinningSide.Blackmailer).Should().Be((0, 0));
        GameRecorder.EloDelta(1200, 1000, Domain.Game.WinningSide.Detectives).Detectives.Should().BeLessThan(16);
        GameRecorder.EloDelta(1000, 1200, Domain.Game.WinningSide.Detectives).Detectives.Should().BeGreaterThan(16);
    }
}
