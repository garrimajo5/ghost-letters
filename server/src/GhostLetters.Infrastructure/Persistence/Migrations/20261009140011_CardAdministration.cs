using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhostLetters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CardAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "annotations",
                schema: "public",
                table: "cards",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "metadata_version",
                schema: "public",
                table: "cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "set_manually_assigned",
                schema: "public",
                table: "cards",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "annotations",
                schema: "public",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "metadata_version",
                schema: "public",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "set_manually_assigned",
                schema: "public",
                table: "cards");
        }
    }
}
