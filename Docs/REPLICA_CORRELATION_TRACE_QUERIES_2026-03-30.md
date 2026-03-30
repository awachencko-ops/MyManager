# Replica Correlation Trace Queries (2026-03-30)

## 1) Trace by Correlation ID (order_events)

```sql
-- :corr_id -> value from X-Correlation-Id / logs
select
    event_id,
    created_at,
    order_internal_id,
    item_id,
    event_type,
    event_source,
    payload_json
from order_events
where coalesce(payload_json ->> 'correlation_id', payload_json -> 'payload' ->> 'correlation_id') = :corr_id
order by created_at asc, event_id asc;
```

## 2) Last events for one order

```sql
-- :order_id -> internal order id
select
    event_id,
    created_at,
    event_type,
    event_source,
    coalesce(payload_json ->> 'correlation_id', payload_json -> 'payload' ->> 'correlation_id') as correlation_id,
    payload_json
from order_events
where order_internal_id = :order_id
order by created_at desc, event_id desc
limit 200;
```

## 3) New API endpoint

`GET /api/diagnostics/operations/by-correlation?correlationId=<id>&limit=100`

Returns recent `order_events` rows filtered by correlation id.
