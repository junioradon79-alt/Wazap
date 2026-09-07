using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddColisSurPayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Garantie Colis Sûr étape 3 : indemnisation en FCFA, caution du livreur et
            // suivi du versement sortant.
            migrationBuilder.Sql("""
                ALTER TABLE "DeliveryClaims" ADD COLUMN IF NOT EXISTS "CompensationAmountFcfa" numeric(18,2) NULL;
                ALTER TABLE "DeliveryClaims" ADD COLUMN IF NOT EXISTS "RiderDepositDebitedFcfa" numeric(18,2) NULL;
                ALTER TABLE "DeliveryClaims" ADD COLUMN IF NOT EXISTS "PayoutStatus" integer NOT NULL DEFAULT 0;
                ALTER TABLE "DeliveryClaims" ADD COLUMN IF NOT EXISTS "PayoutReference" character varying(120) NULL;
                ALTER TABLE "DeliveryClaims" ADD COLUMN IF NOT EXISTS "PayoutError" character varying(300) NULL;
                ALTER TABLE "DeliveryClaims" ADD COLUMN IF NOT EXISTS "PaidAt" timestamp with time zone NULL;

                ALTER TABLE "RiderIdentities" ADD COLUMN IF NOT EXISTS "DepositFcfa" numeric(18,2) NOT NULL DEFAULT 0;

                -- Liste des versements dus dans /app/claims.
                CREATE INDEX IF NOT EXISTS "IX_DeliveryClaims_PayoutStatus"
                    ON "DeliveryClaims" ("PayoutStatus");
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_DeliveryClaims_PayoutStatus";
                ALTER TABLE "RiderIdentities" DROP COLUMN IF EXISTS "DepositFcfa";
                ALTER TABLE "DeliveryClaims" DROP COLUMN IF EXISTS "PaidAt";
                ALTER TABLE "DeliveryClaims" DROP COLUMN IF EXISTS "PayoutError";
                ALTER TABLE "DeliveryClaims" DROP COLUMN IF EXISTS "PayoutReference";
                ALTER TABLE "DeliveryClaims" DROP COLUMN IF EXISTS "PayoutStatus";
                ALTER TABLE "DeliveryClaims" DROP COLUMN IF EXISTS "RiderDepositDebitedFcfa";
                ALTER TABLE "DeliveryClaims" DROP COLUMN IF EXISTS "CompensationAmountFcfa";
            """);
        }
    }
}
