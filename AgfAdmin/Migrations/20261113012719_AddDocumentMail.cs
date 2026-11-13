using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentMail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentRecipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RelationId = table.Column<int>(type: "integer", nullable: true),
                    RelationAddressId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Kinds = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRecipients", x => x.Id);
                    table.CheckConstraint("CK_DocumentRecipients_Kinds", "\"Kinds\" > 0");
                    table.CheckConstraint("CK_DocumentRecipients_Owner", "(\"OwnerKind\" = 'Relation' AND \"RelationId\" IS NOT NULL) OR (\"OwnerKind\" = 'Warehouse' AND \"RelationId\" IS NULL AND \"RelationAddressId\" IS NULL)");
                    table.CheckConstraint("CK_DocumentRecipients_Role", "\"Role\" IN ('To', 'Cc')");
                    table.ForeignKey(
                        name: "FK_DocumentRecipients_RelationAddresses_RelationAddressId",
                        column: x => x.RelationAddressId,
                        principalTable: "RelationAddresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentRecipients_Relations_RelationId",
                        column: x => x.RelationId,
                        principalTable: "Relations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MailDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SalesOrderId = table.Column<int>(type: "integer", nullable: false),
                    DocumentId = table.Column<int>(type: "integer", nullable: false),
                    DocumentKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    To = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Cc = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    MissingRecipient = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MissingRelationId = table.Column<int>(type: "integer", nullable: true),
                    PreviousSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MessageId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAttemptBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailDrafts", x => x.Id);
                    table.CheckConstraint("CK_MailDrafts_Sent", "(\"Status\" = 'Sent') = (\"SentAt\" IS NOT NULL AND \"SentBy\" IS NOT NULL)");
                    table.CheckConstraint("CK_MailDrafts_Status", "\"Status\" IN ('Draft', 'Sending', 'Sent', 'Failed', 'Discarded', 'Superseded')");
                    table.ForeignKey(
                        name: "FK_MailDrafts_SalesOrderDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "SalesOrderDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MailDrafts_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MailSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Security = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    UserName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ProtectedPassword = table.Column<string>(type: "text", nullable: true),
                    FromAddress = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    FromName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Signature = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailSettings", x => x.Id);
                    table.CheckConstraint("CK_MailSettings_Port", "\"Port\" > 0 AND \"Port\" <= 65535");
                });

            migrationBuilder.CreateTable(
                name: "MailTemplates",
                columns: table => new
                {
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailTemplates", x => x.Kind);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRecipients_OwnerKind_RelationId_RelationAddressId",
                table: "DocumentRecipients",
                columns: new[] { "OwnerKind", "RelationId", "RelationAddressId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRecipients_RelationAddressId",
                table: "DocumentRecipients",
                column: "RelationAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRecipients_RelationId",
                table: "DocumentRecipients",
                column: "RelationId");

            migrationBuilder.CreateIndex(
                name: "IX_MailDrafts_DocumentId",
                table: "MailDrafts",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MailDrafts_SalesOrderId",
                table: "MailDrafts",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MailDrafts_Status_SalesOrderId",
                table: "MailDrafts",
                columns: new[] { "Status", "SalesOrderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentRecipients");

            migrationBuilder.DropTable(
                name: "MailDrafts");

            migrationBuilder.DropTable(
                name: "MailSettings");

            migrationBuilder.DropTable(
                name: "MailTemplates");
        }
    }
}
