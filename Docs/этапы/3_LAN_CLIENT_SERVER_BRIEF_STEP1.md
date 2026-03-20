# Этап 3: LAN client-server brief (Step 1 + Step 2 progress)

Дата актуализации: 2026-03-20
Статус: In progress (`Step 1` закрыт, `Step 2` в активной реализации)

## 1. Вход в этап 3

1. Этап 2 закрыт (`E2-P1`...`E2-P6` = Completed).
2. PostgreSQL контур в клиенте работает через `IOrdersRepository` + feature-gate.
3. Финальный аудит этапа 2: `Docs/ready/2_STAGE2_CONSOLIDATED_AUDIT_AND_GO_NO_GO.md` (`GO` в этап 3).

## 2. Step 1 (закрыт)

1. Добавлены `Replica.Shared` и `Replica.Api`.
2. Вынесены shared-контракты (`SharedOrder`, `SharedOrderItem`, `SharedUser`, `SharedOrderEvent`).
3. Поднят HTTP boundary (`/health`, `/api/users`, `/api/orders`, write endpoints для orders/items).

## 3. Step 2 (прогресс на 2026-03-20)

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
6. В `Program.cs`:
   - `ReplicaApi:StoreMode=PostgreSql` + `ConnectionStrings:ReplicaDb`;
   - регистрация `DbContextFactory`;
   - `Database.Migrate()` на старте в PostgreSQL-режиме.
7. Добавлена server-side координация `run/stop`:
   - контракт `RunOrderRequest`/`StopOrderRequest` и endpoints `POST /api/orders/{id}/run|stop`;
   - таблица `order_run_locks` + migration `20260320000200_OrderRunLocks`;
   - конфликт активного запуска возвращается как `409 Conflict` (`run already active`).
8. `/health` возвращает фактический store/mode.

### 3.2 Снижение God Object в MainForm (четвертый срез)

1. Вынесен `OrdersHistoryRepositoryCoordinator`:
   - инициализация repository;
   - bootstrap `history.json` -> PostgreSQL + marker;
   - save/fallback/concurrency обработка;
   - append repository events.
2. Вынесен `OrderRunStateService`:
   - план runnable/skipped заказов;
   - управление run-state (`BeginRunSessions`, `CompleteRunSession`, `TryStopOrder`).
3. Вынесен `OrderStatusTransitionService`:
   - нормализация `source/reason`;
   - атомарное применение status-transition к `OrderData`.
4. `MainForm` переведён на сервисные операции для history/run-state/status-transition.
5. Добавлен `LanOrderRunApiGateway` и URL-настройка `LAN API base URL`:
   - в режиме `LanPostgreSql` кнопки `Run/Stop` сначала вызывают `POST /api/orders/{id}/run|stop`;
   - локальный `_runTokensByOrder` оставлен как runtime-state активной сессии обработки;
   - после server-command выполняется snapshot-refresh (`TryLoadAll`) для предотвращения false `concurrency conflict` в следующем `SaveHistory`.
6. Добавлен `LanRunCommandCoordinator`:
   - `MainForm` больше не оркестрирует LAN `run/stop` напрямую через gateway;
   - orchestration вынесена в отдельный сервисный слой (`LanRunCommandCoordinator` + `ILanOrderRunApiGateway`);
   - добавлены unit-тесты coordinator на ветки success/conflict/fatal и stop-flow.

## 4. Техническая верификация (2026-03-20)

1. `dotnet build Replica.sln` -> PASS (`0 warnings`, `0 errors`).
2. `dotnet test Replica.sln` -> PASS (`60/60`).
3. `REPLICA_RUN_PG_INTEGRATION=1 dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj` -> PASS (`35/35`).
4. Расширен PostgreSQL integration pack:
   - `PostgreSqlIntegration_EfCoreStore_RunStopLifecycle_PersistsLockAndEvents`;
   - `PostgreSqlIntegration_EfCoreStore_RunStop_RejectsVersionMismatch`.
   - `LanOrderRunApiGatewayTests` (client-side HTTP `run/stop`).
   - `LanRunCommandCoordinatorTests` (client-side coordinator behavior).
5. Smoke API:
   - `GET /health` -> `200`, `store=EfCoreLanOrderStore`, `mode=PostgreSql`;
   - `GET /api/users` -> `200`;
   - `GET /api/orders` -> `200`.

## 5. Что остаётся в Step 2 этапа 3

1. Завершить полный client cutover: убрать локальный `_runTokensByOrder` как источник истины в LAN-режиме и оставить его только как UI/runtime-session state.
2. Продолжить вынос application-логики из UI: следующий кандидат — сервисный use-case слой поверх order workflow.
3. Ввести authN/authZ boundary и обязательную actor validation.
4. Добавить structured logging + correlation id.
5. Продолжить декомпозицию `MainForm`: выделить отдельные orchestration-сервисы для file/workflow операций (следом за history/run/status/LAN-run).

## 6. DoD этапа 3 (без изменений)

Этап 3 закрыт, когда одновременно выполнено:
1. Клиент работает через API-контракт без прямой записи UI в PostgreSQL.
2. Сервер управляет конкурентностью и `run/stop` координацией.
3. Введена базовая security boundary (actor identity + server validation).
4. Наблюдаемость покрывает ключевые операции (events + structured logs).

---

Связь с этапами:
1. Вход: `Docs/этапы/2_MULTI_ORDER_LOGIC_AND_POSTGRESQL_PLAN.md`
2. Следующий шаг: `Docs/этапы/4_EF_MIGRATIONS_API_AND_AUTOUPDATE_ROLLOUT_PLAN.md`
