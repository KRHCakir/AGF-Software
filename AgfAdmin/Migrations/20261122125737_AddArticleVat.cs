using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    // Btw-tarief op het artikel (feedback 22 november). Bestaande artikelen (producten) krijgen 9%, bestaande Dienst-artikelen 21%.
    // Emballage houdt haar eigen, al verplichte btw-percentage (sinds de emballage-ronde); daar verandert niets. Wat zonder tarief
    // blijft, staat in "Btw nog invullen".
    public partial class AddArticleVat : Migration
    {
        public const string FillSql = @"
UPDATE ""ProductVariants"" SET ""VatPercentage"" = 9 WHERE ""VatPercentage"" IS NULL;
UPDATE ""CostArticles"" SET ""VatPercentage"" = 21 WHERE ""Kind"" = 'Cost' AND ""VatPercentage"" IS NULL;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CostArticles_Kind",
                table: "CostArticles");

            migrationBuilder.AddColumn<decimal>(
                name: "VatPercentage",
                table: "ProductVariants",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductVariants_VatPercentage_Allowed",
                table: "ProductVariants",
                sql: "\"VatPercentage\" IS NULL OR CAST(\"VatPercentage\" AS NUMERIC) IN (0, 9, 21)");

            migrationBuilder.Sql(FillSql);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CostArticles_Kind",
                table: "CostArticles",
                sql: "(\"Kind\" = 'Cost' AND \"DefaultUnitPrice\" IS NULL AND (\"VatPercentage\" IS NULL OR CAST(\"VatPercentage\" AS NUMERIC) IN (0, 9, 21))) OR (\"Kind\" = 'Packaging' AND \"VatPercentage\" IS NOT NULL AND CAST(\"VatPercentage\" AS NUMERIC) BETWEEN 0 AND 100 AND (\"DefaultUnitPrice\" IS NULL OR CAST(\"DefaultUnitPrice\" AS NUMERIC) >= 0))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductVariants_VatPercentage_Allowed",
                table: "ProductVariants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CostArticles_Kind",
                table: "CostArticles");

            migrationBuilder.DropColumn(
                name: "VatPercentage",
                table: "ProductVariants");

            migrationBuilder.Sql(@"UPDATE ""CostArticles"" SET ""VatPercentage"" = NULL WHERE ""Kind"" = 'Cost';");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CostArticles_Kind",
                table: "CostArticles",
                sql: "(\"Kind\" = 'Cost' AND \"DefaultUnitPrice\" IS NULL AND \"VatPercentage\" IS NULL) OR (\"Kind\" = 'Packaging' AND \"VatPercentage\" IS NOT NULL AND CAST(\"VatPercentage\" AS NUMERIC) BETWEEN 0 AND 100 AND (\"DefaultUnitPrice\" IS NULL OR CAST(\"DefaultUnitPrice\" AS NUMERIC) >= 0))");
        }
    }
}
