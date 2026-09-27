using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboSquadApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSwipeAnswerSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "question_snapshot",
                table: "swipe_answers",
                type: "jsonb",
                nullable: true);

            // Старые ответы фиксируют контент в том состоянии, в котором он есть при обновлении базы.
            migrationBuilder.Sql("""
                UPDATE swipe_answers AS a
                SET question_snapshot = jsonb_build_object(
                    'statement', q.statement,
                    'rightLabel', q.options->'right'->>'label',
                    'leftLabel', q.options->'left'->>'label',
                    'correctSide', q.options->>'correct',
                    'explanation', jsonb_build_object(
                        'text', q.explanation_text,
                        'keyFact', q.explanation_key_fact,
                        'source', q.source))
                FROM questions AS q
                WHERE a.question_id = q.id AND a.question_snapshot IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "question_snapshot",
                table: "swipe_answers");
        }
    }
}
