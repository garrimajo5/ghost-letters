using System.Net;
using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Games;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Api.Tests;

[Collection(DbCollection.Name)]
public sealed class GhostDebriefFlowTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task GhostSpeaksOnlyAfterResult_WithheldLettersAllowedThen_AndNoDuplicateReport()
    {
        var game = await GameHarness.StartAsync(_factory, 4, new LobbySettings { Rounds = 1 });
        await new GameDriver(game).RunUntilAsync(p => p == "Discussion");
        var ghost = await game.WithRoleAsync("Ghost");
        var detective = await game.WithRoleAsync("Detective");
        string hidden = "";
        await _factory.WithDbAsync(async db => {
            var row = await db.Games.SingleAsync(g => g.Id == game.GameId);
            var state = GameStore.Read(row);
            hidden = state.Player(detective.Id).Hand[0];
            state.Letters.Add(new() { From = detective.Id, Round = 1, CardId = hidden, Revealed = false });
            row.State = GameJson.Serialize(state);
            return await db.SaveChangesAsync();
        });
        var chat = $"/api/v1/games/{game.GameId}/chat";
        await ghost.PostAsync(chat, new { text = "Не открыл", cardIds = new[] { hidden } }, HttpStatusCode.Forbidden);
        await _factory.WithDbAsync(async db => {
            var row = await db.Games.SingleAsync(g => g.Id == game.GameId);
            var state = GameStore.Read(row);
            state.Result = new(); state.Phase = Phase.AwardNomination; state.Done.Clear();
            row.State = GameJson.Serialize(state); row.Phase = state.Phase.ToString();
            await db.Users.Where(u => u.Id == ghost.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsBot, true));
            return await db.SaveChangesAsync();
        });
        try
        {
            await _factory.WithServiceAsync<BotService, int>(s => s.TickAsync(default));
            // Awards may be skipped while the ghost is still explaining earlier letters.
            await _factory.WithDbAsync(async db => {
                var row = await db.Games.SingleAsync(g => g.Id == game.GameId);
                var state = GameStore.Read(row); state.Phase = Phase.Finished;
                row.State = GameJson.Serialize(state); row.Phase = "Finished";
                row.Status = "finished"; row.FinishedAt = _factory.Time.GetUtcNow();
                return await db.SaveChangesAsync();
            });
            for (var i = 0; i < 20; i++) {
                _factory.Time.Advance(TimeSpan.FromMinutes(1));
                await _factory.WithServiceAsync<BotService, int>(s => s.TickAsync(default));
            }
            var reports = await _factory.WithDbAsync(db => db.ChatMessages.AsNoTracking()
                .Where(m => m.GameId == game.GameId && m.AuthorId == ghost.Id).ToListAsync());
            reports.Should().Contain(m => m.CardIds.Contains(hidden) && m.Text!.Contains("закрытым"));
            reports.Select(m => (m.Text, Cards: string.Join(",", m.CardIds))).Should().OnlyHaveUniqueItems();
            var before = reports.Count;
            _factory.Time.Advance(TimeSpan.FromMinutes(1));
            await _factory.WithServiceAsync<BotService, int>(s => s.TickAsync(default));
            (await _factory.WithDbAsync(db => db.ChatMessages.CountAsync(m => m.GameId == game.GameId && m.AuthorId == ghost.Id)))
                .Should().Be(before);
        }
        finally {
            await _factory.WithDbAsync(db => db.Users.Where(u => u.Id == ghost.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsBot, false)));
        }
    }

    [Fact]
    public async Task SavedBluffCanBePublished_WithoutReadingOtherPlayersPrivateClaims()
    {
        var game = await GameHarness.StartAsync(_factory, 4, new LobbySettings { Rounds = 1 });
        await new GameDriver(game).RunUntilAsync(p => p == "Discussion");
        var player = await game.WithRoleAsync("Detective");
        var other = await game.WithRoleAsync("Killer");
        var state = await _factory.WithDbAsync(async db => GameStore.Read(await db.Games.AsNoTracking().SingleAsync(g => g.Id == game.GameId)));
        var letter = state.Letters.First(l => l.From == player.Id).CardId;
        var bluff = state.Deck[0];
        var chat = $"/api/v1/games/{game.GameId}/chat";
        await player.PostAsync(chat, new { text = "Моя версия", cardIds = new[] { bluff } }, HttpStatusCode.BadRequest);
        await player.PutAsync($"/api/v1/games/{game.GameId}/marks", new[] {
            new { cardId = letter, crosses = 0, checks = 0, believed = false, sources = new { claim = bluff } }
        });
        (await other.GetAsync($"/api/v1/games/{game.GameId}/marks")).GetArrayLength().Should().Be(0);
        await other.PostAsync(chat, new { text = "Не моя заметка", cardIds = new[] { bluff } }, HttpStatusCode.BadRequest);
        var sent = await player.PostAsync(chat, new { text = "Моя версия", cardIds = new[] { bluff } });
        sent.GetProperty("cardIds")[0].GetString().Should().Be(bluff);
        await player.PutAsync($"/api/v1/games/{game.GameId}/marks", new[] {
            new { cardId = letter, crosses = 0, checks = 0, believed = false, sources = new { claim = "orig_9999" } }
        }, HttpStatusCode.BadRequest);
    }
}
