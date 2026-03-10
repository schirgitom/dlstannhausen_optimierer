using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SoWeiT.Optimizer.Persistence.History.Data;

#nullable disable

namespace SoWeiT.Optimizer.Persistence.History.Migrations
{
    [DbContext(typeof(OptimizerHistoryDbContext))]
    [Migration("20260309194000_AddConsumedPowerToRequests")]
    public partial class AddConsumedPowerToRequests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ConsumedPowerWatt",
                table: "optimizer_requests",
                type: "double precision",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsumedPowerWatt",
                table: "optimizer_requests");
        }
    }
}
