using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    // ROLLOUT-VOORWAARDE (go/no-go), vóór toepassen op een klantdatabase; beide queries moeten 0 geven:
    //   SELECT COUNT(*) FROM "Pallets" WHERE NOT (
    //     ("LocationContext" <> 'Warehouse' OR "LogisticsStatus" = 'Received')
    //     AND ("WarehouseLocation" IS NULL OR ("LocationContext" = 'Warehouse' AND "LogisticsStatus" = 'Received' AND length(trim("WarehouseLocation")) > 0))
    //     AND ("LogisticsStatus" <> 'Delivered' OR "LocationContext" IN ('Customer', 'External'))
    //     AND "LogisticsStatus" IN ('Expected', 'InTransit', 'Received', 'Dispatched', 'Delivered', 'Cancelled')
    //     AND "LocationContext" IN ('Supplier', 'Warehouse', 'Crossdock', 'InTransit', 'Customer', 'External'));
    //   SELECT COUNT(*) FROM "PalletContentLines" WHERE "QualityStatus" NOT IN ('Quarantine', 'Approved', 'Blocked', 'Rejected');
    //   0  -> veilig toepassen.
    //   >0 -> stoppen, niets forceren en eerst een aparte datacorrectie plannen (de check-constraints zouden falen).
    // Version krijgt default 0 (start voor bestaande rijen); RequestId op audit is nullable, dus bestaande events blijven geldig.
    /// <inheritdoc />
    public partial class AddPalletStateConstraintsAndVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Pallets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "PalletContentLines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "PalletAuditEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pallets_Delivered_Location",
                table: "Pallets",
                sql: "\"LogisticsStatus\" <> 'Delivered' OR \"LocationContext\" IN ('Customer', 'External')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pallets_Location_Status",
                table: "Pallets",
                sql: "(\"LocationContext\" <> 'Warehouse' OR \"LogisticsStatus\" = 'Received') AND (\"WarehouseLocation\" IS NULL OR (\"LocationContext\" = 'Warehouse' AND \"LogisticsStatus\" = 'Received' AND length(trim(\"WarehouseLocation\")) > 0))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pallets_LocationContext_Allowed",
                table: "Pallets",
                sql: "\"LocationContext\" IN ('Supplier', 'Warehouse', 'Crossdock', 'InTransit', 'Customer', 'External')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pallets_LogisticsStatus_Allowed",
                table: "Pallets",
                sql: "\"LogisticsStatus\" IN ('Expected', 'InTransit', 'Received', 'Dispatched', 'Delivered', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PalletContentLines_QualityStatus_Allowed",
                table: "PalletContentLines",
                sql: "\"QualityStatus\" IN ('Quarantine', 'Approved', 'Blocked', 'Rejected')");

            migrationBuilder.CreateIndex(
                name: "IX_PalletAuditEvents_RequestId",
                table: "PalletAuditEvents",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Pallets_Delivered_Location",
                table: "Pallets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pallets_Location_Status",
                table: "Pallets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pallets_LocationContext_Allowed",
                table: "Pallets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Pallets_LogisticsStatus_Allowed",
                table: "Pallets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PalletContentLines_QualityStatus_Allowed",
                table: "PalletContentLines");

            migrationBuilder.DropIndex(
                name: "IX_PalletAuditEvents_RequestId",
                table: "PalletAuditEvents");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Pallets");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "PalletContentLines");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "PalletAuditEvents");
        }
    }
}
