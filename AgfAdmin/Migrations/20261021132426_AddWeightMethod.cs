using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddWeightMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DefaultNetKgPerPackage",
                table: "ProductVariants",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeightMethod",
                table: "ProductVariants",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Weighed");

            migrationBuilder.AddColumn<decimal>(
                name: "NetKgPerPackage",
                table: "PalletContentLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeightMethod",
                table: "PalletContentLines",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Weighed");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductVariants_DefaultNetKgPerPackage_Positive",
                table: "ProductVariants",
                sql: "\"DefaultNetKgPerPackage\" IS NULL OR CAST(\"DefaultNetKgPerPackage\" AS NUMERIC) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductVariants_WeightMethod_Allowed",
                table: "ProductVariants",
                sql: "\"WeightMethod\" IN ('Weighed', 'Calculated')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PalletContentLines_Weight_Consistent",
                table: "PalletContentLines",
                sql: "(\"WeightMethod\" = 'Weighed' AND \"NetKgPerPackage\" IS NULL) OR (\"WeightMethod\" = 'Calculated' AND \"NetKgPerPackage\" IS NOT NULL AND CAST(\"NetKgPerPackage\" AS NUMERIC) > 0 AND abs(CAST(\"NetWeightKg\" AS NUMERIC) - CAST(\"Quantity\" AS NUMERIC) * CAST(\"NetKgPerPackage\" AS NUMERIC)) < 0.001)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PalletContentLines_WeightMethod_Allowed",
                table: "PalletContentLines",
                sql: "\"WeightMethod\" IN ('Weighed', 'Calculated')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductVariants_DefaultNetKgPerPackage_Positive",
                table: "ProductVariants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductVariants_WeightMethod_Allowed",
                table: "ProductVariants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PalletContentLines_Weight_Consistent",
                table: "PalletContentLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PalletContentLines_WeightMethod_Allowed",
                table: "PalletContentLines");

            migrationBuilder.DropColumn(
                name: "DefaultNetKgPerPackage",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "WeightMethod",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "NetKgPerPackage",
                table: "PalletContentLines");

            migrationBuilder.DropColumn(
                name: "WeightMethod",
                table: "PalletContentLines");
        }
    }
}
