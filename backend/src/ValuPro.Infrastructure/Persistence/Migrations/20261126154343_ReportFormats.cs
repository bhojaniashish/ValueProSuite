using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValuPro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReportFormats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PanelCode",
                table: "Banks",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReportFormatId",
                table: "Banks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReportFormats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Subtitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AddressTo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Layout = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Declaration = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LimitingConditions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    OpinionWording = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportFormats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Banks_ReportFormatId",
                table: "Banks",
                column: "ReportFormatId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportFormats_TenantId",
                table: "ReportFormats",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Banks_ReportFormats_ReportFormatId",
                table: "Banks",
                column: "ReportFormatId",
                principalTable: "ReportFormats",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // The bank form used to take a free-text format name: each distinct name becomes a format (standard layout,
            // blank Layout = standard) and the banks are linked to it, so nothing a firm entered is lost.
            migrationBuilder.Sql("""
                INSERT INTO ReportFormats (Id, Name, IsDefault, Layout, CreatedAt, TenantId, IsDeleted)
                SELECT NEWID(), x.Name, 0, N'', SYSDATETIMEOFFSET(), x.TenantId, 0
                FROM (SELECT DISTINCT TenantId, LTRIM(RTRIM(ReportFormat)) AS Name
                      FROM Banks WHERE ReportFormat IS NOT NULL AND LTRIM(RTRIM(ReportFormat)) <> N'') x;
                UPDATE b SET ReportFormatId = f.Id
                FROM Banks b JOIN ReportFormats f ON f.TenantId = b.TenantId AND f.Name = LTRIM(RTRIM(b.ReportFormat));
                """);

            migrationBuilder.DropColumn(
                name: "ReportFormat",
                table: "Banks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Banks_ReportFormats_ReportFormatId",
                table: "Banks");

            migrationBuilder.DropTable(
                name: "ReportFormats");

            migrationBuilder.DropIndex(
                name: "IX_Banks_ReportFormatId",
                table: "Banks");

            migrationBuilder.DropColumn(
                name: "PanelCode",
                table: "Banks");

            migrationBuilder.DropColumn(
                name: "ReportFormatId",
                table: "Banks");

            migrationBuilder.AddColumn<string>(
                name: "ReportFormat",
                table: "Banks",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
