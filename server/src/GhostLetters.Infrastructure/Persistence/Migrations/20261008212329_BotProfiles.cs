using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhostLetters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BotProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bot_profiles",
                schema: "public",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    about = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    meaning = table.Column<double>(type: "double precision", nullable: false),
                    shape = table.Column<double>(type: "double precision", nullable: false),
                    color = table.Column<double>(type: "double precision", nullable: false),
                    negative = table.Column<double>(type: "double precision", nullable: false),
                    memory = table.Column<double>(type: "double precision", nullable: false),
                    risk = table.Column<double>(type: "double precision", nullable: false),
                    compromise = table.Column<double>(type: "double precision", nullable: false),
                    variability = table.Column<double>(type: "double precision", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bot_profiles", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_bot_profiles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_profiles",
                schema: "public");
        }
    }
}
