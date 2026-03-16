using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

public partial class UpdateFeedbackSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "UserId",
            table: "Feedbacks",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "ReasonCodesJson",
            table: "Feedbacks",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Comment",
            table: "Feedbacks",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CorrelationId",
            table: "Feedbacks",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        // Migrate existing ReasonCode values into ReasonCodesJson.
        migrationBuilder.Sql(
            """
            UPDATE Feedbacks
            SET ReasonCodesJson = '["' + ReasonCode + '"]'
            WHERE ReasonCode IS NOT NULL AND ReasonCode <> ''
            """);

        migrationBuilder.DropColumn(
            name: "ReasonCode",
            table: "Feedbacks");

        migrationBuilder.CreateIndex(
            name: "IX_Feedbacks_TenantId_SessionId",
            table: "Feedbacks",
            columns: new[] { "TenantId", "SessionId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Feedbacks_TenantId_SessionId",
            table: "Feedbacks");

        migrationBuilder.AddColumn<string>(
            name: "ReasonCode",
            table: "Feedbacks",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.DropColumn(
            name: "ReasonCodesJson",
            table: "Feedbacks");

        migrationBuilder.DropColumn(
            name: "Comment",
            table: "Feedbacks");

        migrationBuilder.DropColumn(
            name: "CorrelationId",
            table: "Feedbacks");

        migrationBuilder.DropColumn(
            name: "UserId",
            table: "Feedbacks");
    }
}
