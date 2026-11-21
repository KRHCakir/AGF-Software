using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificateAiReading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AiExtractions_Approved_Complete",
                table: "AiExtractions");

            migrationBuilder.AddColumn<int>(
                name: "AiExtractionId",
                table: "RelationCertificateFiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelationCertificateId",
                table: "AiExtractions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelationId",
                table: "AiExtractions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RelationCertificateFiles_AiExtractionId",
                table: "RelationCertificateFiles",
                column: "AiExtractionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiExtractions_RelationCertificateId",
                table: "AiExtractions",
                column: "RelationCertificateId");

            migrationBuilder.CreateIndex(
                name: "IX_AiExtractions_RelationId",
                table: "AiExtractions",
                column: "RelationId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiExtractions_Approved_Complete",
                table: "AiExtractions",
                sql: "(\"Status\" = 'Approved' AND \"ApprovedBy\" IS NOT NULL AND \"ApprovedAt\" IS NOT NULL AND ((\"DocumentType\" = 'PurchaseInvoice' AND \"IncomingInvoiceId\" IS NOT NULL AND \"RelationCertificateId\" IS NULL) OR (\"DocumentType\" = 'Certificate' AND \"RelationCertificateId\" IS NOT NULL AND \"IncomingInvoiceId\" IS NULL))) OR (\"Status\" <> 'Approved' AND \"IncomingInvoiceId\" IS NULL AND \"RelationCertificateId\" IS NULL AND \"ApprovedBy\" IS NULL AND \"ApprovedAt\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiExtractions_DocumentType_Allowed",
                table: "AiExtractions",
                sql: "\"DocumentType\" IN ('PurchaseInvoice', 'Certificate')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiExtractions_Relation_Certificate",
                table: "AiExtractions",
                sql: "\"RelationId\" IS NULL OR \"DocumentType\" = 'Certificate'");

            migrationBuilder.AddForeignKey(
                name: "FK_AiExtractions_RelationCertificates_RelationCertificateId",
                table: "AiExtractions",
                column: "RelationCertificateId",
                principalTable: "RelationCertificates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AiExtractions_Relations_RelationId",
                table: "AiExtractions",
                column: "RelationId",
                principalTable: "Relations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RelationCertificateFiles_AiExtractions_AiExtractionId",
                table: "RelationCertificateFiles",
                column: "AiExtractionId",
                principalTable: "AiExtractions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiExtractions_RelationCertificates_RelationCertificateId",
                table: "AiExtractions");

            migrationBuilder.DropForeignKey(
                name: "FK_AiExtractions_Relations_RelationId",
                table: "AiExtractions");

            migrationBuilder.DropForeignKey(
                name: "FK_RelationCertificateFiles_AiExtractions_AiExtractionId",
                table: "RelationCertificateFiles");

            migrationBuilder.DropIndex(
                name: "IX_RelationCertificateFiles_AiExtractionId",
                table: "RelationCertificateFiles");

            migrationBuilder.DropIndex(
                name: "IX_AiExtractions_RelationCertificateId",
                table: "AiExtractions");

            migrationBuilder.DropIndex(
                name: "IX_AiExtractions_RelationId",
                table: "AiExtractions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiExtractions_Approved_Complete",
                table: "AiExtractions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiExtractions_DocumentType_Allowed",
                table: "AiExtractions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiExtractions_Relation_Certificate",
                table: "AiExtractions");

            migrationBuilder.DropColumn(
                name: "AiExtractionId",
                table: "RelationCertificateFiles");

            migrationBuilder.DropColumn(
                name: "RelationCertificateId",
                table: "AiExtractions");

            migrationBuilder.DropColumn(
                name: "RelationId",
                table: "AiExtractions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiExtractions_Approved_Complete",
                table: "AiExtractions",
                sql: "(\"Status\" = 'Approved' AND \"IncomingInvoiceId\" IS NOT NULL AND \"ApprovedBy\" IS NOT NULL AND \"ApprovedAt\" IS NOT NULL) OR (\"Status\" <> 'Approved' AND \"IncomingInvoiceId\" IS NULL AND \"ApprovedBy\" IS NULL AND \"ApprovedAt\" IS NULL)");
        }
    }
}
