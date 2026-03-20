# Архитектурный аудит Replica (текущий код Replica)

> Контекст: аудит выполнен по текущему монолитному WinForms-приложению, которое работает с файловой системой/NAS и JSON-файлами конфигурации/истории.

> Актуализация на 2026-03-19 (этап 2): live-проверка PostgreSQL выполнена, `history.json` импортирован в `replica_db` (10 orders / 11 items), marker `history_json_bootstrap_v1` записан в `storage_meta`, orphan-items не обнаружены; добавлен интеграционный PostgreSQL regression-pack (single/group roundtrip, concurrency conflict, event append).
>
> Актуализация на 2026-03-19 (этап 3, Step 1): добавлены `Replica.Shared` и `Replica.Api`, поднят LAN API skeleton (`/health`, `/api/users`, `/api/orders` + write endpoints), подтверждён smoke-run API (`200` на health/users/orders). Полный cutover клиента на HTTP boundary и server-side orchestration остаются в Step 2 этапа 3.
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress): API переключён на `EfCoreLanOrderStore` + `ReplicaDbContext` (EF Core), добавлена baseline migration `20260320000100_BaselineSchema`, health подтверждает store mode `PostgreSql`; в клиенте вынесена repository/bootstrap логика из `MainForm` в `OrdersHistoryRepositoryCoordinator`.
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress, срез 2): добавлен `OrderRunStateService`, а методы `RunSelectedOrderAsync`/`StopSelectedOrder` переведены на сервисное управление run-state (план runnable/skipped + lifecycle токенов).
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress, срез 3): добавлен `OrderStatusTransitionService`, а `SetOrderStatus` переведён на сервисное применение status-transition policy (нормализация source/reason, единое условие no-op).
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress, срез 4): добавлены server-side `run/stop` endpoints (`POST /api/orders/{id}/run|stop`) и централизованный lock-state (`order_run_locks`) в `EfCoreLanOrderStore`; подтверждено PostgreSQL integration-тестами (lifecycle lock/events + version mismatch).
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress, срез 5): в клиенте добавлен `LanOrderRunApiGateway`; в режиме `LanPostgreSql` команды `Run/Stop` идут через API boundary (`/api/orders/{id}/run|stop`) с локальным snapshot-refresh перед следующим `SaveHistory`.
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress, срез 6): добавлен `LanRunCommandCoordinator` и интерфейсный контракт `ILanOrderRunApiGateway`; orchestration LAN `run/stop` вынесена из `MainForm` в сервисный слой, покрыта unit-тестами coordinator (`success/conflict/fatal/stop`).
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress, срез 7): добавлен `OrderRunExecutionService`; конкурентное выполнение run-сессий (`Task.WhenAll`, cancel/error handling, completion callbacks) вынесено из `MainForm` в сервисный use-case слой, покрыто unit-тестами (`success/cancel/failure/mixed`).
>
> Актуализация на 2026-03-20 (этап 3, Step 2 progress, срез 8): добавлена двусторонняя sync-логика history в `OrdersHistoryRepositoryCoordinator` (`history.json <-> PostgreSQL`): `file->db` импорт отсутствующих заказов по `InternalId`, `db->file` зеркалирование актуального снимка; подтверждено integration-тестом coordinator sync.
>
> Актуализация на 2026-03-20 (этап 3, Step 2 close, срез 9): закрыт client cutover `run/stop` в LAN-режиме (локальный `_runTokensByOrder` больше не источник истины для server run-state), добавлена обязательная actor validation для write-endpoints (`X-Current-User`) и request-level `X-Correlation-Id` middleware.
>
> Актуализация на 2026-03-20 (этап 4 close, срез 10): внедрён auto-update baseline (client bootstrap + `wwwroot/updates/update.xml` feed), этап 4 закрыт; в рамках risk-burndown убраны silent `catch { }` в критическом runtime-пути (`OrderProcessor`, `OrderForm`, `ConfigService`) с заменой на контролируемый fallback + warning-лог.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 11): добавлен `OrderDeletionWorkflowService`; batch-удаление заказов/файлов (`remove-from-disk`, fallback на known paths, item reindex, агрегация ошибок) вынесено из `MainForm` в сервисный слой, покрыто unit-тестами (`orders delete`, `folder-miss fallback`, `item reindex`, `item-not-found`).
>
> Актуализация на 2026-03-20 (risk-burndown, срез 12): введён `ISettingsProvider` + `FileSettingsProvider`; runtime-path (`Program`/`MainForm`/`OrderProcessor`) переведён на provider-инъекцию, `ConfigService` отвязан от прямого `AppSettings.Load()` через `ConfigService.SettingsProvider`, добавлены unit-тесты provider-boundary.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 13): в `OrderProcessor` внедрён `FileOperationRetryPolicy` (retry+backoff для copy/move/delete/create/read), file-операции переведены на policy boundary с retry-telemetry (`FILE-RETRY`), добавлены unit-тесты policy и обновлены UI smoke-тесты cleanup-сценариев.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 14): добавлен `DependencyCircuitBreaker` и dependency health-сигналы в `OrderProcessor` (PitStop/Imposing/Storage), операции переведены на dependency-guard (`circuit-open` + retry-after), в `MainForm` добавлена UI-индикация degraded/unavailable в `toolConnection` и server-header статусе.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 15): внедрён `DependencyBulkheadPolicy` (load shedding per dependency) и readiness-проверки в `OrderProcessor` перед стартом workflow (storage/hotfolder availability для выбранных сценариев), запуск блокируется до восстановления критичных контуров.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 16): введён `WorkflowTimeoutBudgetPolicy`; `OrderProcessor` переведён на stage-timeout budgets (PitStop/Imposing/report), добавлено логирование budget-параметров (`TIMEOUT-BUDGET`) и unit-тесты timeout-policy.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 17): в client-runtime добавлены `LogContext` + structured scope-fields в `Logger` (включая `correlation_id`), `Run/Stop` orchestration в `MainForm` переведена на correlation-scopes, а `LanOrderRunApiGateway` начал пробрасывать `X-Correlation-Id` в API-команды.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 18): для server `run/stop` внедрена идемпотентность команд (`Idempotency-Key` в client gateway + API header-resolve, таблица `order_run_idempotency`, migration `20260320000300_OrderRunIdempotency`, дедупликация с request fingerprint), покрыто unit/integration тестами PostgreSQL.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 19): добавлен `OrderRunWorkflowOrchestrationService`; подготовка `run/stop` workflow (run-plan, LAN command preflight, snapshot refresh, stop preflight/cancel) вынесена из `MainForm` в application-service слой, добавлены unit-тесты orchestration (`OrderRunWorkflowOrchestrationServiceTests`).
>
> Актуализация на 2026-03-20 (risk-burndown, срез 20): выполнен rename и модульный перенос UI-ядра формы: entrypoint переведён на `OrdersWorkspaceForm`, код формы перемещён в `UI/Forms/OrdersWorkspace/*` (Core/FileOps/Filters/Views/Controls), сохранён переходный shim `MainForm` для обратной совместимости автотестов.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 21): введён каркас гибридной структуры (`Features/*`, `Infrastructure/*`, `SharedKernel/*`, `Legacy/*`), выполнен первый feature-slice перенос `Orders` (`Features/Orders/UI|Application|Domain`) и storage adapters в `Infrastructure/Storage/Orders`; зафиксированы правила quarantine/exit для `Legacy`.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 22): `OrderProcessor` модульно перенесён в `Infrastructure/Processing/Orders` и разложен на частичные файлы по зонам ответственности (`OrderProcessor`, `OrderProcessor.FileWorkflow`, `OrderProcessor.DependencyResilience`); остаточный direct `AppSettings.Load()` в рабочем UI-контуре устранён (перевод `ImposingManagerForm` на `ISettingsProvider`), подтверждено build + unit/ui-smoke + PostgreSQL integration regression.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 23): добавлен `OrderRunCommandService`; run-start/run-execution orchestration (`PrepareStart + BeginRunSessions + Execute + CompleteRunSession`) переведена из `OrdersWorkspaceForm` в application-service boundary, форма оставлена как UI/presenter слой для статусов и диалогов; добавлены unit-тесты `OrderRunCommandServiceTests`, подтверждены build + full test + PostgreSQL integration regression.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 24): `OrderRunCommandService` расширен stop-boundary (`ExecuteStopAsync`); stop/status persistence orchestration (`PrepareStop + local status apply + conflict/unconfirmed ветки`) переведена из `OrdersWorkspaceForm` в application-service слой, форма оставлена как UI-обработчик user-feedback; добавлены stop-сценарии в `OrderRunCommandServiceTests`, подтверждены build + full test + PostgreSQL integration regression.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 25): добавлен `OrderEditorMutationService`; create/edit mutation logic (`AddCreatedOrder`, `ApplySimpleEdit`, `ApplyExtendedEdit`) переведена из `OrdersWorkspaceForm` в application-service слой, форма оставлена как UI-shell для диалогов и refresh-потока (`SaveHistory`/`RebuildOrdersGrid`); добавлены unit-тесты `OrderEditorMutationServiceTests`, подтверждены build + full test + PostgreSQL integration regression.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 26): добавлен `OrderItemMutationService`; item-mutation logic (`PrepareAddItem`, `RollbackPreparedItem`, `RemoveItemIfEmpty`, `ApplyTopologyAfterItemMutation`) переведена из `OrdersWorkspaceForm` в application-service слой, форма оставлена как UI/presenter для диалогов, selection-state и operation-log; добавлены unit-тесты `OrderItemMutationServiceTests`, подтверждены build + full test + PostgreSQL integration regression.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 27): добавлен `OrderItemDeleteCommandService`; orchestration удаления выбранных item-ов (`capture affected orders + delete batch + topology post-mutation`) переведена из `OrdersWorkspaceForm` в application-service слой, форма оставлена как UI-shell для confirm/status/ошибок; добавлены unit-тесты `OrderItemDeleteCommandServiceTests`, подтверждены build + full test + PostgreSQL integration regression.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 28): добавлен `OrderDeleteCommandService`; orchestration удаления выбранных заказов (`delete batch + run-session cleanup + expanded-state cleanup`) переведена из `OrdersWorkspaceForm` в application-service слой, форма оставлена как UI-shell для confirm/status/ошибок; добавлены unit-тесты `OrderDeleteCommandServiceTests`, подтверждены build + full test + PostgreSQL integration regression.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 29): добавлен `OrderFilePathMutationService`; file-path/status mutation logic (`ApplyOrderFilePath`, `ApplyItemFilePath`, `CalculateOrderStatusFromItems`) переведена из `OrdersWorkspaceForm` в application-service слой, форма оставлена как UI-shell применения `SetOrderStatus`; добавлены unit-тесты `OrderFilePathMutationServiceTests`, подтверждены build + full test + PostgreSQL integration regression.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 30): добавлен `OrderFileStageCommandService`; stage add-command planning (`TryPrepareOrderAdd`, `TryPrepareItemAdd`: clean-source validation, target naming, print/source-copy flags) переведён из `OrdersWorkspaceForm` в application-service слой, форма оставлена как UI-shell для actual copy IO и user-feedback; добавлены unit-тесты `OrderFileStageCommandServiceTests`, подтверждены build + full test + PostgreSQL integration regression.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 31): добавлен `OrderFileRenameRemoveCommandService`; rename/remove command orchestration (`TryBuildRenamedPath`, `ApplyOrderFileRemoved/Renamed`, `ApplyItemFileRemoved/Renamed`) переведена из `OrdersWorkspaceForm` в application-service слой, форма оставлена как UI-shell для confirm dialogs и файлового IO (`File.Move/Delete`); добавлены unit-тесты `OrderFileRenameRemoveCommandServiceTests`, подтверждены build + full test + PostgreSQL integration regression.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 32): `OrderFileRenameRemoveCommandService` расширен print-tiles rename boundary (`ApplyPrintTileFileRenamed`); sync print-path ссылок order/item и post-rename status-update вынесены из `OrdersWorkspaceForm.PrintTiles` в application-service слой, удалён локальный UI-метод `UpdatePrintPathReferencesForOrder`, добавлены unit-тесты print-tile rename (`match/fallback`), подтверждены build + full test + PostgreSQL integration regression.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 33): добавлен `OrdersHistoryMaintenanceService`; lifecycle `LoadHistory/SaveHistory` (post-load normalization, hash/size backfill, topology normalization, pre-save maintenance) вынесен из `OrdersWorkspaceForm` в application-service слой, форма оставлена как UI-shell для repository IO и logging, добавлены unit-тесты `OrdersHistoryMaintenanceServiceTests`, подтверждены build + full test + PostgreSQL integration regression.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 34): добавлен `OrderFolderPathResolutionService`; folder-path resolution для single/group order (`ResolveBrowseFolderPath`, `ResolvePreferredOrderFolder`, common-folder/root-mismatch policy) вынесен из `OrdersWorkspaceForm` в application-service слой, удалены дублирующие path-алгоритмы из формы, добавлены unit-тесты `OrderFolderPathResolutionServiceTests`, подтверждены build + full test + PostgreSQL integration regression.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 35): добавлен `OrderStorageVersionSyncService`; sync `StorageVersion` при snapshot-refresh (LAN PostgreSQL run/stop path) вынесен из `OrdersWorkspaceForm` в application-service слой, форма оставлена как UI-shell для repository reload + warning-логов, добавлены unit-тесты `OrderStorageVersionSyncServiceTests`, подтверждены build + full test + PostgreSQL integration regression.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 36): добавлен `OrderRunFeedbackService`; planning run-feedback (`server-skipped preview`, `skipped details`, `execution errors preview`) вынесен из `OrdersWorkspaceForm` в application-service слой, удалены дубли локального preview-formatting в `RunSelectedOrderAsync`, добавлены unit-тесты `OrderRunFeedbackServiceTests`, подтверждены build + full test + PostgreSQL integration regression.
>
> Актуализация на 2026-03-20 (risk-burndown, срез 37): введён `OrdersWorkspaceCompositionRoot` + `OrdersWorkspaceRuntimeServices`; создание ключевых runtime-зависимостей формы (run/history/mutation services) вынесено из конструктора `OrdersWorkspaceForm` в composition root, что снижает ручную связность и подготавливает поэтапный DI cutover, подтверждены build + full test + PostgreSQL integration regression.

