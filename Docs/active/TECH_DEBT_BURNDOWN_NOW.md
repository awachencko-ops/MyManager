# РўРµС…РЅРёС‡РµСЃРєРёР№ РґРѕР»Рі: С‡С‚Рѕ В«СЃР¶РµС‡СЊ Рё РїРµСЂРµРїРёСЃР°С‚СЊВ» СЃРµР№С‡Р°СЃ

РђРєС‚СѓР°Р»СЊРЅРѕ РЅР° `2026-03-23`.

## Р¦РµР»СЊ

Р—Р°РєСЂС‹С‚СЊ Р°СЂС…РёС‚РµРєС‚СѓСЂРЅС‹Рµ РґРѕР»РіРё, РєРѕС‚РѕСЂС‹Рµ РїСЂСЏРјРѕ Р±СЊСЋС‚ РїРѕ РЅР°РґРµР¶РЅРѕСЃС‚Рё LAN/PostgreSQL СЂРµР¶РёРјР° Рё РјРµС€Р°СЋС‚ РјР°СЃС€С‚Р°Р±РёСЂРѕРІР°РЅРёСЋ.

## P0 Burn-List (СЃРµР№С‡Р°СЃ)

| РћР±Р»Р°СЃС‚СЊ | РўРµРєСѓС‰РµРµ СЃРѕСЃС‚РѕСЏРЅРёРµ | Р§С‚Рѕ РїРµСЂРµРїРёСЃС‹РІР°РµРј | РљСЂРёС‚РµСЂРёР№ Р·Р°РєСЂС‹С‚РёСЏ |
|---|---|---|---|
| `MainForm` (god-object orchestration) | Shell РїРµСЂРµРёРјРµРЅРѕРІР°РЅ РІ `OrdersWorkspaceForm`, РєРѕРґ `Orders` РїРµСЂРµРЅРµСЃС‘РЅ РІ `Features/Orders/UI/*`, РІРІРµРґС‘РЅ РµРґРёРЅС‹Р№ `IOrderApplicationService` (РІРєР»СЋС‡Р°СЏ run/create/edit/delete/item/file + history/folder orchestration) | Р”РѕР¶Р°С‚СЊ presenter-only СЃР»РѕР№: СѓР±СЂР°С‚СЊ РѕСЃС‚Р°С‚РѕС‡РЅС‹Рµ orchestration РІРµС‚РєРё РёР· UI Рё Р·Р°РІРµСЂС€РёС‚СЊ DI cutover | `OrdersWorkspaceForm` РЅРµ СѓРїСЂР°РІР»СЏРµС‚ Р±РёР·РЅРµСЃ-С†РёРєР»Р°РјРё РЅР°РїСЂСЏРјСѓСЋ, С‚РѕР»СЊРєРѕ UI/presenter |
| Write-command boundary | `DONE` РґР»СЏ С†РµР»РµРІРѕРіРѕ LAN scope: `create/update/items/reorder/status` + `item delete` + `order delete` РёРґСѓС‚ С‡РµСЂРµР· API boundary (РІРєР»СЋС‡Р°СЏ remove/move item-СЃС†РµРЅР°СЂРёРё Рё batch order delete) | РџРѕРґРґРµСЂР¶РёРІР°С‚СЊ РµРґРёРЅС‹Р№ API-first РїСѓС‚СЊ Рё СѓР±РёСЂР°С‚СЊ legacy fallback РІРµС‚РєРё | Р’СЃРµ mutating РѕРїРµСЂР°С†РёРё РёРґСѓС‚ С‡РµСЂРµР· API-РєРѕРјР°РЅРґС‹ Рё server invariants |
| JSON/file РєР°Рє СЂР°Р±РѕС‡РµРµ С…СЂР°РЅРёР»РёС‰Рµ | LAN sync СЂР°Р±РѕС‚Р°РµС‚, РЅРѕ file fallback РІР»РёСЏРµС‚ РЅР° РїРѕРІРµРґРµРЅРёРµ | Р—Р°С„РёРєСЃРёСЂРѕРІР°С‚СЊ PostgreSQL РєР°Рє primary source of truth, file РѕСЃС‚Р°РІРёС‚СЊ С‚РѕР»СЊРєРѕ import/export fallback | Runtime РІ LAN СЂРµР¶РёРјРµ РЅРµ Р·Р°РІРёСЃРёС‚ РѕС‚ `history.json` РґР»СЏ Р°РєС‚СѓР°Р»СЊРЅРѕРіРѕ СЃРѕСЃС‚РѕСЏРЅРёСЏ |
| Audit/observability | Р•СЃС‚СЊ `order_events` + correlation, РЅРѕ РЅРµС‚ РµРґРёРЅРѕР№ СЃС…РµРјС‹ Рё РјРµС‚СЂРёРє | Р’РІРµСЃС‚Рё РµРґРёРЅС‹Р№ structured schema + РјРµС‚СЂРёРєРё/РґР°С€Р±РѕСЂРґС‹ | РРЅС†РёРґРµРЅС‚ РјРѕР¶РЅРѕ РѕС‚СЃР»РµРґРёС‚СЊ end-to-end РїРѕ `correlation_id` + РµСЃС‚СЊ Р±Р°Р·РѕРІС‹Рµ SLO РіСЂР°С„РёРєРё |

