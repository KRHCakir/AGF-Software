using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Veilige overgang: eerst nullable, dan per bestaande klant en leverancier een eigen relatie (geen samenvoeging), dan verplicht.
            migrationBuilder.AddColumn<int>(
                name: "RelationId",
                table: "Vendors",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelationId",
                table: "Customers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Relations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestPayload = table.Column<string>(type: "text", nullable: true),
                    SearchCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SearchCodeKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TradeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PreferredLanguage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Website = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    VatNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    VatVerificationStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "NotChecked"),
                    VatVerifiedNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    VatVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RegistrationNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RegistrationCountry = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    RegistrationType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    LegalForm = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InternalNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relations", x => x.Id);
                    table.CheckConstraint("CK_Relations_Name_NotBlank", "length(trim(\"Name\")) > 0");
                    table.CheckConstraint("CK_Relations_VatVerificationStatus_Allowed", "\"VatVerificationStatus\" IN ('NotChecked', 'Valid', 'Invalid', 'Unavailable', 'NotApplicable')");
                });

            migrationBuilder.CreateTable(
                name: "VendorGgns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VendorId = table.Column<int>(type: "integer", nullable: false),
                    Ggn = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    ProducerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorGgns", x => x.Id);
                    table.CheckConstraint("CK_VendorGgns_Ggn_Length", "length(\"Ggn\") = 13");
                    table.ForeignKey(
                        name: "FK_VendorGgns_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RelationAddresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RelationId = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Line1 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationAddresses", x => x.Id);
                    table.CheckConstraint("CK_RelationAddresses_Purpose_Allowed", "\"Purpose\" IN ('Main', 'Billing', 'Delivery')");
                    table.ForeignKey(
                        name: "FK_RelationAddresses_Relations_RelationId",
                        column: x => x.RelationId,
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RelationContacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RelationId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Department = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    JobTitle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationContacts", x => x.Id);
                    table.CheckConstraint("CK_RelationContacts_Name_NotBlank", "length(trim(\"Name\")) > 0");
                    table.ForeignKey(
                        name: "FK_RelationContacts_Relations_RelationId",
                        column: x => x.RelationId,
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RelationContactChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RelationContactId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationContactChannels", x => x.Id);
                    table.CheckConstraint("CK_RelationContactChannels_Kind_Allowed", "\"Kind\" IN ('Phone', 'Email')");
                    table.ForeignKey(
                        name: "FK_RelationContactChannels_RelationContacts_RelationContactId",
                        column: x => x.RelationContactId,
                        principalTable: "RelationContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ---- Gegevensovergang (PostgreSQL) ----
            // Elke bestaande klant en leverancier wordt afzonderlijk een relatie; bestaande ID's, documentverwijzingen en de inhoud van
            // Customers/Vendors blijven onaangeroerd (de kolommen worden niet herschreven). Vrije adrestekst wordt één adresregel per
            // doel met onbekend land; een contactpersoon wordt één contact met zijn e-mail/telefoon, anders zijn dat bedrijfsgegevens.
            migrationBuilder.Sql("""
                ALTER TABLE "Relations" ADD COLUMN "MigratedCustomerId" integer NULL, ADD COLUMN "MigratedVendorId" integer NULL;

                INSERT INTO "Relations" ("Name", "VatNumber", "Email", "Phone", "VatVerificationStatus", "IsActive", "CreatedAt", "UpdatedAt", "Version", "MigratedCustomerId")
                SELECT COALESCE(NULLIF(trim(c."Name"), ''), 'Klant #' || c."Id"),
                       NULLIF(trim(c."VATNumber"), ''),
                       CASE WHEN NULLIF(trim(c."ContactPerson"), '') IS NULL THEN NULLIF(trim(c."Email"), '') END,
                       CASE WHEN NULLIF(trim(c."ContactPerson"), '') IS NULL THEN NULLIF(trim(c."Phone"), '') END,
                       CASE WHEN NULLIF(trim(c."VATNumber"), '') IS NULL THEN 'NotApplicable' ELSE 'NotChecked' END,
                       TRUE, c."CreatedAt", c."CreatedAt", 0, c."Id"
                FROM "Customers" c
                ORDER BY c."Id";

                INSERT INTO "Relations" ("Name", "VatNumber", "Email", "Phone", "VatVerificationStatus", "IsActive", "CreatedAt", "UpdatedAt", "Version", "MigratedVendorId")
                SELECT COALESCE(NULLIF(trim(v."Name"), ''), 'Leverancier #' || v."Id"),
                       NULLIF(trim(v."VATNumber"), ''),
                       NULLIF(trim(v."Email"), ''),
                       NULL,
                       CASE WHEN NULLIF(trim(v."VATNumber"), '') IS NULL THEN 'NotApplicable' ELSE 'NotChecked' END,
                       TRUE, v."CreatedAt", v."UpdatedAt", 0, v."Id"
                FROM "Vendors" v
                ORDER BY v."Id";

                UPDATE "Customers" c SET "RelationId" = r."Id" FROM "Relations" r WHERE r."MigratedCustomerId" = c."Id";
                UPDATE "Vendors" v SET "RelationId" = r."Id" FROM "Relations" r WHERE r."MigratedVendorId" = v."Id";

                INSERT INTO "RelationAddresses" ("RelationId", "Purpose", "Line1", "IsDefault", "IsActive", "SortOrder")
                SELECT r."Id", 'Billing', left(trim(c."BillingAddress"), 300), TRUE, TRUE, 0
                FROM "Customers" c JOIN "Relations" r ON r."MigratedCustomerId" = c."Id"
                WHERE NULLIF(trim(c."BillingAddress"), '') IS NOT NULL;

                INSERT INTO "RelationAddresses" ("RelationId", "Purpose", "Line1", "IsDefault", "IsActive", "SortOrder")
                SELECT r."Id", 'Delivery', left(trim(c."ShippingAddress"), 300), TRUE, TRUE, 1
                FROM "Customers" c JOIN "Relations" r ON r."MigratedCustomerId" = c."Id"
                WHERE NULLIF(trim(c."ShippingAddress"), '') IS NOT NULL;

                INSERT INTO "RelationAddresses" ("RelationId", "Purpose", "Line1", "IsDefault", "IsActive", "SortOrder")
                SELECT r."Id", 'Billing', left(trim(v."BillingAddress"), 300), TRUE, TRUE, 0
                FROM "Vendors" v JOIN "Relations" r ON r."MigratedVendorId" = v."Id"
                WHERE NULLIF(trim(v."BillingAddress"), '') IS NOT NULL;

                INSERT INTO "RelationContacts" ("RelationId", "Name", "IsPrimary", "IsActive", "SortOrder")
                SELECT r."Id", left(trim(c."ContactPerson"), 100), TRUE, TRUE, 0
                FROM "Customers" c JOIN "Relations" r ON r."MigratedCustomerId" = c."Id"
                WHERE NULLIF(trim(c."ContactPerson"), '') IS NOT NULL;

                INSERT INTO "RelationContactChannels" ("RelationContactId", "Kind", "Value", "SortOrder")
                SELECT rc."Id", 'Email', left(trim(c."Email"), 200), 0
                FROM "Customers" c JOIN "Relations" r ON r."MigratedCustomerId" = c."Id" JOIN "RelationContacts" rc ON rc."RelationId" = r."Id"
                WHERE NULLIF(trim(c."Email"), '') IS NOT NULL;

                INSERT INTO "RelationContactChannels" ("RelationContactId", "Kind", "Value", "SortOrder")
                SELECT rc."Id", 'Phone', left(trim(c."Phone"), 200), 1
                FROM "Customers" c JOIN "Relations" r ON r."MigratedCustomerId" = c."Id" JOIN "RelationContacts" rc ON rc."RelationId" = r."Id"
                WHERE NULLIF(trim(c."Phone"), '') IS NOT NULL;

                ALTER TABLE "Relations" DROP COLUMN "MigratedCustomerId", DROP COLUMN "MigratedVendorId";
                """);

            migrationBuilder.AlterColumn<int>(
                name: "RelationId",
                table: "Vendors",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "RelationId",
                table: "Customers",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_RelationId",
                table: "Vendors",
                column: "RelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_RelationId",
                table: "Customers",
                column: "RelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RelationAddresses_RelationId_Purpose",
                table: "RelationAddresses",
                columns: new[] { "RelationId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_RelationContactChannels_RelationContactId",
                table: "RelationContactChannels",
                column: "RelationContactId");

            migrationBuilder.CreateIndex(
                name: "IX_RelationContacts_RelationId",
                table: "RelationContacts",
                column: "RelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Relations_RequestId",
                table: "Relations",
                column: "RequestId",
                unique: true,
                filter: "\"RequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Relations_SearchCodeKey",
                table: "Relations",
                column: "SearchCodeKey",
                unique: true,
                filter: "\"SearchCodeKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VendorGgns_VendorId_Ggn",
                table: "VendorGgns",
                columns: new[] { "VendorId", "Ggn" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Relations_RelationId",
                table: "Customers",
                column: "RelationId",
                principalTable: "Relations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vendors_Relations_RelationId",
                table: "Vendors",
                column: "RelationId",
                principalTable: "Relations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Relations_RelationId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Vendors_Relations_RelationId",
                table: "Vendors");

            migrationBuilder.DropTable(
                name: "RelationAddresses");

            migrationBuilder.DropTable(
                name: "RelationContactChannels");

            migrationBuilder.DropTable(
                name: "VendorGgns");

            migrationBuilder.DropTable(
                name: "RelationContacts");

            migrationBuilder.DropTable(
                name: "Relations");

            migrationBuilder.DropIndex(
                name: "IX_Vendors_RelationId",
                table: "Vendors");

            migrationBuilder.DropIndex(
                name: "IX_Customers_RelationId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "RelationId",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "RelationId",
                table: "Customers");
        }
    }
}
