using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRiderIdentityScan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Renommage préservant les données éventuelles.
            migrationBuilder.RenameColumn(
                name: "CniNumber",
                table: "RiderIdentities",
                newName: "IdNumber");

            migrationBuilder.RenameColumn(
                name: "MotorcyclePlate",
                table: "RiderIdentities",
                newName: "Motorcycle");

            migrationBuilder.AddColumn<string>(
                name: "IdScanUrl",
                table: "RiderIdentities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScanFileName",
                table: "RiderIdentities",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScanReceivedAt",
                table: "RiderIdentities",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdScanUrl",
                table: "RiderIdentities");

            migrationBuilder.DropColumn(
                name: "ScanFileName",
                table: "RiderIdentities");

            migrationBuilder.DropColumn(
                name: "ScanReceivedAt",
                table: "RiderIdentities");

            migrationBuilder.RenameColumn(
                name: "IdNumber",
                table: "RiderIdentities",
                newName: "CniNumber");

            migrationBuilder.RenameColumn(
                name: "Motorcycle",
                table: "RiderIdentities",
                newName: "MotorcyclePlate");
        }
    }
}
