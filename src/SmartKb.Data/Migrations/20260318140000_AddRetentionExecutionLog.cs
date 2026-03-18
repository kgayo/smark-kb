using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// P2-005: Configurable data retention policies with measurable execution.
/// Adds RetentionExecutionLogs table and MetricRetentionDays column.
/// </summary>
public partial class AddRetentionExecutionLog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "MetricRetentionDays",
            table: "RetentionConfigs",
            type: "int",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "RetentionExecutionLogs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                EntityType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                DeletedCount = table.Column<int>(type: "int", nullable: false),
                CutoffDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ExecutedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                DurationMs = table.Column<long>(type: "bigint", nullable: false),
                ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetentionExecutionLogs", x => x.Id);
                table.ForeignKey(
                    name: "FK_RetentionExecutionLogs_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "TenantId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RetentionExecutionLogs_TenantId",
            table: "RetentionExecutionLogs",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_RetentionExecutionLogs_TenantId_ExecutedAt",
            table: "RetentionExecutionLogs",
            columns: new[] { "TenantId", "ExecutedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_RetentionExecutionLogs_TenantId_EntityType",
            table: "RetentionExecutionLogs",
            columns: new[] { "TenantId", "EntityType" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "RetentionExecutionLogs");

        migrationBuilder.DropColumn(
            name: "MetricRetentionDays",
            table: "RetentionConfigs");
    }
}
