# Stage 3/4 Consolidated Go-No-Go (2026-03-20)

Дата: 2026-03-20  
Статус документа: итоговый срез после закрытия Stage 3 Step 2

## 1. Stage 3 (LAN client-server) — решение

Решение: **GO / Closed**.

### 1.1 Что подтверждено

1. Клиент работает через API boundary для LAN run/stop (`LanRunCommandCoordinator` + `LanOrderRunApiGateway`).
2. Локальный `_runTokensByOrder` переведён в runtime-state, а не source-of-truth для server run-state:
   - запуск в LAN не блокируется только локальными токенами;
   - stop в LAN отправляется на сервер даже без локальной сессии.
3. API write-path защищён actor validation (`X-Current-User`, проверка активного пользователя при непустом users-directory).
4. Введён `X-Correlation-Id` middleware и request-level structured logging.
5. Двусторонняя sync history (`history.json <-> PostgreSQL`) работает.

### 1.2 Техническое подтверждение

1. `dotnet build Replica.sln` -> PASS (`0 warnings`, `0 errors`).
2. `dotnet test Replica.sln` -> PASS (`73/73`).
3. `REPLICA_RUN_PG_INTEGRATION=1 dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj` -> PASS (`48/48`).

## 2. Stage 4 (EF/API/AutoUpdate) — решение

Решение: **Conditional GO (In progress)**.

### 2.1 Что уже готово

1. EF Core + migrations + `Database.Migrate()` на старте PostgreSQL mode.
2. API endpoints users/orders + run/stop с optimistic concurrency.
3. `order_run_locks` и run/stop lifecycle покрыты integration-тестами.
4. Actor validation + correlation/logging baseline внедрены.

### 2.2 Что обязательно закрыть до финального Stage 4 GO

1. Финальный runbook миграций/rollback для эксплуатации.
2. Финальная верификация audit-протокола `order_events` на релизном сценарии.
3. Полный цикл автообновления (`update.xml` + `ReplicaClient.zip`) на пилотном клиенте и затем rollout.

## 3. Общий вывод

1. Stage 3 закрыт и стабилен как рабочий baseline.
2. Stage 4 можно продолжать без архитектурных блокеров.
3. Финальный `GO` по Stage 4 зависит от закрытия автообновления и эксплуатационных runbook-артефактов.
