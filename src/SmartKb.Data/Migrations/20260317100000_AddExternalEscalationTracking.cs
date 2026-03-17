using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// Adds external escalation creation tracking fields to EscalationDrafts table (P1-003).
/// </summary>
public partial class AddExternalEscalationTracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ApprovedAt",
            table: "EscalationDrafts",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ApprovedBy",
            table: "EscalationDrafts",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "TargetConnectorId",
            table: "EscalationDrafts",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TargetConnectorType",
            table: "EscalationDrafts",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ExternalId",
            table: "EscalationDrafts",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ExternalUrl",
            table: "EscalationDrafts",
            type: "nvarchar(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ExternalStatus",
            table: "EscalationDrafts",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ExternalErrorDetail",
            table: "EscalationDrafts",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_EscalationDrafts_TargetConnectorId",
            table: "EscalationDrafts",
            column: "TargetConnectorId");

        migrationBuilder.AddForeignKey(
            name: "FK_EscalationDrafts_Connectors_TargetConnectorId",
            table: "EscalationDrafts",
            column: "TargetConnectorId",
            principalTable: "Connectors",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_EscalationDrafts_Connectors_TargetConnectorId",
            table: "EscalationDrafts");

        migrationBuilder.DropIndex(
            name: "IX_EscalationDrafts_TargetConnectorId",
            table: "EscalationDrafts");

        migrationBuilder.DropColumn(name: "ApprovedAt", table: "EscalationDrafts");
        migrationBuilder.DropColumn(name: "ApprovedBy", table: "EscalationDrafts");
        migrationBuilder.DropColumn(name: "TargetConnectorId", table: "EscalationDrafts");
        migrationBuilder.DropColumn(name: "TargetConnectorType", table: "EscalationDrafts");
        migrationBuilder.DropColumn(name: "ExternalId", table: "EscalationDrafts");
        migrationBuilder.DropColumn(name: "ExternalUrl", table: "EscalationDrafts");
        migrationBuilder.DropColumn(name: "ExternalStatus", table: "EscalationDrafts");
        migrationBuilder.DropColumn(name: "ExternalErrorDetail", table: "EscalationDrafts");
    }
}
