using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_ProductVariants_ProductVariantId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ProductVariantId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ProductVariantId",
                table: "Orders");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "ProductVariants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Season",
                table: "ProductVariants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortcutCode",
                table: "ProductVariants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "Invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFrozen",
                table: "Inventory",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "Season",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "ShortcutCode",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IsFrozen",
                table: "Inventory");

            migrationBuilder.AddColumn<int>(
                name: "ProductVariantId",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ProductVariantId",
                table: "Orders",
                column: "ProductVariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_ProductVariants_ProductVariantId",
                table: "Orders",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id");
        }
    }
}
