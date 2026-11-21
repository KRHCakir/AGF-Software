using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddRelationCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CertificateWarningDays",
                table: "CompanySettings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<string>(
                name: "CustomCertificateTypes",
                table: "CompanySettings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CertificateConfirmations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Context = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SupplyCommitmentId = table.Column<int>(type: "integer", nullable: false),
                    PalletContentLineId = table.Column<int>(type: "integer", nullable: true),
                    VendorId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Kinds = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CertificateIds = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ConfirmedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateConfirmations", x => x.Id);
                    table.CheckConstraint("CK_CertificateConfirmations_Context", "\"Context\" IN ('PurchaseOrder', 'Receipt')");
                    table.CheckConstraint("CK_CertificateConfirmations_Receipt", "(\"Context\" = 'Receipt') = (\"PalletContentLineId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CertificateConfirmations_PalletContentLines_PalletContentLi~",
                        column: x => x.PalletContentLineId,
                        principalTable: "PalletContentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CertificateConfirmations_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CertificateConfirmations_SupplyCommitments_SupplyCommitment~",
                        column: x => x.SupplyCommitmentId,
                        principalTable: "SupplyCommitments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CertificateConfirmations_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RelationCertificates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RelationId = table.Column<int>(type: "integer", nullable: true),
                    TypeCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CustomTypeName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NumberKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CertificationBody = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    ProductsPending = table.Column<bool>(type: "boolean", nullable: false),
                    ProducerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    HasGgnLabel = table.Column<bool>(type: "boolean", nullable: false),
                    CheckedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CheckedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    SourceVendorGgnId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationCertificates", x => x.Id);
                    table.CheckConstraint("CK_RelationCertificates_CustomType", "(\"TypeCode\" = 'Custom') = (\"CustomTypeName\" IS NOT NULL)");
                    table.CheckConstraint("CK_RelationCertificates_Ggn", "\"TypeCode\" <> 'GlobalGapIfa' OR length(\"Number\") = 13");
                    table.CheckConstraint("CK_RelationCertificates_Validity", "\"ValidFrom\" IS NULL OR \"ValidUntil\" IS NULL OR \"ValidFrom\" <= \"ValidUntil\"");
                    table.ForeignKey(
                        name: "FK_RelationCertificates_Relations_RelationId",
                        column: x => x.RelationId,
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RelationCertificateFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RelationCertificateId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayStorageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DisplayMediaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationCertificateFiles", x => x.Id);
                    table.CheckConstraint("CK_RelationCertificateFiles_Version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_RelationCertificateFiles_RelationCertificates_RelationCerti~",
                        column: x => x.RelationCertificateId,
                        principalTable: "RelationCertificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RelationCertificateProducts",
                columns: table => new
                {
                    RelationCertificateId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationCertificateProducts", x => new { x.RelationCertificateId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_RelationCertificateProducts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelationCertificateProducts_RelationCertificates_RelationCe~",
                        column: x => x.RelationCertificateId,
                        principalTable: "RelationCertificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CertificateConfirmations_PalletContentLineId",
                table: "CertificateConfirmations",
                column: "PalletContentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateConfirmations_ProductId",
                table: "CertificateConfirmations",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateConfirmations_SupplyCommitmentId",
                table: "CertificateConfirmations",
                column: "SupplyCommitmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateConfirmations_VendorId",
                table: "CertificateConfirmations",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_RelationCertificateFiles_RelationCertificateId_Version",
                table: "RelationCertificateFiles",
                columns: new[] { "RelationCertificateId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RelationCertificateProducts_ProductId",
                table: "RelationCertificateProducts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_RelationCertificates_NumberKey",
                table: "RelationCertificates",
                column: "NumberKey");

            migrationBuilder.CreateIndex(
                name: "IX_RelationCertificates_RelationId_SortOrder",
                table: "RelationCertificates",
                columns: new[] { "RelationId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RelationCertificates_SourceVendorGgnId",
                table: "RelationCertificates",
                column: "SourceVendorGgnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RelationCertificates_ValidUntil",
                table: "RelationCertificates",
                column: "ValidUntil");

            // De oude GGN-velden bij leveranciers worden certificaten van de soort GLOBALG.A.P. IFA (GGN), gemarkeerd als "producten nog
            // invullen". Er gaat niets verloren en de oude tabel VendorGgns blijft staan (zie CertificateRules.CopyVendorGgnsSql).
            migrationBuilder.Sql(AgfAdmin.Services.CertificateRules.CopyVendorGgnsSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CertificateConfirmations");

            migrationBuilder.DropTable(
                name: "RelationCertificateFiles");

            migrationBuilder.DropTable(
                name: "RelationCertificateProducts");

            migrationBuilder.DropTable(
                name: "RelationCertificates");

            migrationBuilder.DropColumn(
                name: "CertificateWarningDays",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "CustomCertificateTypes",
                table: "CompanySettings");
        }
    }
}
