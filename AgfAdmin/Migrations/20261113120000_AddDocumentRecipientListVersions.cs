using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    // Blijvende versieteller per ontvangerslijst, zodat een lijst die leeggemaakt wordt niet terugvalt op een eerder gezien versienummer.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261113120000_AddDocumentRecipientListVersions")]
    public partial class AddDocumentRecipientListVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentRecipientListVersions",
                columns: table => new
                {
                    OwnerKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OwnerId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRecipientListVersions", x => new { x.OwnerKind, x.OwnerId });
                    table.CheckConstraint("CK_DocumentRecipientListVersions_Version", "\"Version\" >= 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentRecipientListVersions");
        }
    }
}
