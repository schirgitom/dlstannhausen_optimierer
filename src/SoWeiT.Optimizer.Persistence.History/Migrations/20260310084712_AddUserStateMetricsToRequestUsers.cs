using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoWeiT.Optimizer.Persistence.History.Migrations
{
    /// <inheritdoc />
    public partial class AddUserStateMetricsToRequestUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "FairnessFactor",
                table: "optimizer_request_users",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PvAssignedEnergyCumulative",
                table: "optimizer_request_users",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SwitchBudget",
                table: "optimizer_request_users",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SwitchState",
                table: "optimizer_request_users",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TotalConsumptionEnergyCumulative",
                table: "optimizer_request_users",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FairnessFactor",
                table: "optimizer_request_users");

            migrationBuilder.DropColumn(
                name: "PvAssignedEnergyCumulative",
                table: "optimizer_request_users");

            migrationBuilder.DropColumn(
                name: "SwitchBudget",
                table: "optimizer_request_users");

            migrationBuilder.DropColumn(
                name: "SwitchState",
                table: "optimizer_request_users");

            migrationBuilder.DropColumn(
                name: "TotalConsumptionEnergyCumulative",
                table: "optimizer_request_users");
        }
    }
}
