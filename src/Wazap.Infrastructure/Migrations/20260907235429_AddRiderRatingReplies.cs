using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRiderRatingReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RepliedAt",
                table: "RiderRatings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reply",
                table: "RiderRatings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RepliedAt",
                table: "RiderRatings");

            migrationBuilder.DropColumn(
                name: "Reply",
                table: "RiderRatings");
        }
    }
}
