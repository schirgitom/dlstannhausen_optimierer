using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoWeiT.Optimizer.Persistence.History.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceSwitchStateWithShouldSwitch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SwitchState",
                table: "optimizer_request_users");

            migrationBuilder.AddColumn<bool>(
                name: "ShouldSwitch",
                table: "optimizer_request_users",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShouldSwitch",
                table: "optimizer_request_users");

            migrationBuilder.AddColumn<double>(
                name: "SwitchState",
                table: "optimizer_request_users",
                type: "double precision",
                nullable: true);
        }
    }
}
