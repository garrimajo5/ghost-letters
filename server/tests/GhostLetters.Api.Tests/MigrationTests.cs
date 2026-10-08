using GhostLetters.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GhostLetters.Api.Tests;

/// <summary>
/// Миграции накатываются на базу, где уже есть данные, — как на боевом сервере.
/// На пустой базе (CI, docker compose) NOT NULL-колонка без значения по умолчанию проходит, а на живой — нет.
/// </summary>
[Collection(DbCollection.Name)]
public sealed class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ChatCardNotes_AppliesOverExistingMessages()
    {
        await using var db = await FreshDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var notes = all.FindIndex(m => m.EndsWith("_ChatCardNotes", StringComparison.Ordinal));
        notes.Should().BePositive();

        await migrator.MigrateAsync(all[notes - 1]);
        await db.Database.ExecuteSqlRawAsync("""
            SET session_replication_role = replica;
            INSERT INTO public.chat_messages (id, game_id, round, channel, author_id, kind, text, media_id, card_ids, created_at)
            VALUES (gen_random_uuid(), gen_random_uuid(), 1, 'public', NULL, 'text', 'старое сообщение', NULL, '{{}}', now());
            SET session_replication_role = origin;
            """);

        await migrator.MigrateAsync();

        var empty = await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM public.chat_messages WHERE card_notes = '{{}}'").SingleAsync();
        empty.Should().Be(1);
    }

    private async Task<GhostLettersDbContext> FreshDatabaseAsync()
    {
        var name = "migrations_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var cs = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = name }.ConnectionString;
        var options = new DbContextOptionsBuilder<GhostLettersDbContext>().UseNpgsql(cs).UseSnakeCaseNamingConvention().Options;
        return new GhostLettersDbContext(options);
    }
}
