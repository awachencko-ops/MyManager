using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Replica.Api.Data;

#nullable disable

namespace Replica.Api.Data.Migrations;

[DbContext(typeof(ReplicaDbContext))]
[Migration("20260320000300_OrderRunIdempotency")]
public partial class OrderRunIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            create table if not exists order_run_idempotency
            (
                order_internal_id text not null,
                command_name text not null,
                idempotency_key text not null,
                request_fingerprint text not null,
                actor text not null default '',
                result_kind text not null,
                error text not null default '',
                current_version bigint not null default 0,
                response_order_json jsonb not null default '{}'::jsonb,
                created_at timestamp without time zone not null default now(),
                updated_at timestamp without time zone not null default now(),
                primary key (order_internal_id, command_name, idempotency_key),
                constraint fk_order_run_idempotency_order
                    foreign key (order_internal_id) references orders(internal_id) on delete cascade
            );

            create index if not exists ix_order_run_idempotency_created_at
                on order_run_idempotency(created_at);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("drop table if exists order_run_idempotency;");
    }
}