## Executive summary

- Текущая реализация **не готова** к роли транзакционно-безопасной платформы на сотни пользователей.
- Главные причины: API/worker-контур пока не доведён до production-boundary (full authN/authZ, полный cutover всех write-flow и observability/SLO контур ещё неполные), UI-центричная оркестрация остаётся значимой.
- В коде уже закрыт значимый кусок миграции: введён `IOrdersRepository`, реализован LAN PostgreSQL backend с optimistic concurrency (`StorageVersion` + conflict guard), добавлен `order_events` и one-time bootstrap marker в `storage_meta`; на этапе 3 добавлены API skeleton, EF Core storage слой, server-side `run/stop` lock-координация (`order_run_locks`), идемпотентность `run/stop` (`Idempotency-Key` + `order_run_idempotency`), клиентские `LanOrderRunApiGateway` + `LanRunCommandCoordinator`, bootstrap composition root (`OrdersWorkspaceCompositionRoot`) и выносы из `MainForm` в сервисы (`OrdersHistoryRepositoryCoordinator`, `OrderRunStateService`, `OrderStatusTransitionService`, `OrderRunExecutionService`, `OrderRunFeedbackService`, `OrderDeletionWorkflowService`) + двусторонняя sync `history.json <-> PostgreSQL`.

---

