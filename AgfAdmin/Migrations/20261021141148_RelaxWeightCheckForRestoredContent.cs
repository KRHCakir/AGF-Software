using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class RelaxWeightCheckForRestoredContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PalletContentLines_Weight_Consistent",
                table: "PalletContentLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PalletContentLines_Weight_Consistent",
                table: "PalletContentLines",
                sql: "(\"WeightMethod\" = 'Weighed' AND \"NetKgPerPackage\" IS NULL) OR (\"WeightMethod\" = 'Calculated' AND \"NetKgPerPackage\" IS NOT NULL AND CAST(\"NetKgPerPackage\" AS NUMERIC) > 0 AND (\"OriginCorrectionLineId\" IS NOT NULL OR abs(CAST(\"NetWeightKg\" AS NUMERIC) - CAST(\"Quantity\" AS NUMERIC) * CAST(\"NetKgPerPackage\" AS NUMERIC)) < 0.001))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PalletContentLines_Weight_Consistent",
                table: "PalletContentLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PalletContentLines_Weight_Consistent",
                table: "PalletContentLines",
                sql: "(\"WeightMethod\" = 'Weighed' AND \"NetKgPerPackage\" IS NULL) OR (\"WeightMethod\" = 'Calculated' AND \"NetKgPerPackage\" IS NOT NULL AND CAST(\"NetKgPerPackage\" AS NUMERIC) > 0 AND abs(CAST(\"NetWeightKg\" AS NUMERIC) - CAST(\"Quantity\" AS NUMERIC) * CAST(\"NetKgPerPackage\" AS NUMERIC)) < 0.001)");
        }
    }
}
