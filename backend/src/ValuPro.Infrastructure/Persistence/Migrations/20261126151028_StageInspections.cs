using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValuPro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StageInspections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CaseType",
                table: "Assignments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Valuation");

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousAssignmentId",
                table: "Assignments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_PreviousAssignmentId",
                table: "Assignments",
                column: "PreviousAssignmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assignments_PreviousAssignmentId",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "CaseType",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "PreviousAssignmentId",
                table: "Assignments");
        }
    }
}
