using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sécurité : verrouillage anti force-brute (colonnes sur Users).
            // DDL idempotent (base partagée — peut déjà être appliquée par l'aligneur).
            migrationBuilder.Sql("""
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "FailedLoginAttempts" integer NOT NULL DEFAULT 0;
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LockedUntilUtc" timestamp with time zone NULL;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Users" DROP COLUMN IF EXISTS "FailedLoginAttempts";
                ALTER TABLE "Users" DROP COLUMN IF EXISTS "LockedUntilUtc";
            """);
        }
    }
}
