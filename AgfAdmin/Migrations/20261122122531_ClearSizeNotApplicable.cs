using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    // Maat optioneel (feedback 22 november). Tot nu toe was maat verplicht en was "n.v.t." de voorgeschreven waarde voor een product
    // zonder maat. Die waarde en een lege tekst worden hier echt leeg (NULL), zodat er niets meer staat (geen "n.v.t." op scherm of
    // document). Alleen ProductVariants (de artikelstandaard); vastgelegde inhoudsregels en documenten blijven zoals ze zijn.
    public partial class ClearSizeNotApplicable : Migration
    {
        public const string Sql = """
            UPDATE "ProductVariants" SET "SizeCategory" = NULL
            WHERE "SizeCategory" IS NOT NULL AND (btrim("SizeCategory") = '' OR lower(btrim("SizeCategory")) IN ('n.v.t.', 'n.v.t', 'nvt'));
            """;

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql);
        }

        // Niet terug te draaien: welke lege maat vroeger "n.v.t." heette, is niet bewaard (en betekent hetzelfde).
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
