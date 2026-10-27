using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgfAdmin.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderLineWeights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedGrossWeightKg",
                table: "SupplyCommitmentLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeightSource",
                table: "SupplyCommitmentLines",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossWeightKg",
                table: "SalesOrderLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetWeightKg",
                table: "SalesOrderLines",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeightSource",
                table: "SalesOrderLines",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            // Bestaande aanvoerregels met netto kg: dat was werkelijk opgegeven (de enige manier tot nu), dus bron Entered.
            // Er wordt geen kg teruggerekend; regels zonder kg en alle bestaande orderregels houden NULL.
            migrationBuilder.Sql("UPDATE \"SupplyCommitmentLines\" SET \"WeightSource\" = 'Entered' WHERE \"ExpectedNetWeightKg\" IS NOT NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitmentLines_Weights_GrossNotBelowNet",
                table: "SupplyCommitmentLines",
                sql: "\"ExpectedNetWeightKg\" IS NULL OR \"ExpectedGrossWeightKg\" IS NULL OR CAST(\"ExpectedGrossWeightKg\" AS NUMERIC) >= CAST(\"ExpectedNetWeightKg\" AS NUMERIC)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitmentLines_Weights_Positive",
                table: "SupplyCommitmentLines",
                sql: "(\"ExpectedNetWeightKg\" IS NULL OR CAST(\"ExpectedNetWeightKg\" AS NUMERIC) > 0) AND (\"ExpectedGrossWeightKg\" IS NULL OR CAST(\"ExpectedGrossWeightKg\" AS NUMERIC) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupplyCommitmentLines_WeightSource_Consistent",
                table: "SupplyCommitmentLines",
                sql: "(\"WeightSource\" IS NULL AND \"ExpectedNetWeightKg\" IS NULL AND \"ExpectedGrossWeightKg\" IS NULL) OR (\"WeightSource\" IS NOT NULL AND \"WeightSource\" IN ('Calculated', 'Entered') AND (\"ExpectedNetWeightKg\" IS NOT NULL OR \"ExpectedGrossWeightKg\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrderLines_Weights_GrossNotBelowNet",
                table: "SalesOrderLines",
                sql: "\"NetWeightKg\" IS NULL OR \"GrossWeightKg\" IS NULL OR CAST(\"GrossWeightKg\" AS NUMERIC) >= CAST(\"NetWeightKg\" AS NUMERIC)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrderLines_Weights_Positive",
                table: "SalesOrderLines",
                sql: "(\"NetWeightKg\" IS NULL OR CAST(\"NetWeightKg\" AS NUMERIC) > 0) AND (\"GrossWeightKg\" IS NULL OR CAST(\"GrossWeightKg\" AS NUMERIC) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesOrderLines_WeightSource_Consistent",
                table: "SalesOrderLines",
                sql: "(\"WeightSource\" IS NULL AND \"NetWeightKg\" IS NULL AND \"GrossWeightKg\" IS NULL) OR (\"WeightSource\" IS NOT NULL AND \"WeightSource\" IN ('Calculated', 'Entered') AND (\"NetWeightKg\" IS NOT NULL OR \"GrossWeightKg\" IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitmentLines_Weights_GrossNotBelowNet",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitmentLines_Weights_Positive",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SupplyCommitmentLines_WeightSource_Consistent",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrderLines_Weights_GrossNotBelowNet",
                table: "SalesOrderLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrderLines_Weights_Positive",
                table: "SalesOrderLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesOrderLines_WeightSource_Consistent",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "ExpectedGrossWeightKg",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "WeightSource",
                table: "SupplyCommitmentLines");

            migrationBuilder.DropColumn(
                name: "GrossWeightKg",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "NetWeightKg",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "WeightSource",
                table: "SalesOrderLines");
        }
    }
}
