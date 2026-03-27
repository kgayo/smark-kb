using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// TECH-153: Add long epoch column to TokenUsages for SQLite-compatible server-side time filtering.
/// TokenUsageService loaded ALL rows per tenant into memory then filtered by DateTimeOffset client-side.
/// </summary>
public partial class AddEpochColumnForTokenUsageFiltering : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "CreatedAtEpoch",
            table: "TokenUsages",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_TokenUsages_TenantId_CreatedAtEpoch",
            table: "TokenUsages",
            columns: new[] { "TenantId", "CreatedAtEpoch" });

        migrationBuilder.Sql(
            "UPDATE TokenUsages SET CreatedAtEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', CreatedAt) WHERE CreatedAtEpoch = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_TokenUsages_TenantId_CreatedAtEpoch", table: "TokenUsages");
        migrationBuilder.DropColumn(name: "CreatedAtEpoch", table: "TokenUsages");
    }
}
