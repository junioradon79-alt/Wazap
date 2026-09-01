using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPackNameToCreditTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "CreditTransactions" ADD COLUMN IF NOT EXISTS "PackName" character varying(100) NULL;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "CreditTransactions" DROP COLUMN IF EXISTS "PackName";
            """);
        }
    }
}
