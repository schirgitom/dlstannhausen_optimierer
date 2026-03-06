using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SoWeiT.Optimizer.Persistence.History.Data;

#nullable disable

namespace SoWeiT.Optimizer.Persistence.History.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(OptimizerHistoryDbContext))]
    [Migration("20260306170000_ConvertRequestsToHypertables")]
    public partial class ConvertRequestsToHypertables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS timescaledb;");
    
            migrationBuilder.Sql(
                """
                SELECT create_hypertable(
                    'optimizer_requests',
                    by_range('Id', 100000),
                    if_not_exists => TRUE
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
