<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.
Дата: 2026-03-20  
Статус документа: итоговый срез после закрытия Stage 3 и Stage 4

## 1. Stage 3 (LAN client-server) — решение

Решение: **GO / Closed**.

### 1.1 Что подтверждено

1. Клиент работает через API boundary для LAN run/stop (`LanRunCommandCoordinator` + `LanOrderRunApiGateway`).
2. Локальный `_runTokensByOrder` переведён в runtime-state, а не source-of-truth для server run-state.
3. API write-path защищён actor validation (`X-Current-User`, проверка активного пользователя при непустом users-directory).
4. Введён `X-Correlation-Id` middleware и request-level structured logging baseline.
5. Двусторонняя sync history (`history.json <-> PostgreSQL`) работает.

## 2. Stage 4 (EF/API/AutoUpdate) — решение

Решение: **GO / Closed**.

### 2.1 Что закрыто

1. EF Core + migrations + `Database.Migrate()` на старте PostgreSQL mode.
2. API endpoints users/orders/items + run/stop с optimistic concurrency.
3. `order_run_locks` и run/stop lifecycle подтверждены тестами.
4. Actor validation и correlation middleware внедрены.
5. Auto-update baseline внедрён:
   - client bootstrap (`AutoUpdateBootstrapper`);
   - server feed (`wwwroot/updates/update.xml`, `changelog.txt`);
   - release runbook (`Docs/ready/4_STAGE4_RELEASE_RUNBOOK.md`).

## 3. Техническое подтверждение

1. `dotnet build Replica.sln` -> PASS (`0 warnings`, `0 errors`).
2. `dotnet test Replica.sln` -> PASS (`162/162`: `137/137 Verify`, `25/25 UiSmoke`).
3. `REPLICA_RUN_PG_INTEGRATION=1 dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj` -> PASS (`137/137`).

Примечание по revalidation (финальный срез на 2026-03-20):
1. `OrdersWorkspaceForm` переведен на единый `IOrderApplicationService` boundary (включая history/folder orchestration) без регрессий в full/PG regression.

## 4. Общий вывод

1. Stage 3 закрыт и стабилен как рабочий baseline.
2. Stage 4 закрыт.
3. Следующий этап — Stage 5 (installer/dependencies packaging), без изменений в текущем проходе.
