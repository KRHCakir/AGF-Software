using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_SingleOwner",
                table: "StockReservations");

            migrationBuilder.AddColumn<int>(
                name: "InvoiceLineId",
                table: "StockReservations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesInvoiceStockLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceId = table.Column<int>(type: "integer", nullable: false),
                    InvoiceLineId = table.Column<int>(type: "integer", nullable: false),
                    StockReservationId = table.Column<int>(type: "integer", nullable: false),
                    PalletContentLineId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NetWeightKg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    StockMovementId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceStockLines", x => x.Id);
                    table.CheckConstraint("CK_SalesInvoiceStockLines_NetWeight_Positive", "CAST(\"NetWeightKg\" AS NUMERIC) > 0");
                    table.CheckConstraint("CK_SalesInvoiceStockLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    table.ForeignKey(
                        name: "FK_SalesInvoiceStockLines_InvoiceLine_InvoiceLineId",
                        column: x => x.InvoiceLineId,
                        principalTable: "InvoiceLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceStockLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceStockLines_PalletContentLines_PalletContentLine~",
                        column: x => x.PalletContentLineId,
                        principalTable: "PalletContentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceStockLines_StockMovements_StockMovementId",
                        column: x => x.StockMovementId,
                        principalTable: "StockMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceStockLines_StockReservations_StockReservationId",
                        column: x => x.StockReservationId,
                        principalTable: "StockReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_InvoiceLineId",
                table: "StockReservations",
                column: "InvoiceLineId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_InvoiceFlowComplete",
                table: "StockReservations",
                sql: "\"InvoiceLineId\" IS NULL OR (\"RequestId\" IS NOT NULL AND \"Unit\" IS NOT NULL AND \"Source\" = 'Physical' AND \"PalletContentLineId\" IS NOT NULL AND \"NetWeightKg\" IS NOT NULL AND CAST(\"NetWeightKg\" AS NUMERIC) > 0 AND \"SalesDeliveryId\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_SingleOwner",
                table: "StockReservations",
                sql: "(\"OrderLineId\" IS NOT NULL AND \"SalesOrderLineId\" IS NULL AND \"InvoiceLineId\" IS NULL) OR (\"OrderLineId\" IS NULL AND \"SalesOrderLineId\" IS NOT NULL AND \"InvoiceLineId\" IS NULL) OR (\"OrderLineId\" IS NULL AND \"SalesOrderLineId\" IS NULL AND \"InvoiceLineId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceStockLines_InvoiceId",
                table: "SalesInvoiceStockLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceStockLines_InvoiceLineId",
                table: "SalesInvoiceStockLines",
                column: "InvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceStockLines_PalletContentLineId",
                table: "SalesInvoiceStockLines",
                column: "PalletContentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceStockLines_StockMovementId",
                table: "SalesInvoiceStockLines",
                column: "StockMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceStockLines_StockReservationId",
                table: "SalesInvoiceStockLines",
                column: "StockReservationId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StockReservations_InvoiceLine_InvoiceLineId",
                table: "StockReservations",
                column: "InvoiceLineId",
                principalTable: "InvoiceLine",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockReservations_InvoiceLine_InvoiceLineId",
                table: "StockReservations");

            migrationBuilder.DropTable(
                name: "SalesInvoiceStockLines");

            migrationBuilder.DropIndex(
                name: "IX_StockReservations_InvoiceLineId",
                table: "StockReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_InvoiceFlowComplete",
                table: "StockReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_SingleOwner",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "InvoiceLineId",
                table: "StockReservations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_SingleOwner",
                table: "StockReservations",
                sql: "(\"OrderLineId\" IS NOT NULL AND \"SalesOrderLineId\" IS NULL) OR (\"OrderLineId\" IS NULL AND \"SalesOrderLineId\" IS NOT NULL)");
        }
    }
}
