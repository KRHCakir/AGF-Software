using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    // Alleen data, geen schemawijziging (daarom zonder Designer-bestand en zonder wijziging van de modelsnapshot): bestaande klassen
    // die eenduidig één klasse uit de vaste lijst zijn, worden die vaste waarde ("1", "klasse 1", "class I" -> "I"). Al het andere
    // (leeg of niet herkend) blijft ongewijzigd en verschijnt in Artikelen onder "Maat en klasse opschonen". De sleutel is dezelfde
    // als ArticleRules.NormalizeTradeClass: kleine letters, voorvoegsel eraf, scheidingstekens eruit (een test houdt ze gelijk).
    // Down zet niets terug: de oude schrijfwijze is niet bewaard en betekende hetzelfde; terug kan alleen met de dump van vooraf.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261102120000_NormalizeTradeClasses")]
    public partial class NormalizeTradeClasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE ""ProductVariants"" v
SET ""TradeClass"" = m.class
FROM (
    SELECT ""Id"", CASE regexp_replace(regexp_replace(lower(trim(""TradeClass"")), '^(handelsklasse|klasse|klas|kl|class|categorie|category|cat)[\s.:]*', ''), '[\s.:_-]', '', 'g')
        WHEN 'extra' THEN 'Extra'
        WHEN 'e' THEN 'Extra'
        WHEN '0' THEN 'Extra'
        WHEN 'i' THEN 'I'
        WHEN '1' THEN 'I'
        WHEN 'ii' THEN 'II'
        WHEN '2' THEN 'II'
    END AS class
    FROM ""ProductVariants""
    WHERE ""TradeClass"" IS NOT NULL
) m
WHERE v.""Id"" = m.""Id"" AND m.class IS NOT NULL AND v.""TradeClass"" IS DISTINCT FROM m.class;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
