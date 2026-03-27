using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// TECH-153/TECH-154: Add long epoch columns to CasePatterns, PatternMaintenanceTasks,
/// PatternContradictions, RoutingRecommendations, and RetentionExecutionLogs for
/// SQLite-compatible server-side ordering and pagination. These services previously loaded
/// entire tenant tables into memory for client-side OrderByDescending/Skip/Take.
/// </summary>
public partial class AddEpochColumnsForPaginationFiltering : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // CasePatterns.CreatedAtEpoch
        migrationBuilder.AddColumn<long>(
            name: "CreatedAtEpoch",
            table: "CasePatterns",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_CasePatterns_TenantId_CreatedAtEpoch",
            table: "CasePatterns",
            columns: new[] { "TenantId", "CreatedAtEpoch" });

        // PatternMaintenanceTasks.CreatedAtEpoch
        migrationBuilder.AddColumn<long>(
            name: "CreatedAtEpoch",
            table: "PatternMaintenanceTasks",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_PatternMaintenanceTasks_TenantId_CreatedAtEpoch",
            table: "PatternMaintenanceTasks",
            columns: new[] { "TenantId", "CreatedAtEpoch" });

        // PatternContradictions.CreatedAtEpoch
        migrationBuilder.AddColumn<long>(
            name: "CreatedAtEpoch",
            table: "PatternContradictions",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_PatternContradictions_TenantId_CreatedAtEpoch",
            table: "PatternContradictions",
            columns: new[] { "TenantId", "CreatedAtEpoch" });

        // RoutingRecommendations.CreatedAtEpoch
        migrationBuilder.AddColumn<long>(
            name: "CreatedAtEpoch",
            table: "RoutingRecommendations",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_RoutingRecommendations_TenantId_CreatedAtEpoch",
            table: "RoutingRecommendations",
            columns: new[] { "TenantId", "CreatedAtEpoch" });

        // RetentionExecutionLogs.ExecutedAtEpoch
        migrationBuilder.AddColumn<long>(
            name: "ExecutedAtEpoch",
            table: "RetentionExecutionLogs",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_RetentionExecutionLogs_TenantId_ExecutedAtEpoch",
            table: "RetentionExecutionLogs",
            columns: new[] { "TenantId", "ExecutedAtEpoch" });

        // Backfill existing rows.
        migrationBuilder.Sql(
            "UPDATE CasePatterns SET CreatedAtEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', CreatedAt) WHERE CreatedAtEpoch = 0");
        migrationBuilder.Sql(
            "UPDATE PatternMaintenanceTasks SET CreatedAtEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', CreatedAt) WHERE CreatedAtEpoch = 0");
        migrationBuilder.Sql(
            "UPDATE PatternContradictions SET CreatedAtEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', CreatedAt) WHERE CreatedAtEpoch = 0");
        migrationBuilder.Sql(
            "UPDATE RoutingRecommendations SET CreatedAtEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', CreatedAt) WHERE CreatedAtEpoch = 0");
        migrationBuilder.Sql(
            "UPDATE RetentionExecutionLogs SET ExecutedAtEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', ExecutedAt) WHERE ExecutedAtEpoch = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_RetentionExecutionLogs_TenantId_ExecutedAtEpoch", table: "RetentionExecutionLogs");
        migrationBuilder.DropColumn(name: "ExecutedAtEpoch", table: "RetentionExecutionLogs");

        migrationBuilder.DropIndex(name: "IX_RoutingRecommendations_TenantId_CreatedAtEpoch", table: "RoutingRecommendations");
        migrationBuilder.DropColumn(name: "CreatedAtEpoch", table: "RoutingRecommendations");

        migrationBuilder.DropIndex(name: "IX_PatternContradictions_TenantId_CreatedAtEpoch", table: "PatternContradictions");
        migrationBuilder.DropColumn(name: "CreatedAtEpoch", table: "PatternContradictions");

        migrationBuilder.DropIndex(name: "IX_PatternMaintenanceTasks_TenantId_CreatedAtEpoch", table: "PatternMaintenanceTasks");
        migrationBuilder.DropColumn(name: "CreatedAtEpoch", table: "PatternMaintenanceTasks");

        migrationBuilder.DropIndex(name: "IX_CasePatterns_TenantId_CreatedAtEpoch", table: "CasePatterns");
        migrationBuilder.DropColumn(name: "CreatedAtEpoch", table: "CasePatterns");
    }
}
