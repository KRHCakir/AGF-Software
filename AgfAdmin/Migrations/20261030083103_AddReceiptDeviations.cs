using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptDeviations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReplacesLineId",
                table: "SupplyCommitmentLines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SupplyReceiptDeviations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplyCommitmentId = table.Column<int>(type: "integer", nullable: false),
                    OriginalLineId = table.Column<int>(type: "integer", nullable: false),
                    NewLineId = table.Column<int>(type: "integer", nullable: false),
                    PalletContentLineId = table.Column<int>(type: "integer", nullable: false),
                    Kinds = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExpectedProductVariantId = table.Column<int>(type: "integer", nullable: false),
                    ExpectedVariantSnapshot = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ReceivedProductVariantId = table.Column<int>(type: "integer", nullable: false),
                    ReceivedVariantSnapshot = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReducedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    OriginalLineBefore = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    OriginalLineAfter = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ReasonNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplyReceiptDeviations", x => x.Id);
                    table.CheckConstraint("CK_SupplyReceiptDeviations_Lines_Differ", "\"OriginalLineId\" <> \"NewLineId\"");
                    table.CheckConstraint("CK_SupplyReceiptDeviations_OtherExplained", "\"Reason\" IS NULL OR \"Reason\" <> 'Other' OR (\"ReasonNote\" IS NOT NULL AND length(trim(\"ReasonNote\")) > 0)");
                    table.CheckConstraint("CK_SupplyReceiptDeviations_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    table.CheckConstraint("CK_SupplyReceiptDeviations_Reason_Allowed", "\"Reason\" IS NULL OR \"Reason\" IN ('SupplierDeliveredDifferently', 'OtherLotLoaded', 'Other')");
                    table.CheckConstraint("CK_SupplyReceiptDeviations_Reduced_Range", "CAST(\"ReducedQuantity\" AS NUMERIC) >= 0 AND CAST(\"ReducedQuantity\" AS NUMERIC) <= CAST(\"Quantity\" AS NUMERIC)");
                    table.CheckConstraint("CK_SupplyReceiptDeviations_Variants_Differ", "\"ExpectedProductVariantId\" <> \"ReceivedProductVariantId\"");
                    table.ForeignKey(
                        name: "FK_SupplyReceiptDeviations_PalletContentLines_PalletContentLin~",
                        column: x => x.PalletContentLineId,
                        principalTable: "PalletContentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplyReceiptDeviations_ProductVariants_ExpectedProductVari~",
                        column: x => x.ExpectedProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplyReceiptDeviations_ProductVariants_ReceivedProductVari~",
                        column: x => x.ReceivedProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplyReceiptDeviations_SupplyCommitmentLines_NewLineId",
                        column: x => x.NewLineId,
                        principalTable: "SupplyCommitmentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplyReceiptDeviations_SupplyCommitmentLines_OriginalLineId",
                        column: x => x.OriginalLineId,
                        principalTable: "SupplyCommitmentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplyReceiptDeviations_SupplyCommitments_SupplyCommitmentId",
                        column: x => x.SupplyCommitmentId,
                        principalTable: "SupplyCommitments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitmentLines_ReplacesLineId",
                table: "SupplyCommitmentLines",
                column: "ReplacesLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyReceiptDeviations_ExpectedProductVariantId",
                table: "SupplyReceiptDeviations",
                column: "ExpectedProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyReceiptDeviations_NewLineId",
                table: "SupplyReceiptDeviations",
                column: "NewLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplyReceiptDeviations_OriginalLineId",
                table: "SupplyReceiptDeviations",
                column: "OriginalLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyReceiptDeviations_PalletContentLineId",
                table: "SupplyReceiptDeviations",
                column: "PalletContentLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplyReceiptDeviations_ReceivedProductVariantId",
                table: "SupplyReceiptDeviations",
                column: "ReceivedProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyReceiptDeviations_RequestId",
                table: "SupplyReceiptDeviations",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplyReceiptDeviations_SupplyCommitmentId",
                table: "SupplyReceiptDeviations",
                column: "SupplyCommitmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupplyCommitmentLines_SupplyCommitmentLines_ReplacesLineId",
                table: "SupplyCommitmentLines",
                column: "ReplacesLineId",
                principalTable: "SupplyCommitmentLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupplyCommitmentLines_SupplyCommitmentLines_ReplacesLineId",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropTable(
                name: "SupplyReceiptDeviations");

            migrationBuilder.DropIndex(
                name: "IX_SupplyCommitmentLines_ReplacesLineId",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "ReplacesLineId",
                table: "SupplyCommitmentLines");
        }
    }
}
