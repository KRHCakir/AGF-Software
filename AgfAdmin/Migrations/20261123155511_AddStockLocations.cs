using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddStockLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddress",
                table: "SupplyCommitments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoadingAddress",
                table: "SupplyCommitments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReceiveLocationId",
                table: "SupplyCommitments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StockLocationId",
                table: "Pallets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StockLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockLocations", x => x.Id);
                    table.CheckConstraint("CK_StockLocations_Kind", "\"Kind\" IN ('OwnWarehouse', 'Supplier', 'Crossdock', 'ExternalStorage', 'InTransit')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitments_ReceiveLocationId",
                table: "SupplyCommitments",
                column: "ReceiveLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Pallets_StockLocationId",
                table: "Pallets",
                column: "StockLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLocations_Name",
                table: "StockLocations",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Pallets_StockLocations_StockLocationId",
                table: "Pallets",
                column: "StockLocationId",
                principalTable: "StockLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplyCommitments_StockLocations_ReceiveLocationId",
                table: "SupplyCommitments",
                column: "ReceiveLocationId",
                principalTable: "StockLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pallets_StockLocations_StockLocationId",
                table: "Pallets");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplyCommitments_StockLocations_ReceiveLocationId",
                table: "SupplyCommitments");

            migrationBuilder.DropTable(
                name: "StockLocations");

            migrationBuilder.DropIndex(
                name: "IX_SupplyCommitments_ReceiveLocationId",
                table: "SupplyCommitments");

            migrationBuilder.DropIndex(
                name: "IX_Pallets_StockLocationId",
                table: "Pallets");

            migrationBuilder.DropColumn(
                name: "DeliveryAddress",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "LoadingAddress",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "ReceiveLocationId",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "StockLocationId",
                table: "Pallets");
        }
    }
}
