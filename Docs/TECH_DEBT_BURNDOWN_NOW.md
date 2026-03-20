# Технический долг: что «сжечь и переписать» сейчас

Актуально на `2026-03-20`.

## Цель

Закрыть архитектурные долги, которые прямо бьют по надежности LAN/PostgreSQL режима и мешают масштабированию.

## P0 Burn-List (сейчас)

| Область | Текущее состояние | Что переписываем | Критерий закрытия |
|---|---|---|---|
| `MainForm` (god-object orchestration) | Переименован shell в `OrdersWorkspaceForm`, код перенесён в `UI/Forms/OrdersWorkspace/*`, `run/stop` preflight вынесен; общий orchestration ещё в UI | Довынести order workflow orchestration в application/use-case сервис + DI composition root | `OrdersWorkspaceForm` не управляет бизнес-циклами напрямую, только UI/presenter |
| Write-command boundary | `run/stop` уже server-side + idempotency, остальные write-flow частично клиентские | Перевести `create/update/items/reorder/status` в server command handling | Все mutating операции идут через API-команды и server invariants |
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

1. Вынести order workflow orchestration из `MainForm` в application-service.
2. Расширить `Idempotency-Key` на `create/update/items/reorder`.
3. Довести API boundary для status/update flow и убрать прямые клиентские mutate-path.

## Правило завершения блока «сжечь и переписать»

Блок считается закрытым, когда все пункты P0 имеют статус `DONE`, а `MainForm` остается только UI-слоем без прямой бизнес-оркестрации и persistence-решений.
