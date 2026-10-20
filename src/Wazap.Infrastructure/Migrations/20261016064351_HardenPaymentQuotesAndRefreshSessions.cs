using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenPaymentQuotesAndRefreshSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "RefreshTokens",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeePayer",
                table: "OrderPayments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuotedGatewayFee",
                table: "OrderPayments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuotedRiderAmount",
                table: "OrderPayments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuotedVendorAmount",
                table: "OrderPayments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RiderPayoutDue",
                table: "OrderPayments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "FeePayer",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "QuotedGatewayFee",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "QuotedRiderAmount",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "QuotedVendorAmount",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "RiderPayoutDue",
                table: "OrderPayments");
        }
    }
}
