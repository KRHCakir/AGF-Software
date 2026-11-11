using System;
using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    // Emballageregels op een verkooporder (pallets, kratten): nieuwe tabel SalesOrderPackagingLines met artikel (soort Packaging), heel
    // aantal, prijs per stuk en btw; geen voorraad. Bestaande orders veranderen niet. Het Designer-bestand is weggelaten (zie AddPartijen);
    // de modelsnapshot is bijgewerkt en de attributen hieronder vervangen het.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261110174320_AddSalesOrderPackagingLines")]
    public partial class AddSalesOrderPackagingLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesOrderPackagingLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    CostArticleId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    VatPercentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderPackagingLines", x => x.Id);
                    table.CheckConstraint("CK_SalesOrderPackagingLines_Values", "\"Quantity\" > 0 AND (\"UnitPrice\" IS NULL OR CAST(\"UnitPrice\" AS NUMERIC) >= 0) AND CAST(\"VatPercentage\" AS NUMERIC) BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_SalesOrderPackagingLines_CostArticles_CostArticleId",
                        column: x => x.CostArticleId,
                        principalTable: "CostArticles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderPackagingLines_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderPackagingLines_CostArticleId",
                table: "SalesOrderPackagingLines",
                column: "CostArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderPackagingLines_SalesOrderId",
                table: "SalesOrderPackagingLines",
                column: "SalesOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesOrderPackagingLines");
        }
    }
}
