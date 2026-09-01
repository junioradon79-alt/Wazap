using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Livraisons groupées : table DeliveryBatches + rattachement des commandes
            // et des offres à un lot. DDL idempotent (base partagée).
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "DeliveryBatches" (
                    "Id" uuid NOT NULL,
                    "VendorUserId" uuid NOT NULL,
                    "Status" integer NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "AssignedAt" timestamp with time zone NULL,
                    "RiderUserId" uuid NULL,
                    "RiderWhatsAppNumber" text NULL,
                    CONSTRAINT "PK_DeliveryBatches" PRIMARY KEY ("Id")
                );
            """);

            migrationBuilder.Sql("""
                ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "BatchId" uuid NULL;
                ALTER TABLE "DeliveryOffers" ADD COLUMN IF NOT EXISTS "BatchId" uuid NULL;
                ALTER TABLE "DeliveryOffers" ALTER COLUMN "OrderId" DROP NOT NULL;
            """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Orders_BatchId" ON "Orders" ("BatchId");
                CREATE INDEX IF NOT EXISTS "IX_DeliveryOffers_BatchId" ON "DeliveryOffers" ("BatchId");
                CREATE INDEX IF NOT EXISTS "IX_DeliveryBatches_Status" ON "DeliveryBatches" ("Status");
                CREATE INDEX IF NOT EXISTS "IX_DeliveryBatches_VendorUserId" ON "DeliveryBatches" ("VendorUserId");
            """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_Orders_DeliveryBatches_BatchId') THEN
                        ALTER TABLE "Orders" ADD CONSTRAINT "FK_Orders_DeliveryBatches_BatchId"
                            FOREIGN KEY ("BatchId") REFERENCES "DeliveryBatches" ("Id") ON DELETE RESTRICT;
                    END IF;
                END $$;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Orders" DROP CONSTRAINT IF EXISTS "FK_Orders_DeliveryBatches_BatchId";
                DROP INDEX IF EXISTS "IX_DeliveryBatches_VendorUserId";
                DROP INDEX IF EXISTS "IX_DeliveryBatches_Status";
                DROP INDEX IF EXISTS "IX_DeliveryOffers_BatchId";
                DROP INDEX IF EXISTS "IX_Orders_BatchId";
                ALTER TABLE "Orders" DROP COLUMN IF EXISTS "BatchId";
                ALTER TABLE "DeliveryOffers" DROP COLUMN IF EXISTS "BatchId";
                ALTER TABLE "DeliveryOffers" ALTER COLUMN "OrderId" SET NOT NULL;
                DROP TABLE IF EXISTS "DeliveryBatches";
            """);
        }
    }
}
