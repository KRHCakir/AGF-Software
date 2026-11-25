using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "InvoiceNumber",
                table: "Invoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerAddress",
                table: "Invoices",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerVatNumber",
                table: "Invoices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscardReason",
                table: "Invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DiscardedAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscardedBy",
                table: "Invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalizedAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalizedBy",
                table: "Invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceSequence",
                table: "Invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceYear",
                table: "Invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Invoices",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentTermDays",
                table: "Invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "Invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "InvoiceLine",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddColumn<int>(
                name: "CostArticleId",
                table: "InvoiceLine",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ggn",
                table: "InvoiceLine",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LineKind",
                table: "InvoiceLine",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LineNet",
                table: "InvoiceLine",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LineVat",
                table: "InvoiceLine",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LotNumber",
                table: "InvoiceLine",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RemovedAt",
                table: "InvoiceLine",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevenueTypeId",
                table: "InvoiceLine",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesDeliveryId",
                table: "InvoiceLine",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesOrderLineId",
                table: "InvoiceLine",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesOrderPackagingLineId",
                table: "InvoiceLine",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesOrderServiceLineId",
                table: "InvoiceLine",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "InvoiceLine",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RevenueTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternalCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevenueTypes", x => x.Id);
                    table.CheckConstraint("CK_RevenueTypes_Default_Active", "NOT \"IsDefault\" OR \"IsActive\"");
                    table.CheckConstraint("CK_RevenueTypes_Name_NotBlank", "length(trim(\"Name\")) > 0");
                });

            migrationBuilder.InsertData(
                table: "RevenueTypes",
                columns: new[] { "Id", "CreatedAt", "ExternalCode", "IsActive", "IsDefault", "Name", "Version" },
                values: new object[] { 1, new DateTime(2026, 11, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, true, true, "Omzet diensten", 0 });

            // De identiteitsreeks na de vaste rij met Id 1, zodat een nieuwe omzetsoort niet op Id 1 botst.
            migrationBuilder.Sql("SELECT setval(pg_get_serial_sequence('\"RevenueTypes\"', 'Id'), GREATEST((SELECT MAX(\"Id\") FROM \"RevenueTypes\"), 1));");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceYear_InvoiceSequence",
                table: "Invoices",
                columns: new[] { "InvoiceYear", "InvoiceSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_RequestId",
                table: "Invoices",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Status",
                table: "Invoices",
                column: "Status");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Discarded",
                table: "Invoices",
                sql: "\"Kind\" IS NULL OR ((\"Status\" = 'Discarded') = (\"DiscardedAt\" IS NOT NULL AND \"DiscardedBy\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Final_Numbered",
                table: "Invoices",
                sql: "\"Kind\" IS NULL OR ((\"Status\" = 'Final') = (\"InvoiceNumber\" IS NOT NULL AND \"InvoiceYear\" IS NOT NULL AND \"InvoiceSequence\" IS NOT NULL AND \"FinalizedAt\" IS NOT NULL AND \"FinalizedBy\" IS NOT NULL AND \"DueDate\" IS NOT NULL AND \"PaymentTermDays\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Kind_Allowed",
                table: "Invoices",
                sql: "\"Kind\" IS NULL OR \"Kind\" IN ('Delivery', 'Direct')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_NewModel",
                table: "Invoices",
                sql: "\"Kind\" IS NULL OR (\"Status\" IN ('Concept', 'Final', 'Discarded') AND \"CustomerId\" IS NOT NULL AND \"RequestId\" IS NOT NULL AND \"CreatedBy\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Sequence_Positive",
                table: "Invoices",
                sql: "\"InvoiceSequence\" IS NULL OR \"InvoiceSequence\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLine_CostArticleId",
                table: "InvoiceLine",
                column: "CostArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLine_RevenueTypeId",
                table: "InvoiceLine",
                column: "RevenueTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLine_SalesDeliveryId",
                table: "InvoiceLine",
                column: "SalesDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLine_SalesOrderLineId",
                table: "InvoiceLine",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLine_SalesOrderPackagingLineId",
                table: "InvoiceLine",
                column: "SalesOrderPackagingLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLine_SalesOrderServiceLineId",
                table: "InvoiceLine",
                column: "SalesOrderServiceLineId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceLine_DeliverySource",
                table: "InvoiceLine",
                sql: "(\"SalesDeliveryId\" IS NULL) = (\"SalesOrderLineId\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceLine_LineKind_Allowed",
                table: "InvoiceLine",
                sql: "\"LineKind\" IS NULL OR \"LineKind\" IN ('Product', 'Service', 'Packaging')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceLine_Product_Source",
                table: "InvoiceLine",
                sql: "\"LineKind\" IS NULL OR \"LineKind\" <> 'Product' OR (\"ProductVariantId\" IS NOT NULL AND (\"SalesOrderLineId\" IS NOT NULL OR \"PartijId\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceLine_Quantity_Positive",
                table: "InvoiceLine",
                sql: "\"LineKind\" IS NULL OR CAST(\"Quantity\" AS NUMERIC) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceLine_Service_NoStock",
                table: "InvoiceLine",
                sql: "\"LineKind\" IS NULL OR \"LineKind\" = 'Product' OR (\"SalesOrderLineId\" IS NULL AND \"PartijId\" IS NULL AND \"ProductVariantId\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceLine_SingleSource",
                table: "InvoiceLine",
                sql: "(CASE WHEN \"SalesOrderLineId\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"SalesOrderServiceLineId\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"SalesOrderPackagingLineId\" IS NULL THEN 0 ELSE 1 END) <= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceLine_Vat_Allowed",
                table: "InvoiceLine",
                sql: "\"LineKind\" IS NULL OR CAST(\"VATPercentage\" AS NUMERIC) IN (0, 9, 21)");

            migrationBuilder.CreateIndex(
                name: "IX_RevenueTypes_IsDefault",
                table: "RevenueTypes",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\"");

            migrationBuilder.CreateIndex(
                name: "IX_RevenueTypes_Name",
                table: "RevenueTypes",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceLine_CostArticles_CostArticleId",
                table: "InvoiceLine",
                column: "CostArticleId",
                principalTable: "CostArticles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceLine_RevenueTypes_RevenueTypeId",
                table: "InvoiceLine",
                column: "RevenueTypeId",
                principalTable: "RevenueTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceLine_SalesDeliveries_SalesDeliveryId",
                table: "InvoiceLine",
                column: "SalesDeliveryId",
                principalTable: "SalesDeliveries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceLine_SalesOrderLines_SalesOrderLineId",
                table: "InvoiceLine",
                column: "SalesOrderLineId",
                principalTable: "SalesOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceLine_SalesOrderPackagingLines_SalesOrderPackagingLin~",
                table: "InvoiceLine",
                column: "SalesOrderPackagingLineId",
                principalTable: "SalesOrderPackagingLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceLine_SalesOrderServiceLines_SalesOrderServiceLineId",
                table: "InvoiceLine",
                column: "SalesOrderServiceLineId",
                principalTable: "SalesOrderServiceLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Regel 44 ook in de database: een definitieve of vervallen verkoopfactuur (nieuw model) en haar regels veranderen nooit, een
            // factuur of regel van het nieuwe model wordt nooit verwijderd, en regels ontstaan alleen in een concept. Werkt ook buiten de
            // applicatie om (ExecuteUpdate, psql). Oude facturen (Kind leeg) blijven vrij.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION agf_sales_invoice_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.""Kind"" IS NOT NULL THEN
        IF TG_OP = 'DELETE' THEN
            RAISE EXCEPTION 'Sales invoice % is never deleted (rule 44).', OLD.""Id"" USING ERRCODE = 'check_violation';
        END IF;
        IF OLD.""Status"" IN ('Final', 'Discarded') THEN
            RAISE EXCEPTION 'Sales invoice % is % and never changes (rule 44).', OLD.""Id"", OLD.""Status"" USING ERRCODE = 'check_violation';
        END IF;
    ELSIF TG_OP = 'UPDATE' AND NEW.""Kind"" IS NOT NULL THEN
        RAISE EXCEPTION 'Old invoice % is never turned into a sales invoice of the new model (rule 44).', OLD.""Id"" USING ERRCODE = 'check_violation';
    END IF;
    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER ""TR_Invoices_SalesInvoiceGuard"" BEFORE UPDATE OR DELETE ON ""Invoices""
    FOR EACH ROW EXECUTE FUNCTION agf_sales_invoice_guard();

CREATE OR REPLACE FUNCTION agf_sales_invoice_line_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    invoice_kind text;
    invoice_status text;
BEGIN
    IF TG_OP IN ('UPDATE', 'DELETE') THEN
        SELECT ""Kind"", ""Status"" INTO invoice_kind, invoice_status FROM ""Invoices"" WHERE ""Id"" = OLD.""InvoiceId"";
        IF invoice_kind IS NOT NULL AND (TG_OP = 'DELETE' OR invoice_status <> 'Concept') THEN
            RAISE EXCEPTION 'Line % of sales invoice % never changes once the invoice is not a concept, and is never deleted (rule 44).', OLD.""Id"", OLD.""InvoiceId"" USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    IF TG_OP IN ('INSERT', 'UPDATE') THEN
        SELECT ""Kind"", ""Status"" INTO invoice_kind, invoice_status FROM ""Invoices"" WHERE ""Id"" = NEW.""InvoiceId"";
        IF invoice_kind IS NOT NULL AND invoice_status <> 'Concept' THEN
            RAISE EXCEPTION 'Sales invoice % is not a concept: its lines never change (rule 44).', NEW.""InvoiceId"" USING ERRCODE = 'check_violation';
        END IF;
        RETURN NEW;
    END IF;
    RETURN OLD;
END $$;

CREATE TRIGGER ""TR_InvoiceLine_SalesInvoiceGuard"" BEFORE INSERT OR UPDATE OR DELETE ON ""InvoiceLine""
    FOR EACH ROW EXECUTE FUNCTION agf_sales_invoice_line_guard();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS ""TR_InvoiceLine_SalesInvoiceGuard"" ON ""InvoiceLine"";
DROP FUNCTION IF EXISTS agf_sales_invoice_line_guard();
DROP TRIGGER IF EXISTS ""TR_Invoices_SalesInvoiceGuard"" ON ""Invoices"";
DROP FUNCTION IF EXISTS agf_sales_invoice_guard();
UPDATE ""InvoiceLine"" SET ""UnitPrice"" = 0 WHERE ""UnitPrice"" IS NULL;
");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceLine_CostArticles_CostArticleId",
                table: "InvoiceLine");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceLine_RevenueTypes_RevenueTypeId",
                table: "InvoiceLine");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceLine_SalesDeliveries_SalesDeliveryId",
                table: "InvoiceLine");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceLine_SalesOrderLines_SalesOrderLineId",
                table: "InvoiceLine");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceLine_SalesOrderPackagingLines_SalesOrderPackagingLin~",
                table: "InvoiceLine");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceLine_SalesOrderServiceLines_SalesOrderServiceLineId",
                table: "InvoiceLine");

            migrationBuilder.DropTable(
                name: "RevenueTypes");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_InvoiceYear_InvoiceSequence",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_RequestId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_Status",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Discarded",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Final_Numbered",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Kind_Allowed",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_NewModel",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Sequence_Positive",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLine_CostArticleId",
                table: "InvoiceLine");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLine_RevenueTypeId",
                table: "InvoiceLine");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLine_SalesDeliveryId",
                table: "InvoiceLine");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLine_SalesOrderLineId",
                table: "InvoiceLine");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLine_SalesOrderPackagingLineId",
                table: "InvoiceLine");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLine_SalesOrderServiceLineId",
                table: "InvoiceLine");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceLine_DeliverySource",
                table: "InvoiceLine");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceLine_LineKind_Allowed",
                table: "InvoiceLine");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceLine_Product_Source",
                table: "InvoiceLine");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceLine_Quantity_Positive",
                table: "InvoiceLine");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceLine_Service_NoStock",
                table: "InvoiceLine");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceLine_SingleSource",
                table: "InvoiceLine");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceLine_Vat_Allowed",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CustomerAddress",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CustomerVatNumber",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "DiscardReason",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "DiscardedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "DiscardedBy",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FinalizedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FinalizedBy",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "InvoiceSequence",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "InvoiceYear",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PaymentTermDays",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CostArticleId",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "Ggn",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "LineKind",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "LineNet",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "LineVat",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "LotNumber",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "RevenueTypeId",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "SalesDeliveryId",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "SalesOrderLineId",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "SalesOrderPackagingLineId",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "SalesOrderServiceLineId",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "InvoiceLine");

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceNumber",
                table: "Invoices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "InvoiceLine",
                type: "numeric",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);
        }
    }
}
