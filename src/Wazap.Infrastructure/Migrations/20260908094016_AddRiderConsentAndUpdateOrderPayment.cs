using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRiderConsentAndUpdateOrderPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PaidAt",
                table: "OrderPayments",
                newName: "CompletedAt");

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentGivenAt",
                table: "RiderIdentities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsentMethod",
                table: "RiderIdentities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "OrderPayments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsentGivenAt",
                table: "RiderIdentities");

            migrationBuilder.DropColumn(
                name: "ConsentMethod",
                table: "RiderIdentities");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "OrderPayments");

            migrationBuilder.RenameColumn(
                name: "CompletedAt",
                table: "OrderPayments",
                newName: "PaidAt");
        }
    }
}
