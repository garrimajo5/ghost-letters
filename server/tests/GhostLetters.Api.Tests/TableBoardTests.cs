using System.Net;

namespace GhostLetters.Api.Tests;

/// <summary>Доска улик через REST: пост меняет проекцию у всех, Призраку писать нельзя.</summary>
[Collection(DbCollection.Name)]
public sealed class TableBoardTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task TablePost_ThreadFromHint_SeenByOthers_GhostRejected()
    {
        var game = await GameHarness.StartAsync(_factory, 4, new { discussion = "FreeChat" });
        await new GameDriver(game).RunUntilAsync(p => p == "Discussion");
        var ghost = await game.WithRoleAsync("Ghost");
        var author = game.Players.First(p => p != ghost);
        var reader = game.Players.First(p => p != ghost && p != author);
        var view = await author.ViewAsync(game.GameId);
        var hint = view.GetProperty("hints").EnumerateArray().SelectMany(h => h.GetProperty("cards").EnumerateArray())
            .Select(c => c.GetString()!).Last();
        var target = view.GetProperty("board")[1].GetProperty("cards")[2].GetString()!;
        view.GetProperty("table").GetProperty("canPost").GetBoolean().Should().BeTrue();
        view.GetProperty("allowedCommands").EnumerateArray().Select(c => c.GetString()).Should().NotContain("TablePost");

        var ops = new object[]
        {
            new { kind = "Link", sourceKind = "Hint", source = hint, target, stance = "For", reason = "цвет" },
            new { kind = "Pin", target },
        };
        await author.CommandAsync(game.GameId, "TablePost", new { ops });

        var table = (await reader.ViewAsync(game.GameId)).GetProperty("table");
        var thread = table.GetProperty("threads").EnumerateArray().Should().ContainSingle().Subject;
        thread.Id("author").Should().Be(author.Id);
        thread.Str("stance").Should().Be("For");
        thread.Str("reason").Should().Be("цвет");
        table.GetProperty("pins")[0].GetProperty("row").GetInt32().Should().Be(1);

        var byGhost = await ghost.CommandAsync(game.GameId, "TablePost", new { ops }, expected: HttpStatusCode.Forbidden);
        byGhost.Code().Should().Be("NOT_ALLOWED");
        (await ghost.ViewAsync(game.GameId)).GetProperty("table").GetProperty("threads").GetArrayLength().Should().Be(1, "Призрак доску видит");
    }

    [Fact]
    public async Task TablePost_BadOp_IsValidationError()
    {
        var game = await GameHarness.StartAsync(_factory, 4, new { discussion = "FreeChat" });
        await new GameDriver(game).RunUntilAsync(p => p == "Discussion");
        var ghost = await game.WithRoleAsync("Ghost");
        var author = game.Players.First(p => p != ghost);

        var response = await author.CommandAsync(game.GameId, "TablePost",
            new { ops = new object[] { new { kind = "Pin", target = "not-on-board" } } }, expected: HttpStatusCode.BadRequest);
        response.Code().Should().Be("VALIDATION");
        var empty = await author.CommandAsync(game.GameId, "TablePost", new { ops = Array.Empty<object>() }, expected: HttpStatusCode.BadRequest);
        empty.Code().Should().Be("VALIDATION");
    }
}
