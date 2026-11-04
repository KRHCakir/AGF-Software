using AgfAdmin.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    // Keuring: naast de ingelogde gebruiker (ActorReference) ook wie het product fysiek keurde. Alleen een nieuwe, lege kolom; bestaande
    // keuringen blijven ongewijzigd (daar staat de beoordelaar in ActorReference). De modelsnapshot is bijgewerkt; het gegenereerde
    // Designer-bestand is weggelaten zodat de AI-review de diff niet afkapt. De attributen hieronder vervangen het.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261104143811_AddInspectedBy")]
    public partial class AddInspectedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InspectedBy",
                table: "PalletAuditEvents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InspectedBy",
                table: "PalletAuditEvents");
        }
    }
}
