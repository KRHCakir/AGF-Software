using System;
using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    // Kosten verdelen over partijen (P2): PartijCosts (een deel van een inkoopfactuurregel excl. btw op een partij), de append-only
    // historie PartijCostAuditEvents en een versie van de verdeling per factuurregel. Alleen nieuwe, lege tabellen en een kolom met
    // standaard 0: bestaande gegevens veranderen niet. Het Designer-bestand is weggelaten (zie AddPartijen); de modelsnapshot is
    // bijgewerkt en de attributen hieronder vervangen het.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261106130454_AddPartijCosts")]
    public partial class AddPartijCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AllocationVersion",
                table: "IncomingInvoiceLines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PartijCostAuditEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IncomingInvoiceId = table.Column<int>(type: "integer", nullable: false),
                    IncomingInvoiceLineId = table.Column<int>(type: "integer", nullable: false),
                    PartijId = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OldAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    NewAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartijCostAuditEvents", x => x.Id);
                    table.CheckConstraint("CK_PartijCostAuditEvents_EventType_Allowed", "\"EventType\" IN ('Allocated', 'Changed', 'Removed')");
                });

            migrationBuilder.CreateTable(
                name: "PartijCosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IncomingInvoiceLineId = table.Column<int>(type: "integer", nullable: false),
                    PartijId = table.Column<int>(type: "integer", nullable: false),
                    AmountExclVat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartijCosts", x => x.Id);
                    table.CheckConstraint("CK_PartijCosts_Amount_Positive", "\"AmountExclVat\" > 0");
                    table.ForeignKey(
                        name: "FK_PartijCosts_IncomingInvoiceLines_IncomingInvoiceLineId",
                        column: x => x.IncomingInvoiceLineId,
                        principalTable: "IncomingInvoiceLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartijCosts_Partijen_PartijId",
                        column: x => x.PartijId,
                        principalTable: "Partijen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartijCostAuditEvents_IncomingInvoiceId",
                table: "PartijCostAuditEvents",
                column: "IncomingInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PartijCostAuditEvents_PartijId",
                table: "PartijCostAuditEvents",
                column: "PartijId");

            migrationBuilder.CreateIndex(
                name: "IX_PartijCostAuditEvents_RequestId",
                table: "PartijCostAuditEvents",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PartijCosts_IncomingInvoiceLineId_PartijId",
                table: "PartijCosts",
                columns: new[] { "IncomingInvoiceLineId", "PartijId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartijCosts_PartijId",
                table: "PartijCosts",
                column: "PartijId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartijCostAuditEvents");

            migrationBuilder.DropTable(
                name: "PartijCosts");

            migrationBuilder.DropColumn(
                name: "AllocationVersion",
                table: "IncomingInvoiceLines");
        }
    }
}
