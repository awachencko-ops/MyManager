using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Replica.Api.Data;

#nullable disable

namespace Replica.Api.Data.Migrations;

[DbContext(typeof(ReplicaDbContext))]
[Migration("20260327000100_AuthSessionsAndAudit")]
public partial class AuthSessionsAndAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            create table if not exists auth_sessions
            (
                session_id text primary key,
                user_name text not null default '',
                role text not null default 'Operator',
                access_token_hash text not null default '',
                created_at_utc timestamp without time zone not null default now(),
                expires_at_utc timestamp without time zone not null default now(),
                last_seen_at_utc timestamp without time zone not null default now(),
                revoked_at_utc timestamp without time zone null,
                issued_by text not null default '',
                revoked_by text not null default ''
            );

            create table if not exists auth_audit_events
            (
                event_id bigserial primary key,
                event_type text not null default '',
                user_name text not null default '',
                role text not null default '',
                session_id text not null default '',
                outcome text not null default '',
                reason text not null default '',
                ip_address text not null default '',
                user_agent text not null default '',
                metadata_json jsonb not null default '{}',
                created_at_utc timestamp without time zone not null default now()
            );

            create index if not exists ix_auth_sessions_user_name on auth_sessions(user_name);
            create index if not exists ix_auth_sessions_expires_at_utc on auth_sessions(expires_at_utc);
            create index if not exists ix_auth_sessions_revoked_at_utc on auth_sessions(revoked_at_utc);
            create index if not exists ix_auth_audit_events_created_at_utc on auth_audit_events(created_at_utc);
            create index if not exists ix_auth_audit_events_event_type on auth_audit_events(event_type);
            create index if not exists ix_auth_audit_events_user_name on auth_audit_events(user_name);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            drop table if exists auth_audit_events;
            drop table if exists auth_sessions;
            """);
    }
}
