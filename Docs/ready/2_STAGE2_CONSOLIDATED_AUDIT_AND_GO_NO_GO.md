# Stage 2 Consolidated Audit (PostgreSQL/LAN)

Дата: 2026-03-19  
Статус: **Stage 2 Closed**

## 1. Что закрыто

1. Внедрен `IOrdersRepository` и feature-gate хранения (`FileSystem` / `LanPostgreSql`).
2. Реализован `PostgreSqlOrdersRepository` с optimistic concurrency (`StorageVersion`, conflict-guard).
3. В `order_events` пишутся:
   - CRUD/topology-события (`add/update/delete-order`, `add/update/remove-item`);
   - runtime-события (`run/stop/status-change`) из клиентского workflow.
4. Выполнен one-time bootstrap из `history.json` в PostgreSQL с marker `history_json_bootstrap_v1` (`storage_meta`).
5. Подтверждена live-верификация БД:
   - `orders=10`
   - `order_items=11`
   - `orphan_items=0`
   - marker присутствует (`state=imported`).
6. Regression pack закрыт:
   - solution tests: `42/42 PASS` (`17/17 Verify`, `25/25 UiSmoke`);
   - добавлены PostgreSQL integration tests (opt-in: `REPLICA_RUN_PG_INTEGRATION=1`).

## 2. Что остается (вход в Stage 3)

1. Вынести orchestration `run/stop` из in-memory клиента в server-side coordination.
2. Ввести API boundary с authN/authZ и идемпотентностью write-команд.
3. Добавить structured logging + trace/correlation id.
4. Закрыть операционные контуры Stage 3:
   - LAN client-server контракт;
   - deployment/backup/restore/runbook.

## 3. Go / No-Go для Stage 3

Решение: **GO** (с контролируемыми рисками).

Обоснование:
1. Данные и события уже централизованы в PostgreSQL.
2. Конфликты конкурентной записи обрабатываются корректно (`concurrency conflict`).
3. Bootstrap и миграция истории подтверждены на живой БД.
4. Тестовый baseline стабилен.

Ограничения GO:
1. Это не финальная enterprise-архитектура; это устойчивый переходный baseline.
2. Ключевые архитектурные риски (API boundary, distributed observability, server-side coordination) переносятся в Stage 3.
