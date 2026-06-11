using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class UpdateInvoiceWithAgfFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BatchNumber",
                table: "Invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddress",
                table: "Invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryDate",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNumber",
                table: "Invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingMethod",
                table: "Invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "InvoiceLine",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercentage",
                table: "InvoiceLine",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetWeight",
                table: "InvoiceLine",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "OriginCountry",
                table: "InvoiceLine",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualityClass",
                table: "InvoiceLine",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remarks",
                table: "InvoiceLine",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TareWeight",
                table: "InvoiceLine",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "InvoiceLine",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BatchNumber",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "DeliveryAddress",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "DeliveryDate",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ShippingMethod",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "DiscountPercentage",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "NetWeight",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "OriginCountry",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "QualityClass",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "Remarks",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "TareWeight",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "InvoiceLine");

            migrationBuilder.AlterColumn<int>(
                name: "Quantity",
                table: "InvoiceLine",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");
        }
    }
}
