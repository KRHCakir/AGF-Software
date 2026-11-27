using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultReceiveLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultReceiveLocationId",
                table: "CompanySettings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanySettings_DefaultReceiveLocationId",
                table: "CompanySettings",
                column: "DefaultReceiveLocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySettings_StockLocations_DefaultReceiveLocationId",
                table: "CompanySettings",
                column: "DefaultReceiveLocationId",
                principalTable: "StockLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompanySettings_StockLocations_DefaultReceiveLocationId",
                table: "CompanySettings");

            migrationBuilder.DropIndex(
                name: "IX_CompanySettings_DefaultReceiveLocationId",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "DefaultReceiveLocationId",
                table: "CompanySettings");
        }
    }
}
