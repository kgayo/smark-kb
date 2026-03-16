using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

public partial class AddEscalationDrafts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EscalationDrafts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                CustomerSummary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                StepsToReproduce = table.Column<string>(type: "nvarchar(max)", nullable: false),
                LogsIdsRequested = table.Column<string>(type: "nvarchar(max)", nullable: false),
                SuspectedComponent = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Severity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                EvidenceLinksJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                TargetTeam = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ExportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EscalationDrafts", x => x.Id);
                table.ForeignKey(
                    name: "FK_EscalationDrafts_Sessions_SessionId",
                    column: x => x.SessionId,
                    principalTable: "Sessions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_EscalationDrafts_TenantId",
            table: "EscalationDrafts",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_EscalationDrafts_SessionId",
            table: "EscalationDrafts",
            column: "SessionId");

        migrationBuilder.CreateIndex(
            name: "IX_EscalationDrafts_TenantId_SessionId_CreatedAt",
            table: "EscalationDrafts",
            columns: new[] { "TenantId", "SessionId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_EscalationDrafts_TenantId_UserId_CreatedAt",
            table: "EscalationDrafts",
            columns: new[] { "TenantId", "UserId", "CreatedAt" });

        migrationBuilder.CreateTable(
            name: "EscalationRoutingRules",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                ProductArea = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                TargetTeam = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                EscalationThreshold = table.Column<float>(type: "real", nullable: false),
                MinSeverity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EscalationRoutingRules", x => x.Id);
                table.ForeignKey(
                    name: "FK_EscalationRoutingRules_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "TenantId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_EscalationRoutingRules_TenantId",
            table: "EscalationRoutingRules",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_EscalationRoutingRules_TenantId_ProductArea",
            table: "EscalationRoutingRules",
            columns: new[] { "TenantId", "ProductArea" },
            unique: true,
            filter: "[IsActive] = 1");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "EscalationRoutingRules");
        migrationBuilder.DropTable(name: "EscalationDrafts");
    }
}
