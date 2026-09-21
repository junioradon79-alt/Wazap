using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductAvailabilityAndImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "VendorProducts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAvailable",
                table: "VendorProducts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorProducts_VendorId_IsAvailable",
                table: "VendorProducts",
                columns: new[] { "VendorId", "IsAvailable" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VendorProducts_VendorId_IsAvailable",
                table: "VendorProducts");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "VendorProducts");

            migrationBuilder.DropColumn(
                name: "IsAvailable",
                table: "VendorProducts");
        }
    }
}
