using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Replica.Api.Data;

#nullable disable

namespace Replica.Api.Data.Migrations;

[DbContext(typeof(ReplicaDbContext))]
[Migration("20260320000100_BaselineSchema")]
public partial class BaselineSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            create table if not exists orders
            (
                internal_id text primary key,
                order_number text not null default '',
                user_name text not null default '',
                status text not null default '',
                arrival_date timestamp without time zone not null default now(),
                order_date timestamp without time zone not null default now(),
                start_mode integer not null default 0,
                topology_marker integer not null default 0,
                payload_json jsonb not null,
                version bigint not null default 1,
                updated_at timestamp without time zone not null default now()
            );

            create table if not exists order_items
            (
                item_id text primary key,
                order_internal_id text not null references orders(internal_id) on delete cascade,
                sequence_no bigint not null default 0,
                payload_json jsonb not null,
                version bigint not null default 1,
                updated_at timestamp without time zone not null default now(),
                constraint uq_order_items_sequence unique (order_internal_id, sequence_no),
                constraint ck_order_items_sequence_non_negative check (sequence_no >= 0)
            );

            create table if not exists order_events
            (
                event_id bigserial primary key,
                order_internal_id text,
                item_id text,
                event_type text not null,
                event_source text not null,
                payload_json jsonb not null default jsonb_build_object(),
                created_at timestamp without time zone not null default now()
            );

            create table if not exists users
            (
                user_name text primary key,
                is_active boolean not null default true,
                updated_at timestamp without time zone not null default now()
            );

            create table if not exists storage_meta
            (
                meta_key text primary key,
                meta_value text not null default '',
                updated_at timestamp without time zone not null default now()
            );

            create index if not exists ix_orders_order_number on orders(order_number);
            create index if not exists ix_orders_arrival_date on orders(arrival_date);
            create index if not exists ix_order_items_order_internal_id on order_items(order_internal_id);
            create index if not exists ix_order_events_order_internal_id on order_events(order_internal_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            drop table if exists order_events;
            drop table if exists order_items;
            drop table if exists orders;
            drop table if exists users;
            drop table if exists storage_meta;
            """);
    }
}
