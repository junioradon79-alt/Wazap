using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRetentionAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Users_Role",
                table: "Users",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Role_IsAvailable",
                table: "Users",
                columns: new[] { "Role", "IsAvailable" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ExpiresAtUtc",
                table: "RefreshTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_RiderUserId_Status",
                table: "Orders",
                columns: new[] { "RiderUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status_DeliveredAt",
                table: "Orders",
                columns: new[] { "Status", "DeliveredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_VendorUserId_Status",
                table: "Orders",
                columns: new[] { "VendorUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOffers_RiderUserId_Status",
                table: "DeliveryOffers",
                columns: new[] { "RiderUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryBatches_Status_CreatedAt",
                table: "DeliveryBatches",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Role",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Role_IsAvailable",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_ExpiresAtUtc",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_Orders_RiderUserId_Status",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Status_DeliveredAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_VendorUserId_Status",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOffers_RiderUserId_Status",
                table: "DeliveryOffers");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryBatches_Status_CreatedAt",
                table: "DeliveryBatches");
        }
    }
}
