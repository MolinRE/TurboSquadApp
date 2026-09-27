using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboSquadApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "knowledge_delta",
                table: "swipe_answers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "knowledge_cost",
                table: "questions",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.CreateTable(
                name: "conductor_profiles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rank_index = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conductor_profiles", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_conductor_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_masteries",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unit_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    competence = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_mastered = table.Column<bool>(type: "boolean", nullable: false),
                    awarded_cost = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_masteries", x => new { x.user_id, x.unit_type, x.unit_id, x.competence });
                    table.ForeignKey(
                        name: "fk_knowledge_masteries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_masteries_user_id",
                table: "knowledge_masteries",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conductor_profiles");

            migrationBuilder.DropTable(
                name: "knowledge_masteries");

            migrationBuilder.DropColumn(
                name: "knowledge_delta",
                table: "swipe_answers");

            migrationBuilder.DropColumn(
                name: "knowledge_cost",
                table: "questions");
        }
    }
}
