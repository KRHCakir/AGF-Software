using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseLineSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CancelledQuantity",
                table: "SupplyCommitmentLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SplitFromLineId",
                table: "SupplyCommitmentLines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitmentLines_SplitFromLineId",
                table: "SupplyCommitmentLines",
                column: "SplitFromLineId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitmentLines_CancelledQuantity_Positive",
                table: "SupplyCommitmentLines",
                sql: "\"CancelledQuantity\" IS NULL OR \"CancelledQuantity\" > 0");

            migrationBuilder.AddForeignKey(
                name: "FK_SupplyCommitmentLines_SupplyCommitmentLines_SplitFromLineId",
                table: "SupplyCommitmentLines",
                column: "SplitFromLineId",
                principalTable: "SupplyCommitmentLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupplyCommitmentLines_SupplyCommitmentLines_SplitFromLineId",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropIndex(
                name: "IX_SupplyCommitmentLines_SplitFromLineId",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitmentLines_CancelledQuantity_Positive",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "CancelledQuantity",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "SplitFromLineId",
                table: "SupplyCommitmentLines");
        }
    }
}
