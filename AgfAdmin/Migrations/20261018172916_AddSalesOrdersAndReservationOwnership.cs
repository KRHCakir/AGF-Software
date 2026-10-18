using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    // ROLLOUT-VOORWAARDE (go/no-go), vóór toepassen op een klantdatabase:
    //   SELECT COUNT(*) FROM "StockReservations";
    //   0  -> veilig toepassen (er bestaat nog geen writer voor reserveringen).
    //   >0 -> stoppen, niets forceren en eerst een aparte datamigratie plannen: bestaande rijen moeten aan de nieuwe
    //         checks voldoen (precies één eigenaar, status Active/Released/Consumed, hoeveelheid > 0) en krijgen versie 0.
    // OrderLineId wordt alleen nullable (versoepeling, geen dataverlies); er wordt niets verwijderd of hernoemd.
    /// <inheritdoc />
    public partial class AddSalesOrdersAndReservationOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "OrderLineId",
                table: "StockReservations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "ReleaseReason",
                table: "StockReservations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReleaseRequestId",
                table: "StockReservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleasedAt",
                table: "StockReservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "StockReservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesOrderLineId",
                table: "StockReservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "StockReservations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SalesOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CustomerReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequestedDeliveryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrders", x => x.Id);
                    table.CheckConstraint("CK_SalesOrders_Status_Allowed", "\"Status\" IN ('Open', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_SalesOrders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    ProductVariantId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderLines", x => x.Id);
                    table.CheckConstraint("CK_SalesOrderLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    table.CheckConstraint("CK_SalesOrderLines_Unit_Allowed", "\"Unit\" IN ('box', 'colli', 'tray', 'piece', 'dozen')");
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_ReleaseRequestId",
                table: "StockReservations",
                column: "ReleaseRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_RequestId",
                table: "StockReservations",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_SalesOrderLineId",
                table: "StockReservations",
                column: "SalesOrderLineId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_Quantity_Positive",
                table: "StockReservations",
                sql: "CAST(\"Quantity\" AS NUMERIC) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_ReleasedConsistent",
                table: "StockReservations",
                sql: "\"Status\" <> 'Released' OR (\"ReleasedAt\" IS NOT NULL AND \"ReleaseReason\" IS NOT NULL AND length(trim(\"ReleaseReason\")) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_SalesFlowComplete",
                table: "StockReservations",
                sql: "\"SalesOrderLineId\" IS NULL OR (\"PalletContentLineId\" IS NOT NULL AND \"RequestId\" IS NOT NULL AND \"Unit\" IS NOT NULL AND \"NetWeightKg\" IS NOT NULL AND CAST(\"NetWeightKg\" AS NUMERIC) > 0 AND \"Source\" = 'Physical')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_SingleOwner",
                table: "StockReservations",
                sql: "(\"OrderLineId\" IS NOT NULL AND \"SalesOrderLineId\" IS NULL) OR (\"OrderLineId\" IS NULL AND \"SalesOrderLineId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_Status_Allowed",
                table: "StockReservations",
                sql: "\"Status\" IN ('Active', 'Released', 'Consumed')");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_ProductVariantId",
                table: "SalesOrderLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_SalesOrderId_ProductVariantId_Unit",
                table: "SalesOrderLines",
                columns: new[] { "SalesOrderId", "ProductVariantId", "Unit" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CustomerId",
                table: "SalesOrders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_RequestId",
                table: "SalesOrders",
                column: "RequestId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StockReservations_SalesOrderLines_SalesOrderLineId",
                table: "StockReservations",
                column: "SalesOrderLineId",
                principalTable: "SalesOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockReservations_SalesOrderLines_SalesOrderLineId",
                table: "StockReservations");

            migrationBuilder.DropTable(
                name: "SalesOrderLines");

            migrationBuilder.DropTable(
                name: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_StockReservations_ReleaseRequestId",
                table: "StockReservations");

            migrationBuilder.DropIndex(
                name: "IX_StockReservations_RequestId",
                table: "StockReservations");

            migrationBuilder.DropIndex(
                name: "IX_StockReservations_SalesOrderLineId",
                table: "StockReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_Quantity_Positive",
                table: "StockReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_ReleasedConsistent",
                table: "StockReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_SalesFlowComplete",
                table: "StockReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_SingleOwner",
                table: "StockReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_Status_Allowed",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ReleaseReason",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ReleaseRequestId",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ReleasedAt",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "SalesOrderLineId",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "StockReservations");

            migrationBuilder.AlterColumn<int>(
                name: "OrderLineId",
                table: "StockReservations",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
