using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    // Emballage (fust): kostenartikelen krijgen een soort (bestaande rijen: Cost) en emballage een standaardprijs per stuk en btw. De
    // standaard-emballage Pallet wegwerp, Euro-pallet en Krat (9% btw, geen prijs) komt erbij (SeedStandardSql); een eigen artikel met
    // dezelfde naam blijft staan. Bestaande kostenartikelen en factuurregels veranderen verder niet. Het Designer-bestand is weggelaten
    // (zie AddPartijen); de modelsnapshot is bijgewerkt en de attributen hieronder vervangen het.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261110174045_AddPackagingArticles")]
    public partial class AddPackagingArticles : Migration
    {
        // Referentiegegevens zoals de standaardkostensoorten: bij elke (ook lege) database. Dezelfde SQL draait in de tests (CostArticleTestData).
        public const string SeedStandardSql = @"
INSERT INTO ""CostArticles"" (""Name"", ""NameKey"", ""Kind"", ""VatPercentage"", ""IsStandard"", ""IsActive"", ""SortOrder"", ""CreatedAt"", ""CreatedBy"")
SELECT v.""Name"", lower(v.""Name""), 'Packaging', 9, TRUE, TRUE, v.""SortOrder"", CURRENT_TIMESTAMP, 'Standaard'
FROM (SELECT 'Pallet wegwerp' AS ""Name"", 110 AS ""SortOrder"" UNION ALL SELECT 'Euro-pallet', 120 UNION ALL SELECT 'Krat', 130) v
WHERE NOT EXISTS (SELECT 1 FROM ""CostArticles"" c WHERE c.""NameKey"" = lower(v.""Name""));
";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DefaultUnitPrice",
                table: "CostArticles",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "CostArticles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Cost");

            migrationBuilder.AddColumn<decimal>(
                name: "VatPercentage",
                table: "CostArticles",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CostArticles_Kind",
                table: "CostArticles",
                sql: "(\"Kind\" = 'Cost' AND \"DefaultUnitPrice\" IS NULL AND \"VatPercentage\" IS NULL) OR (\"Kind\" = 'Packaging' AND \"VatPercentage\" IS NOT NULL AND CAST(\"VatPercentage\" AS NUMERIC) BETWEEN 0 AND 100 AND (\"DefaultUnitPrice\" IS NULL OR CAST(\"DefaultUnitPrice\" AS NUMERIC) >= 0))");

            migrationBuilder.Sql(SeedStandardSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CostArticles_Kind",
                table: "CostArticles");

            migrationBuilder.DropColumn(
                name: "DefaultUnitPrice",
                table: "CostArticles");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "CostArticles");

            migrationBuilder.DropColumn(
                name: "VatPercentage",
                table: "CostArticles");
        }
    }
}
