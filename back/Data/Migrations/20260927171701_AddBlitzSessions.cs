using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboSquadApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBlitzSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "blitz_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    deck = table.Column<string>(type: "jsonb", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    question_shown_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blitz_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_blitz_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "blitz_answers",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    question_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    selected_option_ids = table.Column<string>(type: "jsonb", nullable: false),
                    timed_out = table.Column<bool>(type: "boolean", nullable: false),
                    verdict = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    elapsed_ms = table.Column<int>(type: "integer", nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blitz_answers", x => new { x.session_id, x.seq });
                    table.ForeignKey(
                        name: "fk_blitz_answers_blitz_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "blitz_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_blitz_answers_session_id_question_id",
                table: "blitz_answers",
                columns: new[] { "session_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_blitz_sessions_user_id",
                table: "blitz_sessions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blitz_answers");

            migrationBuilder.DropTable(
                name: "blitz_sessions");
        }
    }
}
