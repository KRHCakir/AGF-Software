using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    // Feedback 22 november: een kleurenlijst per product en een kleur op de inkoop-partij; beide leeg voor alle bestaande rijen. De historie
    // van een partij kent een soort erbij: ColorChanged.
    public partial class AddLotColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PartijAuditEvents_EventType_Allowed",
                table: "PartijAuditEvents");

            migrationBuilder.AddColumn<string>(
                name: "ColorValues",
                table: "Products",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "Partijen",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PartijAuditEvents_EventType_Allowed",
                table: "PartijAuditEvents",
                sql: "\"EventType\" IN ('NumberCorrected', 'OrderLinked', 'Deleted', 'NumberChanged', 'ColorChanged')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PartijAuditEvents_EventType_Allowed",
                table: "PartijAuditEvents");

            migrationBuilder.DropColumn(
                name: "ColorValues",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "Partijen");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PartijAuditEvents_EventType_Allowed",
                table: "PartijAuditEvents",
                sql: "\"EventType\" IN ('NumberCorrected', 'OrderLinked', 'Deleted', 'NumberChanged')");
        }
    }
}
