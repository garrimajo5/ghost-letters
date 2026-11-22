using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhostLetters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ZhizhaCardSet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "public",
                table: "card_sets",
                columns: new[] { "id", "code", "is_builtin", "owner_id", "title" },
                values: new object[] { new Guid("6f1d0c2e-0001-4a11-9a00-000000000005"), "zhizha", true, null, "Жижа" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "public",
                table: "card_sets",
                keyColumn: "id",
                keyValue: new Guid("6f1d0c2e-0001-4a11-9a00-000000000005"));
        }
    }
}
