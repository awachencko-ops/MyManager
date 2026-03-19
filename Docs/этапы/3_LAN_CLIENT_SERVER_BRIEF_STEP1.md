# Этап 3: LAN client-server brief (Step 1)

Дата актуализации: 2026-03-19
Статус: Completed (`Step 1` закрыт, переход к `Step 2`)

## 1. Вход в этап 3

1. Этап 2 закрыт (`E2-P1`...`E2-P6` = Completed).
2. PostgreSQL контур в клиенте работает через `IOrdersRepository` + feature-gate.
3. Финальный аудит этапа 2: `Docs/ready/2_STAGE2_CONSOLIDATED_AUDIT_AND_GO_NO_GO.md` (`GO` в этап 3).

## 2. Цель Step 1

Создать базовый client-server каркас без полного cutover клиента:
1. Выделить отдельные проекты API и Shared-контрактов.
2. Зафиксировать HTTP boundary для пользователей/заказов.
3. Подготовить основу для дальнейшего перехода на server-side orchestration.

## 3. Что реализовано (факт)

1. В solution добавлены проекты:
   - `Replica.Shared` (контракты и модели);
   - `Replica.Api` (ASP.NET Core Web API).
2. Настроены reference-связи:
   - `Replica.Api` -> `Replica.Shared`;
   - `Replica` (WinForms-клиент) -> `Replica.Shared`.
3. В `Replica.Shared` вынесены базовые модели:
   - `SharedOrder`, `SharedOrderItem`, `SharedUser`, `SharedOrderEvent`;
   - enums: `SharedOrderStartMode`, `SharedOrderTopologyMarker`.
4. В `Replica.Api` поднят LAN API skeleton:
   - `GET /health`;
   - `GET /api/users`;
   - `GET /api/orders`, `GET /api/orders/{id}`;
   - `POST /api/orders`, `PATCH /api/orders/{id}`;
   - `POST /api/orders/{id}/items`, `PATCH /api/orders/{id}/items/{itemId}`;
   - `POST /api/orders/{id}/items/reorder`.
5. В in-memory store реализованы:
   - optimistic concurrency по `Version` (409 on conflict);
   - append событий `add/update-order`, `add/update-item`, `topology`;
   - базовый actor-context через header `X-Current-User`.
6. Добавлена защитная валидация `reorder`:
   - отклоняются дубли `item_id` и несоответствие количества элементов.

## 4. Техническая верификация (2026-03-19)

1. `dotnet build Replica.sln` -> PASS (0 warnings, 0 errors).
2. `dotnet test Replica.sln` -> PASS (`42/42`, включая `17 Verify` + `25 UiSmoke`).
3. Opt-in integration run:
   - `REPLICA_RUN_PG_INTEGRATION=1 dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj` -> PASS.
4. Smoke API run:
   - `GET http://localhost:5000/health` -> `200`;
   - `GET http://localhost:5000/api/users` -> `200`;
   - `GET http://localhost:5000/api/orders` -> `200`.

## 5. Что переходит в Step 2 этапа 3

1. Заменить in-memory store в `Replica.Api` на PostgreSQL/EF Core (server-owned persistence).
2. Перенести run/stop orchestration в server-side coordination (убрать процессный lock только в UI).
3. Вынести клиентский gateway на HTTP-контракты API и поэтапно убрать прямой доступ UI к repository.
4. Ввести authN/authZ boundary и обязательную server-side валидацию actor.
5. Добавить structured logging + correlation id.

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
