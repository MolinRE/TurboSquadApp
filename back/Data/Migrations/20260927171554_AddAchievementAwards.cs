using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboSquadApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAchievementAwards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "achievement_awards",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    achievement_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    earned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_achievement_awards", x => new { x.user_id, x.achievement_id });
                    table.ForeignKey(
                        name: "fk_achievement_awards_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "achievement_awards");
        }
    }
}
