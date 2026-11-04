using System;
using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    // Partij als eigen onderdeel (P1): tabellen Partijen en PartijAuditEvents, een optionele partij op inkooporder, inkoopregel en
    // palletregel, de partij van de leverancier op de order en de partijnummering in de bedrijfsinstellingen. Bestaande gegevens: zie
    // LinkExistingLotsSql. Het gegenereerde Designer-bestand is weggelaten (de AI-review kapt grote diffs af); de modelsnapshot is
    // bijgewerkt en de attributen hieronder vervangen het.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261104193108_AddPartijen")]
    public partial class AddPartijen : Migration
    {
        // Bestaande gegevens, voorzichtig en zonder te raden (dezelfde SQL draait in de tests op SQLite, zie PartijMigrationTests):
        // 1. één partij per verschillende sleutel van PalletContentLines.LotNumber (hoofdletters, zonder spaties en streepjes, zoals
        //    PartijRules.NumberKey), met als nummer de schrijfwijze van de oudste regel en "Migratie" als aanmaker; 2610251 en "2610 251"
        //    worden dus één partij;
        // 2. elke palletregel krijgt de partij van haar eigen lotnummer; LotNumber zelf blijft ongewijzigd staan;
        // 3. een inkoopregel krijgt een partij alleen als al haar ontvangen palletregels bij dezelfde partij horen (regels zonder
        //    ontvangst of met twee partijen blijven zonder partij);
        // 4. een inkooporder krijgt een partij alleen als al haar regels bij dezelfde partij horen.
        public const string LinkExistingLotsSql = @"
INSERT INTO ""Partijen"" (""Number"", ""NumberKey"", ""Status"", ""CreatedAt"", ""CreatedBy"")
SELECT trim(c.""LotNumber""), k.""LotKey"", 'Open', k.""FirstCreatedAt"", 'Migratie'
FROM (
    SELECT upper(replace(replace(trim(""LotNumber""), ' ', ''), '-', '')) AS ""LotKey"", MIN(""Id"") AS ""FirstId"", MIN(""CreatedAt"") AS ""FirstCreatedAt""
    FROM ""PalletContentLines""
    GROUP BY upper(replace(replace(trim(""LotNumber""), ' ', ''), '-', ''))
) k
JOIN ""PalletContentLines"" c ON c.""Id"" = k.""FirstId""
WHERE k.""LotKey"" <> '';

UPDATE ""PalletContentLines""
SET ""PartijId"" = (SELECT p.""Id"" FROM ""Partijen"" p WHERE p.""NumberKey"" = upper(replace(replace(trim(""PalletContentLines"".""LotNumber""), ' ', ''), '-', '')));

UPDATE ""SupplyCommitmentLines""
SET ""PartijId"" = (SELECT MIN(c.""PartijId"") FROM ""PalletContentLines"" c WHERE c.""SupplyCommitmentLineId"" = ""SupplyCommitmentLines"".""Id"")
WHERE (SELECT COUNT(DISTINCT c.""PartijId"") FROM ""PalletContentLines"" c WHERE c.""SupplyCommitmentLineId"" = ""SupplyCommitmentLines"".""Id"") = 1
  AND NOT EXISTS (SELECT 1 FROM ""PalletContentLines"" c WHERE c.""SupplyCommitmentLineId"" = ""SupplyCommitmentLines"".""Id"" AND c.""PartijId"" IS NULL);

UPDATE ""SupplyCommitments""
SET ""PartijId"" = (SELECT MIN(l.""PartijId"") FROM ""SupplyCommitmentLines"" l WHERE l.""SupplyCommitmentId"" = ""SupplyCommitments"".""Id"")
WHERE (SELECT COUNT(DISTINCT l.""PartijId"") FROM ""SupplyCommitmentLines"" l WHERE l.""SupplyCommitmentId"" = ""SupplyCommitments"".""Id"") = 1
  AND NOT EXISTS (SELECT 1 FROM ""SupplyCommitmentLines"" l WHERE l.""SupplyCommitmentId"" = ""SupplyCommitments"".""Id"" AND l.""PartijId"" IS NULL);
";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PartijId",
                table: "SupplyCommitments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierLotReference",
                table: "SupplyCommitments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartijId",
                table: "SupplyCommitmentLines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartijId",
                table: "PalletContentLines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartijNumberFormat",
                table: "CompanySettings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "{JJ}{MM}{volgnr}");

            migrationBuilder.AddColumn<string>(
                name: "PartijNumberMode",
                table: "CompanySettings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Auto");

            migrationBuilder.AddColumn<string>(
                name: "PartijSequenceReset",
                table: "CompanySettings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Yearly");

            migrationBuilder.CreateTable(
                name: "PartijAuditEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PartijId = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OldValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SupplyCommitmentId = table.Column<int>(type: "integer", nullable: true),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartijAuditEvents", x => x.Id);
                    table.CheckConstraint("CK_PartijAuditEvents_EventType_Allowed", "\"EventType\" IN ('NumberCorrected', 'OrderLinked', 'Deleted')");
                });

            migrationBuilder.CreateTable(
                name: "Partijen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NumberKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SequencePeriod = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Partijen", x => x.Id);
                    table.CheckConstraint("CK_Partijen_Number_NotBlank", "length(trim(\"Number\")) > 0");
                    table.CheckConstraint("CK_Partijen_NumberKey_NotBlank", "length(\"NumberKey\") > 0");
                    table.CheckConstraint("CK_Partijen_Sequence_Consistent", "(\"Sequence\" IS NULL AND \"SequencePeriod\" IS NULL) OR (\"Sequence\" > 0 AND \"SequencePeriod\" IS NOT NULL)");
                    table.CheckConstraint("CK_Partijen_Status_Allowed", "\"Status\" IN ('Open')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitments_PartijId",
                table: "SupplyCommitments",
                column: "PartijId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplyCommitmentLines_PartijId",
                table: "SupplyCommitmentLines",
                column: "PartijId");

            migrationBuilder.CreateIndex(
                name: "IX_PalletContentLines_PartijId",
                table: "PalletContentLines",
                column: "PartijId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanySettings_PartijNumberMode_Allowed",
                table: "CompanySettings",
                sql: "\"PartijNumberMode\" IN ('Auto', 'Own')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompanySettings_PartijSequenceReset_Allowed",
                table: "CompanySettings",
                sql: "\"PartijSequenceReset\" IN ('Never', 'Yearly', 'Monthly')");

            migrationBuilder.CreateIndex(
                name: "IX_PartijAuditEvents_PartijId",
                table: "PartijAuditEvents",
                column: "PartijId");

            migrationBuilder.CreateIndex(
                name: "IX_Partijen_NumberKey",
                table: "Partijen",
                column: "NumberKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partijen_RequestId",
                table: "Partijen",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partijen_SequencePeriod_Sequence",
                table: "Partijen",
                columns: new[] { "SequencePeriod", "Sequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PalletContentLines_Partijen_PartijId",
                table: "PalletContentLines",
                column: "PartijId",
                principalTable: "Partijen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplyCommitmentLines_Partijen_PartijId",
                table: "SupplyCommitmentLines",
                column: "PartijId",
                principalTable: "Partijen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplyCommitments_Partijen_PartijId",
                table: "SupplyCommitments",
                column: "PartijId",
                principalTable: "Partijen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(LinkExistingLotsSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PalletContentLines_Partijen_PartijId",
                table: "PalletContentLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplyCommitmentLines_Partijen_PartijId",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplyCommitments_Partijen_PartijId",
                table: "SupplyCommitments");

            migrationBuilder.DropTable(
                name: "PartijAuditEvents");

            migrationBuilder.DropTable(
                name: "Partijen");

            migrationBuilder.DropIndex(
                name: "IX_SupplyCommitments_PartijId",
                table: "SupplyCommitments");

            migrationBuilder.DropIndex(
                name: "IX_SupplyCommitmentLines_PartijId",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropIndex(
                name: "IX_PalletContentLines_PartijId",
                table: "PalletContentLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanySettings_PartijNumberMode_Allowed",
                table: "CompanySettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CompanySettings_PartijSequenceReset_Allowed",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "PartijId",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "SupplierLotReference",
                table: "SupplyCommitments");

            migrationBuilder.DropColumn(
                name: "PartijId",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "PartijId",
                table: "PalletContentLines");

            migrationBuilder.DropColumn(
                name: "PartijNumberFormat",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "PartijNumberMode",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "PartijSequenceReset",
                table: "CompanySettings");
        }
    }
}
