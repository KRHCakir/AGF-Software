using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentDeliveryConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShipmentDeliveryConfirmations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SalesDispatchId = table.Column<int>(type: "integer", nullable: true),
                    DirectTradeShipmentId = table.Column<int>(type: "integer", nullable: true),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    DeliveredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceiptReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeliveredInFullConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    ConfirmationCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConfirmationStatement = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentDeliveryConfirmations", x => x.Id);
                    table.CheckConstraint("CK_ShipmentDeliveryConfirmations_Code", "\"ConfirmationCode\" = 'DELIVERED_IN_FULL'");
                    table.CheckConstraint("CK_ShipmentDeliveryConfirmations_Confirmed", "\"DeliveredInFullConfirmed\" = TRUE");
                    table.CheckConstraint("CK_ShipmentDeliveryConfirmations_OneShipment", "(\"ShipmentKind\" = 'Warehouse' AND \"SalesDispatchId\" IS NOT NULL AND \"DirectTradeShipmentId\" IS NULL) OR (\"ShipmentKind\" = 'DirectTrade' AND \"DirectTradeShipmentId\" IS NOT NULL AND \"SalesDispatchId\" IS NULL)");
                    table.CheckConstraint("CK_ShipmentDeliveryConfirmations_Reference_NullOrNotBlank", "\"ReceiptReference\" IS NULL OR length(trim(\"ReceiptReference\")) > 0");
                    table.CheckConstraint("CK_ShipmentDeliveryConfirmations_Statement", "\"ConfirmationStatement\" = 'Deze verzending is volledig en zonder afwijkingen afgeleverd.'");
                    table.ForeignKey(
                        name: "FK_ShipmentDeliveryConfirmations_DirectTradeShipments_DirectTr~",
                        column: x => x.DirectTradeShipmentId,
                        principalTable: "DirectTradeShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentDeliveryConfirmations_SalesDispatches_SalesDispatch~",
                        column: x => x.SalesDispatchId,
                        principalTable: "SalesDispatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentDeliveryConfirmations_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentDeliveryConfirmations_DirectTradeShipmentId",
                table: "ShipmentDeliveryConfirmations",
                column: "DirectTradeShipmentId",
                unique: true,
                filter: "\"DirectTradeShipmentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentDeliveryConfirmations_RequestId",
                table: "ShipmentDeliveryConfirmations",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentDeliveryConfirmations_SalesDispatchId",
                table: "ShipmentDeliveryConfirmations",
                column: "SalesDispatchId",
                unique: true,
                filter: "\"SalesDispatchId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentDeliveryConfirmations_SalesOrderId",
                table: "ShipmentDeliveryConfirmations",
                column: "SalesOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentDeliveryConfirmations");
        }
    }
}
