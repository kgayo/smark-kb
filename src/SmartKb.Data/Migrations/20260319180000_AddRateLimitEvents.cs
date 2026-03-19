using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// P3-020: Add RateLimitEvents table for tracking HTTP 429 rate-limit hits per connector.
/// </summary>
public partial class AddRateLimitEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RateLimitEvents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ConnectorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ConnectorType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RateLimitEvents", x => x.Id);
                table.ForeignKey(
                    name: "FK_RateLimitEvents_Connectors_ConnectorId",
                    column: x => x.ConnectorId,
                    principalTable: "Connectors",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RateLimitEvents_TenantId_OccurredAt",
            table: "RateLimitEvents",
            columns: new[] { "TenantId", "OccurredAt" });

        migrationBuilder.CreateIndex(
            name: "IX_RateLimitEvents_ConnectorId",
            table: "RateLimitEvents",
            column: "ConnectorId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "RateLimitEvents");
    }
}
