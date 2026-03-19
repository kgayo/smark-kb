using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// P3-013: Add PatternVersionHistory table for field-level change tracking on case patterns.
/// </summary>
public partial class AddPatternVersionHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PatternVersionHistory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                PatternId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Version = table.Column<int>(type: "int", nullable: false),
                ChangedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ChangedFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                PreviousValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ChangeType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Summary = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PatternVersionHistory", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PatternVersionHistory_TenantId_PatternId",
            table: "PatternVersionHistory",
            columns: new[] { "TenantId", "PatternId" });

        migrationBuilder.CreateIndex(
            name: "IX_PatternVersionHistory_TenantId_ChangedAt",
            table: "PatternVersionHistory",
            columns: new[] { "TenantId", "ChangedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PatternVersionHistory");
    }
}
