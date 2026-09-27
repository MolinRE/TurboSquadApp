using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboSquadApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCmsSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_id",
                table: "questions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sources", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_questions_source_id",
                table: "questions",
                column: "source_id");

            migrationBuilder.AddForeignKey(
                name: "fk_questions_sources_source_id",
                table: "questions",
                column: "source_id",
                principalTable: "sources",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_questions_sources_source_id",
                table: "questions");

            migrationBuilder.DropTable(
                name: "sources");

            migrationBuilder.DropIndex(
                name: "ix_questions_source_id",
                table: "questions");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "questions");
        }
    }
}
