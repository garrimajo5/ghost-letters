using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Api.Tests;

[Collection(DbCollection.Name)]
public sealed class BotDiscussionFlowTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task FinalRound_BotsExchangeQuestionsAndAnswers_AndThenStop()
    {
        var game = await GameHarness.StartAsync(_factory, 5, new LobbySettings { Rounds = 1 });
        await new GameDriver(game).RunUntilAsync(p => p == "Discussion");
        var state = await _factory.WithDbAsync(async db => GameStore.Read(await db.Games.AsNoTracking().SingleAsync(g => g.Id == game.GameId)));
        state.EffectiveDiscussion.Should().Be(DiscussionMode.FreeChat);
        var ids = state.Investigators.OrderBy(p => p.Seat).Take(2).Select(p => p.Id).ToList();
        try
        {
            await _factory.WithDbAsync(async db =>
            {
                await db.Users.Where(u => ids.Contains(u.Id)).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsBot, true));
                foreach (var id in ids) db.BotProfiles.Add(new BotProfile { UserId = id });
                return await db.SaveChangesAsync();
            });
            // Human-paced long explanations can take 45 seconds each; allow
            // both bots their bounded six turns rather than truncating speech.
            for (var i = 0; i < 60; i++)
            {
                _factory.Time.Advance(TimeSpan.FromSeconds(15));
                await _factory.WithServiceAsync<BotService, int>(s => s.TickAsync(CancellationToken.None));
            }
            var lines = await _factory.WithDbAsync(db => db.ChatMessages.AsNoTracking()
                .Where(m => m.GameId == game.GameId && m.AuthorId != null && ids.Contains(m.AuthorId.Value))
                .OrderBy(m => m.CreatedAt).ToListAsync());
            lines.Should().Contain(m => m.Text!.Contains("по всем открытым уликам"));
            lines.Should().Contain(m => m.Text!.Contains("будешь голосовать?"));
            lines.Should().Contain(m => m.Text!.Contains("отвечаю про голосование"));
            lines.GroupBy(m => m.AuthorId).Should().OnlyContain(g => g.Count() <= BotDiscussion.MaxMessages);
            lines.Should().OnlyContain(m => m.Channel == ChatChannels.Public && m.Text!.Length <= ChatService.MaxTextLength);
            var count = lines.Count;
            _factory.Time.Advance(TimeSpan.FromMinutes(3));
            await _factory.WithServiceAsync<BotService, int>(s => s.TickAsync(CancellationToken.None));
            (await _factory.WithDbAsync(db => db.ChatMessages.CountAsync(m => m.GameId == game.GameId && m.AuthorId != null && ids.Contains(m.AuthorId.Value))))
                .Should().Be(count);
            (await game.StateAsync()).Phase.Should().Be("Discussion", "боты не завершают обсуждение без готовности людей");
        }
        finally
        {
            await _factory.WithDbAsync(async db =>
            {
                await db.BotProfiles.Where(b => ids.Contains(b.UserId)).ExecuteDeleteAsync();
                return await db.Users.Where(u => ids.Contains(u.Id)).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsBot, false));
            });
        }
    }
}
