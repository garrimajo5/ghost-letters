using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhostLetters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BotStrictness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "strictness",
                schema: "public",
                table: "bot_profiles",
                type: "double precision",
                nullable: false,
                // Уже созданные боты получают середину шкалы — «проверял» по 3 карты, как до появления спектра.
                defaultValue: 0.5);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "strictness",
                schema: "public",
                table: "bot_profiles");
        }
    }
}
