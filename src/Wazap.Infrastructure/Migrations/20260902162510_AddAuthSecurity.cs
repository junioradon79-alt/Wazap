using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wazap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Authentification renforcée : refresh tokens (rotation) + 2FA TOTP + reset code.
            migrationBuilder.Sql("""
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "TwoFactorEnabled" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "TwoFactorSecret" text NULL;
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "ResetCodeHash" text NULL;
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "ResetCodeExpiresAtUtc" timestamp with time zone NULL;

                CREATE TABLE IF NOT EXISTS "RefreshTokens" (
                    "Id" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "TokenHash" varchar(64) NOT NULL,
                    "CreatedAtUtc" timestamp with time zone NOT NULL,
                    "ExpiresAtUtc" timestamp with time zone NOT NULL,
                    "RevokedAtUtc" timestamp with time zone NULL,
                    "ReplacedByTokenHash" text NULL,
                    CONSTRAINT "PK_RefreshTokens" PRIMARY KEY ("Id")
                );

                CREATE INDEX IF NOT EXISTS "IX_RefreshTokens_UserId" ON "RefreshTokens" ("UserId");
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS "RefreshTokens";
                ALTER TABLE "Users" DROP COLUMN IF EXISTS "TwoFactorEnabled";
                ALTER TABLE "Users" DROP COLUMN IF EXISTS "TwoFactorSecret";
                ALTER TABLE "Users" DROP COLUMN IF EXISTS "ResetCodeHash";
                ALTER TABLE "Users" DROP COLUMN IF EXISTS "ResetCodeExpiresAtUtc";
            """);
        }
    }
}
