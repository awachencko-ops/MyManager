<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.
# Replica: Master Doc Map (Single Source of Truth)

Дата актуализации: 2026-03-30  
Статус: `Active`

## 1. Точка входа

Открывать в таком порядке:

1. `Docs/00_REPLICA_MASTER_DOC_MAP_2026-03-26.md`
2. `Docs/REPLICA_DESIGN_SYSTEM_2026-03-27.md`
3. `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_SERVICE_FIRST_ROADMAP_2026-03-26.md`
4. `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE4_DUAL_WRITE_CHECKLIST_2026-03-27.md` (исторический handoff checklist Stage 4)
5. `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE4_DUAL_WRITE_PROGRESS_2026-03-27.md` (исторический execution progress Stage 4)
6. `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE4_EXECUTION_JOURNAL_2026-03-30.md` (исторический execution journal Stage 4)
7. `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE4_RECONCILIATION_RUNBOOK_2026-03-30.md` (архивный runbook Stage 4)
8. `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE5_CLEAN_ARCH_PROGRESS_2026-03-30.md` (исторический execution progress Stage 5)
9. `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE6_CUTOVER_PROGRESS_2026-03-30.md` (исторический execution progress Stage 6)
10. `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE6_CUTOVER_CLOSURE_CHECKLIST_2026-03-30.md` (финальный go/no-go checklist Stage 6)

## 2. Сводный статус

| Блок | Статус | Главный документ |
|---|---|---|
| New Architecture roadmap (Service-First / Push-Pull / MediatR) | Archived (Stage 1-6 closed) | `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_SERVICE_FIRST_ROADMAP_2026-03-26.md` |
| Stage 4 Dual-Write handoff checklist | Done (historical) | `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE4_DUAL_WRITE_CHECKLIST_2026-03-27.md` |
| Stage 4 Dual-Write execution progress | Done (historical) | `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE4_DUAL_WRITE_PROGRESS_2026-03-27.md` |
| Stage 4 execution journal | Archived (historical record) | `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE4_EXECUTION_JOURNAL_2026-03-30.md` |
| Stage 4 reconciliation runbook | Archived (reference) | `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE4_RECONCILIATION_RUNBOOK_2026-03-30.md` |
| Stage 5 Clean Architecture progress | Done (historical) | `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE5_CLEAN_ARCH_PROGRESS_2026-03-30.md` |
| Stage 6 Cutover progress | Done (historical) | `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE6_CUTOVER_PROGRESS_2026-03-30.md` |
| Stage 6 Closure checklist | Signed-off (Go, historical) | `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE6_CUTOVER_CLOSURE_CHECKLIST_2026-03-30.md` |
| UI Design System | Active | `Docs/REPLICA_DESIGN_SYSTEM_2026-03-27.md` |
| Legacy docs set (active + ready, до пересборки плана) | Archived | `Docs/archive/2026-03-26_pre_new_architecture/` |

## 3. Структура папок

### 3.1 `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА` (закрытый bundle Stage 1-6)

1. `REPLICA_SERVICE_FIRST_ROADMAP_2026-03-26.md`
2. `REPLICA_STAGE1_SECURITY_PROGRESS_2026-03-27.md`
3. `REPLICA_STAGE2_COMMAND_BUS_PROGRESS_2026-03-27.md`
4. `REPLICA_STAGE3_SIGNALR_PROGRESS_2026-03-27.md`
5. `REPLICA_STAGE4_DUAL_WRITE_CHECKLIST_2026-03-27.md`
6. `REPLICA_STAGE4_DUAL_WRITE_PROGRESS_2026-03-27.md`
7. `REPLICA_STAGE4_EXECUTION_JOURNAL_2026-03-30.md`
8. `REPLICA_STAGE4_RECONCILIATION_RUNBOOK_2026-03-30.md`
9. `REPLICA_STAGE5_CLEAN_ARCH_PROGRESS_2026-03-30.md`
10. `REPLICA_STAGE6_CUTOVER_PROGRESS_2026-03-30.md`
11. `REPLICA_STAGE6_CUTOVER_CLOSURE_CHECKLIST_2026-03-30.md`

### 3.2 `Docs/archive/2026-03-26_pre_new_architecture` (архив согласованного переноса)

1. `active/*` — ранее рабочие документы (аудиты/планы)
2. `ready/*` — ранее закрытые этапы/runbook

### 3.3 `Docs/archive` (исторические архивы)

1. `CATALOGIZATION_MEMO.md`
2. `UNIVERSAL_LAN_GROUPORDER_IMPLEMENTATION_BRIEF.md`
3. `2026-03-26_pre_new_architecture/*`
4. `2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/*`

## 4. Правило ведения документации

1. Закрытая архитектурная рамка Stage 1-6 хранится в `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА`.
2. Документы прежнего контура не удаляются, а переносятся в `Docs/archive/*`.
3. После закрытия каждого нового этапа допускается публикация consolidated-doc в новом контуре или архивирование в отдельный dated-bundle.
