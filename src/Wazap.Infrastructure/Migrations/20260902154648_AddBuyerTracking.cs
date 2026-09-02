using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBuyerTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Parcours acheteur : coordonnées de livraison du client + mode « suivi ».
            migrationBuilder.Sql("""
                ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "RequiresClientCoordinates" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "ClientLatitude" double precision NULL;
                ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "ClientLongitude" double precision NULL;
                ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "ClientAddress" text NULL;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Orders" DROP COLUMN IF EXISTS "RequiresClientCoordinates";
                ALTER TABLE "Orders" DROP COLUMN IF EXISTS "ClientLatitude";
                ALTER TABLE "Orders" DROP COLUMN IF EXISTS "ClientLongitude";
                ALTER TABLE "Orders" DROP COLUMN IF EXISTS "ClientAddress";
            """);
        }
    }
}
