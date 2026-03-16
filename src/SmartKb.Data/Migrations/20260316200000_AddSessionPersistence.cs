using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

public partial class AddSessionPersistence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Session: add Title, CustomerRef, ExpiresAt columns.
        migrationBuilder.AddColumn<string>(
            name: "Title",
            table: "Sessions",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CustomerRef",
            table: "Sessions",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ExpiresAt",
            table: "Sessions",
            type: "datetimeoffset",
            nullable: true);

        // Message: add CitationsJson, Confidence, ConfidenceLabel, ResponseType columns.
        migrationBuilder.AddColumn<string>(
            name: "CitationsJson",
            table: "Messages",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<float>(
            name: "Confidence",
            table: "Messages",
            type: "real",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ConfidenceLabel",
            table: "Messages",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ResponseType",
            table: "Messages",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ResponseType", table: "Messages");
        migrationBuilder.DropColumn(name: "ConfidenceLabel", table: "Messages");
        migrationBuilder.DropColumn(name: "Confidence", table: "Messages");
        migrationBuilder.DropColumn(name: "CitationsJson", table: "Messages");
        migrationBuilder.DropColumn(name: "ExpiresAt", table: "Sessions");
        migrationBuilder.DropColumn(name: "CustomerRef", table: "Sessions");
        migrationBuilder.DropColumn(name: "Title", table: "Sessions");
    }
}
