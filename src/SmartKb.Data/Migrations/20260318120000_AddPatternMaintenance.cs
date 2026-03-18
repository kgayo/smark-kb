using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// P2-004: Pattern maintenance automation and contradiction detection.
/// Adds PatternContradictions and PatternMaintenanceTasks tables.
/// </summary>
public partial class AddPatternMaintenance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PatternContradictions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                PatternIdA = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                PatternIdB = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                ContradictionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                SimilarityScore = table.Column<float>(type: "real", nullable: false),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ConflictingFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                Resolution = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                ResolvedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ResolutionNotes = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PatternContradictions", x => x.Id);
                table.ForeignKey(
                    name: "FK_PatternContradictions_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "TenantId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PatternMaintenanceTasks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                PatternId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                TaskType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Severity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                RecommendedAction = table.Column<string>(type: "nvarchar(max)", nullable: false),
                MetricsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ResolvedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ResolutionNotes = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PatternMaintenanceTasks", x => x.Id);
                table.ForeignKey(
                    name: "FK_PatternMaintenanceTasks_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "TenantId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PatternContradictions_TenantId",
            table: "PatternContradictions",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_PatternContradictions_TenantId_Status",
            table: "PatternContradictions",
            columns: new[] { "TenantId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_PatternContradictions_TenantId_PatternIdA",
            table: "PatternContradictions",
            columns: new[] { "TenantId", "PatternIdA" });

        migrationBuilder.CreateIndex(
            name: "IX_PatternContradictions_TenantId_PatternIdB",
            table: "PatternContradictions",
            columns: new[] { "TenantId", "PatternIdB" });

        migrationBuilder.CreateIndex(
            name: "IX_PatternMaintenanceTasks_TenantId",
            table: "PatternMaintenanceTasks",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_PatternMaintenanceTasks_TenantId_Status",
            table: "PatternMaintenanceTasks",
            columns: new[] { "TenantId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_PatternMaintenanceTasks_TenantId_PatternId",
            table: "PatternMaintenanceTasks",
            columns: new[] { "TenantId", "PatternId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PatternMaintenanceTasks");
        migrationBuilder.DropTable(name: "PatternContradictions");
    }
}
