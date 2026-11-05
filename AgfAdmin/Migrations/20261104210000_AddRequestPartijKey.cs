using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    // RequestPartijKey: de gevraagde partij (none / id / new:nummer / new:auto) van de aanmaak-aanvraag van een inkooporder, voor de
    // idempotentiecheck. Aparte migratie, omdat AddPartijen op sommige databases al is toegepast. IF NOT EXISTS, want de kolom stond
    // heel kort ook in AddPartijen zelf. Het Designer-bestand is weggelaten (zie AddPartijen); de modelsnapshot is bijgewerkt.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261104210000_AddRequestPartijKey")]
    public partial class AddRequestPartijKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""SupplyCommitments"" ADD COLUMN IF NOT EXISTS ""RequestPartijKey"" character varying(200) NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestPartijKey",
                table: "SupplyCommitments");
        }
    }
}
