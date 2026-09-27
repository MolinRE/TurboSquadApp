using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboSquadApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionsAndSwipeShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "questions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    statement = table.Column<string>(type: "text", nullable: false),
                    options = table.Column<string>(type: "jsonb", nullable: false),
                    explanation_text = table.Column<string>(type: "text", nullable: false),
                    explanation_key_fact = table.Column<string>(type: "text", nullable: false),
                    quote = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    topic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    categories = table.Column<string>(type: "jsonb", nullable: false),
                    service_classes = table.Column<string>(type: "jsonb", nullable: false),
                    base_frequency = table.Column<double>(type: "double precision", nullable: false),
                    time_limit_sec = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "swipe_shifts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cycle = table.Column<int>(type: "integer", nullable: true),
                    previous_cycle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_scale = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    deck = table.Column<string>(type: "jsonb", nullable: false),
                    scales = table.Column<string>(type: "jsonb", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    card_shown_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_swipe_shifts", x => x.id);
                    table.ForeignKey(
                        name: "fk_swipe_shifts_swipe_shifts_previous_cycle_id",
                        column: x => x.previous_cycle_id,
                        principalTable: "swipe_shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_swipe_shifts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "swipe_answers",
                columns: table => new
                {
                    shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    question_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    answer = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    verdict = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_repeat = table.Column<bool>(type: "boolean", nullable: false),
                    elapsed_ms = table.Column<int>(type: "integer", nullable: false),
                    scale_changes = table.Column<string>(type: "jsonb", nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_swipe_answers", x => new { x.shift_id, x.seq });
                    table.ForeignKey(
                        name: "fk_swipe_answers_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_swipe_answers_swipe_shifts_shift_id",
                        column: x => x.shift_id,
                        principalTable: "swipe_shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_questions_type_status",
                table: "questions",
                columns: new[] { "type", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_swipe_answers_question_id",
                table: "swipe_answers",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_swipe_shifts_previous_cycle_id",
                table: "swipe_shifts",
                column: "previous_cycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_swipe_shifts_user_id",
                table: "swipe_shifts",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "swipe_answers");

            migrationBuilder.DropTable(
                name: "questions");

            migrationBuilder.DropTable(
                name: "swipe_shifts");
        }
    }
}
