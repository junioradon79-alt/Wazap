using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionRevocationAndPendingTwoFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "TwoFactorPendingExpiresAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorPendingSecret",
                table: "Users",
                type: "text",
                nullable: true);

            // Reprise des comptes EXISTANTS : la colonne vient d'être créée avec une valeur
            // vide par défaut. Or chaque jeton d'accès porte cette empreinte et elle est
            // comparée à CHAQUE requête : laissée vide, plus aucune authentification ne
            // fonctionnerait. Chaque compte reçoit donc une empreinte unique — ce qui a aussi
            // pour effet, souhaitable, d'invalider les sessions ouvertes avant le déploiement.
            migrationBuilder.Sql(
                "UPDATE \"Users\" "
                + "SET \"SecurityStamp\" = md5(random()::text || clock_timestamp()::text) "
                + "WHERE \"SecurityStamp\" IS NULL OR \"SecurityStamp\" = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TwoFactorPendingExpiresAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TwoFactorPendingSecret",
                table: "Users");
        }
    }
}