## 1) Архитектурная целостность (Clean Architecture)

### Наблюдения

- Точка входа поднимает сразу WinForms (`Application.Run(new MainForm())`), без явного composition root для бизнес-слоя/инфраструктуры.
- `MainForm` агрегирует orchestration, хранение истории, статус-машину, UI-binding, файловые операции и запуск процессора; состояние формы содержит десятки полей и коллекций.
- Добавлен API-каркас (`Replica.Api`) и shared-контракты (`Replica.Shared`); клиент уже частично переведён на API gateway (`run/stop`), но полный cutover всех write-flow ещё не завершён.
- Из `MainForm` выделен `OrdersHistoryRepositoryCoordinator` (инициализация repository, bootstrap/fallback/event append), что уменьшило размер и связность части persistence-логики.
- Из `MainForm` выделен `OrderRunStateService` (run-state lifecycle и фильтрация runnable/skipped заказов), что уменьшило связность части run/stop orchestration.
- Из `MainForm` выделен `OrderStatusTransitionService` (policy status-transition и нормализация reason/source), что уменьшило связность статусной логики.
- В клиенте добавлен `LanOrderRunApiGateway`: `Run/Stop` в `LanPostgreSql` mode вызывают API endpoints вместо прямой локальной координации.
- В клиенте добавлен `LanRunCommandCoordinator`: LAN `run/stop` orchestration вынесена из `MainForm` в отдельный сервис (форма теперь использует coordinator, а не прямую LAN gateway-логику).
- Из `MainForm` выделен `OrderRunExecutionService`: конкурентное выполнение run-сессий и error/cancel lifecycle больше не оркестрируются внутри формы.
- Из `MainForm` выделен `OrderRunWorkflowOrchestrationService`: run/stop preflight (plan, LAN approval, snapshot refresh, local cancel) теперь в сервисном use-case слое.
- Из `MainForm` выделен `OrderRunCommandService`: единая orchestration-цепочка запуска (`prepare/begin/execute/complete`) переведена в application-service, UI управляет только user-feedback.
- Stop/status persistence orchestration переведён на `OrderRunCommandService.ExecuteStopAsync`; форма больше не управляет stop-ветвлением на уровне run-state/invariants.
- Create/edit mutation logic переведена на `OrderEditorMutationService`; форма больше не содержит прямое присваивание полей заказа при simple/extended edit.
- Item mutation/topology logic переведена на `OrderItemMutationService`; форма больше не содержит правила подготовки item при add/rollback/remove-empty/reindex.
- Item-delete orchestration переведена на `OrderItemDeleteCommandService`; форма больше не содержит batch-удаление item-ов с pre/post-topology шагами.
- Order-delete orchestration переведена на `OrderDeleteCommandService`; форма больше не содержит run-state cleanup и batch-delete command flow для order-level удаления.
- File path/status mutation logic переведена на `OrderFilePathMutationService`; форма больше не содержит доменные правила синхронизации order/item путей и агрегирования file-sync статуса.
- File stage add-command planning переведён на `OrderFileStageCommandService`; форма больше не содержит правила подготовки target-name/flags для order/item add-flow.
- File rename/remove command orchestration переведена на `OrderFileRenameRemoveCommandService`; форма больше не содержит правила post-mutation item/topology ветвления и rename-path validation.
- Print-tiles rename path sync переведён на `OrderFileRenameRemoveCommandService.ApplyPrintTileFileRenamed`; форма больше не содержит доменную мутацию ссылок `order.PrintPath/item.PrintPath` в tile-rename сценарии.
- Из `MainForm` выделен `OrderDeletionWorkflowService`: batch-удаление orders/items (включая disk-cleanup, fallback на known paths и reindex item-ов) переведено в use-case сервис.
- Выполнен rename UI-shell: рабочая форма теперь `OrdersWorkspaceForm`; после следующего шага декомпозиции код `Orders` разложен в feature-slice структуру `Features/Orders/UI|Application|Domain`, `MainForm` оставлен как compatibility shim.
- Введён интерфейсный слой настроек (`ISettingsProvider`), а core runtime-flow (`Program`, `MainForm`, `OrderProcessor`, `ConfigService`) переведён с прямого static-IO на provider boundary.
- Остаточный direct `AppSettings.Load()` в manager-форме (`ImposingManagerForm`) убран; рабочий UI-контур использует provider boundary.
- `OrderProcessor` вынесен в модуль `Infrastructure/Processing/Orders` и разложен на partial-срезы file-workflow/resilience для поэтапной декомпозиции.
- В `OrderProcessor` добавлены dependency health-сигналы и circuit-breaker (`DependencyCircuitBreaker`) для внешних file-зависимостей; `MainForm` получает сигналы и отражает degraded/unavailable статус в UI.
- В `OrdersHistoryRepositoryCoordinator` добавлена двусторонняя sync-стратегия `history.json <-> PostgreSQL` (импорт file-only заказов + mirror LAN snapshot обратно в файл).
- Из `OrdersWorkspaceForm` выделен `OrdersHistoryMaintenanceService`: post-load/pre-save maintenance (id/arrival normalization, hash/size backfill, topology normalization) больше не реализуется внутри UI-класса.
- Из `OrdersWorkspaceForm` выделен `OrderFolderPathResolutionService`: правила выбора папки заказа/group-order и common-path вычисления вынесены из UI-класса в application-service boundary.
- Из `OrdersWorkspaceForm` выделен `OrderStorageVersionSyncService`: синхронизация локальных `StorageVersion` из storage snapshot больше не реализуется внутри UI-класса.
- Persistence реализован через прямое чтение/запись JSON (`history.json`) из UI-слоя.
- `ConfigService` и `AppSettings` — статические сервисы/конфиги с прямым file IO, без интерфейсов и DI.

