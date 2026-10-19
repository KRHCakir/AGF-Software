using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesDispatchCorrection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_Status_Allowed",
                table: "StockReservations");

            migrationBuilder.AddColumn<DateTime>(
                name: "CorrectedAt",
                table: "StockReservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesDispatchCorrectionLineId",
                table: "StockMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginCorrectionLineId",
                table: "PalletContentLines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesDispatchCorrections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesDispatchId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GoodsNeverLeftWarehouseConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    CorrectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesDispatchCorrections", x => x.Id);
                    table.CheckConstraint("CK_SalesDispatchCorrections_Confirmed", "\"GoodsNeverLeftWarehouseConfirmed\" = TRUE");
                    table.ForeignKey(
                        name: "FK_SalesDispatchCorrections_SalesDispatches_SalesDispatchId",
                        column: x => x.SalesDispatchId,
                        principalTable: "SalesDispatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesDispatchCorrections_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesDispatchCorrectionLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SalesDispatchCorrectionId = table.Column<int>(type: "integer", nullable: false),
                    SalesDispatchLineId = table.Column<int>(type: "integer", nullable: false),
                    StockReservationId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NetWeightKg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ExpectedReservationVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesDispatchCorrectionLines", x => x.Id);
                    table.CheckConstraint("CK_SalesDispatchCorrectionLines_NetWeightKg_Positive", "CAST(\"NetWeightKg\" AS NUMERIC) > 0");
                    table.CheckConstraint("CK_SalesDispatchCorrectionLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    table.ForeignKey(
                        name: "FK_SalesDispatchCorrectionLines_SalesDispatchCorrections_Sales~",
                        column: x => x.SalesDispatchCorrectionId,
                        principalTable: "SalesDispatchCorrections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesDispatchCorrectionLines_SalesDispatchLines_SalesDispat~",
                        column: x => x.SalesDispatchLineId,
                        principalTable: "SalesDispatchLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesDispatchCorrectionLines_StockReservations_StockReserva~",
                        column: x => x.StockReservationId,
                        principalTable: "StockReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_CorrectedConsistent",
                table: "StockReservations",
                sql: "\"Status\" <> 'Corrected' OR (\"CorrectedAt\" IS NOT NULL AND \"ConsumedAt\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_Status_Allowed",
                table: "StockReservations",
                sql: "\"Status\" IN ('Active', 'Released', 'Consumed', 'Corrected')");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_Correction_StockReservationId",
                table: "StockMovements",
                column: "StockReservationId",
                unique: true,
                filter: "\"Type\" = 'Correction'");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_SalesDispatchCorrectionLineId",
                table: "StockMovements",
                column: "SalesDispatchCorrectionLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PalletContentLines_OriginCorrectionLineId",
                table: "PalletContentLines",
                column: "OriginCorrectionLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesDispatchCorrectionLines_SalesDispatchCorrectionId",
                table: "SalesDispatchCorrectionLines",
                column: "SalesDispatchCorrectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDispatchCorrectionLines_SalesDispatchLineId",
                table: "SalesDispatchCorrectionLines",
                column: "SalesDispatchLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesDispatchCorrectionLines_StockReservationId",
                table: "SalesDispatchCorrectionLines",
                column: "StockReservationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesDispatchCorrections_RequestId",
                table: "SalesDispatchCorrections",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesDispatchCorrections_SalesDispatchId",
                table: "SalesDispatchCorrections",
                column: "SalesDispatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesDispatchCorrections_SalesOrderId",
                table: "SalesDispatchCorrections",
                column: "SalesOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_PalletContentLines_SalesDispatchCorrectionLines_OriginCorre~",
                table: "PalletContentLines",
                column: "OriginCorrectionLineId",
                principalTable: "SalesDispatchCorrectionLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_SalesDispatchCorrectionLines_SalesDispatchCo~",
                table: "StockMovements",
                column: "SalesDispatchCorrectionLineId",
                principalTable: "SalesDispatchCorrectionLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PalletContentLines_SalesDispatchCorrectionLines_OriginCorre~",
                table: "PalletContentLines");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_SalesDispatchCorrectionLines_SalesDispatchCo~",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "SalesDispatchCorrectionLines");

            migrationBuilder.DropTable(
                name: "SalesDispatchCorrections");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_CorrectedConsistent",
                table: "StockReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_Status_Allowed",
                table: "StockReservations");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_Correction_StockReservationId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_SalesDispatchCorrectionLineId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_PalletContentLines_OriginCorrectionLineId",
                table: "PalletContentLines");

            migrationBuilder.DropColumn(
                name: "CorrectedAt",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "SalesDispatchCorrectionLineId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "OriginCorrectionLineId",
                table: "PalletContentLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_Status_Allowed",
                table: "StockReservations",
                sql: "\"Status\" IN ('Active', 'Released', 'Consumed')");
        }
    }
}
