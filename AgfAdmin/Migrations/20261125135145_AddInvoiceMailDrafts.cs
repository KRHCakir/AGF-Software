using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceMailDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "SalesOrderId",
                table: "MailDrafts",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "DocumentId",
                table: "MailDrafts",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "InvoiceDocumentId",
                table: "MailDrafts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceId",
                table: "MailDrafts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MailDrafts_InvoiceDocumentId",
                table: "MailDrafts",
                column: "InvoiceDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MailDrafts_InvoiceId",
                table: "MailDrafts",
                column: "InvoiceId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MailDrafts_Owner",
                table: "MailDrafts",
                sql: "(\"SalesOrderId\" IS NOT NULL AND \"DocumentId\" IS NOT NULL AND \"InvoiceId\" IS NULL AND \"InvoiceDocumentId\" IS NULL) OR (\"SalesOrderId\" IS NULL AND \"DocumentId\" IS NULL AND \"InvoiceId\" IS NOT NULL AND \"InvoiceDocumentId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_MailDrafts_Invoices_InvoiceId",
                table: "MailDrafts",
                column: "InvoiceId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MailDrafts_SalesInvoiceDocuments_InvoiceDocumentId",
                table: "MailDrafts",
                column: "InvoiceDocumentId",
                principalTable: "SalesInvoiceDocuments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MailDrafts_Invoices_InvoiceId",
                table: "MailDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_MailDrafts_SalesInvoiceDocuments_InvoiceDocumentId",
                table: "MailDrafts");

            migrationBuilder.DropIndex(
                name: "IX_MailDrafts_InvoiceDocumentId",
                table: "MailDrafts");

            migrationBuilder.DropIndex(
                name: "IX_MailDrafts_InvoiceId",
                table: "MailDrafts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MailDrafts_Owner",
                table: "MailDrafts");

            migrationBuilder.DropColumn(
                name: "InvoiceDocumentId",
                table: "MailDrafts");

            migrationBuilder.DropColumn(
                name: "InvoiceId",
                table: "MailDrafts");

            migrationBuilder.AlterColumn<int>(
                name: "SalesOrderId",
                table: "MailDrafts",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "DocumentId",
                table: "MailDrafts",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
