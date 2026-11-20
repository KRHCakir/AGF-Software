using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "SupplyCommitments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "EUR");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "SupplyCommitments",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateDate",
                table: "SupplyCommitments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSource",
                table: "SupplyCommitments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotApplicable");

            migrationBuilder.AddColumn<string>(
                name: "PurchaseCurrencies",
                table: "CompanySettings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "EUR,USD,GBP,CHF,TRY,ZAR,CAD,NOK");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitments_ExchangeRate",
                table: "SupplyCommitments",
                sql: "(\"Currency\" = 'EUR' AND CAST(\"ExchangeRate\" AS REAL) = 1 AND \"ExchangeRateDate\" IS NULL AND \"ExchangeRateSource\" = 'NotApplicable') OR (\"Currency\" <> 'EUR' AND CAST(\"ExchangeRate\" AS REAL) > 0 AND \"ExchangeRateSource\" IN ('ECB', 'Manual') AND (\"ExchangeRateSource\" <> 'ECB' OR \"ExchangeRateDate\" IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitments_ExchangeRate",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateDate",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSource",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "PurchaseCurrencies",
                table: "CompanySettings");
        }
    }
}
