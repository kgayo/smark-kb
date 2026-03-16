using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnswerTraces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnswerTraces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Query = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Confidence = table.Column<float>(type: "real", nullable: false),
                    ConfidenceLabel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CitedChunkIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RetrievedChunkIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RetrievedChunkCount = table.Column<int>(type: "int", nullable: false),
                    AclFilteredOutCount = table.Column<int>(type: "int", nullable: false),
                    HasEvidence = table.Column<bool>(type: "bit", nullable: false),
                    EscalationRecommended = table.Column<bool>(type: "bit", nullable: false),
                    SystemPromptVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnswerTraces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnswerTraces_TenantId",
                table: "AnswerTraces",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AnswerTraces_CorrelationId",
                table: "AnswerTraces",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AnswerTraces_TenantId_CreatedAt",
                table: "AnswerTraces",
                columns: new[] { "TenantId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnswerTraces");
        }
    }
}
