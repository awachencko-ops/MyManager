# Технический долг: что «сжечь и переписать» сейчас

Актуально на `2026-03-23`.

## Цель

Закрыть архитектурные долги, которые прямо бьют по надежности LAN/PostgreSQL режима и мешают масштабированию.

## P0 Burn-List (сейчас)

| Область | Текущее состояние | Что переписываем | Критерий закрытия |
|---|---|---|---|
| `MainForm` (god-object orchestration) | Shell переименован в `OrdersWorkspaceForm`, код `Orders` перенесён в `Features/Orders/UI/*`, введён единый `IOrderApplicationService` (включая run/create/edit/delete/item/file + history/folder orchestration) | Дожать presenter-only слой: убрать остаточные orchestration ветки из UI и завершить DI cutover | `OrdersWorkspaceForm` не управляет бизнес-циклами напрямую, только UI/presenter |
| Write-command boundary | `DONE` для целевого LAN scope: `create/update/items/reorder/status` + `item delete` + `order delete` идут через API boundary (включая remove/move item-сценарии и batch order delete) | Поддерживать единый API-first путь и убирать legacy fallback ветки | Все mutating операции идут через API-команды и server invariants |
| JSON/file как рабочее хранилище | LAN sync работает, но file fallback влияет на поведение | Зафиксировать PostgreSQL как primary source of truth, file оставить только import/export fallback | Runtime в LAN режиме не зависит от `history.json` для актуального состояния |
| Audit/observability | Есть `order_events` + correlation, но нет единой схемы и метрик | Ввести единый structured schema + метрики/дашборды | Инцидент можно отследить end-to-end по `correlation_id` + есть базовые SLO графики |

## P1 (следом за P0)

| Область | Статус |
|---|---|
| Optimistic concurrency | `DONE` (LAN path) |
| Idempotency write API | `DONE` (`Idempotency-Key` на `create/update/items(add/update/delete/reorder)/run/stop`, единый dedupe-store `order_write_idempotency` + fingerprint validation) |
| Resilience (retry/circuit/bulkhead/timeout) | `DONE` для file-workflow |
| Health/readiness + SLO ������� | `DONE (baseline)` |

## Следующие 3 итерации

1. ������������ observability baseline (`/live`, `/ready`, `/metrics`, `/slo`) � ��������� dashboards/alerts.
2. Дожать presenter-only слой для `OrdersWorkspaceForm` (убрать остаточные orchestration-ветки из UI).
3. Зафиксировать PostgreSQL как primary source-of-truth в runtime (минимизировать влияние file fallback).

## Правило завершения блока «сжечь и переписать»

Блок считается закрытым, когда все пункты P0 имеют статус `DONE`, а `MainForm` остается только UI-слоем без прямой бизнес-оркестрации и persistence-решений.

## Legacy policy

`Legacy/` работает как временный quarantine. Вход/выход и условия удаления описаны в [Legacy/README.md](/C:/Users/user/Desktop/MyManager%201.0.1/Legacy/README.md).

## Update 2026-03-23

