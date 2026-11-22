using GhostLetters.Infrastructure.Games;
using GhostLetters.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GhostLetters.Api.Tests;

/// <summary>Импорт манифеста карт: новые карты добавляются, перенесённые в другой набор — переезжают.</summary>
[Collection(DbCollection.Name)]
public sealed class CardCatalogTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Zhizha_ImportsRealDeck_AndCanBePlayedOnItsOwn()
    {
        await using var db = await FreshDatabaseAsync();
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "client", "assets", "cards")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var assets = Path.Combine(dir!.FullName, "client", "assets", "cards");
        var catalog = new CardCatalog(db, new ConfigurationBuilder().Build(), NullLogger<CardCatalog>.Instance);
        var set = await db.CardSets.SingleAsync(s => s.Code == "zhizha");
        set.Title.Should().Be("Жижа");
        set.IsBuiltin.Should().BeTrue();
        (await catalog.ImportManifestAsync(Path.Combine(assets, "cards.json"), default)).Should().Be(961);
        (await catalog.ImportManifestAsync(Path.Combine(assets, "cards.json"), default)).Should().Be(0);
        var deck = await catalog.DeckAsync(["zhizha"], default);
        deck.Should().Equal(Enumerable.Range(1, 200).Select(n => $"zhizha_{n:0000}"));
        var tags = CardTags.Parse(File.ReadAllText(Path.Combine(assets, "tags.json")))
            .WithNames(File.ReadAllText(Path.Combine(assets, "names.json")))
            .WithAssociations(File.ReadAllText(Path.Combine(assets, "associations.json")));
        CardCatalog.WithoutDuplicateNames(deck, tags, 42).Should().HaveCount(200);
        tags.NameOf("zhizha_0171").Should().Be("Шёпот");
        tags.Of("zhizha_0171").Should().Contain(["ear", "speak", "hear"]);
        (await catalog.DeckAsync(["original"], default)).Should().NotContain(c => c.StartsWith("zhizha_"));
        (await catalog.DeckAsync(LobbySettings.AllCardSets, default)).Should().HaveCount(961);
    }

    [Fact]
    public async Task Import_MovesCardsToTheirSets_AndDecksFollowSettings()
    {
        await using var db = await FreshDatabaseAsync();
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "cards.json");
        var catalog = new CardCatalog(db, new ConfigurationBuilder().Build(), NullLogger<CardCatalog>.Instance);

        await File.WriteAllTextAsync(path, """
            {"version":1,"sets":[{"code":"original","title":"Оригинальный","cards":[
              {"id":"orig_0001","file":"a"},{"id":"orig_0002","file":"b"},{"id":"orig_0003","file":"c"}]}]}
            """);
        (await catalog.ImportManifestAsync(path, default)).Should().Be(3);

        await File.WriteAllTextAsync(path, """
            {"version":1,"sets":[
              {"code":"original","title":"Оригинальный","cards":[{"id":"orig_0003","file":"c"}]},
              {"code":"mirror","title":"Зеркало истины","cards":[{"id":"orig_0001","file":"a"},{"id":"orig_0004","file":"d"}]},
              {"code":"ritual","title":"Тайный ритуал","cards":[{"id":"orig_0002","file":"b"}]}]}
            """);
        (await catalog.ImportManifestAsync(path, default)).Should().Be(1, "новая только orig_0004");

        (await db.Cards.CountAsync()).Should().Be(4, "карты переезжают, а не копируются");
        (await catalog.DeckAsync(["original"], default)).Should().Equal("orig_0003");
        (await catalog.DeckAsync(["mirror"], default)).Should().Equal("orig_0001", "orig_0004");
        (await catalog.DeckAsync(LobbySettings.AllCardSets, default)).Should().HaveCount(4);
    }

    [Fact]
    public async Task RestartPreservesManualSetAndInactiveState_WithoutSeedDuplicates()
    {
        await using var db = await FreshDatabaseAsync();
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "cards.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                {"version":1,"sets":[{"code":"original","cards":[{"id":"orig_0001","file":"a"}]}]}
                """);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Cards:SeedOriginalCount"] = "1" }).Build();
            var catalog = new CardCatalog(db, config, NullLogger<CardCatalog>.Instance);
            await catalog.ImportManifestAsync(path, default);
            var card = await db.Cards.SingleAsync();
            card.SetId = await db.CardSets.Where(s => s.Code == "mirror").Select(s => s.Id).SingleAsync();
            card.SetManuallyAssigned = true;
            card.IsActive = false;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await catalog.ImportManifestAsync(path, default);
            await catalog.EnsureSeededAsync(default);
            (await db.Cards.CountAsync()).Should().Be(1);
            var saved = await db.Cards.AsNoTracking().SingleAsync();
            saved.SetId.Should().Be(card.SetId);
            saved.IsActive.Should().BeFalse();
        }
        finally { dir.Delete(true); }
    }

    private async Task<GhostLettersDbContext> FreshDatabaseAsync()
    {
        var name = "catalog_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var cs = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = name }.ConnectionString;
        var db = new GhostLettersDbContext(new DbContextOptionsBuilder<GhostLettersDbContext>().UseNpgsql(cs).UseSnakeCaseNamingConvention().Options);
        await db.Database.MigrateAsync();
        return db;
    }
}
