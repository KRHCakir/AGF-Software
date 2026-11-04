using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    // Verkopen vóór aankomst: elke orderregel krijgt een bron (Stock of Expected; bestaande regels Stock) en elke verkooporder een eigen
    // nummer VO-jjjj-nnnn. De modelsnapshot is bijgewerkt; het gegenereerde Designer-bestand (ruim 230.000 tekens, alleen nodig voor
    // 'migrations remove') is bewust weggelaten zodat de AI-review de diff niet afkapt. De attributen hieronder vervangen het.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261103170810_AddSalesBeforeArrival")]
    public partial class AddSalesBeforeArrival : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLines_SalesOrderId_ProductVariantId_Unit",
                table: "SalesOrderLines");

            migrationBuilder.AddColumn<string>(
                name: "OrderNumber",
                table: "SalesOrders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "OrderSequence",
                table: "SalesOrders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrderYear",
                table: "SalesOrders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SupplySource",
                table: "SalesOrderLines",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Stock");

            // Bestaande verkooporders krijgen een nummer op volgorde van CreatedAt (dan Id), doorlopend per jaar van CreatedAt in
            // Europe/Amsterdam en zonder gaten (zoals de IO-nummers in AddPurchaseOrders; C#-spiegel: SalesSupplyRules.InitialNumbers).
            // Daarna pas de unieke indexen en de check op het volgnummer. Niets anders aan bestaande orders of regels verandert.
            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT "Id",
                           CAST(EXTRACT(YEAR FROM ("CreatedAt" AT TIME ZONE 'Europe/Amsterdam')) AS integer) AS year,
                           ROW_NUMBER() OVER (PARTITION BY EXTRACT(YEAR FROM ("CreatedAt" AT TIME ZONE 'Europe/Amsterdam')) ORDER BY "CreatedAt", "Id") AS sequence
                    FROM "SalesOrders"
                )
                UPDATE "SalesOrders" AS o
                SET "OrderYear" = n.year,
                    "OrderSequence" = n.sequence,
                    "OrderNumber" = 'VO-' || n.year || '-' || CASE WHEN n.sequence < 10000 THEN lpad(n.sequence::text, 4, '0') ELSE n.sequence::text END
                FROM numbered AS n
                WHERE o."Id" = n."Id";
                """);
            // De tijdelijke vulwaarden ('' en 0) waren alleen nodig om de kolommen toe te voegen; het model kent daar geen default.
            // SupplySource houdt de default 'Stock' (ook in het model): een schrijver zonder bron maakt nooit een lege bron.
            migrationBuilder.Sql("""
                ALTER TABLE "SalesOrders" ALTER COLUMN "OrderNumber" DROP DEFAULT;
                ALTER TABLE "SalesOrders" ALTER COLUMN "OrderYear" DROP DEFAULT;
                ALTER TABLE "SalesOrders" ALTER COLUMN "OrderSequence" DROP DEFAULT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_OrderNumber",
                table: "SalesOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_OrderYear_OrderSequence",
                table: "SalesOrders",
                columns: new[] { "OrderYear", "OrderSequence" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrders_OrderSequence_Positive",
                table: "SalesOrders",
                sql: "\"OrderSequence\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_SalesOrderId_ProductVariantId_Unit_SupplySo~",
                table: "SalesOrderLines",
                columns: new[] { "SalesOrderId", "ProductVariantId", "Unit", "SupplySource" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrderLines_SupplySource_Allowed",
                table: "SalesOrderLines",
                sql: "\"SupplySource\" IN ('Stock', 'Expected')");
        }

        // Down lukt alleen zolang geen order een gesplitste regel heeft (twee regels met dezelfde variant en eenheid); anders terug met de dump.
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_OrderNumber",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_OrderYear_OrderSequence",
                table: "SalesOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrders_OrderSequence_Positive",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLines_SalesOrderId_ProductVariantId_Unit_SupplySo~",
                table: "SalesOrderLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrderLines_SupplySource_Allowed",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "OrderNumber",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "OrderSequence",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "OrderYear",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "SupplySource",
                table: "SalesOrderLines");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_SalesOrderId_ProductVariantId_Unit",
                table: "SalesOrderLines",
                columns: new[] { "SalesOrderId", "ProductVariantId", "Unit" },
                unique: true);
        }
    }
}
