using GhostLetters.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhostLetters.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Раньше «Добавить бота» каждый раз создавал нового игрока, и в рейтинге копились одноимённые боты.
    /// Склеиваем ботов с одинаковым ником в одного: главный — бот из кабинета, иначе самый старый.
    /// Все ссылки на дубли (внешние ключи и id внутри jsonb — состояние партий, пометки, настройки) переводятся
    /// на главного, статистика складывается (рейтинг — 1000 + сумма изменений), дубли удаляются.
    /// Одноимённые боты никогда не сидели в одном лобби/партии, поэтому уникальные ключи не конфликтуют.
    /// </summary>
    [DbContext(typeof(GhostLettersDbContext))]
    [Migration("20261009000100_MergeDuplicateBots")]
    public partial class MergeDuplicateBots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    grp record;
                    ref record;
                    canon uuid;
                    dup uuid;
                BEGIN
                    FOR grp IN
                        SELECT u.nickname,
                               array_agg(u.id ORDER BY (EXISTS (SELECT 1 FROM public.bot_profiles b WHERE b.user_id = u.id)) DESC,
                                                       u.created_at, u.id) AS ids
                        FROM public.users u
                        WHERE u.is_bot
                        GROUP BY u.nickname
                        HAVING count(*) > 1
                    LOOP
                        canon := grp.ids[1];
                        INSERT INTO public.user_stats (user_id, games, wins, rating, likes_received, role_wins)
                        VALUES (canon, 0, 0, 1000, 0, '{}'::jsonb)
                        ON CONFLICT (user_id) DO NOTHING;

                        FOREACH dup IN ARRAY grp.ids[2:array_length(grp.ids, 1)]
                        LOOP
                            -- Статистика: партии, победы, лайки и победы по ролям складываются, рейтинг — 1000 + сумма изменений.
                            UPDATE public.user_stats c
                            SET games = c.games + d.games,
                                wins = c.wins + d.wins,
                                likes_received = c.likes_received + d.likes_received,
                                rating = c.rating + (d.rating - 1000),
                                role_wins = COALESCE((
                                    SELECT jsonb_object_agg(k, s)
                                    FROM (SELECT k, sum(v::int) AS s
                                          FROM (SELECT * FROM jsonb_each_text(c.role_wins)
                                                UNION ALL
                                                SELECT * FROM jsonb_each_text(d.role_wins)) x(k, v)
                                          GROUP BY k) y), '{}'::jsonb)
                            FROM public.user_stats d
                            WHERE c.user_id = canon AND d.user_id = dup;
                            DELETE FROM public.user_stats WHERE user_id = dup;
                            DELETE FROM public.bot_profiles WHERE user_id = dup;

                            -- Все внешние ключи на users.id.
                            FOR ref IN
                                SELECT kcu.table_schema AS s, kcu.table_name AS t, kcu.column_name AS c
                                FROM information_schema.table_constraints tc
                                JOIN information_schema.key_column_usage kcu
                                  ON kcu.constraint_name = tc.constraint_name AND kcu.constraint_schema = tc.constraint_schema
                                JOIN information_schema.constraint_column_usage ccu
                                  ON ccu.constraint_name = tc.constraint_name AND ccu.constraint_schema = tc.constraint_schema
                                WHERE tc.constraint_type = 'FOREIGN KEY'
                                  AND ccu.table_schema = 'public' AND ccu.table_name = 'users' AND ccu.column_name = 'id'
                            LOOP
                                EXECUTE format('UPDATE %I.%I SET %I = $1 WHERE %I = $2', ref.s, ref.t, ref.c, ref.c) USING canon, dup;
                            END LOOP;

                            -- id внутри jsonb: состояние и итоги партий, пометки на картах, настройки лобби.
                            FOR ref IN
                                SELECT table_schema AS s, table_name AS t, column_name AS c
                                FROM information_schema.columns
                                WHERE table_schema = 'public' AND data_type = 'jsonb'
                            LOOP
                                EXECUTE format(
                                    'UPDATE %I.%I SET %I = replace(%I::text, $2::text, $1::text)::jsonb WHERE strpos(%I::text, $2::text) > 0',
                                    ref.s, ref.t, ref.c, ref.c, ref.c) USING canon, dup;
                            END LOOP;

                            DELETE FROM public.users WHERE id = dup;
                        END LOOP;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Склейку не откатить: дубли удалены, их история перенесена на главного бота.
        }
    }
}
