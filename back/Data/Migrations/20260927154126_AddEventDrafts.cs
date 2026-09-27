using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboSquadApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_drafts", x => x.id);
                    table.ForeignKey(
                        name: "fk_event_drafts_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_drafts_source_id",
                table: "event_drafts",
                column: "source_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_drafts");
        }
    }
}
