using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEvidenceChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvidenceChunks",
                columns: table => new
                {
                    ChunkId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    EvidenceId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ConnectorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChunkIndex = table.Column<int>(type: "int", nullable: false),
                    ChunkText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChunkContext = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    SourceSystem = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProductArea = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Visibility = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AllowedGroups = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AccessLabel = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ErrorTokens = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EnrichmentVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    ContentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReprocessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceChunks", x => x.ChunkId);
                    table.ForeignKey(
                        name: "FK_EvidenceChunks_Connectors_ConnectorId",
                        column: x => x.ConnectorId,
                        principalTable: "Connectors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceChunks_TenantId",
                table: "EvidenceChunks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceChunks_EvidenceId",
                table: "EvidenceChunks",
                column: "EvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceChunks_ConnectorId",
                table: "EvidenceChunks",
                column: "ConnectorId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceChunks_TenantId_EvidenceId",
                table: "EvidenceChunks",
                columns: new[] { "TenantId", "EvidenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceChunks_EvidenceId_ContentHash",
                table: "EvidenceChunks",
                columns: new[] { "EvidenceId", "ContentHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvidenceChunks");
        }
    }
}
