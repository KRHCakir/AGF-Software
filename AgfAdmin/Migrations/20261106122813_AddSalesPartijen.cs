using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    // Verkoop-partij (P2): een soort op de partij (Purchase, Sales) met de verkooporder (1-op-1), het nummer uniek per soort, een eigen
    // formaat en aantal cijfers voor verkoop-partijen, het aantal cijfers van het volgnummer voor inkoop-partijen, de aanpassing zonder reden (NumberChanged) en de gevraagde
    // partij op de verkooporder (RequestPartijKey, voor de idempotentiecheck). Bestaande partijen worden Purchase en veranderen verder
    // niet. Bestaande verkooporders: zie BackfillSalesPartijenSql. Het Designer-bestand is weggelaten (zie AddPartijen); de
    // modelsnapshot is bijgewerkt en de attributen hieronder vervangen het.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261106122813_AddSalesPartijen")]
    public partial class AddSalesPartijen : Migration
    {
        // Elke bestaande verkooporder krijgt een verkoop-partij, op volgorde van aanmaken (CreatedAt, dan Id), met het standaardformaat
        // {JJ}{MM}{volgnr} en drie cijfers (beide kolommen zijn net met die standaard toegevoegd), het volgnummer per periode volgens de
        // bestaande instelling PartijSequenceReset (standaard per jaar), de datum in Europe/Amsterdam (zoals BusinessCalendar) en
        // "Migratie" als aanmaker. Een order die al een verkoop-partij heeft, wordt overgeslagen (en een al bezet volgnummer in de periode
        // telt mee, zodat nogmaals uitvoeren niets dubbel maakt). Niets bestaands wordt gewijzigd of
        // verwijderd. PostgreSQL en SQLite (tests) verschillen alleen in de datumfuncties; SQLite rekent in UTC.
        public static string BackfillSalesPartijenSql(bool postgres)
        {
            var local = postgres ? @"(o.""CreatedAt"" AT TIME ZONE 'Europe/Amsterdam')" : @"o.""CreatedAt""";
            string Part(string pgFormat, string sqliteFormat) => postgres ? $"to_char(l.\"Local\", '{pgFormat}')" : $"strftime('{sqliteFormat}', l.\"Local\")";
            var yy = postgres ? Part("YY", "") : @"substr(strftime('%Y', l.""Local""), 3, 2)";
            var padded = postgres
                ? @"CASE WHEN n.""Seq"" < 1000 THEN lpad(CAST(n.""Seq"" AS text), 3, '0') ELSE CAST(n.""Seq"" AS text) END"
                : @"CASE WHEN n.""Seq"" < 1000 THEN substr('000' || n.""Seq"", -3) ELSE CAST(n.""Seq"" AS text) END";
            return $@"
WITH reset AS (
    SELECT COALESCE((SELECT ""PartijSequenceReset"" FROM ""CompanySettings"" ORDER BY ""Id"" LIMIT 1), 'Yearly') AS ""Value""
),
local AS (
    SELECT o.""Id"", o.""CreatedAt"", {local} AS ""Local""
    FROM ""SalesOrders"" o
    WHERE NOT EXISTS (SELECT 1 FROM ""Partijen"" p WHERE p.""SalesOrderId"" = o.""Id"")
),
periods AS (
    SELECT l.""Id"", l.""CreatedAt"", {yy} || {Part("MM", "%m")} AS ""Prefix"",
        CASE r.""Value"" WHEN 'Never' THEN '' WHEN 'Monthly' THEN {Part("YYYY-MM", "%Y-%m")} ELSE {Part("YYYY", "%Y")} END AS ""Period""
    FROM local l CROSS JOIN reset r
),
numbered AS (
    SELECT p.""Id"", p.""CreatedAt"", p.""Prefix"", p.""Period"",
        ROW_NUMBER() OVER (PARTITION BY p.""Period"" ORDER BY p.""CreatedAt"", p.""Id"")
            + COALESCE((SELECT MAX(x.""Sequence"") FROM ""Partijen"" x WHERE x.""Kind"" = 'Sales' AND x.""SequencePeriod"" = p.""Period""), 0) AS ""Seq""
    FROM periods p
)
INSERT INTO ""Partijen"" (""Kind"", ""Number"", ""NumberKey"", ""Status"", ""SequencePeriod"", ""Sequence"", ""SalesOrderId"", ""CreatedAt"", ""CreatedBy"")
SELECT 'Sales', n.""Prefix"" || {padded}, n.""Prefix"" || {padded}, 'Open', n.""Period"", n.""Seq"", n.""Id"", n.""CreatedAt"", 'Migratie'
FROM numbered n
ORDER BY n.""Period"", n.""Seq"";
";
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Partijen_NumberKey",
                table: "Partijen");

            migrationBuilder.DropIndex(
                name: "IX_Partijen_SequencePeriod_Sequence",
                table: "Partijen");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PartijAuditEvents_EventType_Allowed",
                table: "PartijAuditEvents");

            migrationBuilder.AddColumn<string>(
                name: "RequestPartijKey",
                table: "SalesOrders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Partijen",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Purchase");

            migrationBuilder.AddColumn<int>(
                name: "SalesOrderId",
                table: "Partijen",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartijSequenceDigits",
                table: "CompanySettings",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "SalesPartijSequenceDigits",
                table: "CompanySettings",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<string>(
                name: "SalesPartijNumberFormat",
                table: "CompanySettings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "{JJ}{MM}{volgnr}");

            migrationBuilder.CreateIndex(
                name: "IX_Partijen_Kind_NumberKey",
                table: "Partijen",
                columns: new[] { "Kind", "NumberKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partijen_Kind_SequencePeriod_Sequence",
                table: "Partijen",
                columns: new[] { "Kind", "SequencePeriod", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partijen_SalesOrderId",
                table: "Partijen",
                column: "SalesOrderId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Partijen_Kind_SalesOrder",
                table: "Partijen",
                sql: "(\"Kind\" = 'Purchase' AND \"SalesOrderId\" IS NULL) OR (\"Kind\" = 'Sales' AND \"SalesOrderId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PartijAuditEvents_EventType_Allowed",
                table: "PartijAuditEvents",
                sql: "\"EventType\" IN ('NumberCorrected', 'OrderLinked', 'Deleted', 'NumberChanged')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanySettings_PartijSequenceDigits_Range",
                table: "CompanySettings",
                sql: "\"PartijSequenceDigits\" BETWEEN 1 AND 9");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanySettings_SalesPartijSequenceDigits_Range",
                table: "CompanySettings",
                sql: "\"SalesPartijSequenceDigits\" BETWEEN 1 AND 9");

            migrationBuilder.AddForeignKey(
                name: "FK_Partijen_SalesOrders_SalesOrderId",
                table: "Partijen",
                column: "SalesOrderId",
                principalTable: "SalesOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(BackfillSalesPartijenSql(migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Terug: de verkoop-partijen (en hun historie) verdwijnen met de soort; inkoop-partijen blijven zoals ze waren.
            migrationBuilder.Sql(@"DELETE FROM ""PartijAuditEvents"" WHERE ""PartijId"" IN (SELECT ""Id"" FROM ""Partijen"" WHERE ""Kind"" = 'Sales'); DELETE FROM ""Partijen"" WHERE ""Kind"" = 'Sales';");

            migrationBuilder.DropForeignKey(
                name: "FK_Partijen_SalesOrders_SalesOrderId",
                table: "Partijen");

            migrationBuilder.DropIndex(
                name: "IX_Partijen_Kind_NumberKey",
                table: "Partijen");

            migrationBuilder.DropIndex(
                name: "IX_Partijen_Kind_SequencePeriod_Sequence",
                table: "Partijen");

            migrationBuilder.DropIndex(
                name: "IX_Partijen_SalesOrderId",
                table: "Partijen");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Partijen_Kind_SalesOrder",
                table: "Partijen");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PartijAuditEvents_EventType_Allowed",
                table: "PartijAuditEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanySettings_PartijSequenceDigits_Range",
                table: "CompanySettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanySettings_SalesPartijSequenceDigits_Range",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "SalesPartijSequenceDigits",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "RequestPartijKey",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Partijen");

            migrationBuilder.DropColumn(
                name: "SalesOrderId",
                table: "Partijen");

            migrationBuilder.DropColumn(
                name: "PartijSequenceDigits",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "SalesPartijNumberFormat",
                table: "CompanySettings");

            migrationBuilder.CreateIndex(
                name: "IX_Partijen_NumberKey",
                table: "Partijen",
                column: "NumberKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partijen_SequencePeriod_Sequence",
                table: "Partijen",
                columns: new[] { "SequencePeriod", "Sequence" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PartijAuditEvents_EventType_Allowed",
                table: "PartijAuditEvents",
                sql: "\"EventType\" IN ('NumberCorrected', 'OrderLinked', 'Deleted')");
        }
    }
}
