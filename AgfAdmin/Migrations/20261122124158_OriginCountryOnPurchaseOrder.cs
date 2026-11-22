using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class OriginCountryOnPurchaseOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginCountryCode",
                table: "SupplyCommitments",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginLegacyText",
                table: "SupplyCommitments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitments_OriginCountryCode",
                table: "SupplyCommitments",
                column: "OriginCountryCode");

            migrationBuilder.AddForeignKey(
                name: "FK_SupplyCommitments_Countries_OriginCountryCode",
                table: "SupplyCommitments",
                column: "OriginCountryCode",
                principalTable: "Countries",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(MapSql);
        }

        // Bestaande herkomstteksten van inkooporders (feedback 22 november: een landcode, geen vrije tekst). Elke niet-lege tekst gaat
        // eerst ongewijzigd naar OriginLegacyText (audit). Daarna alleen een eenduidige tekst omzetten: exact de ISO-code, de Nederlandse of
        // de Engelse naam, zonder onderscheid in hoofdletters, accenten en dubbele spaties ("PE", "peru", "Peru", "Brazilie"). Zo'n order
        // krijgt de code en als tekst de Nederlandse naam, zoals bij de artikelen (AddCountries). Al het andere ("Peru (DEMO)", "EU")
        // blijft staan zonder code en komt in "Herkomst controleren". C#-spiegel: CountryRules.Unambiguous.
        public const string MapSql = @"
UPDATE ""SupplyCommitments"" SET ""OriginLegacyText"" = ""Origin""
WHERE ""Origin"" IS NOT NULL AND trim(""Origin"") <> '';

WITH order_keys AS (
    SELECT o.""Id"", translate(regexp_replace(lower(trim(o.""Origin"")), '\s+', ' ', 'g'), 'áàâäãåéèêëíìîïóòôöõúùûüýÿçñ', 'aaaaaaeeeeiiiiooooouuuuyycn') AS key
    FROM ""SupplyCommitments"" o
    WHERE o.""OriginCountryCode"" IS NULL AND o.""Origin"" IS NOT NULL AND trim(o.""Origin"") <> ''
), country_keys AS (
    SELECT c.""Code"", k.key
    FROM ""Countries"" c
    CROSS JOIN LATERAL (VALUES (lower(c.""Code"")), (lower(c.""NameNl"")), (lower(c.""NameEn""))) AS raw(value)
    CROSS JOIN LATERAL (SELECT translate(regexp_replace(trim(raw.value), '\s+', ' ', 'g'), 'áàâäãåéèêëíìîïóòôöõúùûüýÿçñ', 'aaaaaaeeeeiiiiooooouuuuyycn') AS key) AS k
), matches AS (
    SELECT ok.""Id"", min(ck.""Code"") AS code, count(DISTINCT ck.""Code"") AS countries
    FROM order_keys ok JOIN country_keys ck ON ck.key = ok.key
    GROUP BY ok.""Id""
)
UPDATE ""SupplyCommitments"" o
SET ""OriginCountryCode"" = m.code, ""Origin"" = c.""NameNl""
FROM matches m JOIN ""Countries"" c ON c.""Code"" = m.code
WHERE o.""Id"" = m.""Id"" AND m.countries = 1;";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Terug: de oude tekst weer in Origin, daarna de kolommen weg.
            migrationBuilder.Sql(@"UPDATE ""SupplyCommitments"" SET ""Origin"" = ""OriginLegacyText"" WHERE ""OriginLegacyText"" IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplyCommitments_Countries_OriginCountryCode",
                table: "SupplyCommitments");

            migrationBuilder.DropIndex(
                name: "IX_SupplyCommitments_OriginCountryCode",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "OriginCountryCode",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "OriginLegacyText",
                table: "SupplyCommitments");
        }
    }
}
