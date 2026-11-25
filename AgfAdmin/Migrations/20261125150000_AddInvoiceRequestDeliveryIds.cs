using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    // De leveringen van de oorspronkelijke aanvraag (gesorteerd, komma-gescheiden), los van de wijzigbare factuurregels: zo blijft een herhaling
    // van dezelfde requestId herkenbaar nadat er leveringen bij kwamen of regels vervielen.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261125150000_AddInvoiceRequestDeliveryIds")]
    public partial class AddInvoiceRequestDeliveryIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestDeliveryIds",
                table: "Invoices",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestDeliveryIds",
                table: "Invoices");
        }
    }
}