- P0 `Write-command boundary`: moved forward from `run/stop only` to `run/stop + create/update(order-level)` via LAN API gateway/command service.
- In LAN mode, `OrdersWorkspaceForm` now sends create/edit to server API first and refreshes repository snapshot after success, reducing hidden divergence with PostgreSQL.
- API contracts/store implementations now accept and persist `OrderNumber` + `ManagerOrderDate` in update path, so simple order edit no longer depends on local snapshot write for these fields.
- Status persistence in LAN mode moved to API-first path (`SetOrderStatus` -> `TryUpdateOrderViaLanApiAsync`), with local history save kept only as controlled fallback when API update fails.
- Item reorder path is now wired through LAN API (`/api/orders/{id}/items/reorder`) via application service boundary and called after item-delete workflows for affected multi-item orders.
- Item add/update path is now wired through LAN API (`POST /api/orders/{id}/items`, `PATCH /api/orders/{id}/items/{itemId}`) via `TryUpsertOrderItemViaLanApiAsync`; `OrdersWorkspaceForm` applies server item/order versions after response to reduce optimistic concurrency drift in file-item workflows.
- `StageFileOps` now triggers LAN item upsert sync on `add item file` and `rename item file`; gateway/command-service tests were expanded for add/update item flows and pass in full regression runs.
- Item delete path is now wired through LAN API (`DELETE /api/orders/{id}/items/{itemId}`) via `TryDeleteOrderItemViaLanApiAsync`; in LAN mode `RemoveSelectedOrderItems` switched to API-first item delete, and remove/move branches (`RemoveFileFromItem`, drag-move source clear) now sync delete/upsert through LAN API.
- Added server-side `TryDeleteItem` implementations (EF Core/PostgreSQL/InMemory) with optimistic concurrency + sequence reindex + `delete-item` event, plus test coverage in gateway/command-service and PostgreSQL integration pack.
- Full write idempotency is now closed: `OrdersController` resolves `Idempotency-Key` for all mutating endpoints, `EfCoreLanOrderStore` applies a single dedupe pipeline with request fingerprint, and migration `20260323000100_OrderWriteIdempotency` introduces unified store `order_write_idempotency` (including backfill from legacy `order_run_idempotency`).
- `LanOrderWriteApiGateway` now sends `Idempotency-Key` for create/update/items/reorder requests; verify-tests and PostgreSQL integration pack were expanded (including end-to-end idempotency regression for create/update/add/update/delete/reorder + mismatch check).
- `order delete` API-path is now closed end-to-end: added `DELETE /api/orders/{id}` + `DeleteOrderRequest` + store command `TryDeleteOrder` (EF Core/PostgreSQL/InMemory) with optimistic concurrency and `delete-order` event; client boundary expanded with `DeleteOrderAsync/TryDeleteOrderAsync/TryDeleteOrderViaLanApiAsync`, and `OrdersWorkspaceForm` in LAN mode switched to API-first order delete (single/batch) with local disk cleanup + snapshot refresh.

- Observability/SLO baseline is now closed in API runtime: added request metrics aggregator (`ReplicaApiObservability`), write-command outcome counters in `OrdersController`, idempotency hit/miss/mismatch telemetry in `EfCoreLanOrderStore`, and operational endpoints `/live`, `/ready`, `/metrics`, `/slo`; covered by new verify tests (`ReplicaApiObservabilityTests`) and full regression runs.
- Operational runbook for on-call/owners: [OPERATIONS_SLO_RUNBOOK.md](ready/OPERATIONS_SLO_RUNBOOK.md).

## Update 2026-03-26

- P0 `JSON/file as runtime source in LAN mode`: moved from `PARTIAL` to `DONE` for runtime behavior.
  - `OrdersHistoryRepositoryCoordinator` no longer silently falls back to `history.json` when PostgreSQL load/save fails in `LanPostgreSql` mode.
  - LAN mode now keeps PostgreSQL as primary source of truth, while `history.json` is used only for bootstrap and best-effort mirror snapshot.
- Runtime sync hardening:
  - `OrderStorageVersionSyncService` now synchronizes item-level versions (`OrderFileItem.StorageVersion`) by `orderInternalId + itemId`, not only order-level version.
- Config integrity:
  - `Replica.Api` now reads bind settings from configuration (`ReplicaApi:BindAddress`, `ReplicaApi:Port`) with validation and safe fallback.
- Local recovery hardening:
  - `OrdersWorkspaceForm` local API recovery now has additional fallback launch via `dotnet run --project Replica.Api.csproj` when `.exe/.dll` candidates are absent.
- Verification:
  - `dotnet build Replica.Api/Replica.Api.csproj -c Release` succeeded.
  - Targeted tests passed:
    - `OrderStorageVersionSyncServiceTests`
    - `ReplicaApiLaunchLocatorTests`
    - `PostgreSqlIntegration_Coordinator_UsesLanAsPrimaryAndMirrorsToFile`.

