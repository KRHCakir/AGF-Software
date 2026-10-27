using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddLoosePackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitmentLines_PalletInput_Consistent",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrderLines_PalletInput_Consistent",
                table: "SalesOrderLines");

            migrationBuilder.AddColumn<decimal>(
                name: "LoosePackages",
                table: "SupplyCommitmentLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LoosePackages",
                table: "SalesOrderLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitmentLines_PalletInput_Consistent",
                table: "SupplyCommitmentLines",
                sql: "(\"PalletCount\" IS NULL AND \"PackagesPerPallet\" IS NULL AND \"LoosePackages\" IS NULL) OR (\"PalletCount\" IS NOT NULL AND \"PackagesPerPallet\" IS NOT NULL AND CAST(\"PalletCount\" AS NUMERIC) > 0 AND CAST(\"PackagesPerPallet\" AS NUMERIC) > 0 AND (\"LoosePackages\" IS NULL OR (CAST(\"LoosePackages\" AS NUMERIC) >= 0 AND CAST(\"LoosePackages\" AS NUMERIC) = round(CAST(\"LoosePackages\" AS NUMERIC)))) AND abs(CAST(\"ExpectedQuantity\" AS NUMERIC) - CAST(\"PalletCount\" AS NUMERIC) * CAST(\"PackagesPerPallet\" AS NUMERIC) - coalesce(CAST(\"LoosePackages\" AS NUMERIC), 0)) < 0.001)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrderLines_PalletInput_Consistent",
                table: "SalesOrderLines",
                sql: "(\"PalletCount\" IS NULL AND \"PackagesPerPallet\" IS NULL AND \"LoosePackages\" IS NULL) OR (\"PalletCount\" IS NOT NULL AND \"PackagesPerPallet\" IS NOT NULL AND CAST(\"PalletCount\" AS NUMERIC) > 0 AND CAST(\"PackagesPerPallet\" AS NUMERIC) > 0 AND (\"LoosePackages\" IS NULL OR (CAST(\"LoosePackages\" AS NUMERIC) >= 0 AND CAST(\"LoosePackages\" AS NUMERIC) = round(CAST(\"LoosePackages\" AS NUMERIC)))) AND abs(CAST(\"Quantity\" AS NUMERIC) - CAST(\"PalletCount\" AS NUMERIC) * CAST(\"PackagesPerPallet\" AS NUMERIC) - coalesce(CAST(\"LoosePackages\" AS NUMERIC), 0)) < 0.001)");
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

            migrationBuilder.DropColumn(
                name: "LoosePackages",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "LoosePackages",
                table: "SalesOrderLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitmentLines_PalletInput_Consistent",
                table: "SupplyCommitmentLines",
                sql: "(\"PalletCount\" IS NULL AND \"PackagesPerPallet\" IS NULL) OR (\"PalletCount\" IS NOT NULL AND \"PackagesPerPallet\" IS NOT NULL AND CAST(\"PalletCount\" AS NUMERIC) > 0 AND CAST(\"PackagesPerPallet\" AS NUMERIC) > 0 AND abs(CAST(\"ExpectedQuantity\" AS NUMERIC) - CAST(\"PalletCount\" AS NUMERIC) * CAST(\"PackagesPerPallet\" AS NUMERIC)) < 0.001)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrderLines_PalletInput_Consistent",
                table: "SalesOrderLines",
                sql: "(\"PalletCount\" IS NULL AND \"PackagesPerPallet\" IS NULL) OR (\"PalletCount\" IS NOT NULL AND \"PackagesPerPallet\" IS NOT NULL AND CAST(\"PalletCount\" AS NUMERIC) > 0 AND CAST(\"PackagesPerPallet\" AS NUMERIC) > 0 AND abs(CAST(\"Quantity\" AS NUMERIC) - CAST(\"PalletCount\" AS NUMERIC) * CAST(\"PackagesPerPallet\" AS NUMERIC)) < 0.001)");
        }
    }
}
