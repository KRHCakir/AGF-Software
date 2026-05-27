using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class Tier2BatchTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShipmentId",
                table: "Inventory",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderLineAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderLineId = table.Column<int>(type: "integer", nullable: false),
                    InventoryId = table.Column<int>(type: "integer", nullable: false),
                    AllocatedQuantity = table.Column<int>(type: "integer", nullable: false),
                    AllocatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLineAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderLineAllocations_Inventory_InventoryId",
                        column: x => x.InventoryId,
                        principalTable: "Inventory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderLineAllocations_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Inventory_ShipmentId",
                table: "Inventory",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLineAllocations_InventoryId",
                table: "OrderLineAllocations",
                column: "InventoryId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLineAllocations_OrderLineId",
                table: "OrderLineAllocations",
                column: "OrderLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_Inventory_Shipments_ShipmentId",
                table: "Inventory",
                column: "ShipmentId",
                principalTable: "Shipments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Inventory_Shipments_ShipmentId",
                table: "Inventory");

            migrationBuilder.DropTable(
                name: "OrderLineAllocations");

            migrationBuilder.DropIndex(
                name: "IX_Inventory_ShipmentId",
                table: "Inventory");

            migrationBuilder.DropColumn(
                name: "ShipmentId",
                table: "Inventory");
        }
    }
}
