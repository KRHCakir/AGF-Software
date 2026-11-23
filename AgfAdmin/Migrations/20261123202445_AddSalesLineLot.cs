using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    // Feedback 22 november: de gevraagde inkoop-partij op de verkooporderregel (optioneel, leeg voor alle bestaande regels).
    public partial class AddSalesLineLot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PartijId",
                table: "SalesOrderLines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_PartijId",
                table: "SalesOrderLines",
                column: "PartijId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_Partijen_PartijId",
                table: "SalesOrderLines",
                column: "PartijId",
                principalTable: "Partijen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrderLines_Partijen_PartijId",
                table: "SalesOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLines_PartijId",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "PartijId",
                table: "SalesOrderLines");
        }
    }
}