### Вывод

- SoC нарушен: UI-layer контролирует use-case/persistence.
- Налицо «God Object» в виде `MainForm` (+ partial-файлы как физическое разделение, но не архитектурное), хотя декомпозиция уже заметно продвинута (вынесены persistence/run-state/status-transition/LAN run-coordinator/run-execution).
- Замена persistence/API слоя потребует массового рефакторинга из-за сильной связности и отсутствия портов/адаптеров.

---

## 2) Транзакционная надежность (Transactional Integrity)

### Наблюдения

- Введён слой хранения через `IOrdersRepository` с режимами `FileSystem` и `LanPostgreSql` (feature-gate в настройках).
- В модели добавлены version-поля (`OrderData.StorageVersion`, `OrderFileItem.StorageVersion`).
- В `PostgreSqlOrdersRepository` реализован optimistic concurrency: version-check на update/delete и внешний conflict-guard перед save.
- Для первичного переноса истории добавлен one-time marker `history_json_bootstrap_v1` в `storage_meta`.
- В API реализована централизованная lock-координация `run/stop` через `order_run_locks` (`EfCoreLanOrderStore`, endpoints `POST /api/orders/{id}/run|stop`).
- В клиенте `Run/Stop` для `LanPostgreSql` идут через API (`LanOrderRunApiGateway`) и координируются через `LanRunCommandCoordinator`; локальный `OrderRunStateService` оставлен как runtime-state для активной сессии обработки.
- Для совместимости с текущим repository-слоем добавлен snapshot-refresh после server `run/stop`, чтобы исключить ложные `concurrency conflict` на следующем `SaveHistory`.
- Для `run/stop` внедрена API-идемпотентность (`Idempotency-Key` + таблица дедупликации `order_run_idempotency` + fingerprint запроса); для остальных write-команд (`create/update/items/reorder`) это пока следующий шаг.

### Вывод

- Риск `lost update` существенно снижен в LAN-режиме за счёт optimistic concurrency и запрета silent overwrite при конфликте.
- Полной транзакционной модели уровня API/command handling пока нет (идемпотентность закрыта только для `run/stop`, нет полного cutover всех write-flow клиента на server-command boundary).
- Повторные команды `run/stop` уже дедуплицируются на сервере; для остальных write-операций глобальные idempotency key ещё не включены.

---

## 3) Отказоустойчивость и Resiliency

### Наблюдения

- Есть polling-ожидание файлов с timeout (`WaitForFileAsync`, `WaitForFileInAnyAsync`) и отмена по `CancellationToken`.
- В `OrderProcessor` добавлен retry/backoff policy (`FileOperationRetryPolicy`) для file-операций (copy/move/delete/create/read) с логированием попыток и exhausted-событий.
- Добавлен circuit-breaker (`DependencyCircuitBreaker`) и dependency health-state для PitStop/Imposing/Storage с UI-индикацией degraded/unavailable.
- Добавлен bulkhead/load shedding (`DependencyBulkheadPolicy`) и readiness-контур на старте workflow (проверка storage/hotfolder-директорий по активным сценариям до запуска обработки).
- Добавлен stage-timeout budget policy (`WorkflowTimeoutBudgetPolicy`) для PitStop/Imposing/report шагов и telemetry-контур `TIMEOUT-BUDGET`.
- В нескольких местах ошибки suppress-ятся (`catch { }`), что скрывает деградации.
- Архитектура single-process: падение/фриз UI-компонента критично для всего потока выполнения.

### Вывод

- Устойчивость к «дрожащей» инфраструктуре существенно улучшена: есть retry/backoff + circuit-breaker + bulkhead/load shedding + readiness-проверки зависимостей на старте.
- До production-resilience остаются adaptive timeout-tuning по окружениям, event-driven orchestration и снижение single-process blast radius.

---

## 4) Наблюдаемость (Observability & Audit)

### Наблюдения

- Логгер пишет plain-text строки в файл (не JSON), но теперь с scope-based structured полями (`key=value`) и `correlation_id`.
- Логгер в client-runtime переведён на scope-based structured fields (`key=value`) с `correlation_id` через `LogContext`.
- Лог статусов заказа (`AppendOrderStatusLog`) текстовый, best-effort, с глушением ошибок записи.
- В PostgreSQL введён событийный журнал `order_events` (CRUD-события репозитория + `run/stop/delete/topology/add-item/remove-item/status-change` из клиентских workflow-точек).
- Распределенной трассировки нет (и отсутствует распределенная архитектура на текущем этапе).
- `order_events` хранится в БД и снижает риск mutable file-audit; добавлены request correlation id в API и client->API propagation для run/stop, но полноценная end-to-end трассировка и формализованные audit-дашборды пока отсутствуют.

