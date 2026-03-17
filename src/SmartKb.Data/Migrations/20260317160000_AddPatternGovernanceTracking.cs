using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartKb.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPatternGovernanceTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "CasePatterns",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "CasePatterns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNotes",
                table: "CasePatterns",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "CasePatterns",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "CasePatterns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalNotes",
                table: "CasePatterns",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeprecatedAt",
                table: "CasePatterns",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeprecatedBy",
                table: "CasePatterns",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeprecationReason",
                table: "CasePatterns",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ReviewedAt", table: "CasePatterns");
            migrationBuilder.DropColumn(name: "ReviewedBy", table: "CasePatterns");
            migrationBuilder.DropColumn(name: "ReviewNotes", table: "CasePatterns");
            migrationBuilder.DropColumn(name: "ApprovedAt", table: "CasePatterns");
            migrationBuilder.DropColumn(name: "ApprovedBy", table: "CasePatterns");
            migrationBuilder.DropColumn(name: "ApprovalNotes", table: "CasePatterns");
            migrationBuilder.DropColumn(name: "DeprecatedAt", table: "CasePatterns");
            migrationBuilder.DropColumn(name: "DeprecatedBy", table: "CasePatterns");
            migrationBuilder.DropColumn(name: "DeprecationReason", table: "CasePatterns");
        }
    }
}
