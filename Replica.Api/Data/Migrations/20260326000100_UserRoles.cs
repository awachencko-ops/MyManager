using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Replica.Api.Data;

#nullable disable

namespace Replica.Api.Data.Migrations;

[DbContext(typeof(ReplicaDbContext))]
[Migration("20260326000100_UserRoles")]
public partial class UserRoles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            alter table if exists users add column if not exists role text not null default 'Operator';
            update users
            set role = 'Operator'
            where role is null or btrim(role) = '';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            alter table if exists users drop column if exists role;
            """);
    }
}
