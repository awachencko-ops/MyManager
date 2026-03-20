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
   - на момент закрытия этапа 2: `42/42 PASS` (`17/17 Verify`, `25/25 UiSmoke`);
   - повторная проверка на 2026-03-20: `55/55 PASS` (`30/30 Verify`, `25/25 UiSmoke`);
   - расширенная проверка после Step 2 этапа 3 (2026-03-20): `65/65 PASS` (`40/40 Verify`, `25/25 UiSmoke`);
   - дополнительная проверка после закрытия Step 2 этапа 3 (2026-03-20): `73/73 PASS` (`48/48 Verify`, `25/25 UiSmoke`);
   - PostgreSQL integration tests запускаются opt-in: `REPLICA_RUN_PG_INTEGRATION=1` (`48/48 PASS`).

## 2. Что остается (вход в Stage 3)

1. Завершить cutover `run/stop`: server-side coordination уже внедрена, нужно убрать зависимость от локального in-memory state как источника истины.
2. Ввести API boundary с authN/authZ и идемпотентностью write-команд.
3. Добавить structured logging + trace/correlation id.
4. Закрыть операционные контуры Stage 3:
   - LAN client-server контракт;
   - deployment/backup/restore/runbook.

Примечание по актуальному статусу:
1. Пункты 1 и 3 закрыты в Stage 3 Step 2 (2026-03-20).
2. Пункт 2 закрыт частично (actor validation write-path), полный authN/authZ контур перенесён в следующий срез.

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
