using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderConfirmationAndDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt",
                table: "SalesOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedBy",
                table: "SalesOrders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicensePlate",
                table: "SalesOrders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LoadAt",
                table: "SalesOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoadingSlipLanguage",
                table: "CompanySettings",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<bool>(
                name: "LoadingSlipShowCustomer",
                table: "CompanySettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "LoadingSlipShowSupplier",
                table: "CompanySettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "SalesOrderDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderDocuments", x => x.Id);
                    table.CheckConstraint("CK_SalesOrderDocuments_Sent", "(\"SentAt\" IS NULL) = (\"SentBy\" IS NULL)");
                    table.CheckConstraint("CK_SalesOrderDocuments_Version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_SalesOrderDocuments_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrders_Confirmed",
                table: "SalesOrders",
                sql: "(\"ConfirmedAt\" IS NULL) = (\"ConfirmedBy\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderDocuments_SalesOrderId_Kind_Version",
                table: "SalesOrderDocuments",
                columns: new[] { "SalesOrderId", "Kind", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesOrderDocuments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrders_Confirmed",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "ConfirmedBy",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "LicensePlate",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "LoadAt",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "LoadingSlipLanguage",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "LoadingSlipShowCustomer",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "LoadingSlipShowSupplier",
                table: "CompanySettings");
        }
    }
}
