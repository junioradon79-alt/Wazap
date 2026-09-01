using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGeolocationAndDeliveryOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cohabitation de schéma : les colonnes géoloc et la table DeliveryOffers
            // existent déjà en base (ancien DDL idempotent du projet racine Wazap / .NET 8).
            // DDL idempotent obligatoire.

            migrationBuilder.Sql("""
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Latitude" double precision NULL;
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Longitude" double precision NULL;
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LocationUpdatedAt" timestamp with time zone NULL;
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsAvailable" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LocationSharingEnabled" boolean NOT NULL DEFAULT TRUE;
            """);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "DeliveryOffers" (
                    "Id" uuid NOT NULL,
                    "OrderId" uuid NOT NULL,
                    "RiderUserId" uuid NOT NULL,
                    "BatchNumber" integer NOT NULL,
                    "Status" integer NOT NULL,
                    "SentAt" timestamp with time zone NOT NULL,
                    "RespondedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_DeliveryOffers" PRIMARY KEY ("Id")
                );
            """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Users_IsAvailable" ON "Users" ("IsAvailable");
                CREATE INDEX IF NOT EXISTS "IX_DeliveryOffers_OrderId" ON "DeliveryOffers" ("OrderId");
                CREATE INDEX IF NOT EXISTS "IX_DeliveryOffers_Status" ON "DeliveryOffers" ("Status");
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_DeliveryOffers_Status";
                DROP INDEX IF EXISTS "IX_DeliveryOffers_OrderId";
                DROP INDEX IF EXISTS "IX_Users_IsAvailable";
                DROP TABLE IF EXISTS "DeliveryOffers";
                ALTER TABLE "Users" DROP COLUMN IF EXISTS "Longitude";
                ALTER TABLE "Users" DROP COLUMN IF EXISTS "LocationUpdatedAt";
                ALTER TABLE "Users" DROP COLUMN IF EXISTS "LocationSharingEnabled";
                ALTER TABLE "Users" DROP COLUMN IF EXISTS "IsAvailable";
                ALTER TABLE "Users" DROP COLUMN IF EXISTS "Latitude";
            """);
        }
    }
}
