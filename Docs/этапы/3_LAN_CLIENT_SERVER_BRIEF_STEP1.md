# Этап 3: LAN client-server brief (Step 1 + Step 2 progress)

Дата актуализации: 2026-03-20
Статус: In progress (`Step 1` закрыт, `Step 2` начат)

## 1. Вход в этап 3

1. Этап 2 закрыт (`E2-P1`...`E2-P6` = Completed).
2. PostgreSQL контур в клиенте работает через `IOrdersRepository` + feature-gate.
3. Финальный аудит этапа 2: `Docs/ready/2_STAGE2_CONSOLIDATED_AUDIT_AND_GO_NO_GO.md` (`GO` в этап 3).

## 2. Step 1 (закрыт)

1. Добавлены `Replica.Shared` и `Replica.Api`.
2. Вынесены shared-контракты (`SharedOrder`, `SharedOrderItem`, `SharedUser`, `SharedOrderEvent`).
3. Поднят HTTP boundary (`/health`, `/api/users`, `/api/orders`, write endpoints для orders/items).

## 3. Step 2 (прогресс на 2026-03-20)

### 3.1 API storage cutover

1. Введён контракт `ILanOrderStore`.
2. In-memory store переведён на интерфейс и оставлен как fallback.
3. Добавлен `PostgreSqlLanOrderStore` (Npgsql) для серверного хранения:
   - чтение/запись orders/items/events/users;
   - optimistic concurrency (`ExpectedVersion`, `ExpectedItemVersion` -> `409 Conflict`);
   - topology reorder с проверкой дублей item id;
   - server-side append событий (`add/update-order`, `add/update-item`, `topology`).
4. В `Program.cs` реализован выбор storage mode:
   - `ReplicaApi:StoreMode=PostgreSql` + `ConnectionStrings:ReplicaDb`;
   - fallback: `InMemory`.
5. `/health` теперь возвращает фактический store/mode.

### 3.2 Снижение God Object в MainForm (первый срез)

1. Создан `OrdersHistoryRepositoryCoordinator` в `Services/`.
2. Из `MainForm` вынесены тяжёлые блоки:
   - настройка/инициализация repository;
   - bootstrap `history.json` -> PostgreSQL + marker-логика;
   - save/fallback/concurrency обработка;
   - append repository events.
3. `MainForm` оставлен как orchestration/UI-слой с thin wrappers к coordinator.

## 4. Техническая верификация (2026-03-20)

1. `dotnet build Replica.sln` -> PASS (`0 warnings`, `0 errors`).
2. `dotnet test Replica.sln` -> PASS (`42/42`).
3. `REPLICA_RUN_PG_INTEGRATION=1 dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj` -> PASS.
4. Smoke API:
   - `GET /health` -> `200`, `store=PostgreSqlLanOrderStore`, `mode=PostgreSql`;
   - `GET /api/users` -> `200`;
   - `GET /api/orders` -> `200`.

## 5. Что остаётся в Step 2 этапа 3

1. Перевести API-store на EF Core migrations (сейчас PostgreSQL store на Npgsql SQL).
2. Перенести run/stop orchestration в server-side coordination.
3. Вынести клиентский HTTP gateway и уменьшить прямой repository-доступ из UI.
4. Ввести authN/authZ boundary и обязательную actor validation.
5. Добавить structured logging + correlation id.
6. Продолжить декомпозицию `MainForm`: выделить application service для order workflow (`run/stop/status transitions`).

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
