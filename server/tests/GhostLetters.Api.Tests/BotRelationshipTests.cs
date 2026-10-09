using System.Net;
using GhostLetters.Domain.Game;
using GhostLetters.Infrastructure.Bots;
using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Persistence;
using GhostLetters.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GhostLetters.Api.Tests;

public sealed class BotAffinityTests
{
    private static Dictionary<string, double> Observe(BotSocialTraits s, double activity = 1, double own = 1, double? agreement = 1,
        double skill = .3, bool thanked = true, double? honesty = 1, int games = 5) =>
        BotAffinity.Observe(s, activity, own, agreement, skill, thanked, honesty, games);

    [Fact]
    public void ExpectationsAreIndependentOfOwnStyle_AndSimilarityCanBeInverted()
    {
        var s = new BotSocialTraits { ExpectedActivity = 1, ExpectedAgreement = 1, Similarity = 1 };
        Observe(s, own: 0)["expectations"].Should().Be(25);
        Observe(s, own: 0)["similarity"].Should().Be(-15);
        Observe(s with { Similarity = 0 }, own: 0)["similarity"].Should().Be(15);
        Observe(s, activity: 0, agreement: 0)["expectations"].Should().Be(-25);
    }

    [Fact]
    public void SkillCanCauseAdmirationOrEnvy_AndNoEvidenceDoesNotMeanDishonesty()
    {
        Observe(new() { SkillRespect = 1 })["skill"].Should().BePositive();
        Observe(new() { SkillRespect = 0 })["skill"].Should().BeNegative();
        var blank = Observe(new(), agreement: null, thanked: false, honesty: null);
        blank["honesty"].Should().Be(0);
        blank["reciprocity"].Should().Be(0);
        blank["cooperation"].Should().Be(0);
        Observe(new(), honesty: -1)["honesty"].Should().BeNegative();
        Observe(new(), games: 20)["familiarity"].Should().BeGreaterThan(Observe(new(), games: 1)["familiarity"]);
    }

    [Fact]
    public void RecentGamesChangeRelationships_AndForgivingBotsRecoverFaster()
    {
        Dictionary<string, double> old = new() { ["honesty"] = -15 };
        Dictionary<string, double> fresh = new() { ["honesty"] = 15 };
        BotAffinity.Update(old, fresh, 1)["honesty"].Should().BeGreaterThan(BotAffinity.Update(old, fresh, 0)["honesty"]);
        var values = new Dictionary<string, double>();
        for (var i = 0; i < 500; i++) values = BotAffinity.Update(values, Observe(new()), .5);
        values.Values.Sum().Should().BeInRange(-100, 100);
    }

    [Fact]
    public void InfluenceIsOptional_AndTrustBiasIsBounded()
    {
        var id = Guid.NewGuid();
        var mind = BotMind.Neutral with { Affinities = new Dictionary<Guid, int> { [id] = 100 },
            Personality = new BotPersonality { Social = new() { Influence = 0 } } };
        mind.TrustMultiplier(id).Should().Be(1);
        mind.AffinityBias(id).Should().Be(0);
        mind = mind with { Personality = mind.Personality with { Social = new() { Influence = 1 } } };
        mind.TrustMultiplier(id).Should().Be(1.5);
        (mind with { Affinities = new Dictionary<Guid, int> { [id] = -100 } }).TrustMultiplier(id).Should().Be(.5);
        mind.TrustMultiplier(Guid.NewGuid()).Should().Be(1);
        BotSocialTraits.ForBot(id).Should().Be(BotSocialTraits.ForBot(id));
    }
}

