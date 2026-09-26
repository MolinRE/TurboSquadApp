using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboSquadApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameToSnakeCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Brigades_Depots_DepotId",
                table: "Brigades");

            migrationBuilder.DropForeignKey(
                name: "FK_UserRoles_Users_UserId",
                table: "UserRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Brigades_BrigadeId",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Depots_DepotId",
                table: "Users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Users",
                table: "Users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Depots",
                table: "Depots");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Brigades",
                table: "Brigades");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserRoles",
                table: "UserRoles");

            migrationBuilder.RenameTable(
                name: "Users",
                newName: "users");

            migrationBuilder.RenameTable(
                name: "Depots",
                newName: "depots");

            migrationBuilder.RenameTable(
                name: "Brigades",
                newName: "brigades");

            migrationBuilder.RenameTable(
                name: "UserRoles",
                newName: "user_roles");

            migrationBuilder.RenameColumn(
                name: "Username",
                table: "users",
                newName: "username");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "users",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "PasswordHash",
                table: "users",
                newName: "password_hash");

            migrationBuilder.RenameColumn(
                name: "NormalizedUsername",
                table: "users",
                newName: "normalized_username");

            migrationBuilder.RenameColumn(
                name: "DisplayName",
                table: "users",
                newName: "display_name");

            migrationBuilder.RenameColumn(
                name: "DepotId",
                table: "users",
                newName: "depot_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "users",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "BrigadeId",
                table: "users",
                newName: "brigade_id");

            migrationBuilder.RenameIndex(
                name: "IX_Users_NormalizedUsername",
                table: "users",
                newName: "ix_users_normalized_username");

            migrationBuilder.RenameIndex(
                name: "IX_Users_DepotId",
                table: "users",
                newName: "ix_users_depot_id");

            migrationBuilder.RenameIndex(
                name: "IX_Users_BrigadeId",
                table: "users",
                newName: "ix_users_brigade_id");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "depots",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "depots",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "brigades",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "brigades",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "DepotId",
                table: "brigades",
                newName: "depot_id");

            migrationBuilder.RenameIndex(
                name: "IX_Brigades_DepotId",
                table: "brigades",
                newName: "ix_brigades_depot_id");

            migrationBuilder.RenameColumn(
                name: "Role",
                table: "user_roles",
                newName: "role");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "user_roles",
                newName: "user_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_users",
                table: "users",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_depots",
                table: "depots",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_brigades",
                table: "brigades",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_roles",
                table: "user_roles",
                columns: new[] { "user_id", "role" });

            migrationBuilder.AddForeignKey(
                name: "fk_brigades_depots_depot_id",
                table: "brigades",
                column: "depot_id",
                principalTable: "depots",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_roles_users_user_id",
                table: "user_roles",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_users_brigades_brigade_id",
                table: "users",
                column: "brigade_id",
                principalTable: "brigades",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_depots_depot_id",
                table: "users",
                column: "depot_id",
                principalTable: "depots",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_brigades_depots_depot_id",
                table: "brigades");

            migrationBuilder.DropForeignKey(
                name: "fk_user_roles_users_user_id",
                table: "user_roles");

            migrationBuilder.DropForeignKey(
                name: "fk_users_brigades_brigade_id",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "fk_users_depots_depot_id",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "pk_users",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "pk_depots",
                table: "depots");

            migrationBuilder.DropPrimaryKey(
                name: "pk_brigades",
                table: "brigades");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_roles",
                table: "user_roles");

            migrationBuilder.RenameTable(
                name: "users",
                newName: "Users");

            migrationBuilder.RenameTable(
                name: "depots",
                newName: "Depots");

            migrationBuilder.RenameTable(
                name: "brigades",
                newName: "Brigades");

            migrationBuilder.RenameTable(
                name: "user_roles",
                newName: "UserRoles");

            migrationBuilder.RenameColumn(
                name: "username",
                table: "Users",
                newName: "Username");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Users",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "password_hash",
                table: "Users",
                newName: "PasswordHash");

            migrationBuilder.RenameColumn(
                name: "normalized_username",
                table: "Users",
                newName: "NormalizedUsername");

            migrationBuilder.RenameColumn(
                name: "display_name",
                table: "Users",
                newName: "DisplayName");

            migrationBuilder.RenameColumn(
                name: "depot_id",
                table: "Users",
                newName: "DepotId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "Users",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "brigade_id",
                table: "Users",
                newName: "BrigadeId");

            migrationBuilder.RenameIndex(
                name: "ix_users_normalized_username",
                table: "Users",
                newName: "IX_Users_NormalizedUsername");

            migrationBuilder.RenameIndex(
                name: "ix_users_depot_id",
                table: "Users",
                newName: "IX_Users_DepotId");

            migrationBuilder.RenameIndex(
                name: "ix_users_brigade_id",
                table: "Users",
                newName: "IX_Users_BrigadeId");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Depots",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Depots",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Brigades",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Brigades",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "depot_id",
                table: "Brigades",
                newName: "DepotId");

            migrationBuilder.RenameIndex(
                name: "ix_brigades_depot_id",
                table: "Brigades",
                newName: "IX_Brigades_DepotId");

            migrationBuilder.RenameColumn(
                name: "role",
                table: "UserRoles",
                newName: "Role");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "UserRoles",
                newName: "UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Users",
                table: "Users",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Depots",
                table: "Depots",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Brigades",
                table: "Brigades",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserRoles",
                table: "UserRoles",
                columns: new[] { "UserId", "Role" });

            migrationBuilder.AddForeignKey(
                name: "FK_Brigades_Depots_DepotId",
                table: "Brigades",
                column: "DepotId",
                principalTable: "Depots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoles_Users_UserId",
                table: "UserRoles",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Brigades_BrigadeId",
                table: "Users",
                column: "BrigadeId",
                principalTable: "Brigades",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Depots_DepotId",
                table: "Users",
                column: "DepotId",
                principalTable: "Depots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
