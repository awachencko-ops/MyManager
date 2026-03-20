# Этап 3: LAN client-server brief (Step 1 + Step 2)

Дата актуализации: 2026-03-20
Статус: Completed (`Step 1` и `Step 2` закрыты)

## 1. Вход в этап 3

1. Этап 2 закрыт (`E2-P1`...`E2-P6` = Completed).
2. PostgreSQL контур в клиенте работает через `IOrdersRepository` + feature-gate.
3. Финальный аудит этапа 2: `Docs/ready/2_STAGE2_CONSOLIDATED_AUDIT_AND_GO_NO_GO.md` (`GO` в этап 3).

## 2. Step 1 (закрыт)

1. Добавлены `Replica.Shared` и `Replica.Api`.
2. Вынесены shared-контракты (`SharedOrder`, `SharedOrderItem`, `SharedUser`, `SharedOrderEvent`).
3. Поднят HTTP boundary (`/health`, `/api/users`, `/api/orders`, write endpoints для orders/items).

## 3. Step 2 (закрыт, факт на 2026-03-20)

### 3.1 API storage cutover (EF Core)

1. Введён контракт `ILanOrderStore`.
2. In-memory store оставлен как fallback.
3. Добавлен `EfCoreLanOrderStore`:
   - чтение/запись orders/items/events/users;
   - optimistic concurrency (`ExpectedVersion`, `ExpectedItemVersion` -> `409 Conflict`);
   - reorder c валидацией дублей `item_id`;
   - server-side append событий (`add/update-order`, `add/update-item`, `topology`).
4. Добавлен EF Core контур `ReplicaDbContext` + entity mappings (`orders`, `order_items`, `order_events`, `users`, `storage_meta`).
5. Добавлена baseline migration `20260320000100_BaselineSchema` с idempotent SQL (`create table/index if not exists`).
6. Добавлена миграция `20260320000200_OrderRunLocks`.
7. На старте API в PostgreSQL mode выполняется `Database.Migrate()`.
8. Добавлены endpoints `POST /api/orders/{id}/run|stop` с `409 Conflict` при активном запуске.
9. Для write-endpoints введена обязательная actor-проверка:
   - требуется `X-Current-User`;
   - если users-directory уже заполнен, actor должен существовать и быть активным.
10. Введён middleware `X-Correlation-Id` + request logging scope (correlation id, method, path, status, elapsed).
11. `/health` возвращает фактический store/mode.

### 3.2 Снижение God Object в MainForm (пятый срез)

1. Вынесен `OrdersHistoryRepositoryCoordinator`:
   - инициализация repository;
   - bootstrap `history.json` -> PostgreSQL + marker;
   - save/fallback/concurrency обработка;
   - append repository events.
2. Вынесен `OrderRunStateService`:
   - план runnable/skipped заказов;
   - управление run-state (`BeginRunSessions`, `CompleteRunSession`, `TryStopOrder`, `BuildStopPlan`).
3. Вынесен `OrderStatusTransitionService`.
4. Добавлены `LanOrderRunApiGateway` + `LanRunCommandCoordinator`.
5. Добавлен `OrderRunExecutionService`.
6. Добавлена двусторонняя sync-логика history (`history.json <-> PostgreSQL`, DB-first на конфликтах).
7. Закрыт client cutover для run/stop в LAN-режиме:
   - локальный `_runTokensByOrder` больше не источник истины для server run-state;
   - при старте в LAN `BuildRunPlan(..., useLocalRunState: false)` не блокирует повторный запуск только по локальному токену;
   - stop в LAN отправляется на сервер даже без локальной сессии;
   - `tsbStop` активируется по `Processing`-статусу в LAN, даже если локального токена нет.

## 4. Техническая верификация (2026-03-20)

1. `dotnet build Replica.sln` -> PASS (`0 warnings`, `0 errors`).
2. `dotnet test Replica.sln` -> PASS (`77/77`).
3. `REPLICA_RUN_PG_INTEGRATION=1 dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj` -> PASS (`52/52`).
4. Расширен test pack:
   - `PostgreSqlIntegration_EfCoreStore_RunStopLifecycle_PersistsLockAndEvents`;
   - `PostgreSqlIntegration_EfCoreStore_RunStop_RejectsVersionMismatch`;
   - `PostgreSqlIntegration_Coordinator_SynchronizesFileAndLanHistories`;
   - `LanOrderRunApiGatewayTests`;
   - `LanRunCommandCoordinatorTests`;
   - `OrderRunExecutionServiceTests`;
   - `OrderRunStateServiceTests` (LAN cutover cases);
   - `OrdersControllerActorValidationTests`;
   - `CorrelationContextMiddlewareTests`.

## 5. Итог Step 2

Решение: **Step 2 Closed**.

Что перенесено в следующий архитектурный слой:
1. Дальнейший вынос file/workflow orchestration из `MainForm` (следующие декомпозиционные срезы).
2. Расширение security boundary до полноценного authN/authZ контура (на текущем шаге реализована actor validation для write path).

## 6. DoD этапа 3

Этап 3 закрыт по критериям:
1. Клиент работает через API-контракт без прямой записи UI в PostgreSQL.
2. Сервер управляет конкурентностью и `run/stop` координацией.
3. Введена базовая security boundary (actor identity + server validation write path).
4. Добавлена наблюдаемость request-уровня (`X-Correlation-Id`, structured request logs).

---

Связь с этапами:
1. Вход: `Docs/ready/2_MULTI_ORDER_LOGIC_AND_POSTGRESQL_PLAN.md`
2. Следующий шаг: `Docs/ready/4_EF_MIGRATIONS_API_AND_AUTOUPDATE_ROLLOUT_PLAN.md`


