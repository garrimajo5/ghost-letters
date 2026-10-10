using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Api.Tests;

[Collection(DbCollection.Name)]
public sealed class BotNightConversationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task KillerWaitsAndDiscussesPrivately_HumanProposalReachesMind()
    {
        var game = await GameHarness.StartAsync(_factory, 7);
        await new GameDriver(game).RunUntilAsync(p => p == "Night");
        var killer = await game.WithRoleAsync("Killer");
        var accomplice = await game.WithRoleAsync("Accomplice");
        var detective = await game.WithRoleAsync("Detective");
        await _factory.WithDbAsync(async db =>
        {
            await db.Users.Where(u => u.Id == killer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsBot, true));
            return await db.SaveChangesAsync();
        });
        try
        {
            await _factory.WithServiceAsync<BotService, int>(s => s.TickAsync(CancellationToken.None));
            (await game.StateAsync()).Phase.Should().Be("Night");
            _factory.Time.Advance(TimeSpan.FromSeconds(10));
            await _factory.WithServiceAsync<BotService, int>(s => s.TickAsync(CancellationToken.None));
            (await game.StateAsync()).Phase.Should().Be("Night");
            var team = await accomplice.GetAsync($"/api/v1/games/{game.GameId}/chat?channel=killer_team");
            team.GetArrayLength().Should().Be(1);
            (await detective.GetAsync($"/api/v1/games/{game.GameId}/chat")).GetArrayLength().Should().Be(0);
            var board = (await accomplice.ViewAsync(game.GameId)).GetProperty("board")[0].GetProperty("cards");
            var source = board[0].GetString()!;
            var target = board[1].GetString()!;
            await accomplice.PostAsync($"/api/v1/games/{game.GameId}/chat", new {
                channel = "killer_team", text = "Предлагаю эту связь", cardIds = new[] { source, target },
                cardNotes = new[] { "улика", "думаю, эта:0" } });
            var state = await _factory.WithDbAsync(async db => GameStore.Read(await db.Games.AsNoTracking().SingleAsync(g => g.Id == game.GameId)));
            var mind = await _factory.WithServiceAsync<BotService, GhostLetters.Infrastructure.Bots.BotMind?>(s => s.MindAsync(state, killer.Id, CancellationToken.None));
            mind!.Opinions.Should().Contain(o => o.Author == accomplice.Id && o.CardId == target && o.SourceCard == source);
        }
        finally
        {
            await _factory.WithDbAsync(db => db.Users.Where(u => u.Id == killer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsBot, false)));
        }
    }
}
