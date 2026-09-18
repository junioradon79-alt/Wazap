using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppMessageLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppMessageLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipientPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SenderPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MessageType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TemplateName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ErrorCode = table.Column<int>(type: "integer", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EstimatedCostFcfa = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppMessageLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessageLogs_Category",
                table: "WhatsAppMessageLogs",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessageLogs_CreatedAt",
                table: "WhatsAppMessageLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessageLogs_OrderId",
                table: "WhatsAppMessageLogs",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessageLogs_ProviderMessageId",
                table: "WhatsAppMessageLogs",
                column: "ProviderMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessageLogs_RecipientPhone",
                table: "WhatsAppMessageLogs",
                column: "RecipientPhone");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessageLogs_Status",
                table: "WhatsAppMessageLogs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppMessageLogs");
        }
    }
}
