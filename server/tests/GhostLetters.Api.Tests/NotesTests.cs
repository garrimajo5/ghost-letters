using System.Net;

namespace GhostLetters.Api.Tests;

[Collection(DbCollection.Name)]
public sealed class NotesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Notes_ArePrivate_AndValidated()
    {
        var game = await GameHarness.StartAsync(_factory, players: 4);
        var (me, other, third) = (game.Players[0], game.Players[1], game.Players[2]);
        var url = $"/api/v1/games/{game.GameId}/notes";

        var saved = await me.PutAsync($"{url}/{other.Id}", new
        {
            suspicion = 2,
            body = "Слишком уверенно про нож",
            entries = new[] { new { round = 1, said = "отправил ключ" } },
        });
        saved.GetProperty("suspicion").GetInt32().Should().Be(2);
        saved.GetProperty("entries")[0].Str("said").Should().Be("отправил ключ");

        await me.PutAsync($"{url}/{other.Id}", new { suspicion = -1, body = "передумал" });
        var mine = await me.GetAsync(url);
        mine.GetArrayLength().Should().Be(1);
        mine[0].Str("body").Should().Be("передумал");

        (await third.GetAsync(url)).GetArrayLength().Should().Be(0);
        (await me.PutAsync($"{url}/{me.Id}", new { suspicion = 0 }, HttpStatusCode.BadRequest)).Code().Should().Be("VALIDATION");
        (await me.PutAsync($"{url}/{other.Id}", new { suspicion = 3 }, HttpStatusCode.BadRequest)).Code().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task Marks_ReplaceWholeList_OnlyBoardCards()
    {
        var game = await GameHarness.StartAsync(_factory, players: 4);
        var me = game.Players[0];
        var url = $"/api/v1/games/{game.GameId}/marks";
        var board = (await me.ViewAsync(game.GameId)).GetProperty("board");
        var a = board[0].GetProperty("cards")[0].GetString()!;
        var b = board[1].GetProperty("cards")[2].GetString()!;

        var both = await me.PutAsync(url, new[]
        {
            new { cardId = a, crosses = 2, checks = 0, believed = false },
            new { cardId = b, crosses = 0, checks = 1, believed = true },
        });
        both.GetArrayLength().Should().Be(2);

        var one = await me.PutAsync(url, new[] { new { cardId = b, crosses = 1, checks = 3, believed = true } });
        one.GetArrayLength().Should().Be(1);
        one[0].GetProperty("checks").GetInt32().Should().Be(3);

        (await game.Players[1].GetAsync(url)).GetArrayLength().Should().Be(0);
        (await me.PutAsync(url, new[] { new { cardId = "orig_9999", crosses = 0, checks = 0, believed = false } },
            HttpStatusCode.BadRequest)).Code().Should().Be("VALIDATION");
    }

    [Fact]
    public async Task Marks_KeepSources_WhoCheckedAndWhoseLetter()
    {
        var game = await GameHarness.StartAsync(_factory, players: 4);
        var me = game.Players[0];
        var other = game.Players[1];
        var url = $"/api/v1/games/{game.GameId}/marks";
        var board = (await me.ViewAsync(game.GameId)).GetProperty("board");
        var a = board[0].GetProperty("cards")[0].GetString()!;

        var saved = await me.PutAsync(url, new[]
        {
            new
            {
                cardId = a, crosses = 1, checks = 0, believed = false,
                sources = new { crossBy = new[] { other.Id }, checkBy = Array.Empty<Guid>(), claimedBy = other.Id },
            },
        });
        var sources = saved[0].GetProperty("sources");
        sources.GetProperty("crossBy")[0].GetGuid().Should().Be(other.Id);
        sources.GetProperty("claimedBy").GetGuid().Should().Be(other.Id);

        (await me.PutAsync(url, new[]
        {
            new { cardId = a, crosses = 0, checks = 0, believed = false, sources = new { crossBy = new[] { Guid.NewGuid() } } },
        }, HttpStatusCode.BadRequest)).Code().Should().Be("VALIDATION");
    }
}
