using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectTradeShipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_DirectTrade_StatusAllowed",
                table: "StockReservations");

            migrationBuilder.CreateTable(
                name: "DirectTradeShipments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    CustomerNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShippedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectTradeShipments", x => x.Id);
                    table.CheckConstraint("CK_DirectTradeShipments_CustomerName_NotBlank", "length(trim(\"CustomerNameSnapshot\")) > 0");
                    table.ForeignKey(
                        name: "FK_DirectTradeShipments_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DirectTradeShipments_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DirectTradeShipmentLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DirectTradeShipmentId = table.Column<int>(type: "integer", nullable: false),
                    StockReservationId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderLineId = table.Column<int>(type: "integer", nullable: false),
                    SupplyCommitmentLineId = table.Column<int>(type: "integer", nullable: false),
                    SupplyCommitmentId = table.Column<int>(type: "integer", nullable: false),
                    VendorId = table.Column<int>(type: "integer", nullable: false),
                    VendorNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OriginSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DestinationSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AllocatedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NetWeightKg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceLotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LotDeviationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExpectedReservationVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectTradeShipmentLines", x => x.Id);
                    table.CheckConstraint("CK_DirectTradeShipmentLines_Lot_NotBlank", "length(trim(\"LotNumber\")) > 0");
                    table.CheckConstraint("CK_DirectTradeShipmentLines_LotDeviation_Reason", "\"SourceLotNumber\" IS NULL OR \"LotNumber\" = \"SourceLotNumber\" OR (\"LotDeviationReason\" IS NOT NULL AND length(trim(\"LotDeviationReason\")) > 0)");
                    table.CheckConstraint("CK_DirectTradeShipmentLines_NetWeightKg_Positive", "CAST(\"NetWeightKg\" AS NUMERIC) > 0");
                    table.CheckConstraint("CK_DirectTradeShipmentLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    table.CheckConstraint("CK_DirectTradeShipmentLines_WholeAllocation", "CAST(\"Quantity\" AS NUMERIC) = CAST(\"AllocatedQuantity\" AS NUMERIC)");
                    table.ForeignKey(
                        name: "FK_DirectTradeShipmentLines_DirectTradeShipments_DirectTradeSh~",
                        column: x => x.DirectTradeShipmentId,
                        principalTable: "DirectTradeShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DirectTradeShipmentLines_SalesOrderLines_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DirectTradeShipmentLines_StockReservations_StockReservation~",
                        column: x => x.StockReservationId,
                        principalTable: "StockReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DirectTradeShipmentLines_SupplyCommitmentLines_SupplyCommit~",
                        column: x => x.SupplyCommitmentLineId,
                        principalTable: "SupplyCommitmentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_DirectTrade_StatusAllowed",
                table: "StockReservations",
                sql: "\"Source\" IS NULL OR \"Source\" <> 'DirectTrade' OR \"Status\" IN ('Active', 'Released', 'Consumed')");

            migrationBuilder.CreateIndex(
                name: "IX_DirectTradeShipmentLines_DirectTradeShipmentId",
                table: "DirectTradeShipmentLines",
                column: "DirectTradeShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectTradeShipmentLines_SalesOrderLineId",
                table: "DirectTradeShipmentLines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectTradeShipmentLines_StockReservationId",
                table: "DirectTradeShipmentLines",
                column: "StockReservationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectTradeShipmentLines_SupplyCommitmentLineId",
                table: "DirectTradeShipmentLines",
                column: "SupplyCommitmentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectTradeShipments_CustomerId",
                table: "DirectTradeShipments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectTradeShipments_RequestId",
                table: "DirectTradeShipments",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectTradeShipments_SalesOrderId",
                table: "DirectTradeShipments",
                column: "SalesOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DirectTradeShipmentLines");

            migrationBuilder.DropTable(
                name: "DirectTradeShipments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_DirectTrade_StatusAllowed",
                table: "StockReservations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_DirectTrade_StatusAllowed",
                table: "StockReservations",
                sql: "\"Source\" IS NULL OR \"Source\" <> 'DirectTrade' OR \"Status\" IN ('Active', 'Released')");
        }
    }
}