### Вывод

- Для форензики инцидентов ситуация заметно улучшилась (DB event log + correlation в client runtime и API), но observability всё ещё недостаточна для SLA/SRE-уровня.
- Корреляция стала сквозной для ключевого run/stop пути (client->API), но ещё нет стандартизированной end-to-end схемы для всех workflow и аналитических отчётов.

---

## 5) Безопасность (Security by Design)

### Наблюдения

- Бизнес-данные формируются и обрабатываются в thick-client без server-side validation boundary.
- Есть базовая санитаризация имени папки по invalid file chars в `OrderForm`, но отсутствует централизованный validation policy на уровне домена.
- Конфигурация содержит hardcoded сетевые пути по умолчанию (`\\NAS\...`), однако явных паролей/connection string с секретами в коде не найдено.
- Ошибки часто глушатся, что затрудняет выявление атак/аномалий.

### Вывод

- Для enterprise-эксплуатации нужно вводить серверный trust boundary, авторизацию, централизованную валидацию команд и секрет-менеджмент (env/vault), даже если сейчас «секретов в коде» почти нет.

---

## Матрица рисков (Компонент | Риск | Критичность | Рекомендация)

| Компонент | Риск | Критичность | Рекомендация |
|---|---|---|---|
| `MainForm` orchestration | God Object, смешение UI + domain + persistence + file IO (снижено сервисными выносами, включая delete + run/stop + create/edit/item-mutation + item/order-delete + file-path-status + stage-command + rename/remove + print-tiles rename path sync + history lifecycle maintenance + folder-path resolution + storage-version sync + run-feedback orchestration) | **Med** | Продолжить декомпозицию: выделить use-case слой (`IOrderApplicationService`), UI оставить как presenter/view; внедрить DI/composition root. |
| История заказов (`history.json` / LAN PostgreSQL) | В FileSystem-режиме остаётся риск race; в LAN-режиме риск снижен через version-check | **Med** | Оставить FileSystem только как fallback; целевой режим — PostgreSQL + server-side command boundary. |
| `SetOrderStatus` + `SaveHistory` | Клиентская неатомарность между UI-операцией и persistence | **Med/High** | Перенести статусные команды в API/worker с unit of work и server-side invariants. |
| `_runTokensByOrder` (in-memory) | Переведён в runtime-session state; риск смещён в сторону UX-согласованности между клиентами | **Low/Med** | Сохранить server lock/state единственным источником истины и расширять server-driven refresh-сценарии. |
| `OrderProcessor` file workflow | Retry/backoff + circuit-breaker + bulkhead/readiness + stage-timeout budgets внедрены; остаточный риск — polling model и single-process blast radius | **Low/Med** | Следующий шаг: adaptive timeout-tuning + расширение server-side orchestration и queue boundary. |
| Ожидание hotfolder | Circuit-breaker + bulkhead + readiness внедрены; остаточный риск — polling model и latency детекции недоступности | **Low** | Следующий шаг: переход к event/queue-сигналам и proactive dependency telemetry. |
| Логирование (`Logger`) | Client structured scopes + correlation и API request-correlation внедрены; остаточный риск — нет unified tracing/metrics и централизованных dashboard/query стандартов | **Med** | Следующий шаг: единый structured logging schema + tracing/metrics контур (client+api+worker). |
| Order status log file | best-effort append, mutable file (частично компенсировано `order_events`) | **Med** | Сделать `order_events` primary audit source, добавить retention/архив и SQL-аудит отчёты. |
| Ошибки с `catch { }` | В критическом runtime-пути silent catches устранены; остаточный риск остаётся в legacy/UI-участках | **Low/Med** | Поддерживать policy: без silent catch в production-path; остаточные блоки вычищать по итерациям. |
| ConfigService/AppSettings static IO | Сильная связность с файловой системой существенно снижена (`ISettingsProvider` внедрён в runtime-path и manager-forms); остаток в legacy/artifacts | **Low** | Поддерживать provider-boundary как единый путь и постепенно чистить legacy/direct-load участки при касании. |
| API идемпотентность write-команд | Для `run/stop` риск закрыт; остаток — `create/update/items/reorder` без дедупликации | **Med** | Расширить `Idempotency-Key` + dedupe-store на все mutating endpoints. |
| Отсутствие полного authN/authZ контура | Базовая actor validation write-path уже есть, но role/claim policy и полноценная authN не внедрены | **Med/High** | Ввести API authN/authZ (JWT/SSO + role/claim-based authorization). |
| Валидация входных данных | Локальная и фрагментарная | **Med** | Централизовать validation на command DTO/domain rules, добавить schema/contract validation. |
| Хардкод дефолтных путей NAS | Сложность portability/segmentation | **Low/Med** | Вынести в environment-specific config profiles, добавить проверку доступности на старте. |
| Отсутствие распределенной трассировки | Невозможно проследить end-to-end через сервисы | **Med (сейчас), High (после разделения)** | Внедрить OpenTelemetry tracing заранее в новом API/worker-контуре. |

---

## Статус закрытия матрицы рисков (итерации)

Базовые 8 итераций risk-burndown закрыты; дальше идут адресные расширения по остаткам матрицы (без пересмотра факта закрытия этапа 4).

1. Итерация 1 (2026-03-20): закрыт риск silent `catch { }` в критическом runtime-пути.
   - Что сделано: заменены silent catches на контролируемый fallback с логированием в `OrderProcessor`, `OrderForm`, `ConfigService`.
   - Эффект: снижен риск «немых» деградаций при file/workflow и config-операциях.
2. Итерация 2 (2026-03-20): закрыт следующий срез `MainForm` God Object (delete-workflow).
   - Что сделано: удаление заказов и item-ов вынесено в `OrderDeletionWorkflowService` (disk cleanup + fallback + reindex + batch failure aggregation), `MainForm` переключён на сервис, добавлены unit-тесты.
   - Эффект: снижена связность `MainForm` и риск регрессий в delete-сценариях за счёт выделенного use-case слоя и автотестов.
3. Итерация 3 (2026-03-20): закрыт срез `ConfigService/AppSettings` static IO для runtime-path.
   - Что сделано: добавлены `ISettingsProvider` + `FileSettingsProvider`; `Program`, `MainForm`, `OrderProcessor` и `ConfigService` переведены на provider boundary; добавлены unit-тесты `ConfigService` на injected provider path.
   - Эффект: повышена тестируемость и снижена связность core-path с файловой системой/статикой.
