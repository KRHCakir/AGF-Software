using System;
using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    // Inkooporder aanpassen tot hij definitief is (stap 4a): een documentstatus naast de ontvangststatus (Open of Definitive, met wie,
    // wanneer en waardoor), een documentversie voor gelijktijdig aanpassen, en de append-only auditregels PurchaseOrderChanges.
    // Bestaande orders: Open, behalve een order met een factuurnummer (Kind Invoice): die is Definitive via Invoice, op het tijdstip en door
    // de medewerker van het eerste factuurnummer ("Migratie" als die onbekend is). Er wordt niets bestaands gewijzigd of verwijderd.
    // Het Designer-bestand is weggelaten (zie AddPartijen); de attributen staan hier.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261112091611_AddPurchaseOrderDocumentStatus")]
    public partial class AddPurchaseOrderDocumentStatus : Migration
    {
        // Ook in de tests (SQLite) uitgevoerd: alleen standaard-SQL.
        public const string FinalizeInvoicedOrdersSql = @"
UPDATE ""SupplyCommitments"" SET
    ""DocumentStatus"" = 'Definitive',
    ""FinalizedVia"" = 'Invoice',
    ""FinalizedAt"" = (SELECT MIN(r.""CreatedAt"") FROM ""SupplyCommitmentInvoiceRefs"" r WHERE r.""SupplyCommitmentId"" = ""SupplyCommitments"".""Id"" AND r.""Kind"" = 'Invoice'),
    ""FinalizedBy"" = COALESCE((SELECT r.""ActorReference"" FROM ""SupplyCommitmentInvoiceRefs"" r WHERE r.""SupplyCommitmentId"" = ""SupplyCommitments"".""Id"" AND r.""Kind"" = 'Invoice'
        ORDER BY r.""CreatedAt"", r.""Id"" LIMIT 1), 'Migratie')
WHERE EXISTS (SELECT 1 FROM ""SupplyCommitmentInvoiceRefs"" r WHERE r.""SupplyCommitmentId"" = ""SupplyCommitments"".""Id"" AND r.""Kind"" = 'Invoice');";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentStatus",
                table: "SupplyCommitments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Open");

            migrationBuilder.AddColumn<int>(
                name: "EditVersion",
                table: "SupplyCommitments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalizedAt",
                table: "SupplyCommitments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalizedBy",
                table: "SupplyCommitments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalizedVia",
                table: "SupplyCommitments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PurchaseOrderChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplyCommitmentId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangesJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderChanges", x => x.Id);
                    table.CheckConstraint("CK_PurchaseOrderChanges_Correction_Reason", "\"Kind\" <> 'Correction' OR (\"Reason\" IS NOT NULL AND length(trim(\"Reason\")) > 0)");
                    table.CheckConstraint("CK_PurchaseOrderChanges_Kind_Allowed", "\"Kind\" IN ('Edit', 'Finalize', 'Correction')");
                    table.ForeignKey(
                        name: "FK_PurchaseOrderChanges_SupplyCommitments_SupplyCommitmentId",
                        column: x => x.SupplyCommitmentId,
                        principalTable: "SupplyCommitments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(FinalizeInvoicedOrdersSql);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitments_DocumentStatus_Allowed",
                table: "SupplyCommitments",
                sql: "(\"DocumentStatus\" = 'Open' AND \"FinalizedAt\" IS NULL AND \"FinalizedBy\" IS NULL AND \"FinalizedVia\" IS NULL) OR (\"DocumentStatus\" = 'Definitive' AND \"FinalizedAt\" IS NOT NULL AND \"FinalizedBy\" IS NOT NULL AND \"FinalizedVia\" IN ('Manual', 'Invoice'))");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderChanges_RequestId",
                table: "PurchaseOrderChanges",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderChanges_SupplyCommitmentId",
                table: "PurchaseOrderChanges",
                column: "SupplyCommitmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseOrderChanges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitments_DocumentStatus_Allowed",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "DocumentStatus",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "EditVersion",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "FinalizedAt",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "FinalizedBy",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "FinalizedVia",
                table: "SupplyCommitments");
        }
    }
}
