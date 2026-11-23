using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    // Feedback 22 november, prompt 5 (partij is de sleutel):
    // - nieuwe partijen krijgen I- (inkoop) en V- (verkoop) voor jaar, maand en volgnummer. Alleen de standaardformaten van vóór deze ronde
    //   worden omgezet (ook de vaste 5 vooraan voor inkoop); een zelf gekozen formaat blijft staan. Bestaande partijnummers veranderen niet:
    //   ze staan al op geprinte documenten. Het volgnummer loopt door in de periode (de teller is per soort en periode);
    // - een directe factuur krijgt een eigen verkoop-partij (Partijen.InvoiceId) en elke factuurregel optioneel haar inkoop-partij
    //   (InvoiceLine.PartijId). Beide kolommen zijn leeg voor bestaande rijen; de check op de soort wordt uitgebreid, niet versoepeld:
    //   een verkoop-partij hoort bij precies één verkooporder of één directe factuur.
    public partial class AddLotNumberingIV : Migration
    {
        public const string SettingsSql = """
            UPDATE "CompanySettings" SET "PartijNumberFormat" = 'I-{JJ}{MM}{volgnr}' WHERE "PartijNumberFormat" IN ('{JJ}{MM}{volgnr}', '5{JJ}{MM}{volgnr}');
            UPDATE "CompanySettings" SET "SalesPartijNumberFormat" = 'V-{JJ}{MM}{volgnr}' WHERE "SalesPartijNumberFormat" = '{JJ}{MM}{volgnr}';
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Partijen_Kind_SalesOrder",
                table: "Partijen");

            migrationBuilder.AddColumn<int>(
                name: "InvoiceId",
                table: "Partijen",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartijId",
                table: "InvoiceLine",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SalesPartijNumberFormat",
                table: "CompanySettings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "V-{JJ}{MM}{volgnr}",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldDefaultValue: "{JJ}{MM}{volgnr}");

            migrationBuilder.AlterColumn<string>(
                name: "PartijNumberFormat",
                table: "CompanySettings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "I-{JJ}{MM}{volgnr}",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldDefaultValue: "{JJ}{MM}{volgnr}");

            migrationBuilder.CreateIndex(
                name: "IX_Partijen_InvoiceId",
                table: "Partijen",
                column: "InvoiceId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Partijen_Kind_SalesOrder",
                table: "Partijen",
                sql: "(\"Kind\" = 'Purchase' AND \"SalesOrderId\" IS NULL AND \"InvoiceId\" IS NULL) OR (\"Kind\" = 'Sales' AND ((\"SalesOrderId\" IS NOT NULL AND \"InvoiceId\" IS NULL) OR (\"SalesOrderId\" IS NULL AND \"InvoiceId\" IS NOT NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLine_PartijId",
                table: "InvoiceLine",
                column: "PartijId");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceLine_Partijen_PartijId",
                table: "InvoiceLine",
                column: "PartijId",
                principalTable: "Partijen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Partijen_Invoices_InvoiceId",
                table: "Partijen",
                column: "InvoiceId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(SettingsSql);
        }

        /// <inheritdoc />
        // Terug: kolommen, index en check zoals ervoor, en de standaardwaarde van de formaten; de omgezette instellingen blijven I- en V-.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceLine_Partijen_PartijId",
                table: "InvoiceLine");

            migrationBuilder.DropForeignKey(
                name: "FK_Partijen_Invoices_InvoiceId",
                table: "Partijen");

            migrationBuilder.DropIndex(
                name: "IX_Partijen_InvoiceId",
                table: "Partijen");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Partijen_Kind_SalesOrder",
                table: "Partijen");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLine_PartijId",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "InvoiceId",
                table: "Partijen");

            migrationBuilder.DropColumn(
                name: "PartijId",
                table: "InvoiceLine");

            migrationBuilder.AlterColumn<string>(
                name: "SalesPartijNumberFormat",
                table: "CompanySettings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "{JJ}{MM}{volgnr}",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldDefaultValue: "V-{JJ}{MM}{volgnr}");

            migrationBuilder.AlterColumn<string>(
                name: "PartijNumberFormat",
                table: "CompanySettings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "{JJ}{MM}{volgnr}",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldDefaultValue: "I-{JJ}{MM}{volgnr}");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Partijen_Kind_SalesOrder",
                table: "Partijen",
                sql: "(\"Kind\" = 'Purchase' AND \"SalesOrderId\" IS NULL) OR (\"Kind\" = 'Sales' AND \"SalesOrderId\" IS NOT NULL)");
        }
    }
}
