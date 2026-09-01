using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferralCode",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReferredByUserId",
                table: "Users",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferralCode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ReferredByUserId",
                table: "Users");
        }
    }
}
