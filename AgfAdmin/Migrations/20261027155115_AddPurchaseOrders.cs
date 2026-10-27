using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "SupplyCommitments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderChannel",
                table: "SupplyCommitments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderNumber",
                table: "SupplyCommitments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "OrderSequence",
                table: "SupplyCommitments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrderYear",
                table: "SupplyCommitments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OrderedOn",
                table: "SupplyCommitments",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "SupplierReference",
                table: "SupplyCommitments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Bestaande toezeggingen krijgen een ordernummer op volgorde van CreatedAt (dan Id), doorlopend per jaar van CreatedAt in
            // Europe/Amsterdam en zonder gaten; besteldatum = die aanmaakdatum. Daarna pas de unieke indexen en de check op het volgnummer.
            // Niets anders aan bestaande rijen verandert; referentie, kanaal en notitie blijven leeg.
            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT "Id",
                           CAST(EXTRACT(YEAR FROM ("CreatedAt" AT TIME ZONE 'Europe/Amsterdam')) AS integer) AS year,
                           CAST(("CreatedAt" AT TIME ZONE 'Europe/Amsterdam') AS date) AS ordered_on,
                           ROW_NUMBER() OVER (PARTITION BY EXTRACT(YEAR FROM ("CreatedAt" AT TIME ZONE 'Europe/Amsterdam')) ORDER BY "CreatedAt", "Id") AS sequence
                    FROM "SupplyCommitments"
                )
                UPDATE "SupplyCommitments" AS c
                SET "OrderYear" = n.year,
                    "OrderSequence" = n.sequence,
                    "OrderNumber" = 'IO-' || n.year || '-' || CASE WHEN n.sequence < 10000 THEN lpad(n.sequence::text, 4, '0') ELSE n.sequence::text END,
                    "OrderedOn" = n.ordered_on
                FROM numbered AS n
                WHERE c."Id" = n."Id";
                """);
            // De tijdelijke vulwaarden ('' en 0001-01-01) waren alleen nodig om de kolommen toe te voegen; het model kent geen default.
            migrationBuilder.Sql("""
                ALTER TABLE "SupplyCommitments" ALTER COLUMN "OrderNumber" DROP DEFAULT;
                ALTER TABLE "SupplyCommitments" ALTER COLUMN "OrderYear" DROP DEFAULT;
                ALTER TABLE "SupplyCommitments" ALTER COLUMN "OrderSequence" DROP DEFAULT;
                ALTER TABLE "SupplyCommitments" ALTER COLUMN "OrderedOn" DROP DEFAULT;
                """);

            migrationBuilder.AddColumn<int>(
                name: "OriginRejectionLineId",
                table: "PalletContentLines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SplitOffNetWeightKg",
                table: "PalletContentLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SplitOffQuantity",
                table: "PalletContentLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "SupplyCommitmentInvoiceRefs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplyCommitmentId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InvoiceNumberKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AmountExclVat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    IncomingInvoiceId = table.Column<int>(type: "integer", nullable: true),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplyCommitmentInvoiceRefs", x => x.Id);
                    table.CheckConstraint("CK_SupplyCommitmentInvoiceRefs_InvoiceNumber_NotBlank", "length(trim(\"InvoiceNumber\")) > 0");
                    table.CheckConstraint("CK_SupplyCommitmentInvoiceRefs_InvoiceNumberKey_NotBlank", "length(trim(\"InvoiceNumberKey\")) > 0");
                    table.CheckConstraint("CK_SupplyCommitmentInvoiceRefs_Kind_Allowed", "\"Kind\" IN ('Invoice', 'CreditNote')");
                    table.ForeignKey(
                        name: "FK_SupplyCommitmentInvoiceRefs_IncomingInvoices_IncomingInvoic~",
                        column: x => x.IncomingInvoiceId,
                        principalTable: "IncomingInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplyCommitmentInvoiceRefs_SupplyCommitments_SupplyCommitm~",
                        column: x => x.SupplyCommitmentId,
                        principalTable: "SupplyCommitments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplyRejections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplyCommitmentId = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Explanation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RejectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplyRejections", x => x.Id);
                    table.CheckConstraint("CK_SupplyRejections_Decision_Allowed", "\"Decision\" IN ('ReturnToSupplier', 'ClaimDiscount', 'Dispose')");
                    table.CheckConstraint("CK_SupplyRejections_OtherExplained", "\"Reason\" <> 'Other' OR (\"Explanation\" IS NOT NULL AND length(trim(\"Explanation\")) > 0)");
                    table.CheckConstraint("CK_SupplyRejections_Reason_Allowed", "\"Reason\" IN ('Quality', 'Ripeness', 'Temperature', 'Damaged', 'WrongArticle', 'Other')");
                    table.ForeignKey(
                        name: "FK_SupplyRejections_SupplyCommitments_SupplyCommitmentId",
                        column: x => x.SupplyCommitmentId,
                        principalTable: "SupplyCommitments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplyRejectionLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupplyRejectionId = table.Column<int>(type: "integer", nullable: false),
                    PalletContentLineId = table.Column<int>(type: "integer", nullable: false),
                    SupplyCommitmentLineId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NetWeightKg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    SplitOff = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplyRejectionLines", x => x.Id);
                    table.CheckConstraint("CK_SupplyRejectionLines_NetWeightKg_Positive", "CAST(\"NetWeightKg\" AS NUMERIC) > 0");
                    table.CheckConstraint("CK_SupplyRejectionLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    table.ForeignKey(
                        name: "FK_SupplyRejectionLines_PalletContentLines_PalletContentLineId",
                        column: x => x.PalletContentLineId,
                        principalTable: "PalletContentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplyRejectionLines_SupplyCommitmentLines_SupplyCommitment~",
                        column: x => x.SupplyCommitmentLineId,
                        principalTable: "SupplyCommitmentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplyRejectionLines_SupplyRejections_SupplyRejectionId",
                        column: x => x.SupplyRejectionId,
                        principalTable: "SupplyRejections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitments_OrderNumber",
                table: "SupplyCommitments",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitments_OrderYear_OrderSequence",
                table: "SupplyCommitments",
                columns: new[] { "OrderYear", "OrderSequence" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitments_OrderChannel_Allowed",
                table: "SupplyCommitments",
                sql: "\"OrderChannel\" IS NULL OR \"OrderChannel\" IN ('Phone', 'Email', 'Portal', 'Other')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitments_OrderSequence_Positive",
                table: "SupplyCommitments",
                sql: "\"OrderSequence\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_PalletContentLines_OriginRejectionLineId",
                table: "PalletContentLines",
                column: "OriginRejectionLineId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PalletContentLines_SingleOrigin",
                table: "PalletContentLines",
                sql: "\"OriginCorrectionLineId\" IS NULL OR \"OriginRejectionLineId\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PalletContentLines_SplitOff_Bounds",
                table: "PalletContentLines",
                sql: "CAST(\"SplitOffQuantity\" AS NUMERIC) >= 0 AND CAST(\"SplitOffNetWeightKg\" AS NUMERIC) >= 0 AND CAST(\"DispatchedQuantity\" AS NUMERIC) + CAST(\"SplitOffQuantity\" AS NUMERIC) <= CAST(\"Quantity\" AS NUMERIC) AND CAST(\"DispatchedNetWeightKg\" AS NUMERIC) + CAST(\"SplitOffNetWeightKg\" AS NUMERIC) <= CAST(\"NetWeightKg\" AS NUMERIC)");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitmentInvoiceRefs_IncomingInvoiceId",
                table: "SupplyCommitmentInvoiceRefs",
                column: "IncomingInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitmentInvoiceRefs_InvoiceNumberKey",
                table: "SupplyCommitmentInvoiceRefs",
                column: "InvoiceNumberKey");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitmentInvoiceRefs_RequestId",
                table: "SupplyCommitmentInvoiceRefs",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitmentInvoiceRefs_SupplyCommitmentId_Kind_Invoice~",
                table: "SupplyCommitmentInvoiceRefs",
                columns: new[] { "SupplyCommitmentId", "Kind", "InvoiceNumberKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplyRejectionLines_PalletContentLineId",
                table: "SupplyRejectionLines",
                column: "PalletContentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyRejectionLines_SupplyCommitmentLineId",
                table: "SupplyRejectionLines",
                column: "SupplyCommitmentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyRejectionLines_SupplyRejectionId",
                table: "SupplyRejectionLines",
                column: "SupplyRejectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyRejections_RequestId",
                table: "SupplyRejections",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplyRejections_SupplyCommitmentId",
                table: "SupplyRejections",
                column: "SupplyCommitmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_PalletContentLines_SupplyRejectionLines_OriginRejectionLine~",
                table: "PalletContentLines",
                column: "OriginRejectionLineId",
                principalTable: "SupplyRejectionLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PalletContentLines_SupplyRejectionLines_OriginRejectionLine~",
                table: "PalletContentLines");

            migrationBuilder.DropTable(
                name: "SupplyCommitmentInvoiceRefs");

            migrationBuilder.DropTable(
                name: "SupplyRejectionLines");

            migrationBuilder.DropTable(
                name: "SupplyRejections");

            migrationBuilder.DropIndex(
                name: "IX_SupplyCommitments_OrderNumber",
                table: "SupplyCommitments");

            migrationBuilder.DropIndex(
                name: "IX_SupplyCommitments_OrderYear_OrderSequence",
                table: "SupplyCommitments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitments_OrderChannel_Allowed",
                table: "SupplyCommitments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitments_OrderSequence_Positive",
                table: "SupplyCommitments");

            migrationBuilder.DropIndex(
                name: "IX_PalletContentLines_OriginRejectionLineId",
                table: "PalletContentLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PalletContentLines_SingleOrigin",
                table: "PalletContentLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PalletContentLines_SplitOff_Bounds",
                table: "PalletContentLines");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "OrderChannel",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "OrderNumber",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "OrderSequence",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "OrderYear",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "OrderedOn",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "SupplierReference",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "OriginRejectionLineId",
                table: "PalletContentLines");

            migrationBuilder.DropColumn(
                name: "SplitOffNetWeightKg",
                table: "PalletContentLines");

            migrationBuilder.DropColumn(
                name: "SplitOffQuantity",
                table: "PalletContentLines");
        }
    }
}