## P1 (СЃР»РµРґРѕРј Р·Р° P0)

| РћР±Р»Р°СЃС‚СЊ | РЎС‚Р°С‚СѓСЃ |
|---|---|
| Optimistic concurrency | `DONE` (LAN path) |
| Idempotency write API | `DONE` (`Idempotency-Key` РЅР° `create/update/items(add/update/delete/reorder)/run/stop`, РµРґРёРЅС‹Р№ dedupe-store `order_write_idempotency` + fingerprint validation) |
| Resilience (retry/circuit/bulkhead/timeout) | `DONE` РґР»СЏ file-workflow |
| Health/readiness + SLO метрики | `DONE (baseline)` |

## РЎР»РµРґСѓСЋС‰РёРµ 3 РёС‚РµСЂР°С†РёРё

1. Поддерживать observability baseline (`/live`, `/ready`, `/metrics`, `/slo`) и развивать dashboards/alerts.
2. Р”РѕР¶Р°С‚СЊ presenter-only СЃР»РѕР№ РґР»СЏ `OrdersWorkspaceForm` (СѓР±СЂР°С‚СЊ РѕСЃС‚Р°С‚РѕС‡РЅС‹Рµ orchestration-РІРµС‚РєРё РёР· UI).
3. Р—Р°С„РёРєСЃРёСЂРѕРІР°С‚СЊ PostgreSQL РєР°Рє primary source-of-truth РІ runtime (РјРёРЅРёРјРёР·РёСЂРѕРІР°С‚СЊ РІР»РёСЏРЅРёРµ file fallback).

## РџСЂР°РІРёР»Рѕ Р·Р°РІРµСЂС€РµРЅРёСЏ Р±Р»РѕРєР° В«СЃР¶РµС‡СЊ Рё РїРµСЂРµРїРёСЃР°С‚СЊВ»

Р‘Р»РѕРє СЃС‡РёС‚Р°РµС‚СЃСЏ Р·Р°РєСЂС‹С‚С‹Рј, РєРѕРіРґР° РІСЃРµ РїСѓРЅРєС‚С‹ P0 РёРјРµСЋС‚ СЃС‚Р°С‚СѓСЃ `DONE`, Р° `MainForm` РѕСЃС‚Р°РµС‚СЃСЏ С‚РѕР»СЊРєРѕ UI-СЃР»РѕРµРј Р±РµР· РїСЂСЏРјРѕР№ Р±РёР·РЅРµСЃ-РѕСЂРєРµСЃС‚СЂР°С†РёРё Рё persistence-СЂРµС€РµРЅРёР№.

## Legacy policy

`Legacy/` СЂР°Р±РѕС‚Р°РµС‚ РєР°Рє РІСЂРµРјРµРЅРЅС‹Р№ quarantine. Р’С…РѕРґ/РІС‹С…РѕРґ Рё СѓСЃР»РѕРІРёСЏ СѓРґР°Р»РµРЅРёСЏ РѕРїРёСЃР°РЅС‹ РІ [Legacy/README.md](/C:/Users/user/Desktop/MyManager%201.0.1/Legacy/README.md).

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
  - Validation:
    - `dotnet build Replica.csproj -c Debug` passed.
    - `dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj -c Release --filter \"OrderStatusTransitionServiceTests\"` passed (5/5).
    - `dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj -c Release --filter \"OrderRunFeedbackServiceTests|OrderApplicationServiceTests\"` passed (20/20).

