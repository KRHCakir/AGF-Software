using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectTradeShipmentCorrection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_DirectTrade_StatusAllowed",
                table: "StockReservations");

            migrationBuilder.CreateTable(
                name: "DirectTradeShipmentCorrections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    DirectTradeShipmentId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShipmentDidNotTakePlaceConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    ConfirmationCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConfirmationStatement = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CorrectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectTradeShipmentCorrections", x => x.Id);
                    table.CheckConstraint("CK_DirectTradeShipmentCorrections_Code", "\"ConfirmationCode\" = 'SHIPMENT_DID_NOT_TAKE_PLACE'");
                    table.CheckConstraint("CK_DirectTradeShipmentCorrections_Confirmed", "\"ShipmentDidNotTakePlaceConfirmed\" = TRUE");
                    table.CheckConstraint("CK_DirectTradeShipmentCorrections_Reason_NotBlank", "length(trim(\"Reason\")) > 0");
                    table.CheckConstraint("CK_DirectTradeShipmentCorrections_Statement", "\"ConfirmationStatement\" = 'Deze geregistreerde verzending heeft niet plaatsgevonden.'");
                    table.ForeignKey(
                        name: "FK_DirectTradeShipmentCorrections_DirectTradeShipments_DirectT~",
                        column: x => x.DirectTradeShipmentId,
                        principalTable: "DirectTradeShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DirectTradeShipmentCorrections_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DirectTradeShipmentCorrectionLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DirectTradeShipmentCorrectionId = table.Column<int>(type: "integer", nullable: false),
                    DirectTradeShipmentLineId = table.Column<int>(type: "integer", nullable: false),
                    StockReservationId = table.Column<int>(type: "integer", nullable: false),
                    SupplyCommitmentLineId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderLineId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NetWeightKg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ExpectedReservationVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectTradeShipmentCorrectionLines", x => x.Id);
                    table.CheckConstraint("CK_DirectTradeShipmentCorrectionLines_NetWeightKg_Positive", "CAST(\"NetWeightKg\" AS NUMERIC) > 0");
                    table.CheckConstraint("CK_DirectTradeShipmentCorrectionLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    table.ForeignKey(
                        name: "FK_DirectTradeShipmentCorrectionLines_DirectTradeShipmentCorre~",
                        column: x => x.DirectTradeShipmentCorrectionId,
                        principalTable: "DirectTradeShipmentCorrections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DirectTradeShipmentCorrectionLines_DirectTradeShipmentLines~",
                        column: x => x.DirectTradeShipmentLineId,
                        principalTable: "DirectTradeShipmentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DirectTradeShipmentCorrectionLines_StockReservations_StockR~",
                        column: x => x.StockReservationId,
                        principalTable: "StockReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_DirectTrade_StatusAllowed",
                table: "StockReservations",
                sql: "\"Source\" IS NULL OR \"Source\" <> 'DirectTrade' OR \"Status\" IN ('Active', 'Released', 'Consumed', 'Corrected')");

            migrationBuilder.CreateIndex(
                name: "IX_DirectTradeShipmentCorrectionLines_DirectTradeShipmentCorre~",
                table: "DirectTradeShipmentCorrectionLines",
                column: "DirectTradeShipmentCorrectionId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectTradeShipmentCorrectionLines_DirectTradeShipmentLineId",
                table: "DirectTradeShipmentCorrectionLines",
                column: "DirectTradeShipmentLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectTradeShipmentCorrectionLines_StockReservationId",
                table: "DirectTradeShipmentCorrectionLines",
                column: "StockReservationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectTradeShipmentCorrections_DirectTradeShipmentId",
                table: "DirectTradeShipmentCorrections",
                column: "DirectTradeShipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectTradeShipmentCorrections_RequestId",
                table: "DirectTradeShipmentCorrections",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DirectTradeShipmentCorrections_SalesOrderId",
                table: "DirectTradeShipmentCorrections",
                column: "SalesOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DirectTradeShipmentCorrectionLines");

            migrationBuilder.DropTable(
                name: "DirectTradeShipmentCorrections");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_DirectTrade_StatusAllowed",
                table: "StockReservations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_DirectTrade_StatusAllowed",
                table: "StockReservations",
                sql: "\"Source\" IS NULL OR \"Source\" <> 'DirectTrade' OR \"Status\" IN ('Active', 'Released', 'Consumed')");
        }
    }
}
