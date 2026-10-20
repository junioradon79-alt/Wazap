using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentPayoutRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentPayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeneficiaryRole = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    BeneficiaryUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    BeneficiaryPhone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Evidence = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentPayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentPayouts_OrderPayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "OrderPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPayouts_PaymentId_BeneficiaryRole",
                table: "PaymentPayouts",
                columns: new[] { "PaymentId", "BeneficiaryRole" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPayouts_Provider_ProviderReference",
                table: "PaymentPayouts",
                columns: new[] { "Provider", "ProviderReference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentPayouts");
        }
    }
}
