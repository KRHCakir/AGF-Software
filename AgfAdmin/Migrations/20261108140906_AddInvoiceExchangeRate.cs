using System;
using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    // Koers bij een inkoopfactuur in een andere valuta (feedback 8 november): de ECB-koers die de server ophaalt, met publicatiedag, bron
    // en ophaalmoment. Alleen nieuwe, lege kolommen (NULL) en een controle die lege kolommen toestaat: bestaande facturen veranderen niet.
    // Het Designer-bestand is weggelaten (zie AddPartijen); de modelsnapshot is bijgewerkt en de attributen hieronder vervangen het.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261108140906_AddInvoiceExchangeRate")]
    public partial class AddInvoiceExchangeRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "IncomingInvoices",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExchangeRateDate",
                table: "IncomingInvoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExchangeRateFetchedAt",
                table: "IncomingInvoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeRateSource",
                table: "IncomingInvoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_IncomingInvoices_ExchangeRate",
                table: "IncomingInvoices",
                sql: "(\"ExchangeRate\" IS NULL AND \"ExchangeRateDate\" IS NULL AND \"ExchangeRateSource\" IS NULL) OR (\"ExchangeRate\" > 0 AND \"ExchangeRateDate\" IS NOT NULL AND \"ExchangeRateSource\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_IncomingInvoices_ExchangeRate",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRateDate",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRateFetchedAt",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRateSource",
                table: "IncomingInvoices");
        }
    }
}
