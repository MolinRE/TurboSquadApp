using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboSquadApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trips",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_class = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_cause = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    failure_scale = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    event_versions = table.Column<string>(type: "jsonb", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    step_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trips", x => x.id);
                    table.ForeignKey(
                        name: "fk_trips_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trip_journal",
                columns: table => new
                {
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    option_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    event_version = table.Column<int>(type: "integer", nullable: true),
                    step_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    variant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    timed_out = table.Column<bool>(type: "boolean", nullable: false),
                    elapsed_ms = table.Column<int>(type: "integer", nullable: true),
                    scale_changes = table.Column<string>(type: "jsonb", nullable: true),
                    flags_set = table.Column<string>(type: "jsonb", nullable: true),
                    critical_error = table.Column<bool>(type: "boolean", nullable: false),
                    to_step_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    outcome_step_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_journal", x => new { x.trip_id, x.seq });
                    table.ForeignKey(
                        name: "fk_trip_journal_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_trips_user_id",
                table: "trips",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trip_journal");

            migrationBuilder.DropTable(
                name: "trips");
        }
    }
}