4. Итерация 4 (2026-03-20): закрыт срез resiliency в `OrderProcessor` (retry/backoff policy).
   - Что сделано: добавлен `FileOperationRetryPolicy`; file-операции `copy/move/delete/create/read` переведены на policy boundary, добавлен telemetry-контур `FILE-RETRY`, добавлены unit-тесты policy и обновлены UI smoke-тесты cleanup.
   - Эффект: снижен риск фейлов от кратковременных NAS/file-lock сбоев и улучшена диагностируемость file-workflow.
5. Итерация 5 (2026-03-20): закрыт срез dependency health-state + circuit-breaker для hotfolder-интеграций.
   - Что сделано: добавлен `DependencyCircuitBreaker`; операции в `OrderProcessor` обёрнуты dependency-guard (`circuit-open`, `retry-after`), введены сигналы `OnDependencyHealthChanged`, `MainForm` показывает degraded/unavailable состояние в tray и server-header.
   - Эффект: снижена вероятность каскадных ошибок при недоступности внешних hotfolder-зависимостей, улучшена оперативная диагностика через UI-индикаторы.
6. Итерация 6 (2026-03-20): закрыт срез bulkhead/load shedding + dependency readiness policy.
   - Что сделано: добавлен `DependencyBulkheadPolicy` и подключён в dependency boundary `OrderProcessor`; при перегрузе включается load shedding (`bulkhead-reject`) с деградацией health-state. Добавлены readiness-проверки storage/hotfolder-контуров по активным сценариям перед стартом workflow.
   - Эффект: снижен риск каскадного перегруза и «слепых» запусков при недоступных dependency; запуск блокируется до восстановления критичных директорий.
7. Итерация 7 (2026-03-20): закрыт срез timeout budget per stage в `OrderProcessor`.
   - Что сделано: добавлен `WorkflowTimeoutBudgetPolicy`, ожидания `WaitForFile*` и PitStop report переведены с общего timeout на stage budgets (PitStop/Imposing/report), добавлен `TIMEOUT-BUDGET` telemetry и unit-тесты policy.
   - Эффект: уменьшён риск бесконтрольного зависания на одном этапе, улучшена предсказуемость SLA по этапам pipeline.
8. Итерация 8 (2026-03-20): закрыт срез structured logging + correlation в client-runtime.
   - Что сделано: добавлены `LogContext` и scope-based structured fields в `Logger` (`correlation_id`, `workflow`, `order_*`), `Run/Stop` workflow в `MainForm` обёрнут correlation-scope, `LanOrderRunApiGateway` пробрасывает `X-Correlation-Id` в API.
   - Эффект: улучшена сквозная диагностика run/stop цепочек (client <-> API), снижен риск «непрослеживаемых» инцидентов в runtime.
9. Итерация 9 (2026-03-20, адресная): закрыт high-risk срез API идемпотентности для `run/stop`.
   - Что сделано: добавлены `Idempotency-Key` в client gateway и API controller, таблица `order_run_idempotency` + migration `20260320000300_OrderRunIdempotency`, дедупликация и fingerprint-проверка в `EfCoreLanOrderStore`, обновлены unit/integration тесты.
   - Эффект: повторная отправка `run/stop` с одним ключом больше не дублирует side effects; риск повторного запуска/остановки по ретраям снижен.
10. Итерация 10 (2026-03-20, адресная): закрыт следующий срез `MainForm` God Object по `run/stop` orchestration preflight.
   - Что сделано: добавлен `OrderRunWorkflowOrchestrationService`; подготовка `run/stop` цепочки (run-plan, LAN preflight, local cancel, snapshot refresh decisions) вынесена из `MainForm` в сервисный слой; добавлены unit-тесты orchestration.
   - Эффект: уменьшена связность формы и повышена тестируемость критичного run/stop workflow без UI-зависимости.
11. Итерация 11 (2026-03-20, адресная): закрыт rename/decomposition срез UI-shell (`MainForm -> OrdersWorkspaceForm`).
   - Что сделано: точка входа переведена на `OrdersWorkspaceForm`, файлы формы перенесены в `UI/Forms/OrdersWorkspace/*` с модульным делением, оставлен совместимый shim `MainForm` для тестов и плавной миграции.
   - Эффект: снижена архитектурная «тяжесть» legacy-нейминга, упорядочена навигация по UI-коду, подготовлена база для дальнейшей поэтапной декомпозиции формы.
12. Итерация 12 (2026-03-20, адресная): закрыт первый шаг hybrid-layout migration.
   - Что сделано: созданы и задействованы каталоги `Features`, `Infrastructure`, `SharedKernel`, `Legacy`; выполнен перенос `Orders` UI/application/domain кода в `Features/Orders/*`, а `FileSystem/PostgreSql` репозиторных адаптеров — в `Infrastructure/Storage/Orders`; добавлены правила `Legacy` quarantine/exit.
   - Эффект: повышена эргономика разработки по модульным срезам, упорядочены границы слоёв и подготовлен безопасный маршрут к полной ликвидации legacy-контуров.
13. Итерация 13 (2026-03-20, адресная): закрыт остаточный settings static-IO в manager UI и улучшена модульность processing-контура.
   - Что сделано: `ImposingManagerForm` переведён на injected `ISettingsProvider` (без прямого `AppSettings.Load()`), `OrderProcessor` перенесён в `Infrastructure/Processing/Orders` и разложен на partial-файлы по зонам (`OrderProcessor`, `FileWorkflow`, `DependencyResilience`).
   - Эффект: снижена связность UI-форм с static-config IO, улучшена навигация/поддерживаемость processing-кода и подготовлена база для следующего выноса orchestration из формы.
14. Итерация 14 (2026-03-20, адресная): закрыт следующий срез `OrdersWorkspaceForm` God Object по run-start/run-execution orchestration.
   - Что сделано: добавлен `OrderRunCommandService`; цепочка `PrepareStartAsync -> BeginRunSessions -> ExecuteAsync -> CompleteRunSession` вынесена из формы в application-service слой, `RunSelectedOrderAsync` переключён на сервисный вызов, добавлены unit-тесты `OrderRunCommandServiceTests`.
   - Эффект: дополнительно снижена связность формы с run-state runtime-деталями и повышена тестируемость критичного start/execute workflow без UI-зависимостей.
