using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValuPro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RateMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CircleRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Scheme = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Locality = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Residential = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true),
                    Commercial = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true),
                    OpenLand = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_CircleRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConstructionRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConstructionType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CpwdRate = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true),
                    PwdRate = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true),
                    BankRate = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("PK_ConstructionRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DepreciationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StructureType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TotalLifeYears = table.Column<int>(type: "int", nullable: false),
                    SalvagePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("PK_DepreciationRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocationRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Locality = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Zone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MinRate = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    MaxRate = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_LocationRates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CircleRates_TenantId",
                table: "CircleRates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CircleRates_TenantId_State_Scheme_City_Locality_EffectiveFrom",
                table: "CircleRates",
                columns: new[] { "TenantId", "State", "Scheme", "City", "Locality", "EffectiveFrom" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionRates_TenantId",
                table: "ConstructionRates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ConstructionRates_TenantId_ConstructionType_Unit_EffectiveFrom",
                table: "ConstructionRates",
                columns: new[] { "TenantId", "ConstructionType", "Unit", "EffectiveFrom" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DepreciationRules_TenantId",
                table: "DepreciationRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DepreciationRules_TenantId_StructureType",
                table: "DepreciationRules",
                columns: new[] { "TenantId", "StructureType" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_LocationRates_TenantId",
                table: "LocationRates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationRates_TenantId_City_Locality_Zone_EffectiveFrom",
                table: "LocationRates",
                columns: new[] { "TenantId", "City", "Locality", "Zone", "EffectiveFrom" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CircleRates");

            migrationBuilder.DropTable(
                name: "ConstructionRates");

            migrationBuilder.DropTable(
                name: "DepreciationRules");

            migrationBuilder.DropTable(
                name: "LocationRates");
        }
    }
}
