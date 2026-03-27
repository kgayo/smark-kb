using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// TECH-152: Add long epoch columns to OutcomeEvents and EvalReports for SQLite-compatible
/// server-side time filtering. Also enables AuditEventQueryService and PatternUsageMetricsService
/// to use existing epoch columns (added in TECH-151) for server-side date filtering.
/// EF Core 10's SQLite provider cannot translate DateTimeOffset comparisons, forcing client-side
/// evaluation that loads unbounded rows into memory.
/// </summary>
public partial class AddEpochColumnsForQueryFiltering : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // OutcomeEvents.CreatedAtEpoch
        migrationBuilder.AddColumn<long>(
            name: "CreatedAtEpoch",
            table: "OutcomeEvents",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_OutcomeEvents_TenantId_CreatedAtEpoch",
            table: "OutcomeEvents",
            columns: new[] { "TenantId", "CreatedAtEpoch" });

        // EvalReports.CreatedAtEpoch
        migrationBuilder.AddColumn<long>(
            name: "CreatedAtEpoch",
            table: "EvalReports",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_EvalReports_TenantId_CreatedAtEpoch",
            table: "EvalReports",
            columns: new[] { "TenantId", "CreatedAtEpoch" });

        // Backfill existing rows with epoch values derived from DateTimeOffset columns.
        migrationBuilder.Sql(
            "UPDATE OutcomeEvents SET CreatedAtEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', CreatedAt) WHERE CreatedAtEpoch = 0");
        migrationBuilder.Sql(
            "UPDATE EvalReports SET CreatedAtEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', CreatedAt) WHERE CreatedAtEpoch = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_EvalReports_TenantId_CreatedAtEpoch", table: "EvalReports");
        migrationBuilder.DropColumn(name: "CreatedAtEpoch", table: "EvalReports");

        migrationBuilder.DropIndex(name: "IX_OutcomeEvents_TenantId_CreatedAtEpoch", table: "OutcomeEvents");
        migrationBuilder.DropColumn(name: "CreatedAtEpoch", table: "OutcomeEvents");
    }
}
