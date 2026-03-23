# Технический долг: что «сжечь и переписать» сейчас

Актуально на `2026-03-23`.

## Цель

Закрыть архитектурные долги, которые прямо бьют по надежности LAN/PostgreSQL режима и мешают масштабированию.

## P0 Burn-List (сейчас)

| Область | Текущее состояние | Что переписываем | Критерий закрытия |
|---|---|---|---|
| `MainForm` (god-object orchestration) | Shell переименован в `OrdersWorkspaceForm`, код `Orders` перенесён в `Features/Orders/UI/*`, введён единый `IOrderApplicationService` (включая run/create/edit/delete/item/file + history/folder orchestration) | Дожать presenter-only слой: убрать остаточные orchestration ветки из UI и завершить DI cutover | `OrdersWorkspaceForm` не управляет бизнес-циклами напрямую, только UI/presenter |
| Write-command boundary | `DONE` для целевого LAN scope: `create/update/items/reorder/status` + `item delete` идут через API boundary (включая remove/move item-сценарии) | Расширять на `order delete` API-path и full idempotency всех mutating endpoints | Все mutating операции идут через API-команды и server invariants |
| JSON/file как рабочее хранилище | LAN sync работает, но file fallback влияет на поведение | Зафиксировать PostgreSQL как primary source of truth, file оставить только import/export fallback | Runtime в LAN режиме не зависит от `history.json` для актуального состояния |
| Audit/observability | Есть `order_events` + correlation, но нет единой схемы и метрик | Ввести единый structured schema + метрики/дашборды | Инцидент можно отследить end-to-end по `correlation_id` + есть базовые SLO графики |

## P1 (следом за P0)

| Область | Статус |
|---|---|
| Optimistic concurrency | `DONE` (LAN path) |
| Idempotency write API | `PARTIAL` (`run/stop` готово, остальные endpoints в очереди) |
| Resilience (retry/circuit/bulkhead/timeout) | `DONE` для file-workflow |
| Health/readiness + SLO метрики | `PARTIAL` |

## Следующие 3 итерации

1. Расширить `Idempotency-Key` на все mutating endpoints (не только `run/stop`).
2. Завершить API-path для `order delete` и убрать client-side fallback orchestration из LAN ветки.
3. Финализировать observability baseline (единая схема логов + SLO метрики + dashboard).

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
