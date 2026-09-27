using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboSquadApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClaimPassengerReply : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "voice_reply_started_at",
                table: "trip_journal",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "voice_reply_started_at",
                table: "trip_journal");
        }
    }
}
