using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddProductGgn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Ggn",
                table: "SupplyCommitmentLines",
                type: "character varying(13)",
                maxLength: 13,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LoadingSlipShowGgn",
                table: "CompanySettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "TransportOrderShowGgn",
                table: "CompanySettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitmentLines_Ggn_Length",
                table: "SupplyCommitmentLines",
                sql: "\"Ggn\" IS NULL OR length(\"Ggn\") = 13");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitmentLines_Ggn_Length",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "Ggn",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "LoadingSlipShowGgn",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "TransportOrderShowGgn",
                table: "CompanySettings");
        }
    }
}
