using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TurboSquadApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContentTablesToSnakeCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Scales",
                table: "Scales");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TripSettings",
                table: "TripSettings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ServiceClasses",
                table: "ServiceClasses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_EventDocuments",
                table: "EventDocuments");

            migrationBuilder.RenameTable(
                name: "Scales",
                newName: "scales");

            migrationBuilder.RenameTable(
                name: "TripSettings",
                newName: "trip_settings");

            migrationBuilder.RenameTable(
                name: "ServiceClasses",
                newName: "service_classes");

            migrationBuilder.RenameTable(
                name: "EventDocuments",
                newName: "event_documents");

            migrationBuilder.RenameColumn(
                name: "Start",
                table: "scales",
                newName: "start");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "scales",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Min",
                table: "scales",
                newName: "min");

            migrationBuilder.RenameColumn(
                name: "Max",
                table: "scales",
                newName: "max");

            migrationBuilder.RenameColumn(
                name: "Mandatory",
                table: "scales",
                newName: "mandatory");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "scales",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "FailureThreshold",
                table: "scales",
                newName: "failure_threshold");

            migrationBuilder.RenameColumn(
                name: "FailureReason",
                table: "scales",
                newName: "failure_reason");

            migrationBuilder.RenameColumn(
                name: "Document",
                table: "trip_settings",
                newName: "document");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "trip_settings",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "service_classes",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "service_classes",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Code",
                table: "service_classes",
                newName: "code");

            migrationBuilder.RenameColumn(
                name: "SortOrder",
                table: "service_classes",
                newName: "sort_order");

            migrationBuilder.RenameColumn(
                name: "Document",
                table: "event_documents",
                newName: "document");

            migrationBuilder.RenameColumn(
                name: "Version",
                table: "event_documents",
                newName: "version");

            migrationBuilder.RenameColumn(
                name: "PublishedAt",
                table: "event_documents",
                newName: "published_at");

            migrationBuilder.RenameColumn(
                name: "EventId",
                table: "event_documents",
                newName: "event_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_scales",
                table: "scales",
                column: "code");

            migrationBuilder.AddPrimaryKey(
                name: "pk_trip_settings",
                table: "trip_settings",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_service_classes",
                table: "service_classes",
                column: "code");

            migrationBuilder.AddPrimaryKey(
                name: "pk_event_documents",
                table: "event_documents",
                columns: new[] { "event_id", "version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_scales",
                table: "scales");

            migrationBuilder.DropPrimaryKey(
                name: "pk_trip_settings",
                table: "trip_settings");

            migrationBuilder.DropPrimaryKey(
                name: "pk_service_classes",
                table: "service_classes");

            migrationBuilder.DropPrimaryKey(
                name: "pk_event_documents",
                table: "event_documents");

            migrationBuilder.RenameTable(
                name: "scales",
                newName: "Scales");

            migrationBuilder.RenameTable(
                name: "trip_settings",
                newName: "TripSettings");

            migrationBuilder.RenameTable(
                name: "service_classes",
                newName: "ServiceClasses");

            migrationBuilder.RenameTable(
                name: "event_documents",
                newName: "EventDocuments");

            migrationBuilder.RenameColumn(
                name: "start",
                table: "Scales",
                newName: "Start");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Scales",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "min",
                table: "Scales",
                newName: "Min");

            migrationBuilder.RenameColumn(
                name: "max",
                table: "Scales",
                newName: "Max");

            migrationBuilder.RenameColumn(
                name: "mandatory",
                table: "Scales",
                newName: "Mandatory");

            migrationBuilder.RenameColumn(
                name: "code",
                table: "Scales",
                newName: "Code");

            migrationBuilder.RenameColumn(
                name: "failure_threshold",
                table: "Scales",
                newName: "FailureThreshold");

            migrationBuilder.RenameColumn(
                name: "failure_reason",
                table: "Scales",
                newName: "FailureReason");

            migrationBuilder.RenameColumn(
                name: "document",
                table: "TripSettings",
                newName: "Document");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "TripSettings",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "ServiceClasses",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "ServiceClasses",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "code",
                table: "ServiceClasses",
                newName: "Code");

            migrationBuilder.RenameColumn(
                name: "sort_order",
                table: "ServiceClasses",
                newName: "SortOrder");

            migrationBuilder.RenameColumn(
                name: "document",
                table: "EventDocuments",
                newName: "Document");

            migrationBuilder.RenameColumn(
                name: "version",
                table: "EventDocuments",
                newName: "Version");

            migrationBuilder.RenameColumn(
                name: "published_at",
                table: "EventDocuments",
                newName: "PublishedAt");

            migrationBuilder.RenameColumn(
                name: "event_id",
                table: "EventDocuments",
                newName: "EventId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Scales",
                table: "Scales",
                column: "Code");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TripSettings",
                table: "TripSettings",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ServiceClasses",
                table: "ServiceClasses",
                column: "Code");

            migrationBuilder.AddPrimaryKey(
                name: "PK_EventDocuments",
                table: "EventDocuments",
                columns: new[] { "EventId", "Version" });
        }
    }
}
