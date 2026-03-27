using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// TECH-151: Add long epoch columns to Sessions, Messages, AuditEvents, EvidenceChunks, and
/// AnswerTraces for SQLite-compatible server-side time filtering in RetentionCleanupService.
/// EF Core 10's SQLite provider cannot translate DateTimeOffset comparisons, forcing client-side
/// evaluation that loads unbounded rows into memory.
/// </summary>
public partial class AddEpochColumnsForRetentionFiltering : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Sessions.CreatedAtEpoch
        migrationBuilder.AddColumn<long>(
            name: "CreatedAtEpoch",
            table: "Sessions",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_Sessions_TenantId_CreatedAtEpoch",
            table: "Sessions",
            columns: new[] { "TenantId", "CreatedAtEpoch" });

        // Messages.CreatedAtEpoch
        migrationBuilder.AddColumn<long>(
            name: "CreatedAtEpoch",
            table: "Messages",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_Messages_TenantId_CreatedAtEpoch",
            table: "Messages",
            columns: new[] { "TenantId", "CreatedAtEpoch" });

        // AuditEvents.TimestampEpoch
        migrationBuilder.AddColumn<long>(
            name: "TimestampEpoch",
            table: "AuditEvents",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_TenantId_TimestampEpoch",
            table: "AuditEvents",
            columns: new[] { "TenantId", "TimestampEpoch" });

        // EvidenceChunks.CreatedAtEpoch
        migrationBuilder.AddColumn<long>(
            name: "CreatedAtEpoch",
            table: "EvidenceChunks",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_EvidenceChunks_TenantId_CreatedAtEpoch",
            table: "EvidenceChunks",
            columns: new[] { "TenantId", "CreatedAtEpoch" });

        // AnswerTraces.CreatedAtEpoch
        migrationBuilder.AddColumn<long>(
            name: "CreatedAtEpoch",
            table: "AnswerTraces",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateIndex(
            name: "IX_AnswerTraces_TenantId_CreatedAtEpoch",
            table: "AnswerTraces",
            columns: new[] { "TenantId", "CreatedAtEpoch" });

        // Backfill existing rows with epoch values derived from DateTimeOffset columns.
        // SQL Server: DATEDIFF_BIG(second, '1970-01-01', column) gives Unix epoch seconds.
        migrationBuilder.Sql(
            "UPDATE Sessions SET CreatedAtEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', CreatedAt) WHERE CreatedAtEpoch = 0");
        migrationBuilder.Sql(
            "UPDATE Messages SET CreatedAtEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', CreatedAt) WHERE CreatedAtEpoch = 0");
        migrationBuilder.Sql(
            "UPDATE AuditEvents SET TimestampEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', Timestamp) WHERE TimestampEpoch = 0");
        migrationBuilder.Sql(
            "UPDATE EvidenceChunks SET CreatedAtEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', CreatedAt) WHERE CreatedAtEpoch = 0");
        migrationBuilder.Sql(
            "UPDATE AnswerTraces SET CreatedAtEpoch = DATEDIFF_BIG(second, '1970-01-01 00:00:00 +00:00', CreatedAt) WHERE CreatedAtEpoch = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_AnswerTraces_TenantId_CreatedAtEpoch", table: "AnswerTraces");
        migrationBuilder.DropColumn(name: "CreatedAtEpoch", table: "AnswerTraces");

        migrationBuilder.DropIndex(name: "IX_EvidenceChunks_TenantId_CreatedAtEpoch", table: "EvidenceChunks");
        migrationBuilder.DropColumn(name: "CreatedAtEpoch", table: "EvidenceChunks");

        migrationBuilder.DropIndex(name: "IX_AuditEvents_TenantId_TimestampEpoch", table: "AuditEvents");
        migrationBuilder.DropColumn(name: "TimestampEpoch", table: "AuditEvents");

        migrationBuilder.DropIndex(name: "IX_Messages_TenantId_CreatedAtEpoch", table: "Messages");
        migrationBuilder.DropColumn(name: "CreatedAtEpoch", table: "Messages");

        migrationBuilder.DropIndex(name: "IX_Sessions_TenantId_CreatedAtEpoch", table: "Sessions");
        migrationBuilder.DropColumn(name: "CreatedAtEpoch", table: "Sessions");
    }
}
