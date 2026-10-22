using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddGgn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultGgn",
                table: "ProductVariants",
                type: "character varying(13)",
                maxLength: 13,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ggn",
                table: "PalletContentLines",
                type: "character varying(13)",
                maxLength: 13,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductVariants_DefaultGgn_Length",
                table: "ProductVariants",
                sql: "\"DefaultGgn\" IS NULL OR length(\"DefaultGgn\") = 13");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PalletContentLines_Ggn_Length",
                table: "PalletContentLines",
                sql: "\"Ggn\" IS NULL OR length(\"Ggn\") = 13");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductVariants_DefaultGgn_Length",
                table: "ProductVariants");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PalletContentLines_Ggn_Length",
                table: "PalletContentLines");

            migrationBuilder.DropColumn(
                name: "DefaultGgn",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "Ggn",
                table: "PalletContentLines");
        }
    }
}
