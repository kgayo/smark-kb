using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <summary>
/// P3-005: Add IndexSchemaVersions table for search index schema versioning and rollback.
/// Tracks blue-green index versions with status lifecycle (Active → Migrating → Retired).
/// </summary>
public partial class AddIndexSchemaVersions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "IndexSchemaVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IndexType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                IndexName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Version = table.Column<int>(type: "int", nullable: false),
                SchemaHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                DocumentCount = table.Column<int>(type: "int", nullable: true),
                CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ActivatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                RetiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IndexSchemaVersions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_IndexSchemaVersions_IndexType",
            table: "IndexSchemaVersions",
            column: "IndexType");

        migrationBuilder.CreateIndex(
            name: "IX_IndexSchemaVersions_IndexType_Status",
            table: "IndexSchemaVersions",
            columns: new[] { "IndexType", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_IndexSchemaVersions_IndexName",
            table: "IndexSchemaVersions",
            column: "IndexName",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "IndexSchemaVersions");
    }
}
