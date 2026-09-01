using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorAndCreditTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cohabitation de schéma : les objets peuvent déjà exister
            // (DDL idempotent du projet racine Wazap / .NET 8 sur la base partagée).
            // On utilise donc du DDL idempotent plutôt que les helpers EF natifs.

            migrationBuilder.Sql("""
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Credits" integer NOT NULL DEFAULT 0;
            """);

            // Renommage Date -> CreatedAt si la table a été créée avec l'ancien nom
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = current_schema() AND table_name = 'CreditTransactions' AND column_name = 'Date'
                    ) THEN
                        ALTER TABLE "CreditTransactions" RENAME COLUMN "Date" TO "CreatedAt";
                    END IF;
                END $$;
            """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "CreditTransactions" (
                    "Id" uuid NOT NULL,
                    "VendorId" uuid NOT NULL,
                    "Amount" numeric(18,2) NOT NULL,
                    "CreditsPurchased" integer NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "TransactionReference" character varying(100) NOT NULL,
                    "Status" integer NOT NULL,
                    CONSTRAINT "PK_CreditTransactions" PRIMARY KEY ("Id")
                );
            """);

            // La table pouvait exister avec "TransactionReference" en text : aligner sur varchar(100)
            migrationBuilder.Sql("""
                ALTER TABLE "CreditTransactions" ALTER COLUMN "TransactionReference" TYPE character varying(100);
            """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_CreditTransactions_VendorId" ON "CreditTransactions" ("VendorId");
                CREATE INDEX IF NOT EXISTS "IX_CreditTransactions_CreatedAt" ON "CreditTransactions" ("CreatedAt");
            """);

            migrationBuilder.Sql("""
                ALTER TABLE "CreditTransactions" DROP CONSTRAINT IF EXISTS "FK_CreditTransactions_Users_VendorId";
                ALTER TABLE "CreditTransactions" ADD CONSTRAINT "FK_CreditTransactions_Users_VendorId" FOREIGN KEY ("VendorId") REFERENCES "Users" ("Id") ON DELETE RESTRICT;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "CreditTransactions" DROP CONSTRAINT IF EXISTS "FK_CreditTransactions_Users_VendorId";
                DROP TABLE IF EXISTS "CreditTransactions";
                ALTER TABLE "Users" DROP COLUMN IF EXISTS "Credits";
            """);
        }
    }
}
