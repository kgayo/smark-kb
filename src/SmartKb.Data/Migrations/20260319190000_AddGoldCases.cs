using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// P3-022: Add GoldCases table for in-app gold dataset management.
/// </summary>
public partial class AddGoldCases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "GoldCases",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CaseId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Query = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ContextJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ExpectedJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                TagsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                SourceFeedbackId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GoldCases", x => x.Id);
                table.ForeignKey(
                    name: "FK_GoldCases_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "TenantId");
            });

        migrationBuilder.CreateIndex(
            name: "IX_GoldCases_TenantId",
            table: "GoldCases",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_GoldCases_TenantId_CaseId",
            table: "GoldCases",
            columns: new[] { "TenantId", "CaseId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "GoldCases");
    }
}
