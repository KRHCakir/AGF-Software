using System;
using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    // AI-lezen van inkoopfacturen (stap 7a): de lezingen (AiExtractions: document, model, voorstel, controles, verbruik, kosten, wie keurde
    // goed of wees af) en de bijlagen bij inkoopfacturen (DocumentAttachments, append-only). In CompanySettings: AI-lezen aan/uit (standaard
    // uit, bestaande rij ook), wie het aanzette en wanneer, en de maandlimiet in euro (standaard 25). Raakt geen bestaande gegevens: alleen
    // nieuwe tabellen en kolommen met een standaardwaarde. Het Designer-bestand is weggelaten (zie AddPartijen); de attributen staan hier.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261111215737_AddAiInvoiceReading")]
    public partial class AddAiInvoiceReading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AiReadingEnabled",
                table: "CompanySettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiReadingEnabledAt",
                table: "CompanySettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiReadingEnabledBy",
                table: "CompanySettings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AiReadingMonthlyLimitEur",
                table: "CompanySettings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 25m);

            migrationBuilder.CreateTable(
                name: "AiExtractions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayStorageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DisplayMediaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FileDeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModelVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UsedFallback = table.Column<bool>(type: "boolean", nullable: false),
                    AttemptsJson = table.Column<string>(type: "text", nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    EurPerUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CostEur = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    ProposalJson = table.Column<string>(type: "text", nullable: true),
                    ProblemsJson = table.Column<string>(type: "text", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ApprovedValuesJson = table.Column<string>(type: "text", nullable: true),
                    AdjustmentsJson = table.Column<string>(type: "text", nullable: true),
                    ApprovedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IncomingInvoiceId = table.Column<int>(type: "integer", nullable: true),
                    ClosedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiExtractions", x => x.Id);
                    table.CheckConstraint("CK_AiExtractions_Approved_Complete", "(\"Status\" = 'Approved' AND \"IncomingInvoiceId\" IS NOT NULL AND \"ApprovedBy\" IS NOT NULL AND \"ApprovedAt\" IS NOT NULL) OR (\"Status\" <> 'Approved' AND \"IncomingInvoiceId\" IS NULL AND \"ApprovedBy\" IS NULL AND \"ApprovedAt\" IS NULL)");
                    table.CheckConstraint("CK_AiExtractions_Costs_NotNegative", "CAST(\"CostUsd\" AS NUMERIC) >= 0 AND CAST(\"CostEur\" AS NUMERIC) >= 0 AND \"InputTokens\" >= 0 AND \"OutputTokens\" >= 0");
                    table.CheckConstraint("CK_AiExtractions_Status_Allowed", "\"Status\" IN ('Proposed', 'Approved', 'Rejected', 'Manual', 'Failed')");
                    table.ForeignKey(
                        name: "FK_AiExtractions_IncomingInvoices_IncomingInvoiceId",
                        column: x => x.IncomingInvoiceId,
                        principalTable: "IncomingInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IncomingInvoiceId = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AiExtractionId = table.Column<int>(type: "integer", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentAttachments", x => x.Id);
                    table.CheckConstraint("CK_DocumentAttachments_Size_Positive", "\"SizeBytes\" > 0");
                    table.ForeignKey(
                        name: "FK_DocumentAttachments_AiExtractions_AiExtractionId",
                        column: x => x.AiExtractionId,
                        principalTable: "AiExtractions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentAttachments_IncomingInvoices_IncomingInvoiceId",
                        column: x => x.IncomingInvoiceId,
                        principalTable: "IncomingInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiExtractions_CreatedAt",
                table: "AiExtractions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiExtractions_IncomingInvoiceId",
                table: "AiExtractions",
                column: "IncomingInvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachments_AiExtractionId",
                table: "DocumentAttachments",
                column: "AiExtractionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAttachments_IncomingInvoiceId",
                table: "DocumentAttachments",
                column: "IncomingInvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentAttachments");

            migrationBuilder.DropTable(
                name: "AiExtractions");

            migrationBuilder.DropColumn(
                name: "AiReadingEnabled",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "AiReadingEnabledAt",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "AiReadingEnabledBy",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "AiReadingMonthlyLimitEur",
                table: "CompanySettings");
        }
    }
}
