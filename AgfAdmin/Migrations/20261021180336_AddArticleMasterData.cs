using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Brand",
                table: "ProductVariants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultGrossKgPerPackage",
                table: "ProductVariants",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoodsCode",
                table: "ProductVariants",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoodsCodeSystem",
                table: "ProductVariants",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackagingUnit",
                table: "ProductVariants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeClass",
                table: "ProductVariants",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductVariants_DefaultGrossKgPerPackage_Positive",
                table: "ProductVariants",
                sql: "\"DefaultGrossKgPerPackage\" IS NULL OR CAST(\"DefaultGrossKgPerPackage\" AS NUMERIC) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductVariants_GoodsCodeSystem_Allowed",
                table: "ProductVariants",
                sql: "\"GoodsCodeSystem\" IS NULL OR \"GoodsCodeSystem\" IN ('HS', 'CN', 'TARIC')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductVariants_DefaultGrossKgPerPackage_Positive",
                table: "ProductVariants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductVariants_GoodsCodeSystem_Allowed",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "Brand",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "DefaultGrossKgPerPackage",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "GoodsCode",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "GoodsCodeSystem",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "PackagingUnit",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "TradeClass",
                table: "ProductVariants");
        }
    }
}
