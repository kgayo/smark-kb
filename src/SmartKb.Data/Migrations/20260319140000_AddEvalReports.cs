using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// P3-021: Persist eval report history and implement eval API.
/// Adds EvalReports table for storing eval run results, metrics, violations, and baseline comparisons.
/// </summary>
public partial class AddEvalReports : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EvalReports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RunId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                RunType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                TotalCases = table.Column<int>(type: "int", nullable: false),
                SuccessfulCases = table.Column<int>(type: "int", nullable: false),
                FailedCases = table.Column<int>(type: "int", nullable: false),
                MetricsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ViolationsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                BaselineComparisonJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                HasBlockingRegression = table.Column<bool>(type: "bit", nullable: false),
                ViolationCount = table.Column<int>(type: "int", nullable: false),
                TriggeredBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EvalReports", x => x.Id);
                table.ForeignKey(
                    name: "FK_EvalReports_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "TenantId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_EvalReports_TenantId",
            table: "EvalReports",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_EvalReports_TenantId_CreatedAt",
            table: "EvalReports",
            columns: new[] { "TenantId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_EvalReports_TenantId_RunType",
            table: "EvalReports",
            columns: new[] { "TenantId", "RunType" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "EvalReports");
    }
}
