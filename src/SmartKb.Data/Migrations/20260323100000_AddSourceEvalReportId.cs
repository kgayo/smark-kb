using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// P3-023: Add SourceEvalReportId FK to RoutingRecommendations for eval-to-improvement traceability.
/// </summary>
public partial class AddSourceEvalReportId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "SourceEvalReportId",
            table: "RoutingRecommendations",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_RoutingRecommendations_SourceEvalReportId",
            table: "RoutingRecommendations",
            column: "SourceEvalReportId");

        migrationBuilder.AddForeignKey(
            name: "FK_RoutingRecommendations_EvalReports_SourceEvalReportId",
            table: "RoutingRecommendations",
            column: "SourceEvalReportId",
            principalTable: "EvalReports",
            principalColumn: "Id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_RoutingRecommendations_EvalReports_SourceEvalReportId",
            table: "RoutingRecommendations");

        migrationBuilder.DropIndex(
            name: "IX_RoutingRecommendations_SourceEvalReportId",
            table: "RoutingRecommendations");

        migrationBuilder.DropColumn(
            name: "SourceEvalReportId",
            table: "RoutingRecommendations");
    }
}
