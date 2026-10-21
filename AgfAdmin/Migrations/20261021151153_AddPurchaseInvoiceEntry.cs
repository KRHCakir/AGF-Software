using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseInvoiceEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContainerNumber",
                table: "IncomingInvoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "IncomingInvoices",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CustomsValue",
                table: "IncomingInvoices",
                type: "numeric(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverName",
                table: "IncomingInvoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossWeightKg",
                table: "IncomingInvoices",
                type: "numeric(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Incoterms",
                table: "IncomingInvoices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicensePlate",
                table: "IncomingInvoices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetWeightKg",
                table: "IncomingInvoices",
                type: "numeric(18,3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginCountry",
                table: "IncomingInvoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentReference",
                table: "IncomingInvoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentTermsDays",
                table: "IncomingInvoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptReference",
                table: "IncomingInvoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedDate",
                table: "IncomingInvoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remarks",
                table: "IncomingInvoices",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "IncomingInvoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Transporter",
                table: "IncomingInvoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "IncomingInvoiceLines",
                type: "numeric(18,3)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)");

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercentage",
                table: "IncomingInvoiceLines",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ProductVariantId",
                table: "IncomingInvoiceLines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "IncomingInvoiceLines",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchaseInvoiceDefaultCurrency",
                table: "CompanySettings",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true,
                defaultValue: "EUR");

            migrationBuilder.AddColumn<int>(
                name: "PurchaseInvoiceDefaultPaymentTermsDays",
                table: "CompanySettings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<decimal>(
                name: "PurchaseInvoiceDefaultVatPercentage",
                table: "CompanySettings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 21m);

            migrationBuilder.AddColumn<string>(
                name: "PurchaseInvoiceOptionalFields",
                table: "CompanySettings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                defaultValue: "receivedDate,paymentReference,remarks,originCountry,receiptReference");

            migrationBuilder.CreateIndex(
                name: "IX_IncomingInvoices_RequestId",
                table: "IncomingInvoices",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomingInvoiceLines_ProductVariantId",
                table: "IncomingInvoiceLines",
                column: "ProductVariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_IncomingInvoiceLines_ProductVariants_ProductVariantId",
                table: "IncomingInvoiceLines",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IncomingInvoiceLines_ProductVariants_ProductVariantId",
                table: "IncomingInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_IncomingInvoices_RequestId",
                table: "IncomingInvoices");

            migrationBuilder.DropIndex(
                name: "IX_IncomingInvoiceLines_ProductVariantId",
                table: "IncomingInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ContainerNumber",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "CustomsValue",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "DriverName",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "GrossWeightKg",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "Incoterms",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "LicensePlate",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "NetWeightKg",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "OriginCountry",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "PaymentReference",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "PaymentTermsDays",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "ReceiptReference",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "ReceivedDate",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "Remarks",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "Transporter",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "DiscountPercentage",
                table: "IncomingInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ProductVariantId",
                table: "IncomingInvoiceLines");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "IncomingInvoiceLines");

            migrationBuilder.DropColumn(
                name: "PurchaseInvoiceDefaultCurrency",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "PurchaseInvoiceDefaultPaymentTermsDays",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "PurchaseInvoiceDefaultVatPercentage",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "PurchaseInvoiceOptionalFields",
                table: "CompanySettings");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "IncomingInvoiceLines",
                type: "numeric(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)");
        }
    }
}
