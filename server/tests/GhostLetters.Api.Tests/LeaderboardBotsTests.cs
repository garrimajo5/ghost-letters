using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GhostLetters.Api.Tests;

[Collection(DbCollection.Name)]
public sealed class LeaderboardBotsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task CabinetReplacesLegacyDuplicates_BeforeLimit_WithoutMergingStatsOrHumans()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var name = "Бот " + Guid.NewGuid().ToString("N")[..10];
        var oldOnly = name + "2";
        var now = DateTimeOffset.UtcNow;
        try
        {
            await _factory.WithDbAsync(async db =>
            {
                for (var i = 0; i < ids.Length; i++)
                {
                    db.Users.Add(new User { Id = ids[i], Nickname = i < 4 ? name : oldOnly,
                        IsBot = i != 2 && i != 3, CreatedAt = now.AddDays(i), LastSeenAt = now });
                    db.Stats.Add(new UserStats { UserId = ids[i], Games = i + 1, Wins = i,
                        Rating = 10000 - i * 100 });
                }
                db.BotProfiles.Add(new BotProfile { UserId = ids[1] });
                return await db.SaveChangesAsync();
            });
            var rows = await _factory.WithServiceAsync<ProfileService, IReadOnlyList<LeaderboardRow>>(
                s => s.LeaderboardAsync(100, CancellationToken.None, bots: true));
            var own = rows.Where(r => ids.Contains(r.User.Id)).ToList();
            own.Select(r => r.User.Id).Should().BeEquivalentTo(new[] { ids[1], ids[2], ids[3], ids[4] });
            var cabinet = own.Single(r => r.User.Id == ids[1]);
            cabinet.Rating.Should().Be(9900);
            cabinet.Games.Should().Be(2);
            cabinet.Wins.Should().Be(1);
            var top = await _factory.WithServiceAsync<ProfileService, IReadOnlyList<LeaderboardRow>>(
                s => s.LeaderboardAsync(1, CancellationToken.None, bots: true));
            top.Single().User.Id.Should().Be(ids[1], "старый дубль с большим рейтингом исключён до лимита");
            var people = await _factory.WithServiceAsync<ProfileService, IReadOnlyList<LeaderboardRow>>(
                s => s.LeaderboardAsync(100, CancellationToken.None));
            people.Should().OnlyContain(r => !r.IsBot);
            people.Where(r => ids.Contains(r.User.Id)).Should().HaveCount(2, "одноимённых людей не объединяем");
            (await _factory.WithDbAsync(db => db.Users.CountAsync(u => ids.Contains(u.Id)))).Should().Be(6);

            // Новый профиль без игр не наследует рейтинг старого одноимённого бота.
            await _factory.WithDbAsync(db => db.Stats.Where(s => s.UserId == ids[1])
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Games, 0)));
            rows = await _factory.WithServiceAsync<ProfileService, IReadOnlyList<LeaderboardRow>>(
                s => s.LeaderboardAsync(100, CancellationToken.None, bots: true));
            rows.Should().NotContain(r => r.User.Id == ids[0] || r.User.Id == ids[1]);
        }
        finally
        {
            await _factory.WithDbAsync(db => db.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync());
        }
    }
}
