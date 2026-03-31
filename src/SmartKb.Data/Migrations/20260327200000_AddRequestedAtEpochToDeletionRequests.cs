using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// TECH-155: Add RequestedAtEpoch to DataSubjectDeletionRequests for server-side ordering.
/// The ListDeletionRequestsAsync method previously loaded all tenant rows into memory for
/// client-side OrderByDescending sorting.
/// </summary>
public partial class AddRequestedAtEpochToDeletionRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "RequestedAtEpoch",
            table: "DataSubjectDeletionRequests",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_DataSubjectDeletionRequests_TenantId_RequestedAtEpoch",
            table: "DataSubjectDeletionRequests",
            columns: new[] { "TenantId", "RequestedAtEpoch" });

        migrationBuilder.Sql(
            "UPDATE DataSubjectDeletionRequests SET RequestedAtEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', RequestedAt) WHERE RequestedAtEpoch = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_DataSubjectDeletionRequests_TenantId_RequestedAtEpoch",
            table: "DataSubjectDeletionRequests");

        migrationBuilder.DropColumn(
            name: "RequestedAtEpoch",
            table: "DataSubjectDeletionRequests");
    }
}