- UI performance (Orders grid) hardening in `OrdersWorkspaceForm`:
  - Added fast row refresh path (`TryRefreshGridRowsWithoutRebuild`) that updates existing `order|...` / `item|...` rows in-place and falls back to full `RebuildOrdersGrid()` only on topology mismatch.
  - Wired fast-path into hot status transitions (`SetOrderStatusCore`) and run workflow batch points where previously full grid rebuild was forced.
  - `PersistGridChanges` now tries fast refresh first; full rebuild remains as safety fallback for structural mutations.
  - Verified by `dotnet build Replica.csproj -c Debug` (0 errors, 0 warnings).
  - Added short coalescing window for noisy processor status updates:
    - `RequestCoalescedGridRefresh` + `GridRefreshCoalesceTimer` batches frequent `SetOrderStatus(..., source=processor, rebuildGrid=true)` into one deferred grid refresh.
    - Non-processor status sources keep immediate behavior (fast refresh/fallback rebuild).
    - Timer cleanup/reset is wired into `MainForm_FormClosed` to avoid stale UI callbacks.
  - Added coalescing for derived grid UI refresh (`HandleOrdersGridChanged`):
    - Heavy post-grid cascade (`ApplyStatusFilterToGrid`, filter captions/checklists, queue presentation, print tiles, tray indicators) now runs through `RequestCoalescedGridDerivedRefresh` + `GridDerivedRefreshCoalesceTimer`.
    - This reduces redundant repeated recomputations during bursts of row/cell/status updates.
    - Cleanup/reset is wired into `MainForm_FormClosed`.
  - Run/Stop orchestration decomposition (application feedback layer):
    - Added typed feedback models in `OrderRunFeedbackService` (`OrderRunStartUiFeedback`, `OrderRunStopUiFeedback`, dialog/log entries, severity).
    - Moved phase interpretation (fatal/no-runnable/server-rejected for run-start, and stop outcomes including conflict/unavailable/server-failure logs) out of `OrdersWorkspaceForm` into application service methods.
    - `OrdersWorkspaceForm` now applies prepared feedback (status bar text, optional dialog, warning/info logs) instead of branching on phase internals.
    - Added post-run feedback models (`OrderRunStartProgressUiFeedback`, `OrderRunCompletionUiFeedback`) and moved start-progress/completion summaries (skipped reasons, server-skipped dialog, error summary, batch completion status) into `OrderRunFeedbackService`.
    - `RunSelectedOrderAsync` now delegates these decisions to application feedback methods and only renders the returned UI feedback.
    - Added lifecycle feedback model (`OrderRunLifecycleUiFeedback`) for run/stop command logs (`command-start`, `stop-command-start`, `snapshot-refresh-failed`, `command-finish`) and switched `OrdersWorkspaceForm` to consume these logs from `IOrderApplicationService` instead of hardcoded `Logger.Info/Warn` branches.
    - Moved run/stop precondition UI feedback (`no selection`) into `OrderRunFeedbackService` (`BuildRunSelectionRequiredUiFeedback`, `BuildStopSelectionRequiredUiFeedback`), so `OrdersWorkspaceForm` only renders returned feedback.
    - Moved run/stop status mutation decisions into typed plans (`OrderRunStartUiMutation`, `OrderRunStatusUiMutation`, `OrderRunStopLocalUiMutation`):
      - `OrdersWorkspaceForm` no longer hardcodes `Processing/Cancelled/Error` transition reasons for run callbacks (`onCancelled/onFailed`) and local stop apply callback.
      - Form now applies prepared status mutation plans from `IOrderApplicationService` (`BuildRunStartUiMutation`, `BuildRunCancelledUiMutation`, `BuildRunFailedUiMutation`, `BuildRunStopLocalUiMutation`).
    - Introduced typed side-effects plan (`OrderRunUiEffectsPlan`) for post-run/post-stop UI actions (`tray/save-history/grid-refresh/action-buttons`):
      - Run flow now applies prepared plans (`BuildRunPostStatusApplyUiEffectsPlan`, `BuildRunPerOrderCompletionUiEffectsPlan`, `BuildRunPostExecutionUiEffectsPlan`) instead of hardcoded side-effect branches.
      - Stop flow now applies `BuildStopPostPhaseUiEffectsPlan(stopPhase, stopUiFeedback)` for tray/buttons updates.
    - Consolidated run/stop feedback rendering in `OrdersWorkspaceForm` via dedicated render-pipeline methods (`RenderRunStartUiFeedback`, `RenderRunStartProgressUiFeedback`, `RenderRunCompletionUiFeedback`, `RenderRunStopUiFeedback`) to remove repeated `SetBottomStatus/ShowDialog/ApplyLogs` branches from workflow methods.
    - UI freeze mitigation for tray/startup maintenance:
      - Archive index scan (`Directory.EnumerateFiles` over `������`) moved off UI thread; refresh now builds name/hash indexes asynchronously and applies result back on UI.
      - Added archive sync cadence guard (`RefreshArchivedStatusesIfDue`) and switched tray timer to run archive sync by interval instead of every tick.
      - Removed per-tick hash backfill/save from tray timer hot path; startup archive refresh is now queued after UI handle is ready.
    - Async orders-data bootstrap on form startup:
      - `InitializeOrdersDataFlow` now queues background load/post-load migration (`TryLoadHistory` + `ApplyHistoryPostLoad`) and applies prepared result to UI after completion.
      - Initial grid rebuild now runs after background load completes, improving first-paint responsiveness.
      - Settings changes that affect data source/path (`orders root`, `history file`, storage backend, LAN connection) now trigger forced async orders reload instead of synchronous refresh path.
  - Validation:
    - `dotnet build Replica.csproj -c Release` passed.
    - `dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj -c Release --filter \"OrderStatusTransitionServiceTests\"` passed (5/5).
    - `dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj -c Release --filter \"OrderRunFeedbackServiceTests|OrderApplicationServiceTests\"` passed (35/35).
  - Queue/treeview freeze mitigation:
    - Introduced queue-status counter cache (`_queueStatusCountsCache`) so `treeView1` owner-draw no longer rescans all grid rows for every node repaint.
    - Cache is invalidated on grid-change pipeline (`HandleOrdersGridChanged`) and rebuilt lazily once per refresh cycle.
    - Derived refresh no longer rebuilds print tiles in list mode; `RefreshPrintTilesFromVisibleRows` now runs only when tiles mode is active.
    - Added no-op guards for queue selection handlers: re-selecting already active status in `treeView1/cbQueue` no longer triggers full derived refresh.
    - Enabled double buffering for `treeView1` and reduced filter pass redraw pressure (`row.Visible` updates only on actual value change + batched `SuspendLayout/ResumeLayout`).
    - Queue switch path now suppresses redundant queue repaint in the next derived refresh cycle (`_suppressNextQueuePresentationRefresh`), reducing visible flicker during `treeView1` selection change.
    - Derived refresh now updates status/user filter checklists only when corresponding popup is open (`_statusFilterDropDown.Visible`, `_userFilterDropDown.Visible`), removing extra list rebuilds on every queue click.
    - Added lightweight tray refresh path for grid/filter changes (`RefreshTrayIndicatorsForGridChange`) to avoid unnecessary LAN/disk probe updates on each queue switch.
    - `ApplyStatusFilterToGrid` fast-paths optional predicates (status/user/orderNo/date) and caches visible-orders count for tray stats, reducing per-row work during queue transitions.
    - Re-validated by `dotnet build Replica.csproj -c Release` and targeted verify tests (`35/35`).

