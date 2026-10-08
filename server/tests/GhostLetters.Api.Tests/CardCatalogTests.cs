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
