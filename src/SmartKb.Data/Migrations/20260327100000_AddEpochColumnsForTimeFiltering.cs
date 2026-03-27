using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// TECH-146: Add long epoch columns to EmbeddingCache and RateLimitEvents for SQLite-compatible
/// server-side time filtering. EF Core 10's SQLite provider cannot translate DateTimeOffset
/// comparisons, forcing client-side evaluation that loads unbounded rows into memory.
/// </summary>
public partial class AddEpochColumnsForTimeFiltering : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "ExpiresAtEpoch",
            table: "EmbeddingCache",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_EmbeddingCache_ExpiresAtEpoch",
            table: "EmbeddingCache",
            column: "ExpiresAtEpoch");

        migrationBuilder.AddColumn<long>(
            name: "OccurredAtEpoch",
            table: "RateLimitEvents",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_RateLimitEvents_TenantId_OccurredAtEpoch",
            table: "RateLimitEvents",
            columns: new[] { "TenantId", "OccurredAtEpoch" });

        // Backfill existing rows with epoch values derived from DateTimeOffset columns.
        // SQL Server: DATEDIFF_BIG(second, '1970-01-01', column) gives Unix epoch seconds.
        migrationBuilder.Sql(
            "UPDATE EmbeddingCache SET ExpiresAtEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', ExpiresAt) WHERE ExpiresAtEpoch = 0");
        migrationBuilder.Sql(
            "UPDATE RateLimitEvents SET OccurredAtEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', OccurredAt) WHERE OccurredAtEpoch = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_RateLimitEvents_TenantId_OccurredAtEpoch",
            table: "RateLimitEvents");

        migrationBuilder.DropColumn(
            name: "OccurredAtEpoch",
            table: "RateLimitEvents");

        migrationBuilder.DropIndex(
            name: "IX_EmbeddingCache_ExpiresAtEpoch",
            table: "EmbeddingCache");

        migrationBuilder.DropColumn(
            name: "ExpiresAtEpoch",
            table: "EmbeddingCache");
    }
}
