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

    [Fact]
    public async Task MergeDuplicateBots_OneBotPerName_WithWholeHistory()
    {
        await using var db = await FreshDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var merge = all.FindIndex(m => m.EndsWith("_MergeDuplicateBots", StringComparison.Ordinal));
        merge.Should().BePositive();
        await migrator.MigrateAsync(all[merge - 1]);

        // Три «Бота Пуаро»: один из кабинета (главный), два старых дубля со своими партиями; плюс одинокий «Бот Марпл».
        await db.Database.ExecuteSqlRawAsync("""
            SET session_replication_role = replica;
            INSERT INTO public.users (id, nickname, avatar_color, created_at, last_seen_at, is_bot) VALUES
              ('00000000-0000-0000-0000-00000000000a', 'Бот Пуаро', '#5C7C99', now() - interval '3 day', now(), true),
              ('00000000-0000-0000-0000-00000000000b', 'Бот Пуаро', '#5C7C99', now() - interval '2 day', now(), true),
              ('00000000-0000-0000-0000-00000000000c', 'Бот Пуаро', '#5C7C99', now() - interval '1 day', now(), true),
              ('00000000-0000-0000-0000-00000000000d', 'Бот Марпл', '#B370D9', now(), now(), true);
            INSERT INTO public.bot_profiles (user_id, about, meaning, shape, color, negative, memory, risk, compromise, variability, enabled, updated_at)
              VALUES ('00000000-0000-0000-0000-00000000000c', 'из кабинета', 0.5, 0.25, 0.25, 0.5, 0.3, 0.5, 0.5, 0.2, true, now());
            INSERT INTO public.user_stats (user_id, games, wins, rating, likes_received, role_wins) VALUES
              ('00000000-0000-0000-0000-00000000000a', 3, 2, 1020, 1, '{{"Killer": 1, "Detective": 1}}'),
              ('00000000-0000-0000-0000-00000000000b', 2, 1, 990, 0, '{{"Detective": 1}}'),
              ('00000000-0000-0000-0000-00000000000c', 1, 0, 1000, 0, '{{}}');
            INSERT INTO public.rating_history (id, user_id, game_id, delta, rating_after, created_at) VALUES
              (gen_random_uuid(), '00000000-0000-0000-0000-00000000000a', gen_random_uuid(), 20, 1020, now()),
              (gen_random_uuid(), '00000000-0000-0000-0000-00000000000b', gen_random_uuid(), -10, 990, now());
            SET session_replication_role = origin;
            """);

        await migrator.MigrateAsync();

        var poirots = await db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM public.users WHERE nickname = 'Бот Пуаро'").ToListAsync();
        poirots.Should().ContainSingle().Which.Should().Be(new Guid("00000000-0000-0000-0000-00000000000c"), "главный — бот из кабинета");
        (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM public.users WHERE nickname = 'Бот Марпл'").SingleAsync()).Should().Be(1);

        var stats = await db.Database.SqlQueryRaw<string>(
            "SELECT games || '/' || wins || '/' || rating || '/' || likes_received || '/' || (role_wins->>'Detective') || '/' || (role_wins->>'Killer') AS \"Value\" " +
            "FROM public.user_stats WHERE user_id = '00000000-0000-0000-0000-00000000000c'").SingleAsync();
        stats.Should().Be("6/3/1010/1/2/1", "партии и победы сложены, рейтинг 1000 + 20 − 10");
        (await db.Database.SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM public.rating_history WHERE user_id = '00000000-0000-0000-0000-00000000000c'").SingleAsync())
            .Should().Be(2, "история партий дублей перешла к главному");
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
