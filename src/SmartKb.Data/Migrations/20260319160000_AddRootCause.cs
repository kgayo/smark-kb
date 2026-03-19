using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// P3-014: Add explicit RootCause field to case pattern schema.
/// Separates root cause from ProblemStatement for clearer pattern structure.
/// </summary>
public partial class AddRootCause : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RootCause",
            table: "CasePatterns",
            type: "nvarchar(2000)",
            maxLength: 2000,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RootCause",
            table: "CasePatterns");
    }
}
