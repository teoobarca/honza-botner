using System;
using HonzaBotner.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HonzaBotner.Migrations;

[DbContext(typeof(HonzaBotnerDbContext))]
[Migration("20260715230000_AddVerificationTimestamps")]
public sealed class AddVerificationTimestamps : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "LastVerifiedAt",
            table: "Verifications",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "CURRENT_TIMESTAMP");

        migrationBuilder.AddColumn<DateTime>(
            name: "StaffVerifiedAt",
            table: "Verifications",
            type: "timestamp with time zone",
            nullable: true);

        // Existing deployments get one grace period before staff roles expire.
        migrationBuilder.Sql("UPDATE \"Verifications\" SET \"StaffVerifiedAt\" = CURRENT_TIMESTAMP;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "LastVerifiedAt", table: "Verifications");
        migrationBuilder.DropColumn(name: "StaffVerifiedAt", table: "Verifications");
    }
}
