using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddPalletTraceability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PalletContentLineId",
                table: "StockReservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PalletContentLineId",
                table: "StockMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Pallets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sscc = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: true),
                    PalletType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TareKg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    GrossKg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    DeclaredNetKg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    LogisticsStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LocationContext = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WarehouseLocation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StockReceiptId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pallets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pallets_StockReceipts_StockReceiptId",
                        column: x => x.StockReceiptId,
                        principalTable: "StockReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PalletContentLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PalletId = table.Column<int>(type: "integer", nullable: false),
                    SupplyCommitmentLineId = table.Column<int>(type: "integer", nullable: false),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NetWeightKg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    QualityStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    QualityReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PalletContentLines", x => x.Id);
                    table.CheckConstraint("CK_PalletContentLines_LotNumber_NotBlank", "length(trim(\"LotNumber\")) > 0");
                    table.CheckConstraint("CK_PalletContentLines_NetWeightKg_Positive", "CAST(\"NetWeightKg\" AS NUMERIC) > 0");
                    table.CheckConstraint("CK_PalletContentLines_Quantity_Positive", "CAST(\"Quantity\" AS NUMERIC) > 0");
                    table.CheckConstraint("CK_PalletContentLines_Unit_NotBlank", "length(trim(\"Unit\")) > 0");
                    table.ForeignKey(
                        name: "FK_PalletContentLines_Pallets_PalletId",
                        column: x => x.PalletId,
                        principalTable: "Pallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PalletContentLines_SupplyCommitmentLines_SupplyCommitmentLi~",
                        column: x => x.SupplyCommitmentLineId,
                        principalTable: "SupplyCommitmentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PalletAuditEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PalletId = table.Column<int>(type: "integer", nullable: true),
                    PalletContentLineId = table.Column<int>(type: "integer", nullable: true),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OldValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PalletAuditEvents", x => x.Id);
                    table.CheckConstraint("CK_PalletAuditEvents_HasTarget", "\"PalletId\" IS NOT NULL OR \"PalletContentLineId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_PalletAuditEvents_PalletContentLines_PalletContentLineId",
                        column: x => x.PalletContentLineId,
                        principalTable: "PalletContentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PalletAuditEvents_Pallets_PalletId",
                        column: x => x.PalletId,
                        principalTable: "Pallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_PalletContentLineId",
                table: "StockReservations",
                column: "PalletContentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_PalletContentLineId",
                table: "StockMovements",
                column: "PalletContentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PalletAuditEvents_PalletContentLineId",
                table: "PalletAuditEvents",
                column: "PalletContentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PalletAuditEvents_PalletId",
                table: "PalletAuditEvents",
                column: "PalletId");

            migrationBuilder.CreateIndex(
                name: "IX_PalletContentLines_PalletId",
                table: "PalletContentLines",
                column: "PalletId");

            migrationBuilder.CreateIndex(
                name: "IX_PalletContentLines_SupplyCommitmentLineId",
                table: "PalletContentLines",
                column: "SupplyCommitmentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_Pallets_Reference",
                table: "Pallets",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pallets_Sscc",
                table: "Pallets",
                column: "Sscc",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pallets_StockReceiptId",
                table: "Pallets",
                column: "StockReceiptId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_PalletContentLines_PalletContentLineId",
                table: "StockMovements",
                column: "PalletContentLineId",
                principalTable: "PalletContentLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockReservations_PalletContentLines_PalletContentLineId",
                table: "StockReservations",
                column: "PalletContentLineId",
                principalTable: "PalletContentLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_PalletContentLines_PalletContentLineId",
                table: "StockMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_StockReservations_PalletContentLines_PalletContentLineId",
                table: "StockReservations");

            migrationBuilder.DropTable(
                name: "PalletAuditEvents");

            migrationBuilder.DropTable(
                name: "PalletContentLines");

            migrationBuilder.DropTable(
                name: "Pallets");

            migrationBuilder.DropIndex(
                name: "IX_StockReservations_PalletContentLineId",
                table: "StockReservations");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_PalletContentLineId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "PalletContentLineId",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "PalletContentLineId",
                table: "StockMovements");
        }
    }
}
