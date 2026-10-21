using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddPalletInput : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PackagesPerPallet",
                table: "SupplyCommitmentLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PalletCount",
                table: "SupplyCommitmentLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagesPerPallet",
                table: "SalesOrderLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PalletCount",
                table: "SalesOrderLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultPackagesPerPallet",
                table: "ProductVariants",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitmentLines_PalletInput_Consistent",
                table: "SupplyCommitmentLines",
                sql: "(\"PalletCount\" IS NULL AND \"PackagesPerPallet\" IS NULL) OR (\"PalletCount\" IS NOT NULL AND \"PackagesPerPallet\" IS NOT NULL AND CAST(\"PalletCount\" AS NUMERIC) > 0 AND CAST(\"PackagesPerPallet\" AS NUMERIC) > 0 AND abs(CAST(\"ExpectedQuantity\" AS NUMERIC) - CAST(\"PalletCount\" AS NUMERIC) * CAST(\"PackagesPerPallet\" AS NUMERIC)) < 0.001)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrderLines_PalletInput_Consistent",
                table: "SalesOrderLines",
                sql: "(\"PalletCount\" IS NULL AND \"PackagesPerPallet\" IS NULL) OR (\"PalletCount\" IS NOT NULL AND \"PackagesPerPallet\" IS NOT NULL AND CAST(\"PalletCount\" AS NUMERIC) > 0 AND CAST(\"PackagesPerPallet\" AS NUMERIC) > 0 AND abs(CAST(\"Quantity\" AS NUMERIC) - CAST(\"PalletCount\" AS NUMERIC) * CAST(\"PackagesPerPallet\" AS NUMERIC)) < 0.001)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductVariants_DefaultPackagesPerPallet_Positive",
                table: "ProductVariants",
                sql: "\"DefaultPackagesPerPallet\" IS NULL OR CAST(\"DefaultPackagesPerPallet\" AS NUMERIC) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitmentLines_PalletInput_Consistent",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrderLines_PalletInput_Consistent",
                table: "SalesOrderLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductVariants_DefaultPackagesPerPallet_Positive",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "PackagesPerPallet",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "PalletCount",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "PackagesPerPallet",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "PalletCount",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "DefaultPackagesPerPallet",
                table: "ProductVariants");
        }
    }
}
