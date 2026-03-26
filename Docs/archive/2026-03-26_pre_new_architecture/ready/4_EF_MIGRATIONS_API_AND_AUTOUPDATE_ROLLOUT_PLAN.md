# Этап 4: EF migrations, API endpoints и автообновление клиента

Дата актуализации: 2026-03-20
Статус: Completed

## 1. Итог этапа

Этап 4 закрыт: EF Core/migrations, API write-boundary и LAN auto-update контур доведены до рабочего baseline.

## 2. Что внедрено

1. EF Core storage в `Replica.Api` (`ReplicaDbContext`, entity mappings).
2. Миграции PostgreSQL:
   - `20260320000100_BaselineSchema`;
   - `20260320000200_OrderRunLocks`.
3. Автоприменение миграций на старте API (`Database.Migrate()` в PostgreSQL mode).
4. API endpoints для users/orders/items + `run/stop` с optimistic concurrency (`409 Conflict`).
5. Server-side lock-координация `run/stop` через `order_run_locks`.
6. Actor validation на write-path:
   - обязательный `X-Current-User`;
   - при заполненном users-directory actor должен существовать и быть активным.
7. Correlation middleware:
   - входящий/исходящий `X-Correlation-Id`;
   - request-level logging scope (method/path/status/elapsed).
8. Клиентский auto-update bootstrap:
   - добавлен `AutoUpdateBootstrapper`;
   - проверка `update.xml` запускается до `Application.Run(MainForm)` в LAN PostgreSQL mode.
9. Подготовлен серверный update feed шаблон:
   - `Replica.Api/wwwroot/updates/update.xml`;
   - `Replica.Api/wwwroot/updates/changelog.txt`.

## 3. Техническая верификация

1. `dotnet build Replica.sln` -> PASS (`0 warnings`, `0 errors`).
2. `dotnet test Replica.sln` -> PASS (`77/77`).
3. `REPLICA_RUN_PG_INTEGRATION=1 dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj` -> PASS (`52/52`).

Добавленные test-slices этапа 4:
1. `OrdersControllerActorValidationTests`.
2. `CorrelationContextMiddlewareTests`.
3. `AutoUpdateBootstrapperTests`.

## 4. Эксплуатационный минимум

1. Update feed размещается в `Replica.Api/wwwroot/updates`.
2. Клиент в LAN режиме берёт manifest URL из `LanApiBaseUrl` (`.../updates/update.xml`).
3. Короткий runbook релиза: `Docs/ready/4_STAGE4_RELEASE_RUNBOOK.md`.

## 5. Definition of Done этапа 4

1. EF + migrations внедрены и воспроизводимы.
2. API endpoints пользователей/заказов работают стабильно.
3. Concurrency + audit baseline подтверждены тестами.
4. Auto-update контур внедрён (client bootstrap + server update feed).
5. Есть короткая эксплуатационная инструкция релиза.

Решение: **Stage 4 Closed**.

---

Связь с этапами:
- Вход: `Docs/ready/3_LAN_CLIENT_SERVER_BRIEF_STEP1.md`.
- Следующий этап: `INSTALLER_AND_DEPENDENCIES_PACKAGING_PLAN.md`.


