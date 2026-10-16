using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    // ROLLOUT-VOORWAARDE (go/no-go), vóór toepassen op een klantdatabase:
    //   SELECT COUNT(*) FROM "SupplyCommitments";
    //   0  -> veilig toepassen.
    //   >0 -> stoppen, niets forceren en eerst een aparte datamigratie plannen: de tijdelijke Guid.Empty-default
    //         botst bij meerdere rijen met de unieke RequestId-index, en een DirectTrade-rij zonder klant of een
    //         Warehouse-rij met klant schendt CK_SupplyCommitments_Route_CustomerLink.
    /// <inheritdoc />
    public partial class AddSupplyCommitmentCustomerAndRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "SupplyCommitments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "SupplyCommitments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitments_CustomerId",
                table: "SupplyCommitments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitments_RequestId",
                table: "SupplyCommitments",
                column: "RequestId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitments_Route_CustomerLink",
                table: "SupplyCommitments",
                sql: "(\"Route\" <> 'DirectTrade' OR \"CustomerId\" IS NOT NULL) AND (\"Route\" <> 'Warehouse' OR \"CustomerId\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_SupplyCommitments_Customers_CustomerId",
                table: "SupplyCommitments",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupplyCommitments_Customers_CustomerId",
                table: "SupplyCommitments");

            migrationBuilder.DropIndex(
                name: "IX_SupplyCommitments_CustomerId",
                table: "SupplyCommitments");

            migrationBuilder.DropIndex(
                name: "IX_SupplyCommitments_RequestId",
                table: "SupplyCommitments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitments_Route_CustomerLink",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "SupplyCommitments");
        }
    }
}
