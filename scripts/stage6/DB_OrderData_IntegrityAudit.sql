-- Replica Stage 6: order data integrity audit
-- Run in DBeaver against the production/acceptance PostgreSQL database.
-- This script is read-only.

-- 1) Quick totals
select
    (select count(*) from orders) as orders_total,
    (select count(*) from order_items) as order_items_total,
    (select count(*) from order_events) as order_events_total,
    (select count(*) from order_run_locks where is_active = true) as active_run_locks_total,
    (select count(*) from order_write_idempotency) as write_idempotency_total;

-- 2) Orders with empty number (can trigger "without number" run skip)
select
    o.internal_id,
    o.order_number,
    o.version,
    o.status,
    o.order_date,
    o.updated_at
from orders o
where btrim(coalesce(o.order_number, '')) = ''
order by o.updated_at desc;

-- 3) Header columns vs payload_json mismatches for order critical fields
with parsed as (
    select
        o.internal_id,
        o.order_number,
        o.user_name,
        o.status,
        o.order_date,
        o.version,
        o.payload_json,
        coalesce(o.payload_json ->> 'orderNumber', '') as p_order_number,
        coalesce(o.payload_json ->> 'userName', '') as p_user_name,
        coalesce(o.payload_json ->> 'status', '') as p_status,
        case
            when coalesce(o.payload_json ->> 'managerOrderDate', '') = '' then null
            else ((o.payload_json ->> 'managerOrderDate')::timestamptz)::date
        end as p_order_date,
        case
            when coalesce(o.payload_json ->> 'version', '') ~ '^[0-9]+$'
                then (o.payload_json ->> 'version')::bigint
            else null
        end as p_version
    from orders o
)
select
    internal_id,
    order_number,
    p_order_number,
    user_name,
    p_user_name,
    status,
    p_status,
    order_date::date as order_date_col,
    p_order_date as order_date_payload,
    version as version_col,
    p_version as version_payload,
    updated_at
from (
    select p.*, o.updated_at
    from parsed p
    join orders o on o.internal_id = p.internal_id
) t
where
    btrim(order_number) <> btrim(p_order_number)
    or btrim(user_name) <> btrim(p_user_name)
    or btrim(status) <> btrim(p_status)
    or coalesce(order_date::date, date '1900-01-01') <> coalesce(p_order_date, date '1900-01-01')
    or coalesce(version, -1) <> coalesce(p_version, -1)
order by updated_at desc;

-- 4) Orphan items (item exists, parent order missing)
select
    i.item_id,
    i.order_internal_id,
    i.sequence_no,
    i.version,
    i.updated_at
from order_items i
left join orders o on o.internal_id = i.order_internal_id
where o.internal_id is null
order by i.updated_at desc;

-- 5) Duplicate sequence numbers inside one order
select
    order_internal_id,
    sequence_no,
    count(*) as item_count
from order_items
group by order_internal_id, sequence_no
having count(*) > 1
order by order_internal_id, sequence_no;

-- 6) order_items columns vs payload_json mismatches
with parsed as (
    select
        i.item_id,
        i.order_internal_id,
        i.sequence_no,
        i.version,
        i.updated_at,
        i.payload_json,
        coalesce(i.payload_json ->> 'itemId', '') as p_item_id,
        case
            when coalesce(i.payload_json ->> 'sequenceNo', '') ~ '^-?[0-9]+$'
                then (i.payload_json ->> 'sequenceNo')::bigint
            else null
        end as p_sequence_no,
        case
            when coalesce(i.payload_json ->> 'version', '') ~ '^[0-9]+$'
                then (i.payload_json ->> 'version')::bigint
            else null
        end as p_version
    from order_items i
)
select
    item_id,
    p_item_id,
    order_internal_id,
    sequence_no,
    p_sequence_no,
    version,
    p_version,
    updated_at
from parsed
where
    btrim(item_id) <> btrim(p_item_id)
    or coalesce(sequence_no, -1) <> coalesce(p_sequence_no, -1)
    or coalesce(version, -1) <> coalesce(p_version, -1)
order by updated_at desc;

-- 7) Active run locks that do not have active statuses
-- Active statuses here are Processing/Building (normalized form).
select
    l.order_internal_id,
    l.is_active,
    l.lease_owner,
    l.lease_token,
    l.started_at,
    l.updated_at,
    o.status as status_column,
    coalesce(o.payload_json ->> 'status', '') as status_payload
from order_run_locks l
left join orders o on o.internal_id = l.order_internal_id
where
    l.is_active = true
    and (
        o.internal_id is null
        or upper(coalesce(o.payload_json ->> 'status', o.status, '')) not in ('PROCESSING', 'BUILDING')
    )
order by l.updated_at desc;

-- 8) Recent write conflicts (expected source of "order version mismatch" in UI)
select
    command_name,
    idempotency_key,
    actor,
    order_internal_id,
    result_kind,
    current_version,
    error,
    created_at,
    updated_at
from order_write_idempotency
where lower(coalesce(result_kind, '')) = 'conflict'
order by created_at desc
limit 200;

-- 9) Last events per order (quick trace)
select
    e.order_internal_id,
    e.item_id,
    e.event_type,
    e.event_source,
    e.created_at,
    coalesce(e.payload_json ->> 'actor', '') as actor,
    coalesce(e.payload_json ->> 'correlation_id', '') as correlation_id
from order_events e
order by e.created_at desc
limit 300;
