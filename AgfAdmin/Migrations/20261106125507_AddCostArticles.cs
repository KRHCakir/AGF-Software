using System;
using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    // Kostenartikelen (type Dienst, P2): tabel CostArticles met de standaardsoorten (SeedStandardSql) en een optioneel kostenartikel op
    // een inkoopfactuurregel (nooit samen met een artikel). Bestaande factuurregels veranderen niet. Het Designer-bestand is weggelaten
    // (zie AddPartijen); de modelsnapshot is bijgewerkt en de attributen hieronder vervangen het.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261106125507_AddCostArticles")]
    public partial class AddCostArticles : Migration
    {
        // De standaardsoorten, zoals de betaalcondities 14D en 30D: referentiegegevens die bij elke (ook lege) database horen. Dezelfde SQL
        // draait in de tests op SQLite (CostArticleTestData).
        public const string SeedStandardSql = @"
INSERT INTO ""CostArticles"" (""Name"", ""NameKey"", ""IsStandard"", ""IsActive"", ""SortOrder"", ""CreatedAt"", ""CreatedBy"")
SELECT v.""Name"", lower(v.""Name""), TRUE, TRUE, v.""SortOrder"", CURRENT_TIMESTAMP, 'Standaard'
FROM (SELECT 'Handling' AS ""Name"", 10 AS ""SortOrder"" UNION ALL SELECT 'Transport', 20 UNION ALL SELECT 'QC', 30
      UNION ALL SELECT 'Zeevracht', 40 UNION ALL SELECT 'Afval', 50 UNION ALL SELECT 'Overig', 60) v
WHERE NOT EXISTS (SELECT 1 FROM ""CostArticles"" c WHERE c.""NameKey"" = lower(v.""Name""));
";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CostArticleId",
                table: "IncomingInvoiceLines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CostArticles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsStandard = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostArticles", x => x.Id);
                    table.CheckConstraint("CK_CostArticles_Name_NotBlank", "length(trim(\"Name\")) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncomingInvoiceLines_CostArticleId",
                table: "IncomingInvoiceLines",
                column: "CostArticleId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IncomingInvoiceLines_ArticleOrCost",
                table: "IncomingInvoiceLines",
                sql: "\"ProductVariantId\" IS NULL OR \"CostArticleId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CostArticles_NameKey",
                table: "CostArticles",
                column: "NameKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostArticles_RequestId",
                table: "CostArticles",
                column: "RequestId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_IncomingInvoiceLines_CostArticles_CostArticleId",
                table: "IncomingInvoiceLines",
                column: "CostArticleId",
                principalTable: "CostArticles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(SeedStandardSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IncomingInvoiceLines_CostArticles_CostArticleId",
                table: "IncomingInvoiceLines");

            migrationBuilder.DropTable(
                name: "CostArticles");

            migrationBuilder.DropIndex(
                name: "IX_IncomingInvoiceLines_CostArticleId",
                table: "IncomingInvoiceLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IncomingInvoiceLines_ArticleOrCost",
                table: "IncomingInvoiceLines");

            migrationBuilder.DropColumn(
                name: "CostArticleId",
                table: "IncomingInvoiceLines");
        }
    }
}
