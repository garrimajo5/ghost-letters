using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhostLetters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BotSandbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sandbox_runs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scenario = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    seed = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "jsonb", nullable: false),
                    report = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sandbox_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sandbox_runs_created_at",
                schema: "public",
                table: "sandbox_runs",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sandbox_runs",
                schema: "public");
        }
    }
}
