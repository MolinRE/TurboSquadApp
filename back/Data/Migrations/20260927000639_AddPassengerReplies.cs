using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboSquadApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPassengerReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "voice_attempt_id",
                table: "trip_journal",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "voice_passenger_reply",
                table: "trip_journal",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "voice_reply_error",
                table: "trip_journal",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_trip_journal_trip_id_voice_attempt_id",
                table: "trip_journal",
                columns: new[] { "trip_id", "voice_attempt_id" },
                unique: true,
                filter: "\"kind\" = 'voiceAttempt'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_trip_journal_trip_id_voice_attempt_id",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_attempt_id",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_passenger_reply",
                table: "trip_journal");

            migrationBuilder.DropColumn(
                name: "voice_reply_error",
                table: "trip_journal");
        }
    }
}
