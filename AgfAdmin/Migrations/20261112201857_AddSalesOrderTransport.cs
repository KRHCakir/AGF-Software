using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderTransport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCarrier",
                table: "Relations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TransportOrderLanguage",
                table: "CompanySettings",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<string>(
                name: "TransportOrderPalletKind",
                table: "CompanySettings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Europallet");

            migrationBuilder.CreateTable(
                name: "SalesOrderTransports",
                columns: table => new
                {
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CarrierRelationId = table.Column<int>(type: "integer", nullable: true),
                    LoadingPlace = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    UnloadingAddressId = table.Column<int>(type: "integer", nullable: true),
                    UnloadingPlace = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    UnloadDate = table.Column<DateTime>(type: "date", nullable: true),
                    TemperatureC = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    LoadingReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UnloadingReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PalletKind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderTransports", x => x.SalesOrderId);
                    table.CheckConstraint("CK_SalesOrderTransports_Carrier", "(\"Mode\" = 'Carrier') = (\"CarrierRelationId\" IS NOT NULL)");
                    table.CheckConstraint("CK_SalesOrderTransports_Mode", "\"Mode\" IN ('Carrier', 'Own')");
                    table.CheckConstraint("CK_SalesOrderTransports_Temperature", "\"TemperatureC\" IS NULL OR (\"TemperatureC\" >= -30 AND \"TemperatureC\" <= 30)");
                    table.ForeignKey(
                        name: "FK_SalesOrderTransports_RelationAddresses_UnloadingAddressId",
                        column: x => x.UnloadingAddressId,
                        principalTable: "RelationAddresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SalesOrderTransports_Relations_CarrierRelationId",
                        column: x => x.CarrierRelationId,
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderTransports_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderTransports_CarrierRelationId",
                table: "SalesOrderTransports",
                column: "CarrierRelationId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderTransports_UnloadingAddressId",
                table: "SalesOrderTransports",
                column: "UnloadingAddressId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesOrderTransports");

            migrationBuilder.DropColumn(
                name: "IsCarrier",
                table: "Relations");

            migrationBuilder.DropColumn(
                name: "TransportOrderLanguage",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "TransportOrderPalletKind",
                table: "CompanySettings");
        }
    }
}
