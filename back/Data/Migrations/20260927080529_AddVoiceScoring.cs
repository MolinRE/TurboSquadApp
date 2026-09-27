using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboSquadApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "voice_laya_latency_ms",
                table: "trip_journal",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "voice_llm_latency_ms",
                table: "trip_journal",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "voice_role_stages",
                table: "trip_journal",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "voice_safety_confidence",
                table: "trip_journal",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "voice_safety_violation",
                table: "trip_journal",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "voice_score",
                table: "trip_journal",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "voice_score_confidence",
                table: "trip_journal",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "voice_stt_latency_ms",
                table: "trip_journal",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "voice_laya_latency_ms",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_llm_latency_ms",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_role_stages",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_safety_confidence",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_safety_violation",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_score",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_score_confidence",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_stt_latency_ms",
                table: "trip_journal");
        }
    }
}
