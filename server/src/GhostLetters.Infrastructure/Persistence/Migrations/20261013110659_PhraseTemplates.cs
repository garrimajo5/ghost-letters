using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GhostLetters.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhraseTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "phrase_templates",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    intent = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    emotion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    manner = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    template = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    hash = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seen = table.Column<int>(type: "integer", nullable: false),
                    uses = table.Column<int>(type: "integer", nullable: false),
                    blocked = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_phrase_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_phrase_templates_users_author_id",
                        column: x => x.author_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_phrase_templates_author_id",
                schema: "public",
                table: "phrase_templates",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_phrase_templates_hash",
                schema: "public",
                table: "phrase_templates",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_phrase_templates_intent_manner",
                schema: "public",
                table: "phrase_templates",
                columns: new[] { "intent", "manner" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "phrase_templates",
                schema: "public");
        }
    }
}
