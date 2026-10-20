using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectTradeAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_SalesFlowComplete",
                table: "StockReservations");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "SupplyCommitmentLines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_DirectTrade_StatusAllowed",
                table: "StockReservations",
                sql: "\"Source\" IS NULL OR \"Source\" <> 'DirectTrade' OR \"Status\" IN ('Active', 'Released')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_SalesFlowComplete",
                table: "StockReservations",
                sql: "\"SalesOrderLineId\" IS NULL OR (\"RequestId\" IS NOT NULL AND \"Unit\" IS NOT NULL AND \"Source\" IS NOT NULL AND \"Source\" IN ('Physical', 'DirectTrade') AND ((\"Source\" = 'Physical' AND \"PalletContentLineId\" IS NOT NULL AND \"NetWeightKg\" IS NOT NULL AND CAST(\"NetWeightKg\" AS NUMERIC) > 0) OR (\"Source\" = 'DirectTrade' AND \"PalletContentLineId\" IS NULL AND \"NetWeightKg\" IS NULL)))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_DirectTrade_StatusAllowed",
                table: "StockReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReservations_SalesFlowComplete",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "SupplyCommitmentLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReservations_SalesFlowComplete",
                table: "StockReservations",
                sql: "\"SalesOrderLineId\" IS NULL OR (\"PalletContentLineId\" IS NOT NULL AND \"RequestId\" IS NOT NULL AND \"Unit\" IS NOT NULL AND \"NetWeightKg\" IS NOT NULL AND CAST(\"NetWeightKg\" AS NUMERIC) > 0 AND \"Source\" = 'Physical')");
        }
    }
}
