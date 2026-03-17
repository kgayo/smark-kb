using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCasePatterns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CasePatterns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatternId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ProblemStatement = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SymptomsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DiagnosisStepsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResolutionStepsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VerificationStepsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Workaround = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EscalationCriteriaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EscalationTargetTeam = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RelatedEvidenceIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Confidence = table.Column<float>(type: "real", nullable: false),
                    TrustLevel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    SupersedesPatternId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApplicabilityConstraintsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExclusionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProductArea = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TagsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Visibility = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AllowedGroupsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AccessLabel = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CasePatterns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CasePatterns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CasePatterns_TenantId",
                table: "CasePatterns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CasePatterns_PatternId",
                table: "CasePatterns",
                column: "PatternId",
                unique: true,
                filter: "[DeletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CasePatterns_TenantId_TrustLevel",
                table: "CasePatterns",
                columns: new[] { "TenantId", "TrustLevel" });

            migrationBuilder.CreateIndex(
                name: "IX_CasePatterns_TenantId_ProductArea",
                table: "CasePatterns",
                columns: new[] { "TenantId", "ProductArea" });

            migrationBuilder.CreateIndex(
                name: "IX_CasePatterns_TenantId_UpdatedAt",
                table: "CasePatterns",
                columns: new[] { "TenantId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CasePatterns");
        }
    }
}
