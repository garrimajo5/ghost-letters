using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GhostLetters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoreNominations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "public",
                table: "nominations",
                columns: new[] { "id", "code", "description", "is_active", "title" },
                values: new object[,]
                {
                    { new Guid("6f1d0c2e-0002-4a11-9a00-000000000002"), "sherlock", "Раньше всех понял, что произошло.", true, "Шерлок" },
                    { new Guid("6f1d0c2e-0002-4a11-9a00-000000000003"), "best_liar", "Убедительнее всех водил следствие за нос.", true, "Лучший лжец" },
                    { new Guid("6f1d0c2e-0002-4a11-9a00-000000000004"), "ghost_whisperer", "Лучше всех понимал подсказки Призрака.", true, "Голос Призрака" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "public",
                table: "nominations",
                keyColumn: "id",
                keyValue: new Guid("6f1d0c2e-0002-4a11-9a00-000000000002"));

            migrationBuilder.DeleteData(
                schema: "public",
                table: "nominations",
                keyColumn: "id",
                keyValue: new Guid("6f1d0c2e-0002-4a11-9a00-000000000003"));

            migrationBuilder.DeleteData(
                schema: "public",
                table: "nominations",
                keyColumn: "id",
                keyValue: new Guid("6f1d0c2e-0002-4a11-9a00-000000000004"));
        }
    }
}
