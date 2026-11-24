using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValuPro.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhotoManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DistanceFromPropertyMetres",
                table: "AssignmentPhotos",
                type: "int",
                nullable: true);

            // Existing photos (all from site inspections) stay in the report.
            migrationBuilder.AddColumn<bool>(
                name: "IncludeInReport",
                table: "AssignmentPhotos",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "AssignmentPhotos",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Site");

            migrationBuilder.AddColumn<bool>(
                name: "Stamped",
                table: "AssignmentPhotos",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DistanceFromPropertyMetres",
                table: "AssignmentPhotos");

            migrationBuilder.DropColumn(
                name: "IncludeInReport",
                table: "AssignmentPhotos");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "AssignmentPhotos");

            migrationBuilder.DropColumn(
                name: "Stamped",
                table: "AssignmentPhotos");
        }
    }
}
