using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAvailabilityDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupplyCommitments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VendorId = table.Column<int>(type: "integer", nullable: false),
                    Route = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Origin = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Destination = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExpectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplyCommitments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplyCommitments_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplyCommitmentId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockReceipts_SupplyCommitments_SupplyCommitmentId",
                        column: x => x.SupplyCommitmentId,
                        principalTable: "SupplyCommitments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplyCommitmentLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupplyCommitmentId = table.Column<int>(type: "integer", nullable: false),
                    ProductVariantId = table.Column<int>(type: "integer", nullable: false),
                    ExpectedQuantity = table.Column<int>(type: "integer", nullable: false),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplyCommitmentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplyCommitmentLines_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplyCommitmentLines_SupplyCommitments_SupplyCommitmentId",
                        column: x => x.SupplyCommitmentId,
                        principalTable: "SupplyCommitments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockReservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderLineId = table.Column<int>(type: "integer", nullable: false),
                    SupplyCommitmentLineId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockReservations_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReservations_SupplyCommitmentLines_SupplyCommitmentLin~",
                        column: x => x.SupplyCommitmentLineId,
                        principalTable: "SupplyCommitmentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    SupplyCommitmentLineId = table.Column<int>(type: "integer", nullable: false),
                    StockReceiptId = table.Column<int>(type: "integer", nullable: true),
                    StockReservationId = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMovements_StockReceipts_StockReceiptId",
                        column: x => x.StockReceiptId,
                        principalTable: "StockReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_StockReservations_StockReservationId",
                        column: x => x.StockReservationId,
                        principalTable: "StockReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_SupplyCommitmentLines_SupplyCommitmentLineId",
                        column: x => x.SupplyCommitmentLineId,
                        principalTable: "SupplyCommitmentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_StockReceiptId",
                table: "StockMovements",
                column: "StockReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_StockReservationId",
                table: "StockMovements",
                column: "StockReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_SupplyCommitmentLineId",
                table: "StockMovements",
                column: "SupplyCommitmentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReceipts_RequestId",
                table: "StockReceipts",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockReceipts_SupplyCommitmentId",
                table: "StockReceipts",
                column: "SupplyCommitmentId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_OrderLineId",
                table: "StockReservations",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_SupplyCommitmentLineId",
                table: "StockReservations",
                column: "SupplyCommitmentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitmentLines_ProductVariantId",
                table: "SupplyCommitmentLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitmentLines_SupplyCommitmentId",
                table: "SupplyCommitmentLines",
                column: "SupplyCommitmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitments_VendorId",
                table: "SupplyCommitments",
                column: "VendorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropTable(
                name: "StockReceipts");

            migrationBuilder.DropTable(
                name: "StockReservations");

            migrationBuilder.DropTable(
                name: "SupplyCommitmentLines");

            migrationBuilder.DropTable(
                name: "SupplyCommitments");
        }
    }
}
