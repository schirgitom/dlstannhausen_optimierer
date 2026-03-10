using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SoWeiT.Optimizer.Persistence.History.Data;

#nullable disable

namespace SoWeiT.Optimizer.Persistence.History.Migrations
{
    [DbContext(typeof(OptimizerHistoryDbContext))]
    [Migration("20260309200500_AddTotalRequiredPowerToRequests")]
    public partial class AddTotalRequiredPowerToRequests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "TotalRequiredPowerWatt",
                table: "optimizer_requests",
                type: "double precision",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalRequiredPowerWatt",
                table: "optimizer_requests");
        }
    }
}
