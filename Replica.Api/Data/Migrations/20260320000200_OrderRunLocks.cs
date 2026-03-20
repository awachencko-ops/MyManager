using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Replica.Api.Data;

#nullable disable

namespace Replica.Api.Data.Migrations;

[DbContext(typeof(ReplicaDbContext))]
[Migration("20260320000200_OrderRunLocks")]
public partial class OrderRunLocks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            create table if not exists order_run_locks
            (
                order_internal_id text primary key references orders(internal_id) on delete cascade,
                is_active boolean not null default false,
                lease_token text not null default '',
                lease_owner text not null default '',
                started_at timestamp without time zone not null default now(),
                updated_at timestamp without time zone not null default now()
            );

            create index if not exists ix_order_run_locks_is_active on order_run_locks(is_active);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("drop table if exists order_run_locks;");
    }
}
