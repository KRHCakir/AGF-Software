using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviationWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyReceiptDeviations_Variants_Differ",
                table: "SupplyReceiptDeviations");

            migrationBuilder.AlterColumn<int>(
                name: "NewLineId",
                table: "SupplyReceiptDeviations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedKgPerPackage",
                table: "SupplyReceiptDeviations",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReceivedKgPerPackage",
                table: "SupplyReceiptDeviations",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyReceiptDeviations_Variants_Differ",
                table: "SupplyReceiptDeviations",
                sql: "\"ExpectedProductVariantId\" <> \"ReceivedProductVariantId\" OR \"NewLineId\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyReceiptDeviations_WeightOnly_SameLine",
                table: "SupplyReceiptDeviations",
                sql: "\"NewLineId\" IS NOT NULL OR (\"ExpectedProductVariantId\" = \"ReceivedProductVariantId\" AND CAST(\"ReducedQuantity\" AS NUMERIC) = 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyReceiptDeviations_Variants_Differ",
                table: "SupplyReceiptDeviations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyReceiptDeviations_WeightOnly_SameLine",
                table: "SupplyReceiptDeviations");

            migrationBuilder.DropColumn(
                name: "ExpectedKgPerPackage",
                table: "SupplyReceiptDeviations");

            migrationBuilder.DropColumn(
                name: "ReceivedKgPerPackage",
                table: "SupplyReceiptDeviations");

            migrationBuilder.AlterColumn<int>(
                name: "NewLineId",
                table: "SupplyReceiptDeviations",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyReceiptDeviations_Variants_Differ",
                table: "SupplyReceiptDeviations",
                sql: "\"ExpectedProductVariantId\" <> \"ReceivedProductVariantId\"");
        }
    }
}
