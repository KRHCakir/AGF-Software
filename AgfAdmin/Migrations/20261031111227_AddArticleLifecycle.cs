using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "ProductVariants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedBy",
                table: "ProductVariants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "ProductVariants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "ProductVariants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpectedShownAt",
                table: "PalletContentLines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExpectedShownBeforeEntry",
                table: "PalletContentLines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedShownBy",
                table: "PalletContentLines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArticleAuditEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductVariantId = table.Column<int>(type: "integer", nullable: true),
                    ProductId = table.Column<int>(type: "integer", nullable: true),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleAuditEvents", x => x.Id);
                    table.CheckConstraint("CK_ArticleAuditEvents_HasTarget", "\"ProductVariantId\" IS NOT NULL OR \"ProductId\" IS NOT NULL");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_RequestId",
                table: "ProductVariants",
                column: "RequestId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductVariants_Archived_HasMoment",
                table: "ProductVariants",
                sql: "NOT \"IsArchived\" OR \"ArchivedAt\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PalletContentLines_ExpectedShown_Consistent",
                table: "PalletContentLines",
                sql: "(\"ExpectedShownBeforeEntry\" AND \"ExpectedShownAt\" IS NOT NULL) OR (NOT \"ExpectedShownBeforeEntry\" AND \"ExpectedShownAt\" IS NULL AND \"ExpectedShownBy\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleAuditEvents_ProductId",
                table: "ArticleAuditEvents",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleAuditEvents_ProductVariantId",
                table: "ArticleAuditEvents",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleAuditEvents_RequestId",
                table: "ArticleAuditEvents",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArticleAuditEvents");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_RequestId",
                table: "ProductVariants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductVariants_Archived_HasMoment",
                table: "ProductVariants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PalletContentLines_ExpectedShown_Consistent",
                table: "PalletContentLines");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "ArchivedBy",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "ExpectedShownAt",
                table: "PalletContentLines");

            migrationBuilder.DropColumn(
                name: "ExpectedShownBeforeEntry",
                table: "PalletContentLines");

            migrationBuilder.DropColumn(
                name: "ExpectedShownBy",
                table: "PalletContentLines");
        }
    }
}