[Collection(DbCollection.Name)]
public sealed class BotRelationshipApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly DbApiFactory _factory = new(postgres);
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task OnlyPublicParticipationCounts_ClaimsAreDeduplicated_AndAnotherGameChangesAffinity()
    {
        var bot = await TestPlayer.LoginAsync(_factory, "Бот Симпатия");
        var player = await TestPlayer.LoginAsync(_factory, "Игрок Симпатия");
        _factory.Services.GetRequiredService<IConfiguration>()["Admin:UserIds"] = player.Id.ToString();
        await _factory.WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == bot.Id)).IsBot = true;
            db.BotProfiles.Add(new BotProfile { UserId = bot.Id });
            return await db.SaveChangesAsync();
        });
        try
        {
            var spectra = BotSpectra.From(new BotPersonality { Social = new() { Honesty = 1, Influence = .8, SkillRespect = 0 } });
            var updated = await player.PutAsync($"/api/v1/admin/bots/{bot.Id}", new { nickname = "Симпатия", spectra });
            updated.GetProperty("spectra").GetProperty("social").GetProperty("influence").GetDouble().Should().Be(.8);
            async Task<BotRelationship> Record(bool honest, bool finished)
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<GhostLettersDbContext>();
                var id = Guid.NewGuid();
                db.Games.Add(new Game { Id = id, Status = GameStatuses.Finished });
                for (var i = 0; i < 5; i++)
                    db.ChatMessages.Add(new ChatMessage { Id = Guid.NewGuid(), GameId = id, AuthorId = player.Id,
                        Round = 1, Channel = ChatChannels.Public, CardIds = [honest ? "own" : "fake"], CardNotes = ["кидал эту"] });
                db.ChatMessages.Add(new ChatMessage { Id = Guid.NewGuid(), GameId = id, AuthorId = player.Id,
                    Round = 2, Channel = ChatChannels.KillerTeam, CardIds = [honest ? "fake" : "own"], CardNotes = ["кидал эту"] });
                await db.SaveChangesAsync();
                var state = new GameState { Id = id, Phase = finished ? Phase.Finished : Phase.AwardNomination, TotalRounds = 2,
                    Players = [new() { Id = bot.Id }, new() { Id = player.Id }], Result = new(),
                    Letters = [new() { From = player.Id, CardId = "own", Round = 1 }] };
                await using var tx = await db.Database.BeginTransactionAsync();
                var service = scope.ServiceProvider.GetRequiredService<BotRelationshipService>();
                await service.RecordAsync(state, DateTimeOffset.UtcNow, default);
                await service.RecordAsync(state, DateTimeOffset.UtcNow, default);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return await db.BotRelationships.AsNoTracking().SingleAsync(r => r.BotId == bot.Id && r.PlayerId == player.Id);
            }
            var first = await Record(false, true);
            first.SharedGames.Should().Be(1);
            BotRelationshipService.Read(first.Components)["honesty"].Should().Be(-3.75, "одно ложное утверждение независимо от повторений, приватный чат не читается");
            var active = await Record(true, false);
            active.Components.Should().Be(first.Components);
            var second = await Record(true, true);
            second.SharedGames.Should().Be(2);
            BotRelationshipService.Read(second.Components)["honesty"].Should().BeGreaterThan(-3.75);
            second.Score.Should().BeGreaterThan(first.Score);
        }
        finally
        {
            await _factory.WithDbAsync(async db =>
            {
                await db.BotProfiles.Where(p => p.UserId == bot.Id).ExecuteDeleteAsync();
                return await db.Users.Where(u => u.Id == bot.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsBot, false));
            });
        }
    }

    [Fact]
    public async Task FinishedGameUpdatesOnce_PublicProfileHasOnlyScore_AdminCanSeeReasons()
    {
        var game = await GameHarness.StartAsync(_factory, 4);
        var bot = game.Players[1];
        _factory.Services.GetRequiredService<IConfiguration>()["Admin:UserIds"] = game.Host.Id.ToString();
        await _factory.WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == bot.Id)).IsBot = true;
            db.BotProfiles.Add(new BotProfile { UserId = bot.Id });
            return await db.SaveChangesAsync();
        });
        try
        {
            var endpoint = $"/api/v1/bots/{bot.Id}/relationships";
            (await game.Host.GetAsync(endpoint)).GetArrayLength().Should().Be(0);
            var driver = new GameDriver(game);
            await driver.RunUntilAsync(p => p == "AwardNomination");
            (await game.Host.GetAsync(endpoint)).GetArrayLength().Should().Be(0, "до конца партии симпатия не раскрывает роли");
            await driver.RunUntilAsync(p => p == "Finished");
            var publicData = await game.Players[2].GetAsync(endpoint);
            publicData.GetArrayLength().Should().Be(3);
            foreach (var row in publicData.EnumerateArray())
            {
                row.GetProperty("sharedGames").GetInt32().Should().Be(1);
                row.TryGetProperty("components", out _).Should().BeFalse();
                row.TryGetProperty("social", out _).Should().BeFalse();
            }
            var adminEndpoint = $"/api/v1/admin/bots/{bot.Id}/relationships";
            await game.Players[2].GetAsync(adminEndpoint, HttpStatusCode.Forbidden);
            (await game.Host.GetAsync(adminEndpoint))[0].GetProperty("components").TryGetProperty("expectations", out _).Should().BeTrue();
            (await game.Host.GetAsync($"/api/v1/users/{bot.Id}/profile")).GetProperty("isBot").GetBoolean().Should().BeTrue();

            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GhostLettersDbContext>();
            var state = GameStore.Read(await db.Games.SingleAsync(g => g.Id == game.GameId));
            await using var tx = await db.Database.BeginTransactionAsync();
            var relationships = scope.ServiceProvider.GetRequiredService<BotRelationshipService>();
            await relationships.RecordAsync(state, DateTimeOffset.UtcNow, default);
            await relationships.RecordAsync(state, DateTimeOffset.UtcNow, default);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            (await game.Host.GetAsync(endpoint)).GetRawText().Should().Be(publicData.GetRawText());
            var mind = (await _factory.WithServiceAsync<BotService, BotMind?>(s => s.MindAsync(state, bot.Id, default)))!;
            mind.Affinities.Should().HaveCount(3);
        }
        finally
        {
            await _factory.WithDbAsync(async db =>
            {
                await db.BotProfiles.Where(p => p.UserId == bot.Id).ExecuteDeleteAsync();
                return await db.Users.Where(u => u.Id == bot.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsBot, false));
            });
        }
    }
}
