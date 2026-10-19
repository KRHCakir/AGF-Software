using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConsumedAt",
                table: "StockReservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DispatchedNetWeightKg",
                table: "PalletContentLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DispatchedQuantity",
                table: "PalletContentLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "SalesDispatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesDispatches_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesDispatchLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SalesDispatchId = table.Column<int>(type: "integer", nullable: false),
                    StockReservationId = table.Column<int>(type: "integer", nullable: false),
                    SalesOrderLineId = table.Column<int>(type: "integer", nullable: false),
                    PalletContentLineId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NetWeightKg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ExpectedReservationVersion = table.Column<int>(type: "integer", nullable: false),
                    StockMovementId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesDispatchLines", x => x.Id);
                    table.CheckConstraint("CK_SalesDispatchLines_NetWeightKg_Positive", "CAST(\"NetWeightKg\" AS NUMERIC) > 0");
                    table.CheckConstraint("CK_SalesDispatchLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    table.ForeignKey(
                        name: "FK_SalesDispatchLines_PalletContentLines_PalletContentLineId",
                        column: x => x.PalletContentLineId,
                        principalTable: "PalletContentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesDispatchLines_SalesDispatches_SalesDispatchId",
                        column: x => x.SalesDispatchId,
                        principalTable: "SalesDispatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesDispatchLines_SalesOrderLines_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SalesOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesDispatchLines_StockMovements_StockMovementId",
                        column: x => x.StockMovementId,
                        principalTable: "StockMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesDispatchLines_StockReservations_StockReservationId",
                        column: x => x.StockReservationId,
                        principalTable: "StockReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_ConsumedConsistent",
                table: "StockReservations",
                sql: "\"Status\" <> 'Consumed' OR \"ConsumedAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_Dispatch_StockReservationId",
                table: "StockMovements",
                column: "StockReservationId",
                unique: true,
                filter: "\"Type\" = 'Dispatch'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PalletContentLines_Dispatched_Bounds",
                table: "PalletContentLines",
                sql: "CAST(\"DispatchedQuantity\" AS NUMERIC) >= 0 AND CAST(\"DispatchedQuantity\" AS NUMERIC) <= CAST(\"Quantity\" AS NUMERIC) AND CAST(\"DispatchedNetWeightKg\" AS NUMERIC) >= 0 AND CAST(\"DispatchedNetWeightKg\" AS NUMERIC) <= CAST(\"NetWeightKg\" AS NUMERIC)");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDispatches_RequestId",
                table: "SalesDispatches",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesDispatches_SalesOrderId",
                table: "SalesDispatches",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDispatchLines_PalletContentLineId",
                table: "SalesDispatchLines",
                column: "PalletContentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDispatchLines_SalesDispatchId",
                table: "SalesDispatchLines",
                column: "SalesDispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDispatchLines_SalesOrderLineId",
                table: "SalesDispatchLines",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDispatchLines_StockMovementId",
                table: "SalesDispatchLines",
                column: "StockMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesDispatchLines_StockReservationId",
                table: "SalesDispatchLines",
                column: "StockReservationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesDispatchLines");

            migrationBuilder.DropTable(
                name: "SalesDispatches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_ConsumedConsistent",
                table: "StockReservations");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_Dispatch_StockReservationId",
                table: "StockMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PalletContentLines_Dispatched_Bounds",
                table: "PalletContentLines");

            migrationBuilder.DropColumn(
                name: "ConsumedAt",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "DispatchedNetWeightKg",
                table: "PalletContentLines");

            migrationBuilder.DropColumn(
                name: "DispatchedQuantity",
                table: "PalletContentLines");
        }
    }
}
