using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

public partial class AddScheduledSyncTracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastScheduledSyncAt",
            table: "Connectors",
            type: "datetimeoffset",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LastScheduledSyncAt",
            table: "Connectors");
    }
}
