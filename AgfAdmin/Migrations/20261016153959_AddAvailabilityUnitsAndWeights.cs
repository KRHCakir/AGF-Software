using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddAvailabilityUnitsAndWeights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "ExpectedQuantity",
                table: "SupplyCommitmentLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedNetWeightKg",
                table: "SupplyCommitmentLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedUnit",
                table: "SupplyCommitmentLines",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "StockReservations",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<decimal>(
                name: "NetWeightKg",
                table: "StockReservations",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "StockReservations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "StockMovements",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<decimal>(
                name: "NetWeightKg",
                table: "StockMovements",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "StockMovements",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvailabilityUnit",
                table: "ProductVariants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpectedNetWeightKg",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "ExpectedUnit",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "NetWeightKg",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "NetWeightKg",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "AvailabilityUnit",
                table: "ProductVariants");

            migrationBuilder.AlterColumn<int>(
                name: "ExpectedQuantity",
                table: "SupplyCommitmentLines",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3);

            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "StockReservations",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3);

            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "StockMovements",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3);
        }
    }
}
