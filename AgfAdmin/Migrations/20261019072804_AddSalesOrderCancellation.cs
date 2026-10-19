using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "SalesOrders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelRequestId",
                table: "SalesOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "SalesOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesOrderAuditEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OldValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderAuditEvents_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CancelRequestId",
                table: "SalesOrders",
                column: "CancelRequestId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrders_CancelledConsistent",
                table: "SalesOrders",
                sql: "\"Status\" <> 'Cancelled' OR (\"CancelledAt\" IS NOT NULL AND \"CancelReason\" IS NOT NULL AND length(trim(\"CancelReason\")) > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderAuditEvents_RequestId",
                table: "SalesOrderAuditEvents",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderAuditEvents_SalesOrderId",
                table: "SalesOrderAuditEvents",
                column: "SalesOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesOrderAuditEvents");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_CancelRequestId",
                table: "SalesOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrders_CancelledConsistent",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "CancelRequestId",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "SalesOrders");
        }
    }
}
