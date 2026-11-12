using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseInvoiceOrderLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrderPriceDecision",
                table: "IncomingInvoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OrderUnitPrice",
                table: "IncomingInvoiceLines",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplyCommitmentLineId",
                table: "IncomingInvoiceLines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_IncomingInvoices_OrderPriceDecision",
                table: "IncomingInvoices",
                sql: "\"OrderPriceDecision\" IS NULL OR \"OrderPriceDecision\" IN ('AdjustOrder', 'RecordDifference')");

            migrationBuilder.CreateIndex(
                name: "IX_IncomingInvoiceLines_SupplyCommitmentLineId",
                table: "IncomingInvoiceLines",
                column: "SupplyCommitmentLineId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IncomingInvoiceLines_OrderLink",
                table: "IncomingInvoiceLines",
                sql: "\"SupplyCommitmentLineId\" IS NOT NULL OR \"OrderUnitPrice\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_IncomingInvoiceLines_SupplyCommitmentLines_SupplyCommitment~",
                table: "IncomingInvoiceLines",
                column: "SupplyCommitmentLineId",
                principalTable: "SupplyCommitmentLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IncomingInvoiceLines_SupplyCommitmentLines_SupplyCommitment~",
                table: "IncomingInvoiceLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IncomingInvoices_OrderPriceDecision",
                table: "IncomingInvoices");

            migrationBuilder.DropIndex(
                name: "IX_IncomingInvoiceLines_SupplyCommitmentLineId",
                table: "IncomingInvoiceLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IncomingInvoiceLines_OrderLink",
                table: "IncomingInvoiceLines");

            migrationBuilder.DropColumn(
                name: "OrderPriceDecision",
                table: "IncomingInvoices");

            migrationBuilder.DropColumn(
                name: "OrderUnitPrice",
                table: "IncomingInvoiceLines");

            migrationBuilder.DropColumn(
                name: "SupplyCommitmentLineId",
                table: "IncomingInvoiceLines");
        }
    }
}
