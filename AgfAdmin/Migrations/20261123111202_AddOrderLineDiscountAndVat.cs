using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    // Feedback 22 november: korting en btw op de inkooporderregel, en prijs, korting en btw op de verkooporderregel. Alleen nieuwe, lege
    // kolommen: bestaande regels blijven zoals ze zijn opgeslagen (geen korting, geen btw vastgelegd, verkoopregels zonder prijs).
    public partial class AddOrderLineDiscountAndVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercentage",
                table: "SupplyCommitmentLines",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatPercentage",
                table: "SupplyCommitmentLines",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercentage",
                table: "SalesOrderLines",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "SalesOrderLines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatPercentage",
                table: "SalesOrderLines",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitmentLines_DiscountPercentage_Range",
                table: "SupplyCommitmentLines",
                sql: "\"DiscountPercentage\" IS NULL OR CAST(\"DiscountPercentage\" AS NUMERIC) BETWEEN 0 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitmentLines_VatPercentage_Allowed",
                table: "SupplyCommitmentLines",
                sql: "\"VatPercentage\" IS NULL OR CAST(\"VatPercentage\" AS NUMERIC) IN (0, 9, 21)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrderLines_DiscountPercentage_Range",
                table: "SalesOrderLines",
                sql: "\"DiscountPercentage\" IS NULL OR CAST(\"DiscountPercentage\" AS NUMERIC) BETWEEN 0 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrderLines_UnitPrice_NotNegative",
                table: "SalesOrderLines",
                sql: "\"UnitPrice\" IS NULL OR CAST(\"UnitPrice\" AS NUMERIC) >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrderLines_VatPercentage_Allowed",
                table: "SalesOrderLines",
                sql: "\"VatPercentage\" IS NULL OR CAST(\"VatPercentage\" AS NUMERIC) IN (0, 9, 21)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitmentLines_DiscountPercentage_Range",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitmentLines_VatPercentage_Allowed",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrderLines_DiscountPercentage_Range",
                table: "SalesOrderLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrderLines_UnitPrice_NotNegative",
                table: "SalesOrderLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrderLines_VatPercentage_Allowed",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "DiscountPercentage",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "VatPercentage",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "DiscountPercentage",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "VatPercentage",
                table: "SalesOrderLines");
        }
    }
}
