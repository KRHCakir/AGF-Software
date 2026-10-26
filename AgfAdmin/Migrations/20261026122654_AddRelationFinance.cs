using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddRelationFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Alleen toevoegend: bestaande rollen krijgen betaalwijze BankTransfer, klanten valuta EUR en geen kredietverzekering;
            // leveranciersvaluta (DefaultCurrency) blijft per rij ongewijzigd. Bankrekeningen en hun audit starten leeg.
            migrationBuilder.AddColumn<string>(
                name: "DeliveryTermKind",
                table: "Vendors",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryTermText",
                table: "Vendors",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Incoterm",
                table: "Vendors",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncotermPlace",
                table: "Vendors",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MandateReference",
                table: "Vendors",
                type: "character varying(35)",
                maxLength: 35,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MandateSignedOn",
                table: "Vendors",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Vendors",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "BankTransfer");

            migrationBuilder.AddColumn<string>(
                name: "TradeConditions",
                table: "Vendors",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeConditionsText",
                table: "Vendors",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CertificateValidUntil",
                table: "VendorGgns",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckedBy",
                table: "VendorGgns",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CheckedOn",
                table: "VendorGgns",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasGgnLabel",
                table: "VendorGgns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit",
                table: "Customers",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Customers",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "EUR");

            migrationBuilder.AddColumn<string>(
                name: "DeliveryTermKind",
                table: "Customers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryTermText",
                table: "Customers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Incoterm",
                table: "Customers",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncotermPlace",
                table: "Customers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InsuredLimit",
                table: "Customers",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "InsuredUntil",
                table: "Customers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Insurer",
                table: "Customers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InsurerReference",
                table: "Customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InsurerText",
                table: "Customers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInsured",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MandateReference",
                table: "Customers",
                type: "character varying(35)",
                maxLength: 35,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MandateSignedOn",
                table: "Customers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Customers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "BankTransfer");

            migrationBuilder.AddColumn<string>(
                name: "TradeConditions",
                table: "Customers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TradeConditionsText",
                table: "Customers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GlobalGapPortalUrl",
                table: "CompanySettings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "https://prod.osapiens.cloud/portal/webbundle/foodplus/field-service-os/supply-chain-portal?app-route-hash=%252Fcertificates");

            migrationBuilder.CreateTable(
                name: "RelationBankAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RelationId = table.Column<int>(type: "integer", nullable: false),
                    Iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    OtherAccountNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Bic = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    AccountHolder = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ConfirmationStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationBankAccounts", x => x.Id);
                    table.CheckConstraint("CK_RelationBankAccounts_AccountHolder_NotBlank", "length(trim(\"AccountHolder\")) > 0");
                    table.CheckConstraint("CK_RelationBankAccounts_ConfirmationStatus_Allowed", "\"ConfirmationStatus\" IN ('Unconfirmed', 'Confirmed')");
                    table.CheckConstraint("CK_RelationBankAccounts_ExactlyOneNumber", "(\"Iban\" IS NULL) <> (\"OtherAccountNumber\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_RelationBankAccounts_Relations_RelationId",
                        column: x => x.RelationId,
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RelationBankAccountEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RelationBankAccountId = table.Column<int>(type: "integer", nullable: false),
                    RelationId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OldIban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    NewIban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    OldAccountHolder = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NewAccountHolder = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OldOtherAccountNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NewOtherAccountNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationBankAccountEvents", x => x.Id);
                    table.CheckConstraint("CK_RelationBankAccountEvents_Actor_NotBlank", "length(trim(\"ActorReference\")) > 0");
                    table.CheckConstraint("CK_RelationBankAccountEvents_Kind_Allowed", "\"Kind\" IN ('Added', 'Changed', 'Confirmed', 'Deactivated')");
                    table.ForeignKey(
                        name: "FK_RelationBankAccountEvents_RelationBankAccounts_RelationBank~",
                        column: x => x.RelationBankAccountId,
                        principalTable: "RelationBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RelationBankAccountEvents_Relations_RelationId",
                        column: x => x.RelationId,
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Vendors_DeliveryTermKind_Allowed",
                table: "Vendors",
                sql: "\"DeliveryTermKind\" IS NULL OR \"DeliveryTermKind\" IN ('Incoterm', 'Franco', 'AfKwekerij', 'Other')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Vendors_PaymentMethod_Allowed",
                table: "Vendors",
                sql: "\"PaymentMethod\" IN ('BankTransfer', 'DirectDebit', 'Cash', 'Offset')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Vendors_TradeConditions_Allowed",
                table: "Vendors",
                sql: "\"TradeConditions\" IS NULL OR \"TradeConditions\" IN ('Own', 'Frugiventa', 'Cofreurop', 'Other')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_CreditLimit_NotNegative",
                table: "Customers",
                sql: "\"CreditLimit\" IS NULL OR \"CreditLimit\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_DeliveryTermKind_Allowed",
                table: "Customers",
                sql: "\"DeliveryTermKind\" IS NULL OR \"DeliveryTermKind\" IN ('Incoterm', 'Franco', 'AfKwekerij', 'Other')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_InsuredLimit_NotNegative",
                table: "Customers",
                sql: "\"InsuredLimit\" IS NULL OR \"InsuredLimit\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_Insurer_Allowed",
                table: "Customers",
                sql: "\"Insurer\" IS NULL OR \"Insurer\" IN ('Atradius', 'AllianzTrade', 'Coface', 'Other')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_PaymentMethod_Allowed",
                table: "Customers",
                sql: "\"PaymentMethod\" IN ('BankTransfer', 'DirectDebit', 'Cash', 'Offset')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_TradeConditions_Allowed",
                table: "Customers",
                sql: "\"TradeConditions\" IS NULL OR \"TradeConditions\" IN ('Own', 'Frugiventa', 'Cofreurop', 'Other')");

            migrationBuilder.CreateIndex(
                name: "IX_RelationBankAccountEvents_RelationBankAccountId",
                table: "RelationBankAccountEvents",
                column: "RelationBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RelationBankAccountEvents_RelationId_OccurredAt",
                table: "RelationBankAccountEvents",
                columns: new[] { "RelationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RelationBankAccounts_RelationId_ActiveDefault",
                table: "RelationBankAccounts",
                column: "RelationId",
                unique: true,
                filter: "\"IsDefault\" AND \"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_RelationBankAccounts_RelationId_SortOrder",
                table: "RelationBankAccounts",
                columns: new[] { "RelationId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RelationBankAccountEvents");

            migrationBuilder.DropTable(
                name: "RelationBankAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Vendors_DeliveryTermKind_Allowed",
                table: "Vendors");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Vendors_PaymentMethod_Allowed",
                table: "Vendors");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Vendors_TradeConditions_Allowed",
                table: "Vendors");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_CreditLimit_NotNegative",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_DeliveryTermKind_Allowed",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_InsuredLimit_NotNegative",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_Insurer_Allowed",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_PaymentMethod_Allowed",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_TradeConditions_Allowed",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "DeliveryTermKind",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "DeliveryTermText",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "Incoterm",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "IncotermPlace",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "MandateReference",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "MandateSignedOn",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "TradeConditions",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "TradeConditionsText",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "CertificateValidUntil",
                table: "VendorGgns");

            migrationBuilder.DropColumn(
                name: "CheckedBy",
                table: "VendorGgns");

            migrationBuilder.DropColumn(
                name: "CheckedOn",
                table: "VendorGgns");

            migrationBuilder.DropColumn(
                name: "HasGgnLabel",
                table: "VendorGgns");

            migrationBuilder.DropColumn(
                name: "CreditLimit",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "DeliveryTermKind",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "DeliveryTermText",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Incoterm",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IncotermPlace",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "InsuredLimit",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "InsuredUntil",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Insurer",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "InsurerReference",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "InsurerText",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IsInsured",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "MandateReference",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "MandateSignedOn",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TradeConditions",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TradeConditionsText",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "GlobalGapPortalUrl",
                table: "CompanySettings");
        }
    }
}
