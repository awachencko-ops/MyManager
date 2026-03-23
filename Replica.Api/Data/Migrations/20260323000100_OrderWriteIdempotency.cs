using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Replica.Api.Data;

#nullable disable

namespace Replica.Api.Data.Migrations;

[DbContext(typeof(ReplicaDbContext))]
[Migration("20260323000100_OrderWriteIdempotency")]
public partial class OrderWriteIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            create table if not exists order_write_idempotency
            (
                command_name text not null,
                idempotency_key text not null,
                request_fingerprint text not null,
                actor text not null default '',
                order_internal_id text not null default '',
                result_kind text not null,
                error text not null default '',
                current_version bigint not null default 0,
                response_order_json jsonb not null default '{}'::jsonb,
                created_at timestamp without time zone not null default now(),
                updated_at timestamp without time zone not null default now(),
                primary key (command_name, idempotency_key)
            );

            create index if not exists ix_order_write_idempotency_created_at
                on order_write_idempotency(created_at);
            create index if not exists ix_order_write_idempotency_order_internal_id
                on order_write_idempotency(order_internal_id);

            insert into order_write_idempotency
            (
                command_name,
                idempotency_key,
                request_fingerprint,
                actor,
                order_internal_id,
                result_kind,
                error,
                current_version,
                response_order_json,
                created_at,
                updated_at
            )
            select
                command_name,
                idempotency_key,
                request_fingerprint,
                actor,
                order_internal_id,
                result_kind,
                error,
                current_version,
                response_order_json,
                created_at,
                updated_at
            from order_run_idempotency
            on conflict (command_name, idempotency_key) do nothing;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("drop table if exists order_write_idempotency;");
    }
}
