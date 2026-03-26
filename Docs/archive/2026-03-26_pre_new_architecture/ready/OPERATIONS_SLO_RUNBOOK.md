# Operational Runbook: Replica API Observability/SLO

Статус: Completed

Актуально на `2026-03-23`.

## Для кого

1. Владелец/ведущий разработчик Replica.
2. Инженер, который дежурит по инцидентам.
3. Новый участник команды, которому нужно быстро войти в эксплуатацию.

## Цель

Быстро определить состояние API-контура (`ok/degraded/not_ready`), локализовать проблему и выбрать первые действия.

## Быстрая проверка (2-3 минуты)

1. Проверить liveness:
   - `GET /live` -> ожидается `status=live`.
2. Проверить readiness:
   - `GET /ready` -> ожидается `status=ready` (или `degraded` при pending migrations).
   - `status=not_ready` = инцидент доступности БД/контура.
3. Проверить SLO-срез:
   - `GET /slo` -> агрегированный статус и текущие значения по целям.
4. Проверить детальные метрики:
   - `GET /metrics` -> HTTP/write/idempotency counters + latency buckets + per-command срез.

## SLO baseline (текущие цели)

1. `HttpAvailabilityRatio >= 0.995`
2. `HttpLatencyP95Ms <= 500`
3. `WriteSuccessRatio >= 0.99`

Если любая из целей не выполнена, `/slo` возвращает `status=degraded`.

## Когда считать degraded/critical

1. `Degraded`:
   - `/slo.status=degraded`
   - или `/ready.status=degraded`
2. `Critical`:
   - `/ready.status=not_ready`
   - или устойчивый рост `5xx` + падение `WriteSuccessRatio` ниже 0.99

## Первые алерты (включать в таком порядке)

1. Readiness fail:
   - условие: `/ready.status=not_ready`
   - приоритет: P1 (немедленно)
2. HTTP availability breach:
   - условие: `HttpAvailabilityRatio < 0.995` (окно 5-10 минут)
   - приоритет: P1/P2
3. Write success breach:
   - условие: `WriteSuccessRatio < 0.99`
   - приоритет: P1
4. Latency breach:
   - условие: `HttpLatencyP95Ms > 500` (устойчиво 5+ минут)
   - приоритет: P2
5. Idempotency mismatch spike:
   - условие: рост `IdempotencyMismatches` (особенно по write endpoints)
   - приоритет: P2 (проверка клиентских ретраев/повторов)

## Пошаговая реакция на инцидент

1. Снять моментальный срез:
   - `/live`, `/ready`, `/slo`, `/metrics`
2. Определить класс проблемы:
   - доступность БД (`/ready=not_ready`)
   - ошибки write-path (`WriteSuccessRatio`, `WriteConflict`, `WriteBadRequest`)
   - деградация производительности (`HttpLatencyP95Ms`)
3. Проверить PostgreSQL в DBeaver:
   - `select now(), current_database();`
   - `select count(*) from orders;`
   - `select count(*) from order_events;`
   - `select count(*) from order_run_locks;`
4. Проверить idempotency/повторы:
   - рост `IdempotencyMismatches` по командам в `/metrics.commands`
5. Зафиксировать incident note:
   - время начала, affected endpoints, correlation id (если есть), первопричина/гипотеза.

## Минимальный post-incident checklist

1. Добавить/уточнить алерт, который сработал поздно или не сработал.
2. Добавить regression-тест, если это был дефект логики.
3. Обновить этот runbook, если был новый тип сбоя.

## Полезные команды (PowerShell)

```powershell
Invoke-RestMethod http://localhost:5000/live
Invoke-RestMethod http://localhost:5000/ready
Invoke-RestMethod http://localhost:5000/slo
Invoke-RestMethod http://localhost:5000/metrics
```
