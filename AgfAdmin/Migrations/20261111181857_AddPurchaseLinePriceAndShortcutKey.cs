using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    // Orderregel van de inkooporder (feedback 11 november): een optionele prijs per verpakking op SupplyCommitmentLines (bestaande regels
    // blijven zonder prijs), en een genormaliseerde snelcode (ShortcutKey) met een unieke index, zodat een snelcode hoofdletterongevoelig
    // uniek is, ook bij gelijktijdig opslaan. ShortcutKey wordt voor bestaande artikelen gevuld uit ShortcutCode (getrimd, kleine letters)
    // vóór de index; bestaat er dan toch een dubbele, dan stopt de migratie (eerst één van beide snelcodes aanpassen). Het Designer-bestand
    // is weggelaten (zie AddPartijen); de attributen staan hier.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261111181857_AddPurchaseLinePriceAndShortcutKey")]
    public partial class AddPurchaseLinePriceAndShortcutKey : Migration
    {
        public const string FillShortcutKeysSql =
            @"UPDATE ""ProductVariants"" SET ""ShortcutKey"" = lower(trim(""ShortcutCode"")) WHERE ""ShortcutCode"" IS NOT NULL AND trim(""ShortcutCode"") <> '';";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "SupplyCommitmentLines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortcutKey",
                table: "ProductVariants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitmentLines_UnitPrice_NotNegative",
                table: "SupplyCommitmentLines",
                sql: "\"UnitPrice\" IS NULL OR CAST(\"UnitPrice\" AS NUMERIC) >= 0");

            migrationBuilder.Sql(FillShortcutKeysSql);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ShortcutKey",
                table: "ProductVariants",
                column: "ShortcutKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitmentLines_UnitPrice_NotNegative",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_ShortcutKey",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "ShortcutKey",
                table: "ProductVariants");
        }
    }
}
