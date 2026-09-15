using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPhoneSuffix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneSuffix",
                table: "Users",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_PhoneSuffix",
                table: "Users",
                column: "PhoneSuffix");

            // Reprise des comptes EXISTANTS : la colonne est alimentée par le domaine à chaque
            // écriture du numéro, mais les lignes déjà en base n'ont pas de valeur — sans ce
            // remplissage, AUCUN compte existant ne serait retrouvé par son numéro (message
            // WhatsApp entrant, création de commande, diffusion, réinitialisation de mot de
            // passe). Même règle que le domaine : chiffres uniquement, 8 derniers, vide → NULL.
            migrationBuilder.Sql(
                "UPDATE \"Users\" "
                + "SET \"PhoneSuffix\" = NULLIF(right(regexp_replace(\"PhoneNumber\", '[^0-9]', '', 'g'), 8), '') "
                + "WHERE \"PhoneNumber\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_PhoneSuffix",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneSuffix",
                table: "Users");
        }
    }
}
