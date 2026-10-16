using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    // ROLLOUT-VOORWAARDE (go/no-go), vóór toepassen op een klantdatabase:
    //   SELECT COUNT(*) FROM "PalletContentLines";
    //   0  -> veilig toepassen.
    //   >0 -> stoppen, niets forceren en eerst een aparte datamigratie plannen: de tijdelijke Guid.Empty-default
    //         botst bij meerdere bestaande rijen met de unieke RequestId-index.
    /// <inheritdoc />
    public partial class AddPalletContentLineRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "PalletContentLines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_PalletContentLines_RequestId",
                table: "PalletContentLines",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PalletContentLines_RequestId",
                table: "PalletContentLines");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "PalletContentLines");
        }
    }
}
