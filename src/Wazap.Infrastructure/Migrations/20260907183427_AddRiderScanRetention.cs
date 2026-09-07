using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRiderScanRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // RGPD : date d'effacement du scan de pièce d'identité (la décision de
            // certification, elle, reste conservée).
            migrationBuilder.Sql("""
                ALTER TABLE "RiderIdentities" ADD COLUMN IF NOT EXISTS "ScanPurgedAt" timestamp with time zone NULL;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "RiderIdentities" DROP COLUMN IF EXISTS "ScanPurgedAt";
            """);
        }
    }
}
