using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Veilige overgang: eerst de stamtabel en nullable verwijzingen, dan per bestaande termijn één conditie "{n}D" (basis
            // factuurdatum, geen korting) en de rollen daaraan koppelen op exact dezelfde dagen, dan pas verplicht. PaymentTermsDays
            // op Customers/Vendors wordt niet herschreven; het blijft de spiegel van PaymentConditions.Days.
            migrationBuilder.AddColumn<int>(
                name: "PaymentConditionId",
                table: "Vendors",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentConditionId",
                table: "Customers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentConditions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestPayload = table.Column<string>(type: "text", nullable: true),
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CodeKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DescriptionNl = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DescriptionEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Days = table.Column<int>(type: "integer", nullable: false),
                    Basis = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    DiscountDays = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentConditions", x => x.Id);
                    table.CheckConstraint("CK_PaymentConditions_Basis_Allowed", "\"Basis\" IN ('InvoiceDate', 'EndOfMonth')");
                    table.CheckConstraint("CK_PaymentConditions_Code_NotBlank", "length(trim(\"Code\")) > 0");
                    table.CheckConstraint("CK_PaymentConditions_Discount_BothOrNeither", "(\"DiscountPercent\" IS NULL) = (\"DiscountDays\" IS NULL)");
                });

            // ---- Gegevensovergang (PostgreSQL) ----
            // Eén conditie per voorkomende termijn: klanten ∪ leveranciers ∪ {14, 30} ∪ de standaardtermijn voor inkoopfacturen.
            // Zelfde code en omschrijving als PaymentConditionRules.DefaultCodeForDays / DescriptionNl / DescriptionEn.
            migrationBuilder.Sql("""
                INSERT INTO "PaymentConditions" ("Code", "CodeKey", "DescriptionNl", "DescriptionEn", "Days", "Basis", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt", "Version")
                SELECT s.d || 'D',
                       lower(s.d || 'D'),
                       s.d || CASE WHEN s.d = 1 THEN ' dag na factuurdatum' ELSE ' dagen na factuurdatum' END,
                       s.d || CASE WHEN s.d = 1 THEN ' day after invoice date' ELSE ' days after invoice date' END,
                       s.d, 'InvoiceDate', TRUE, s.d, now(), now(), 0
                FROM (
                    SELECT "PaymentTermsDays" AS d FROM "Customers"
                    UNION SELECT "PaymentTermsDays" FROM "Vendors"
                    UNION SELECT 14
                    UNION SELECT 30
                    UNION SELECT "PurchaseInvoiceDefaultPaymentTermsDays" FROM "CompanySettings"
                ) s
                ORDER BY s.d;

                UPDATE "Customers" c SET "PaymentConditionId" = pc."Id" FROM "PaymentConditions" pc WHERE pc."CodeKey" = lower(c."PaymentTermsDays" || 'D');
                UPDATE "Vendors" v SET "PaymentConditionId" = pc."Id" FROM "PaymentConditions" pc WHERE pc."CodeKey" = lower(v."PaymentTermsDays" || 'D');
                """);

            migrationBuilder.AlterColumn<int>(
                name: "PaymentConditionId",
                table: "Vendors",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PaymentConditionId",
                table: "Customers",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_PaymentConditionId",
                table: "Vendors",
                column: "PaymentConditionId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_PaymentConditionId",
                table: "Customers",
                column: "PaymentConditionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentConditions_CodeKey",
                table: "PaymentConditions",
                column: "CodeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentConditions_RequestId",
                table: "PaymentConditions",
                column: "RequestId",
                unique: true,
                filter: "\"RequestId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_PaymentConditions_PaymentConditionId",
                table: "Customers",
                column: "PaymentConditionId",
                principalTable: "PaymentConditions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vendors_PaymentConditions_PaymentConditionId",
                table: "Vendors",
                column: "PaymentConditionId",
                principalTable: "PaymentConditions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_PaymentConditions_PaymentConditionId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Vendors_PaymentConditions_PaymentConditionId",
                table: "Vendors");

            migrationBuilder.DropTable(
                name: "PaymentConditions");

            migrationBuilder.DropIndex(
                name: "IX_Vendors_PaymentConditionId",
                table: "Vendors");

            migrationBuilder.DropIndex(
                name: "IX_Customers_PaymentConditionId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PaymentConditionId",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "PaymentConditionId",
                table: "Customers");
        }
    }
}
