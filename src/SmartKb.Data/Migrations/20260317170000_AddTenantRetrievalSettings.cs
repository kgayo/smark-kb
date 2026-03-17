using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <inheritdoc />
public partial class AddTenantRetrievalSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TenantRetrievalSettings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                TopK = table.Column<int>(type: "int", nullable: true),
                EnableSemanticReranking = table.Column<bool>(type: "bit", nullable: true),
                EnablePatternFusion = table.Column<bool>(type: "bit", nullable: true),
                PatternTopK = table.Column<int>(type: "int", nullable: true),
                TrustBoostApproved = table.Column<float>(type: "real", nullable: true),
                TrustBoostReviewed = table.Column<float>(type: "real", nullable: true),
                TrustBoostDraft = table.Column<float>(type: "real", nullable: true),
                RecencyBoostRecent = table.Column<float>(type: "real", nullable: true),
                RecencyBoostOld = table.Column<float>(type: "real", nullable: true),
                PatternAuthorityBoost = table.Column<float>(type: "real", nullable: true),
                DiversityMaxPerSource = table.Column<int>(type: "int", nullable: true),
                NoEvidenceScoreThreshold = table.Column<float>(type: "real", nullable: true),
                NoEvidenceMinResults = table.Column<int>(type: "int", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TenantRetrievalSettings", x => x.Id);
                table.ForeignKey(
                    name: "FK_TenantRetrievalSettings_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "TenantId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TenantRetrievalSettings_TenantId",
            table: "TenantRetrievalSettings",
            column: "TenantId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TenantRetrievalSettings");
    }
}
