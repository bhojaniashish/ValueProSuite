using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ValuPro.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Replaces Users.IsActive with Status (Active / OnLeave / Inactive) and adds MustChangePassword.
    /// Hand-edited: EF proposed renaming IsActive to MustChangePassword, which would have forced
    /// every active user to change password and left Status blank.
    /// </summary>
    public partial class UserStatusAndPasswordChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE Users SET Status = 'Inactive' WHERE IsActive = 0;");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("UPDATE Users SET IsActive = 0 WHERE Status = 'Inactive';");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Users");
        }
    }
}
