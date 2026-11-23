using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    // Feedback 22 november: dienstregels op een verkooporder (doorberekende handling of transport). Alleen een nieuwe, lege tabel.
    public partial class AddSalesOrderServiceLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalesOrderServiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    CostArticleId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DiscountPercentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    VatPercentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderServiceLines", x => x.Id);
                    table.CheckConstraint("CK_SalesOrderServiceLines_DiscountPercentage_Range", "\"DiscountPercentage\" IS NULL OR CAST(\"DiscountPercentage\" AS NUMERIC) BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_SalesOrderServiceLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    table.CheckConstraint("CK_SalesOrderServiceLines_UnitPrice_NotNegative", "\"UnitPrice\" IS NULL OR CAST(\"UnitPrice\" AS NUMERIC) >= 0");
                    table.CheckConstraint("CK_SalesOrderServiceLines_VatPercentage_Allowed", "\"VatPercentage\" IS NULL OR CAST(\"VatPercentage\" AS NUMERIC) IN (0, 9, 21)");
                    table.ForeignKey(
                        name: "FK_SalesOrderServiceLines_CostArticles_CostArticleId",
                        column: x => x.CostArticleId,
                        principalTable: "CostArticles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderServiceLines_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderServiceLines_CostArticleId",
                table: "SalesOrderServiceLines",
                column: "CostArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderServiceLines_SalesOrderId",
                table: "SalesOrderServiceLines",
                column: "SalesOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesOrderServiceLines");
        }
    }
}
