using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class Phase16WasteAndWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalWeightKg",
                table: "Inventory",
                type: "numeric",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WasteLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InventoryId = table.Column<int>(type: "integer", nullable: false),
                    ProductVariantId = table.Column<int>(type: "integer", nullable: false),
                    QuantityDumped = table.Column<int>(type: "integer", nullable: false),
                    WeightDumpedKg = table.Column<decimal>(type: "numeric", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    LoggedBy = table.Column<string>(type: "text", nullable: true),
                    DumpedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WasteLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WasteLogs_Inventory_InventoryId",
                        column: x => x.InventoryId,
                        principalTable: "Inventory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WasteLogs_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WasteLogs_InventoryId",
                table: "WasteLogs",
                column: "InventoryId");

            migrationBuilder.CreateIndex(
                name: "IX_WasteLogs_ProductVariantId",
                table: "WasteLogs",
                column: "ProductVariantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WasteLogs");

            migrationBuilder.DropColumn(
                name: "TotalWeightKg",
                table: "Inventory");
        }
    }
}
