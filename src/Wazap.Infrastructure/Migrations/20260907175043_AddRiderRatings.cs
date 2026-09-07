using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRiderRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Réputation livreur : note 1-5 laissée par le client après la livraison.
            // Une seule note par commande (index unique).
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "RiderRatings" (
                    "Id" uuid NOT NULL,
                    "OrderId" uuid NOT NULL,
                    "RiderUserId" uuid NOT NULL,
                    "ClientWhatsAppNumber" character varying(30) NOT NULL,
                    "Score" integer NOT NULL,
                    "Comment" character varying(300) NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_RiderRatings" PRIMARY KEY ("Id")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_RiderRatings_OrderId"
                    ON "RiderRatings" ("OrderId");

                CREATE INDEX IF NOT EXISTS "IX_RiderRatings_RiderUserId"
                    ON "RiderRatings" ("RiderUserId");
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "RiderRatings";""");
        }
    }
}
