using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    // Feedback 22 november: de leveringsconditie per verkoop- en inkooporder. Alleen nieuwe, lege kolommen: bestaande orders houden geen
    // eigen conditie (leeg); een nieuwe order neemt die van de klant of leverancier over of krijgt de gekozen conditie.
    public partial class AddOrderDeliveryTerm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryTermKind",
                table: "SupplyCommitments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryTermText",
                table: "SupplyCommitments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Incoterm",
                table: "SupplyCommitments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncotermPlace",
                table: "SupplyCommitments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryTermKind",
                table: "SalesOrders",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryTermText",
                table: "SalesOrders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Incoterm",
                table: "SalesOrders",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncotermPlace",
                table: "SalesOrders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitments_DeliveryTermKind_Allowed",
                table: "SupplyCommitments",
                sql: "\"DeliveryTermKind\" IS NULL OR \"DeliveryTermKind\" IN ('Incoterm', 'Franco', 'AfKwekerij', 'Other')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrders_DeliveryTermKind_Allowed",
                table: "SalesOrders",
                sql: "\"DeliveryTermKind\" IS NULL OR \"DeliveryTermKind\" IN ('Incoterm', 'Franco', 'AfKwekerij', 'Other')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitments_DeliveryTermKind_Allowed",
                table: "SupplyCommitments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrders_DeliveryTermKind_Allowed",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "DeliveryTermKind",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "DeliveryTermText",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "Incoterm",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "IncotermPlace",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "DeliveryTermKind",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "DeliveryTermText",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "Incoterm",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "IncotermPlace",
                table: "SalesOrders");
        }
    }
}
