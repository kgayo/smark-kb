using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutingRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoutingRecommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecommendationType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProductArea = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CurrentTargetTeam = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SuggestedTargetTeam = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CurrentThreshold = table.Column<float>(type: "real", nullable: true),
                    SuggestedThreshold = table.Column<float>(type: "real", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Confidence = table.Column<float>(type: "real", nullable: false),
                    SupportingOutcomeCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AppliedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DismissedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DismissedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutingRecommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoutingRecommendations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRecommendations_TenantId",
                table: "RoutingRecommendations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRecommendations_TenantId_Status",
                table: "RoutingRecommendations",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRecommendations_TenantId_ProductArea",
                table: "RoutingRecommendations",
                columns: new[] { "TenantId", "ProductArea" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RoutingRecommendations");
        }
    }
}
