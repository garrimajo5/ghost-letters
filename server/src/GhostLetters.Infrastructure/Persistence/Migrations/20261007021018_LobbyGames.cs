using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhostLetters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LobbyGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "client_command_id",
                schema: "public",
                table: "game_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_game_events_game_id_actor_user_id_client_command_id",
                schema: "public",
                table: "game_events",
                columns: new[] { "game_id", "actor_user_id", "client_command_id" },
                filter: "client_command_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_game_events_game_id_actor_user_id_client_command_id",
                schema: "public",
                table: "game_events");

            migrationBuilder.DropColumn(
                name: "client_command_id",
                schema: "public",
                table: "game_events");
        }
    }
}
