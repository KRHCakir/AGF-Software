using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentDeliveryRetraction : Migration
    {
        /// <inheritdoc />
        // Veilige volgorde, alles in één transactie (Npgsql): eerst het volgnummer toevoegen met waarde 1 voor de bestaande rijen (de oude
        // unieke index garandeert hooguit één rij per verzending, dus verzending + 1 is uniek), dan de check en de nieuwe samengestelde
        // unieke indexen, pas daarna de oude unieke indexen weg en de default eraf (de applicatie zet het volgnummer altijd zelf).
        // Bestaande bevestigingen blijven verder byte-gelijk.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Sequence",
                table: "ShipmentDeliveryConfirmations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ShipmentDeliveryConfirmations_Sequence_Positive",
                table: "ShipmentDeliveryConfirmations",
                sql: "\"Sequence\" >= 1");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentDeliveryConfirmations_DirectTradeShipmentId_Sequence",
                table: "ShipmentDeliveryConfirmations",
                columns: new[] { "DirectTradeShipmentId", "Sequence" },
                unique: true,
                filter: "\"DirectTradeShipmentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentDeliveryConfirmations_SalesDispatchId_Sequence",
                table: "ShipmentDeliveryConfirmations",
                columns: new[] { "SalesDispatchId", "Sequence" },
                unique: true,
                filter: "\"SalesDispatchId\" IS NOT NULL");

            migrationBuilder.DropIndex(
                name: "IX_ShipmentDeliveryConfirmations_DirectTradeShipmentId",
                table: "ShipmentDeliveryConfirmations");

            migrationBuilder.DropIndex(
                name: "IX_ShipmentDeliveryConfirmations_SalesDispatchId",
                table: "ShipmentDeliveryConfirmations");

            migrationBuilder.AlterColumn<int>(
                name: "Sequence",
                table: "ShipmentDeliveryConfirmations",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.CreateTable(
                name: "ShipmentDeliveryRetractions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentDeliveryConfirmationId = table.Column<int>(type: "integer", nullable: false),
                    ShipmentKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SalesDispatchId = table.Column<int>(type: "integer", nullable: true),
                    DirectTradeShipmentId = table.Column<int>(type: "integer", nullable: true),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecordedInErrorConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    ConfirmationCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConfirmationStatement = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentDeliveryRetractions", x => x.Id);
                    table.CheckConstraint("CK_ShipmentDeliveryRetractions_Code", "\"ConfirmationCode\" = 'DELIVERY_CONFIRMATION_RECORDED_IN_ERROR'");
                    table.CheckConstraint("CK_ShipmentDeliveryRetractions_Confirmed", "\"RecordedInErrorConfirmed\" = TRUE");
                    table.CheckConstraint("CK_ShipmentDeliveryRetractions_OneShipment", "(\"ShipmentKind\" = 'Warehouse' AND \"SalesDispatchId\" IS NOT NULL AND \"DirectTradeShipmentId\" IS NULL) OR (\"ShipmentKind\" = 'DirectTrade' AND \"DirectTradeShipmentId\" IS NOT NULL AND \"SalesDispatchId\" IS NULL)");
                    table.CheckConstraint("CK_ShipmentDeliveryRetractions_Reason_NotBlank", "length(trim(\"Reason\")) > 0");
                    table.CheckConstraint("CK_ShipmentDeliveryRetractions_Statement", "\"ConfirmationStatement\" = 'Deze afleverbevestiging is ten onrechte vastgelegd.'");
                    table.ForeignKey(
                        name: "FK_ShipmentDeliveryRetractions_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentDeliveryRetractions_ShipmentDeliveryConfirmations_S~",
                        column: x => x.ShipmentDeliveryConfirmationId,
                        principalTable: "ShipmentDeliveryConfirmations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentDeliveryRetractions_RequestId",
                table: "ShipmentDeliveryRetractions",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentDeliveryRetractions_SalesOrderId",
                table: "ShipmentDeliveryRetractions",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentDeliveryRetractions_ShipmentDeliveryConfirmationId",
                table: "ShipmentDeliveryRetractions",
                column: "ShipmentDeliveryConfirmationId",
                unique: true);
        }

        /// <inheritdoc />
        // Terug kan alleen zolang elke verzending hooguit één bevestiging heeft en er geen intrekking bestaat; anders zou de oude unieke
        // index historie moeten weggooien. De migratie weigert dan expliciet.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM ""ShipmentDeliveryRetractions"")
     OR EXISTS (SELECT 1 FROM ""ShipmentDeliveryConfirmations"" WHERE ""Sequence"" > 1) THEN
    RAISE EXCEPTION 'AddShipmentDeliveryRetraction cannot be reverted: delivery retraction history exists.';
  END IF;
END $$;");

            migrationBuilder.DropTable(
                name: "ShipmentDeliveryRetractions");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentDeliveryConfirmations_DirectTradeShipmentId",
                table: "ShipmentDeliveryConfirmations",
                column: "DirectTradeShipmentId",
                unique: true,
                filter: "\"DirectTradeShipmentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentDeliveryConfirmations_SalesDispatchId",
                table: "ShipmentDeliveryConfirmations",
                column: "SalesDispatchId",
                unique: true,
                filter: "\"SalesDispatchId\" IS NOT NULL");

            migrationBuilder.DropIndex(
                name: "IX_ShipmentDeliveryConfirmations_DirectTradeShipmentId_Sequence",
                table: "ShipmentDeliveryConfirmations");

            migrationBuilder.DropIndex(
                name: "IX_ShipmentDeliveryConfirmations_SalesDispatchId_Sequence",
                table: "ShipmentDeliveryConfirmations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ShipmentDeliveryConfirmations_Sequence_Positive",
                table: "ShipmentDeliveryConfirmations");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "ShipmentDeliveryConfirmations");
        }
    }
}
