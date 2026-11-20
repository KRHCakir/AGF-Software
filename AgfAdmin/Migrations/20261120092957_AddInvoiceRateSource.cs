using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceRateSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_IncomingInvoices_ExchangeRate",
                table: "IncomingInvoices");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IncomingInvoices_ExchangeRate",
                table: "IncomingInvoices",
                sql: "(\"ExchangeRate\" IS NULL AND \"ExchangeRateDate\" IS NULL AND \"ExchangeRateSource\" IS NULL) OR (\"ExchangeRate\" IS NOT NULL AND \"ExchangeRate\" > 0 AND \"ExchangeRateSource\" IS NOT NULL AND ((\"ExchangeRateSource\" = 'ECB' AND \"ExchangeRateDate\" IS NOT NULL) OR (\"ExchangeRateSource\" IN ('Manual', 'NotApplicable') AND \"ExchangeRateDate\" IS NULL)))");

            // Valuta-ronde: bestaande facturen in euro (ook zonder valuta, dat telde al als euro) zijn euro met koers 1 en bron NotApplicable.
            // Een factuur in een andere valuta zonder koers blijft zo: die haalt haar ECB-koers op zoals voorheen (zolang er niets verdeeld is).
            migrationBuilder.Sql(
                "UPDATE \"IncomingInvoices\" SET \"Currency\" = 'EUR', \"ExchangeRate\" = 1, \"ExchangeRateDate\" = NULL, \"ExchangeRateSource\" = 'NotApplicable' "
                + "WHERE \"ExchangeRate\" IS NULL AND (\"Currency\" IS NULL OR TRIM(\"Currency\") = '' OR UPPER(TRIM(\"Currency\")) = 'EUR');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"IncomingInvoices\" SET \"ExchangeRate\" = NULL, \"ExchangeRateDate\" = NULL, \"ExchangeRateSource\" = NULL WHERE \"ExchangeRateSource\" IN ('NotApplicable', 'Manual');");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IncomingInvoices_ExchangeRate",
                table: "IncomingInvoices");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IncomingInvoices_ExchangeRate",
                table: "IncomingInvoices",
                sql: "(\"ExchangeRate\" IS NULL AND \"ExchangeRateDate\" IS NULL AND \"ExchangeRateSource\" IS NULL) OR (\"ExchangeRate\" IS NOT NULL AND \"ExchangeRate\" > 0 AND \"ExchangeRateDate\" IS NOT NULL AND \"ExchangeRateSource\" IS NOT NULL)");
        }
    }
}
