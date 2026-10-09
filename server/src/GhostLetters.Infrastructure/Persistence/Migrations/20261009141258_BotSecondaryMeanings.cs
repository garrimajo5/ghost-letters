using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhostLetters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BotSecondaryMeanings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "secondary_meanings",
                schema: "public",
                table: "bot_profiles",
                type: "double precision",
                nullable: false,
                defaultValue: 0.35);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "secondary_meanings",
                schema: "public",
                table: "bot_profiles");
        }
    }
}
