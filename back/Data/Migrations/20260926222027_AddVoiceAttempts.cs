using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboSquadApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "voice_applied",
                table: "trip_journal",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "voice_choice",
                table: "trip_journal",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "voice_confidence",
                table: "trip_journal",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "voice_error",
                table: "trip_journal",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "voice_latency_ms",
                table: "trip_journal",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "voice_request_id",
                table: "trip_journal",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "voice_transcript",
                table: "trip_journal",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "voice_applied",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_choice",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_confidence",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_error",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_latency_ms",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_request_id",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_transcript",
                table: "trip_journal");
        }
    }
}