15. Итерация 15 (2026-03-20, адресная): закрыт следующий срез `OrdersWorkspaceForm` God Object по stop/status persistence orchestration.
   - Что сделано: в `OrderRunCommandService` добавлен `ExecuteStopAsync`; stop-цепочка `PrepareStopAsync -> local stop-status apply -> conflict/unconfirmed resolution` вынесена из формы в application-service слой, `StopSelectedOrderAsync` переключён на сервисный вызов, расширены unit-тесты `OrderRunCommandServiceTests` (not-running/local stop/lan unavailable/conflict).
   - Эффект: уменьшена связность формы с stop-state lifecycle и server result branching, повышена тестируемость stop-workflow без UI-зависимости.
16. Итерация 16 (2026-03-20, адресная): закрыт следующий срез `OrdersWorkspaceForm` God Object по create/edit mutation logic.
   - Что сделано: добавлен `OrderEditorMutationService`; мутации `AddCreatedOrder`, `ApplySimpleEdit`, `ApplyExtendedEdit` вынесены в application-service слой, `OrdersWorkspaceForm` переведён на сервисные вызовы, добавлены unit-тесты `OrderEditorMutationServiceTests`.
   - Эффект: уменьшена связность UI-формы с прямой доменной мутацией `OrderData`, улучшена тестируемость create/edit сценариев и подготовлена база для дальнейшего выноса write-flow в единый orchestration сервис.
17. Итерация 17 (2026-03-20, адресная): закрыт следующий срез `OrdersWorkspaceForm` God Object по item mutation/topology orchestration.
   - Что сделано: добавлен `OrderItemMutationService`; цепочки `PrepareAddItem`, `RollbackPreparedItem`, `RemoveItemIfEmpty`, `ApplyTopologyAfterItemMutation` вынесены в application-service слой, `OrdersWorkspaceForm` переведён на сервисные вызовы в add/remove/drag сценариях, добавлены unit-тесты `OrderItemMutationServiceTests`.
   - Эффект: уменьшена связность формы с mutation-инвариантами item-уровня и topology-normalization, повышена тестируемость file/item write-flow без UI-зависимостей.
18. Итерация 18 (2026-03-20, адресная): закрыт следующий срез `OrdersWorkspaceForm` God Object по item-delete command orchestration.
   - Что сделано: добавлен `OrderItemDeleteCommandService`; цепочка `capture affected orders -> delete items batch -> topology post-mutation` вынесена в application-service слой, `RemoveSelectedOrderItems` в `OrdersWorkspaceForm` переключён на сервисный вызов, добавлены unit-тесты `OrderItemDeleteCommandServiceTests`.
   - Эффект: уменьшена связность формы с batch-delete orchestration и post-delete topology-ветвлением, повышена тестируемость удаления item-ов без UI-зависимостей.
19. Итерация 19 (2026-03-20, адресная): закрыт следующий срез `OrdersWorkspaceForm` God Object по order-delete command orchestration.
   - Что сделано: добавлен `OrderDeleteCommandService`; цепочка `delete selected orders -> run-state cleanup -> expanded-state cleanup` вынесена в application-service слой, `RemoveSelectedOrder` в `OrdersWorkspaceForm` переключён на сервисный вызов, добавлены unit-тесты `OrderDeleteCommandServiceTests`.
   - Эффект: уменьшена связность формы с order-level batch-delete и cleanup-побочными эффектами, повышена тестируемость удаления заказов без UI-зависимостей.
20. Итерация 20 (2026-03-20, адресная): закрыт следующий срез `OrdersWorkspaceForm` God Object по file-path/status mutation logic.
   - Что сделано: добавлен `OrderFilePathMutationService`; цепочки `UpdateOrderFilePath`, `UpdateItemFilePath`, `RefreshOrderStatusFromItems` переведены в application-service слой, `OrdersWorkspaceForm` оставлен как UI-точка применения `SetOrderStatus`, добавлены unit-тесты `OrderFilePathMutationServiceTests`.
   - Эффект: уменьшена связность формы с правилами синхронизации order/item метаданных и file-sync статусов, повышена тестируемость file mutation flow без UI-зависимостей.
21. Итерация 21 (2026-03-20, адресная): закрыт следующий срез `OrdersWorkspaceForm` God Object по add-stage command planning.
   - Что сделано: добавлен `OrderFileStageCommandService`; цепочки `AddFileToOrderAsync`/`AddFileToItemAsync` переведены на service-planning (`clean-source validation`, `target-name resolve`, `print/source-copy flags`), форма оставлена как UI/IO-shell для copy-операций и статусов, добавлены unit-тесты `OrderFileStageCommandServiceTests`.
   - Эффект: уменьшена связность формы с правилами подготовки add-flow команд и target-naming, повышена тестируемость stage planning без UI-зависимостей.
22. Итерация 22 (2026-03-20, адресная): закрыт следующий срез `OrdersWorkspaceForm` God Object по rename/remove file command orchestration.
   - Что сделано: добавлен `OrderFileRenameRemoveCommandService`; цепочки `RemoveFileFromOrder`, `RemoveFileFromItem`, `RenameFileForOrder`, `RenameFileForItem` и `TryBuildRenamedPath` переведены на сервисные command-методы (rename-path validation + item/topology post-mutation), форма оставлена как UI-обёртка confirm/errors и `File.Move/Delete`, добавлены unit-тесты `OrderFileRenameRemoveCommandServiceTests`.
   - Эффект: уменьшена связность формы с rename/remove business-ветвлением и post-file mutation-инвариантами, повышена тестируемость file-command flow без UI-зависимостей.
23. Итерация 23 (2026-03-20, адресная): закрыт следующий срез `OrdersWorkspaceForm` God Object по print-tiles rename path sync.
   - Что сделано: `OrderFileRenameRemoveCommandService` расширен методом `ApplyPrintTileFileRenamed`; `RenamePrintTileFile` переключён на сервисное обновление print-path ссылок order/item и status update через `SetOrderStatus`, локальная мутация `UpdatePrintPathReferencesForOrder` удалена; добавлены unit-тесты `OrderFileRenameRemoveCommandServiceTests` (print-rename match/fallback).
   - Эффект: уменьшена связность формы с доменными правилами tile-rename и post-rename sync, повышена тестируемость print-path mutation flow без UI-зависимостей.
24. Итерация 24 (2026-03-20, адресная): закрыт следующий срез `OrdersWorkspaceForm` God Object по history lifecycle maintenance.
   - Что сделано: добавлен `OrdersHistoryMaintenanceService`; логика `LoadHistory/SaveHistory` для post-load/pre-save maintenance (id/arrival normalization, hash/size backfill, topology normalization) переведена в application-service слой; `OrdersWorkspaceForm` оставлен как UI-shell для repository IO и логирования; добавлены unit-тесты `OrdersHistoryMaintenanceServiceTests`.
   - Эффект: уменьшена связность формы с доменными правилами обслуживания истории и file-metadata migration, повышена тестируемость history maintenance без UI-зависимостей.
