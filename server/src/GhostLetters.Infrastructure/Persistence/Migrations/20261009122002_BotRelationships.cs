using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhostLetters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BotRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "social",
                schema: "public",
                table: "bot_profiles",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateTable(
                name: "bot_relationship_games",
                schema: "public",
                columns: table => new
                {
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bot_relationship_games", x => new { x.game_id, x.bot_id });
                    table.ForeignKey(
                        name: "fk_bot_relationship_games_games_game_id",
                        column: x => x.game_id,
                        principalSchema: "public",
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bot_relationship_games_users_bot_id",
                        column: x => x.bot_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bot_relationships",
                schema: "public",
                columns: table => new
                {
                    bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    shared_games = table.Column<int>(type: "integer", nullable: false),
                    components = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bot_relationships", x => new { x.bot_id, x.player_id });
                    table.ForeignKey(
                        name: "fk_bot_relationships_users_bot_id",
                        column: x => x.bot_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bot_relationships_users_player_id",
                        column: x => x.player_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bot_relationship_games_bot_id",
                schema: "public",
                table: "bot_relationship_games",
                column: "bot_id");

            migrationBuilder.CreateIndex(
                name: "ix_bot_relationships_player_id",
                schema: "public",
                table: "bot_relationships",
                column: "player_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_relationship_games",
                schema: "public");

            migrationBuilder.DropTable(
                name: "bot_relationships",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "social",
                schema: "public",
                table: "bot_profiles");
        }
    }
}

