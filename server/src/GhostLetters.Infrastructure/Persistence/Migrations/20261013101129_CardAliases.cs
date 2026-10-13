using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhostLetters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CardAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "card_aliases",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    phrase = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    valid = table.Column<bool>(type: "boolean", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_aliases", x => x.id);
                    table.ForeignKey(
                        name: "fk_card_aliases_games_game_id",
                        column: x => x.game_id,
                        principalSchema: "public",
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_card_aliases_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_card_aliases_game_id",
                schema: "public",
                table: "card_aliases",
                column: "game_id");

            migrationBuilder.CreateIndex(
                name: "ix_card_aliases_phrase_card_id",
                schema: "public",
                table: "card_aliases",
                columns: new[] { "phrase", "card_id" });

            migrationBuilder.CreateIndex(
                name: "ix_card_aliases_user_id_phrase",
                schema: "public",
                table: "card_aliases",
                columns: new[] { "user_id", "phrase" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_aliases",
                schema: "public");
        }
    }
}