25. Итерация 25 (2026-03-20, адресная): закрыт следующий срез `OrdersWorkspaceForm` God Object по folder-path resolution logic.
   - Что сделано: добавлен `OrderFolderPathResolutionService`; алгоритмы `TryGetBrowseFolderPathForOrder/GetPreferredOrderFolder` (single/group folder resolve, common-directory для group-order, root-mismatch policy) вынесены в application-service слой, удалены дублирующие path-утилиты из формы, добавлены unit-тесты `OrderFolderPathResolutionServiceTests`.
   - Эффект: уменьшена связность формы с path-policy и directory-normalization логикой, повышена тестируемость folder-resolution сценариев без UI-зависимостей.
26. Итерация 26 (2026-03-20, адресная): закрыт следующий срез `OrdersWorkspaceForm` God Object по storage-version snapshot sync.
   - Что сделано: добавлен `OrderStorageVersionSyncService`; синхронизация `StorageVersion` локальных заказов после repository snapshot-refresh (`run/stop` LAN path) вынесена из `OrdersWorkspaceForm` в application-service слой, форма оставлена как UI-shell для reload/logging, добавлены unit-тесты `OrderStorageVersionSyncServiceTests`.
   - Эффект: уменьшена связность формы с version-merge логикой persistence-контура и повышена тестируемость sync-сценариев без UI-зависимостей.
27. Итерация 27 (2026-03-20, адресная): закрыт следующий срез `OrdersWorkspaceForm` God Object по run-feedback planning.
   - Что сделано: добавлен `OrderRunFeedbackService`; в `RunSelectedOrderAsync` вынесены `server skipped preview`, `skipped details` и `execution errors preview`, локальные дубли `Take(5)/Join` удалены; добавлены unit-тесты `OrderRunFeedbackServiceTests`.
   - Эффект: уменьшена связность формы с текстовым branching run-flow и повышена тестируемость feedback-правил без UI-зависимостей.
28. Итерация 28 (2026-03-20, адресная): закрыт следующий срез DI/composition root для `OrdersWorkspaceForm`.
   - Что сделано: добавлены `OrdersWorkspaceCompositionRoot` и `OrdersWorkspaceRuntimeServices`; конструктор формы переведён на получение runtime-зависимостей через composition root (вместо ручной сборки `new ...` внутри `OrdersWorkspaceForm`), сохранив совместимость текущих entrypoint/тестов.
   - Эффект: снижена ручная связность UI-конструктора и подготовлена безопасная база для следующего этапа — инъекционного `IOrderApplicationService`/use-case orchestration.

---

## Технический долг: что «сжечь и переписать» сейчас

### P0 (делать немедленно)

1. **Persistence-модель на JSON в UI** — `PARTIAL`: LAN PostgreSQL + двусторонняя sync работают, но FileSystem-ветка ещё жива как fallback.
2. **Статусные переходы и аудит «мимо транзакций»** — `PARTIAL`: status policy вынесена, `order_events` есть, но полный server-side command handling для всех write-flow не завершён.
3. **God Object (MainForm/OrdersWorkspaceForm как бизнес-оркестратор)** — `IN PROGRESS`: вынесены history/run-state/status-transition/run-execution/delete-workflow/run-stop-preflight/create-edit/item-mutation/item-delete-command/order-delete-command/file-path-status-mutation/file-stage-command-planning/file-rename-remove-command/print-tiles-rename-sync/history-lifecycle-maintenance/folder-path-resolution/storage-version-sync/run-feedback + выполнен rename shell, модульный перенос UI-кода и старт DI/composition-root bootstrap (`OrdersWorkspaceCompositionRoot`); следующий фокус — общий order workflow orchestration (`IOrderApplicationService`) и поэтапный DI cutover.
4. **Неструктурированное логирование и mutable file-audit** — `PARTIAL`: correlation + structured scopes внедрены, `order_events` работает; остаётся унификация схемы и централизованный observability stack.

### P1 (сразу после P0)

1. Ввести **optimistic concurrency** (`row_version`) и конфликто-разрешение — `DONE` (LAN path).
2. Ввести **idempotency** для write-операций API — `PARTIAL` (`run/stop` закрыты, остальные endpoints в очереди).
3. Добавить **resilience policies** (retry/circuit breaker/timeouts/bulkhead) — `DONE` для file-workflow path.
4. Ввести **health-checks + readiness/liveness + SLO метрики** — `PARTIAL` (health/readiness есть, SLO-метрики и dashboards в работе).

### P2 (масштабирование на сотни пользователей)

1. Разделить pipeline на сервисы/воркеры: `Order API`, `Processing Worker`, `Integration Adapter (PitStop/Imposing)`.
2. Event-driven интеграция (outbox/inbox pattern).
3. Observability platform: logs+metrics+traces, correlation id везде.

---

## Целевая архитектура Replica (рекомендуемая)

- **Client (WinForms/Web)**: только UI и orchestration UX.
- **Replica API (Application Layer)**: команды/запросы, валидация, authZ.
- **Domain Layer**: агрегаты `Order`, `OrderItem`, инварианты и статусная машина.
- **Infrastructure Layer**:
  - PostgreSQL (orders, order_items, order_events, idempotency_keys);
  - message broker/queue для долгих задач;
  - adapters к NAS/PitStop/Imposing.
- **Worker Layer**: выполнение файловых операций и внешних интеграций под контролем retry/circuit-breaker.
- **Observability Layer**: OpenTelemetry + централизованный лог-стек + аудит.

---

## Минимальный roadmap миграции (8–12 недель)

1. **Week 1–2**: выделение contracts/shared domain, API skeleton, миграция модели `Order` + `OrderEvent`.
2. **Week 3–4**: PostgreSQL + EF migrations, optimistic concurrency, идемпотентность create/update.
3. **Week 5–6**: воркер обработки заказов, очередь задач, resilient adapters.
4. **Week 7–8**: structured logging, tracing, audit queries, dashboards.
5. **Week 9+**: cutover client на API, постепенный deprecation локального JSON хранения.

---

## Заключение

Текущий Replica (будущий Replica) хорош как локальный/переходный инструмент, но для enterprise-scale и критичных данных требуется архитектурный pivot: **от UI-центричного file-driven монолита к транзакционному API+worker контуру с наблюдаемостью и строгими границами доверия**.

