using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations;

/// <inheritdoc />
public partial class AddCostOptimization : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TokenUsages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                PromptTokens = table.Column<int>(type: "int", nullable: false),
                CompletionTokens = table.Column<int>(type: "int", nullable: false),
                TotalTokens = table.Column<int>(type: "int", nullable: false),
                EmbeddingTokens = table.Column<int>(type: "int", nullable: false),
                EmbeddingCacheHit = table.Column<bool>(type: "bit", nullable: false),
                EvidenceChunksUsed = table.Column<int>(type: "int", nullable: false),
                EstimatedCostUsd = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TokenUsages", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TokenUsages_TenantId",
            table: "TokenUsages",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_TokenUsages_CorrelationId",
            table: "TokenUsages",
            column: "CorrelationId");

        migrationBuilder.CreateIndex(
            name: "IX_TokenUsages_TenantId_CreatedAt",
            table: "TokenUsages",
            columns: new[] { "TenantId", "CreatedAt" });

        migrationBuilder.CreateTable(
            name: "TenantCostSettings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                DailyTokenBudget = table.Column<long>(type: "bigint", nullable: true),
                MonthlyTokenBudget = table.Column<long>(type: "bigint", nullable: true),
                MaxPromptTokensPerQuery = table.Column<int>(type: "int", nullable: true),
                MaxEvidenceChunksInPrompt = table.Column<int>(type: "int", nullable: true),
                EnableEmbeddingCache = table.Column<bool>(type: "bit", nullable: true),
                EmbeddingCacheTtlHours = table.Column<int>(type: "int", nullable: true),
                EnableRetrievalCompression = table.Column<bool>(type: "bit", nullable: true),
                MaxChunkCharsCompressed = table.Column<int>(type: "int", nullable: true),
                BudgetAlertThresholdPercent = table.Column<int>(type: "int", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TenantCostSettings", x => x.Id);
                table.ForeignKey(
                    name: "FK_TenantCostSettings_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "TenantId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TenantCostSettings_TenantId",
            table: "TenantCostSettings",
            column: "TenantId",
            unique: true);

        migrationBuilder.CreateTable(
            name: "EmbeddingCache",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                InputText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                EmbeddingJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ModelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Dimensions = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                LastAccessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EmbeddingCache", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_EmbeddingCache_ContentHash",
            table: "EmbeddingCache",
            column: "ContentHash");

        migrationBuilder.CreateIndex(
            name: "IX_EmbeddingCache_ExpiresAt",
            table: "EmbeddingCache",
            column: "ExpiresAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "EmbeddingCache");
        migrationBuilder.DropTable(name: "TenantCostSettings");
        migrationBuilder.DropTable(name: "TokenUsages");
    }
}
