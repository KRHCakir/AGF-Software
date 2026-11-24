using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    // Feedback 22 november: een verkoop-partij is één levering aan één klant. Elke bestaande verkooporder krijgt haar eerste levering
    // (volgnummer 1) met de verkoop-partij die ze al had: geen nummer verandert. Bestaande laadbonnen en transportopdrachten horen bij die
    // eerste levering. Bestaande reserveringen blijven ongewijzigd (een lege levering = de eerste).
    public partial class AddSalesDeliveries : Migration
    {
        // Gewone SQL die ook in SQLite (de tests) draait. Twee keer uitvoeren doet niets meer.
        public const string BackfillSql = """
            INSERT INTO "SalesDeliveries" ("SalesOrderId", "Sequence", "PartijId", "CreatedAt", "CreatedBy", "Version")
            SELECT p."SalesOrderId", 1, p."Id", o."CreatedAt", p."CreatedBy", 0
            FROM "Partijen" p JOIN "SalesOrders" o ON o."Id" = p."SalesOrderId"
            WHERE p."Kind" = 'Sales' AND p."SalesOrderId" IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM "SalesDeliveries" d WHERE d."SalesOrderId" = p."SalesOrderId");
            UPDATE "SalesOrderDocuments" SET "SalesDeliveryId" =
                (SELECT d."Id" FROM "SalesDeliveries" d WHERE d."SalesOrderId" = "SalesOrderDocuments"."SalesOrderId" AND d."Sequence" = 1)
            WHERE "SalesDeliveryId" = 0;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SalesOrderDocuments_SalesOrderId_Kind_Version",
                table: "SalesOrderDocuments");

            migrationBuilder.DropIndex(
                name: "IX_Partijen_SalesOrderId",
                table: "Partijen");

            migrationBuilder.AddColumn<int>(
                name: "SalesDeliveryId",
                table: "StockReservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesDeliveryId",
                table: "SalesOrderDocuments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SalesDeliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    PartijId = table.Column<int>(type: "integer", nullable: false),
                    LoadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LicensePlate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesDeliveries", x => x.Id);
                    table.CheckConstraint("CK_SalesDeliveries_Sequence", "\"Sequence\" >= 1");
                    table.ForeignKey(
                        name: "FK_SalesDeliveries_Partijen_PartijId",
                        column: x => x.PartijId,
                        principalTable: "Partijen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesDeliveries_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(BackfillSql);

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_SalesDeliveryId",
                table: "StockReservations",
                column: "SalesDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderDocuments_SalesDeliveryId_Kind_Version",
                table: "SalesOrderDocuments",
                columns: new[] { "SalesDeliveryId", "Kind", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderDocuments_SalesOrderId",
                table: "SalesOrderDocuments",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Partijen_SalesOrderId",
                table: "Partijen",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDeliveries_PartijId",
                table: "SalesDeliveries",
                column: "PartijId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesDeliveries_RequestId",
                table: "SalesDeliveries",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesDeliveries_SalesOrderId_Sequence",
                table: "SalesDeliveries",
                columns: new[] { "SalesOrderId", "Sequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderDocuments_SalesDeliveries_SalesDeliveryId",
                table: "SalesOrderDocuments",
                column: "SalesDeliveryId",
                principalTable: "SalesDeliveries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockReservations_SalesDeliveries_SalesDeliveryId",
                table: "StockReservations",
                column: "SalesDeliveryId",
                principalTable: "SalesDeliveries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrderDocuments_SalesDeliveries_SalesDeliveryId",
                table: "SalesOrderDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_StockReservations_SalesDeliveries_SalesDeliveryId",
                table: "StockReservations");

            migrationBuilder.DropTable(
                name: "SalesDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_StockReservations_SalesDeliveryId",
                table: "StockReservations");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderDocuments_SalesDeliveryId_Kind_Version",
                table: "SalesOrderDocuments");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderDocuments_SalesOrderId",
                table: "SalesOrderDocuments");

            migrationBuilder.DropIndex(
                name: "IX_Partijen_SalesOrderId",
                table: "Partijen");

            migrationBuilder.DropColumn(
                name: "SalesDeliveryId",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "SalesDeliveryId",
                table: "SalesOrderDocuments");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderDocuments_SalesOrderId_Kind_Version",
                table: "SalesOrderDocuments",
                columns: new[] { "SalesOrderId", "Kind", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partijen_SalesOrderId",
                table: "Partijen",
                column: "SalesOrderId",
                unique: true);
        }
    }
}
