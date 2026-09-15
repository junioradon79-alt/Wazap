using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLookupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Users_ReferralCode",
                table: "Users",
                column: "ReferralCode");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ReferredByUserId",
                table: "Users",
                column: "ReferredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RiderPriorityPurchases_TransactionReference",
                table: "RiderPriorityPurchases",
                column: "TransactionReference");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_TransactionReference",
                table: "OrderPayments",
                column: "TransactionReference");

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_TransactionReference",
                table: "CreditTransactions",
                column: "TransactionReference");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_ReferralCode",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_ReferredByUserId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_RiderPriorityPurchases_TransactionReference",
                table: "RiderPriorityPurchases");

            migrationBuilder.DropIndex(
                name: "IX_OrderPayments_TransactionReference",
                table: "OrderPayments");

            migrationBuilder.DropIndex(
                name: "IX_CreditTransactions_TransactionReference",
                table: "CreditTransactions");
        }
    }
}
