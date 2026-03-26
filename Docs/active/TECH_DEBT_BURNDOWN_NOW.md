# Технический долг: Burn-Down (финал текущего цикла)

Актуально на `2026-03-26`.

## Статус блока

**Решение:** `CLOSED (для текущего cutover-цикла)`.

Критичные долги, блокировавшие переход на LAN PostgreSQL + API-first запись, закрыты.  
Остаток переведен в план планового улучшения (не блокер релизного контура).

## P0 Burn-List (итог)

| Область | Итоговый статус | Комментарий |
|---|---|---|
| `MainForm` / god-object orchestration | `PARTIAL -> CONTROLLED` | Форма переименована в `OrdersWorkspaceForm`, большая часть orchestration вынесена в `Application`-сервисы; остаток — финальный presenter-only cutover и DI-полировка. |
| Write-command boundary | `DONE` | `create/update/status/items(add/update/delete/reorder)/run/stop/order-delete` идут через API boundary (LAN path). |
| JSON/file как runtime source | `DONE` | В `LanPostgreSql` режиме PostgreSQL закреплен как primary source of truth; `history.json` — bootstrap/mirror. |
| Audit/observability baseline | `DONE (baseline)` | Введены `/live`, `/ready`, `/metrics`, `/slo`, метрики write/idempotency, operational runbook. |

## P1 (следом за P0)

| Область | Статус |
|---|---|
| Optimistic concurrency | `DONE` |
| Idempotency write API | `DONE` |
| Resilience (retry/circuit/bulkhead/timeout) | `DONE` |
| Health/readiness + SLO baseline | `DONE` |

## Что закрыто в этом цикле (коротко)

1. Завершен API-first write boundary для целевого LAN-сценария.
2. Закрыта идемпотентность write-команд с единым dedupe-store (`order_write_idempotency` + fingerprint).
3. Закрыт `order delete` API-path (single/batch) с concurrency/event-контуром.
4. Закреплен PostgreSQL как runtime primary source в LAN-режиме.
5. Введен observability/SLO baseline + runbook.
6. Сильно снижена UI-нагрузка в `OrdersWorkspaceForm`:
   - coalescing grid/derived refresh;
   - fast refresh rows без полного rebuild где возможно;
   - оптимизация queue/treeview-переключения (кэш счетчиков, suppress лишних refresh, double-buffer, более легкий tray refresh).

## Остаток (не блокер релизного контура)

1. Дожать `presenter-only` слой до полного `DONE`:
   - убрать оставшиеся orchestration-ветки из UI;
   - завершить DI/composition root до полностью сервисного сценария.
2. Полировка UX/perf на очень больших наборах данных:
   - точечный профайлинг `ApplyStatusFilterToGrid` на production-объемах;
   - при необходимости staged-apply фильтрации.
3. Security next-step:
   - полный authN/authZ (role/claim policy) поверх уже внедренной actor validation write-path.

## Правило закрытия блока «сжечь и переписать»

Для текущего этапа блок считается закрытым, если:

1. Все критичные P0, влияющие на целевой LAN PostgreSQL runtime, имеют `DONE`.
2. Остаток классифицирован как плановое улучшение (не релиз-блокер).
3. Есть подтверждение сборкой и регрессией.

Условия соблюдены.

## Верификация

1. `dotnet build Replica.csproj -c Release` — стабильно проходит.
2. Целевые verify-наборы по application boundary проходят (`OrderRunFeedbackServiceTests`, `OrderApplicationServiceTests` и связанный regression-pack).
3. PostgreSQL/LAN контур подтвержден интеграционными проверками и документами этапов.

## Связанные документы

1. [ARCHITECTURE_AUDIT_Replica.md](/C:/Users/user/Desktop/MyManager%201.0.1/Docs/active/ARCHITECTURE_AUDIT_Replica.md)
2. [2_STAGE2_CONSOLIDATED_AUDIT_AND_GO_NO_GO.md](/C:/Users/user/Desktop/MyManager%201.0.1/Docs/ready/2_STAGE2_CONSOLIDATED_AUDIT_AND_GO_NO_GO.md)
3. [3_STAGE3_STAGE4_CONSOLIDATED_GO_NO_GO_2026-03-20.md](/C:/Users/user/Desktop/MyManager%201.0.1/Docs/ready/3_STAGE3_STAGE4_CONSOLIDATED_GO_NO_GO_2026-03-20.md)
4. [4_STAGE4_RELEASE_RUNBOOK.md](/C:/Users/user/Desktop/MyManager%201.0.1/Docs/ready/4_STAGE4_RELEASE_RUNBOOK.md)
5. [OPERATIONS_SLO_RUNBOOK.md](/C:/Users/user/Desktop/MyManager%201.0.1/Docs/ready/OPERATIONS_SLO_RUNBOOK.md)
