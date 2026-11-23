using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValuPro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BrandingVersion",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RegisteredValuerNo",
                table: "Tenants",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Tenants",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FirmSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SmtpHost = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SmtpPort = table.Column<int>(type: "int", nullable: false),
                    SmtpUseSsl = table.Column<bool>(type: "bit", nullable: false),
                    SmtpUsername = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SmtpPasswordEncrypted = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FromEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FromName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SmsProvider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SmsSenderId = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    SmsApiKeyEncrypted = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Gstin = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    DefaultGstRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    StateCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    PlaceOfSupply = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    InvoicePrefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AssignmentPrefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notifications = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FirmSettings_TenantId",
                table: "FirmSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FirmSettings");

            migrationBuilder.DropColumn(
                name: "BrandingVersion",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "RegisteredValuerNo",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Tenants");
        }
    }
}
