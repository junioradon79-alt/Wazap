using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryProof : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preuve de livraison : code à 4 chiffres remis au client, restitué par le
            // livreur pour clôturer la course (+ compteur anti-force brute).
            migrationBuilder.Sql("""
                ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "DeliveryCode" character varying(4) NULL;
                ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "DeliveryCodeVerifiedAt" timestamp with time zone NULL;
                ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "DeliveryCodeAttempts" integer NOT NULL DEFAULT 0;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Orders" DROP COLUMN IF EXISTS "DeliveryCode";
                ALTER TABLE "Orders" DROP COLUMN IF EXISTS "DeliveryCodeVerifiedAt";
                ALTER TABLE "Orders" DROP COLUMN IF EXISTS "DeliveryCodeAttempts";
            """);
        }
    }
}
