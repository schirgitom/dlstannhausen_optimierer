using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SoWeiT.Optimizer.Persistence.History.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "optimizer_sessions",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    N = table.Column<int>(type: "integer", nullable: false),
                    Sperrzeit1 = table.Column<int>(type: "integer", nullable: false),
                    Sperrzeit2 = table.Column<int>(type: "integer", nullable: false),
                    UseOrTools = table.Column<bool>(type: "boolean", nullable: false),
                    UseGreedyFallback = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_optimizer_sessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "optimizer_requests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AvailablePvPowerWatt = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_optimizer_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_optimizer_requests_optimizer_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "optimizer_sessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "optimizer_request_users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestEntryId = table.Column<long>(type: "bigint", nullable: false),
                    UserIndex = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    RequiredPowerWatt = table.Column<double>(type: "double precision", nullable: false),
                    IsSwitchAllowed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_optimizer_request_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_optimizer_request_users_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_optimizer_request_users_optimizer_requests_RequestEntryId",
                        column: x => x.RequestEntryId,
                        principalTable: "optimizer_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customers_Name",
                table: "customers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_optimizer_request_users_CustomerId",
                table: "optimizer_request_users",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_optimizer_request_users_RequestEntryId",
                table: "optimizer_request_users",
                column: "RequestEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_optimizer_request_users_RequestEntryId_UserIndex",
                table: "optimizer_request_users",
                columns: new[] { "RequestEntryId", "UserIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_optimizer_requests_RequestTimestamp",
                table: "optimizer_requests",
                column: "RequestTimestamp");

            migrationBuilder.CreateIndex(
                name: "IX_optimizer_requests_SessionId",
                table: "optimizer_requests",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_optimizer_sessions_CreatedAtUtc",
                table: "optimizer_sessions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_optimizer_sessions_EndedAtUtc",
                table: "optimizer_sessions",
                column: "EndedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "optimizer_request_users");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "optimizer_requests");

            migrationBuilder.DropTable(
                name: "optimizer_sessions");
        }
    }
}
