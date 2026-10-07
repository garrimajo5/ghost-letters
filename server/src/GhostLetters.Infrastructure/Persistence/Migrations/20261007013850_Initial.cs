using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GhostLetters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "nominations",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nominations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nickname = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    avatar_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "auth_identities",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_identities", x => x.id);
                    table.ForeignKey(
                        name: "fk_auth_identities_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_sets",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_builtin = table.Column<bool>(type: "boolean", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_sets", x => x.id);
                    table.ForeignKey(
                        name: "fk_card_sets_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "lobbies",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    title = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    host_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false),
                    current_game_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lobbies", x => x.id);
                    table.ForeignKey(
                        name: "fk_lobbies_users_host_user_id",
                        column: x => x.host_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "media",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media", x => x.id);
                    table.ForeignKey(
                        name: "fk_media_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "push_tokens",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_push_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rating_history",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    rating_after = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rating_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_rating_history_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_achievements",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nomination_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    votes = table.Column<int>(type: "integer", nullable: false),
                    awarded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_achievements", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_achievements_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_stats",
                schema: "public",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    games = table.Column<int>(type: "integer", nullable: false),
                    wins = table.Column<int>(type: "integer", nullable: false),
                    role_wins = table.Column<string>(type: "jsonb", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    likes_received = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_stats", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_stats_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cards",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cards", x => x.id);
                    table.ForeignKey(
                        name: "fk_cards_card_sets_set_id",
                        column: x => x.set_id,
                        principalSchema: "public",
                        principalTable: "card_sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "games",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lobby_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false),
                    seed = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    phase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    phase_deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result = table.Column<string>(type: "jsonb", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_games", x => x.id);
                    table.ForeignKey(
                        name: "fk_games_lobbies_lobby_id",
                        column: x => x.lobby_id,
                        principalSchema: "public",
                        principalTable: "lobbies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "lobby_members",
                schema: "public",
                columns: table => new
                {
                    lobby_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat = table.Column<int>(type: "integer", nullable: false),
                    join_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_ready = table.Column<bool>(type: "boolean", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lobby_members", x => new { x.lobby_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_lobby_members_lobbies_lobby_id",
                        column: x => x.lobby_id,
                        principalSchema: "public",
                        principalTable: "lobbies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lobby_members_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "award_nominations",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nominator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nominee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nomination_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_award_nominations", x => x.id);
                    table.ForeignKey(
                        name: "fk_award_nominations_games_game_id",
                        column: x => x.game_id,
                        principalSchema: "public",
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_marks",
                schema: "public",
                columns: table => new
                {
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    crosses = table.Column<int>(type: "integer", nullable: false),
                    checks = table.Column<int>(type: "integer", nullable: false),
                    believed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_marks", x => new { x.game_id, x.owner_id, x.card_id });
                    table.ForeignKey(
                        name: "fk_card_marks_games_game_id",
                        column: x => x.game_id,
                        principalSchema: "public",
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_messages",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    card_ids = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_messages_games_game_id",
                        column: x => x.game_id,
                        principalSchema: "public",
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_messages_media_files_media_id",
                        column: x => x.media_id,
                        principalSchema: "public",
                        principalTable: "media",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "game_events",
                schema: "public",
                columns: table => new
                {
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    visible_to = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_events", x => new { x.game_id, x.seq });
                    table.ForeignKey(
                        name: "fk_game_events_games_game_id",
                        column: x => x.game_id,
                        principalSchema: "public",
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_players",
                schema: "public",
                columns: table => new
                {
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    character_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    is_connected = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_players", x => new { x.game_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_game_players_games_game_id",
                        column: x => x.game_id,
                        principalSchema: "public",
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_game_players_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "likes",
                schema: "public",
                columns: table => new
                {
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_likes", x => new { x.game_id, x.from_user_id, x.to_user_id });
                    table.ForeignKey(
                        name: "fk_likes_games_game_id",
                        column: x => x.game_id,
                        principalSchema: "public",
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_notes",
                schema: "public",
                columns: table => new
                {
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    suspicion = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    entries = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_notes", x => new { x.game_id, x.owner_id, x.target_user_id });
                    table.ForeignKey(
                        name: "fk_player_notes_games_game_id",
                        column: x => x.game_id,
                        principalSchema: "public",
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "votes",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    stage_kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    voter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    column = table.Column<int>(type: "integer", nullable: true),
                    suspect_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_votes", x => x.id);
                    table.ForeignKey(
                        name: "fk_votes_games_game_id",
                        column: x => x.game_id,
                        principalSchema: "public",
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "award_votes",
                schema: "public",
                columns: table => new
                {
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    award_nomination_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_award_votes", x => new { x.game_id, x.voter_id });
                    table.ForeignKey(
                        name: "fk_award_votes_award_nominations_award_nomination_id",
                        column: x => x.award_nomination_id,
                        principalSchema: "public",
                        principalTable: "award_nominations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_award_votes_games_game_id",
                        column: x => x.game_id,
                        principalSchema: "public",
                        principalTable: "games",
                        principalColumn: "id");
                });

            migrationBuilder.InsertData(
                schema: "public",
                table: "card_sets",
                columns: new[] { "id", "code", "is_builtin", "owner_id", "title" },
                values: new object[,]
                {
                    { new Guid("6f1d0c2e-0001-4a11-9a00-000000000001"), "original", true, null, "Оригинальный" },
                    { new Guid("6f1d0c2e-0001-4a11-9a00-000000000002"), "mailbox", true, null, "Почтовый ящик" },
                    { new Guid("6f1d0c2e-0001-4a11-9a00-000000000003"), "ritual", true, null, "Тайный ритуал" },
                    { new Guid("6f1d0c2e-0001-4a11-9a00-000000000004"), "mirror", true, null, "Зеркало истины" }
                });

            migrationBuilder.InsertData(
                schema: "public",
                table: "nominations",
                columns: new[] { "id", "code", "description", "is_active", "title" },
                values: new object[] { new Guid("6f1d0c2e-0002-4a11-9a00-000000000001"), "steel_balls", "За самый дерзкий ход партии.", true, "Стальные яйца" });

            migrationBuilder.CreateIndex(
                name: "ix_auth_identities_provider_subject",
                schema: "public",
                table: "auth_identities",
                columns: new[] { "provider", "subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_auth_identities_user_id",
                schema: "public",
                table: "auth_identities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_award_nominations_game_id_nominator_id",
                schema: "public",
                table: "award_nominations",
                columns: new[] { "game_id", "nominator_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_award_votes_award_nomination_id",
                schema: "public",
                table: "award_votes",
                column: "award_nomination_id");

            migrationBuilder.CreateIndex(
                name: "ix_card_sets_code",
                schema: "public",
                table: "card_sets",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_card_sets_owner_id",
                schema: "public",
                table: "card_sets",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_cards_set_id_image_key",
                schema: "public",
                table: "cards",
                columns: new[] { "set_id", "image_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_game_id_created_at",
                schema: "public",
                table: "chat_messages",
                columns: new[] { "game_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_media_id",
                schema: "public",
                table: "chat_messages",
                column: "media_id");

            migrationBuilder.CreateIndex(
                name: "ix_game_players_user_id",
                schema: "public",
                table: "game_players",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_games_lobby_id",
                schema: "public",
                table: "games",
                column: "lobby_id");

            migrationBuilder.CreateIndex(
                name: "ix_games_status_phase_deadline",
                schema: "public",
                table: "games",
                columns: new[] { "status", "phase_deadline" });

            migrationBuilder.CreateIndex(
                name: "ix_likes_to_user_id",
                schema: "public",
                table: "likes",
                column: "to_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_lobbies_code",
                schema: "public",
                table: "lobbies",
                column: "code",
                unique: true,
                filter: "status <> 'closed'");

            migrationBuilder.CreateIndex(
                name: "ix_lobbies_host_user_id",
                schema: "public",
                table: "lobbies",
                column: "host_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_lobby_members_user_id",
                schema: "public",
                table: "lobby_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_owner_id",
                schema: "public",
                table: "media",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_nominations_code",
                schema: "public",
                table: "nominations",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_push_tokens_user_id_token",
                schema: "public",
                table: "push_tokens",
                columns: new[] { "user_id", "token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rating_history_user_id_created_at",
                schema: "public",
                table: "rating_history",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                schema: "public",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                schema: "public",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_achievements_user_id",
                schema: "public",
                table: "user_achievements",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_stats_rating",
                schema: "public",
                table: "user_stats",
                column: "rating");

            migrationBuilder.CreateIndex(
                name: "ix_votes_game_id_stage_attempt_voter_id",
                schema: "public",
                table: "votes",
                columns: new[] { "game_id", "stage", "attempt", "voter_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_identities",
                schema: "public");

            migrationBuilder.DropTable(
                name: "award_votes",
                schema: "public");

            migrationBuilder.DropTable(
                name: "card_marks",
                schema: "public");

            migrationBuilder.DropTable(
                name: "cards",
                schema: "public");

            migrationBuilder.DropTable(
                name: "chat_messages",
                schema: "public");

            migrationBuilder.DropTable(
                name: "game_events",
                schema: "public");

            migrationBuilder.DropTable(
                name: "game_players",
                schema: "public");

            migrationBuilder.DropTable(
                name: "likes",
                schema: "public");

            migrationBuilder.DropTable(
                name: "lobby_members",
                schema: "public");

            migrationBuilder.DropTable(
                name: "nominations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "player_notes",
                schema: "public");

            migrationBuilder.DropTable(
                name: "push_tokens",
                schema: "public");

            migrationBuilder.DropTable(
                name: "rating_history",
                schema: "public");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "public");

            migrationBuilder.DropTable(
                name: "user_achievements",
                schema: "public");

            migrationBuilder.DropTable(
                name: "user_stats",
                schema: "public");

            migrationBuilder.DropTable(
                name: "votes",
                schema: "public");

            migrationBuilder.DropTable(
                name: "award_nominations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "card_sets",
                schema: "public");

            migrationBuilder.DropTable(
                name: "media",
                schema: "public");

            migrationBuilder.DropTable(
                name: "games",
                schema: "public");

            migrationBuilder.DropTable(
                name: "lobbies",
                schema: "public");

            migrationBuilder.DropTable(
                name: "users",
                schema: "public");
        }
    }
}
