# Replica: Master Doc Map (Single Source of Truth)

Дата актуализации: 2026-03-26  
Статус: `Active`

## 1. Точка входа

Открывать в таком порядке:

1. `Docs/00_REPLICA_MASTER_DOC_MAP_2026-03-26.md`
2. `Docs/active/MEDIATOR_PERFORMANCE_DEEP_BRIEF_2026-03-26.md`
3. `Docs/ready/TECH_DEBT_BURNDOWN_NOW.md`
4. `Docs/active/ARCHITECTURE_AUDIT_Replica.md`

## 2. Сводный статус этапов

| Блок | Статус | Главный документ |
|---|---|---|
| Stage 1 (MainForm migration + regression) | Closed | `Docs/ready/1_MAINFORM_MIGRATION_COMPLEX_RESEARCH_AND_PLAN.md` |
| Stage 2 (group-order + PostgreSQL) | Closed | `Docs/ready/2_STAGE2_CONSOLIDATED_AUDIT_AND_GO_NO_GO.md` |
| Stage 3-4 (LAN client-server + EF/API + auto-update) | Closed | `Docs/ready/3_STAGE3_STAGE4_CONSOLIDATED_GO_NO_GO_2026-03-20.md` |
| Stage 5 (security/auth cutover) | Completed | `Docs/ready/5_STAGE5_SECURITY_AUTH_AND_CUTOVER_BRIEF_2026-03-26.md` |
| SLO/Operations | Completed | `Docs/ready/OPERATIONS_SLO_RUNBOOK.md` |
| Performance + Mediator | In progress | `Docs/active/MEDIATOR_PERFORMANCE_DEEP_BRIEF_2026-03-26.md` |
| Tech debt burn | Completed | `Docs/ready/TECH_DEBT_BURNDOWN_NOW.md` |
| Installer/packaging | Planned | `Docs/active/INSTALLER_AND_DEPENDENCIES_PACKAGING_PLAN.md` |

| New Architecture roadmap (Service-First / Push-Pull / MediatR) | In progress | `Docs/НОВАЯ АРХИТЕКТУРА/REPLICA_SERVICE_FIRST_ROADMAP_2026-03-26.md` |

## 3. Структура папок

### 3.1 `Docs/active` (текущее управление работой)

1. `ARCHITECTURE_AUDIT_Replica.md`
2. `MEDIATOR_PERFORMANCE_DEEP_BRIEF_2026-03-26.md`
3. `connection-sync-audit-2026-03-23.md`
4. `connection-sync-audit-2026-03-23.md`
5. `INSTALLER_AND_DEPENDENCIES_PACKAGING_PLAN.md`

### 3.2 `Docs/ready` (закрытые этапы и итоговые runbook)

1. `1_MAINFORM_MIGRATION_COMPLEX_RESEARCH_AND_PLAN.md`
2. `1_SINGLE_ORDER_REGRESSION_CHECKLIST.md`
3. `2_MULTI_ORDER_LOGIC_AND_POSTGRESQL_PLAN.md`
4. `2_STAGE2_CONSOLIDATED_AUDIT_AND_GO_NO_GO.md`
5. `3_LAN_CLIENT_SERVER_BRIEF_STEP1.md`
6. `3_STAGE3_STAGE4_CONSOLIDATED_GO_NO_GO_2026-03-20.md`
7. `4_EF_MIGRATIONS_API_AND_AUTOUPDATE_ROLLOUT_PLAN.md`
8. `4_STAGE4_RELEASE_RUNBOOK.md`
9. `5_STAGE5_SECURITY_AUTH_AND_CUTOVER_BRIEF_2026-03-26.md`
10. `5_STRICT_AUTH_CUTOVER_CHECKLIST_2026-03-26.md`
11. `OPERATIONS_SLO_RUNBOOK.md`
12. `TECH_DEBT_BURNDOWN_NOW.md`

### 3.3 `Docs/НОВАЯ АРХИТЕКТУРА` (новый целевой план)

1. `REPLICA_SERVICE_FIRST_ROADMAP_2026-03-26.md`

### 3.4 `Docs/archive` (исторические/снятые с активного контура)

1. `CATALOGIZATION_MEMO.md`
2. `UNIVERSAL_LAN_GROUPORDER_IMPLEMENTATION_BRIEF.md`

## 4. Текущий рабочий фокус

1. P0 performance (убрать sync-over-async и блокировки UI).
2. Снижение стоимости `RebuildOrdersGrid` (debounce + частичное обновление).
3. После стабилизации latency: mediator rollout по use-case срезам.

Источник плана: `Docs/active/MEDIATOR_PERFORMANCE_DEEP_BRIEF_2026-03-26.md`.

## 5. Правило ведения документации

1. Новый документ сначала регистрируется в этой карте.
2. У каждого документа обязателен явный `Статус` (`Planned/In progress/Completed/Deprecated`).
3. На каждый этап один consolidated-файл в `Docs/ready`, остальное только как приложения/заметки.
