# Replica: Master Doc Map (Single Source of Truth)

Дата актуализации: 2026-03-26  
Статус: `Active`

## 1. Как пользоваться этой картой

Если нужно быстро понять состояние проекта, читай документы в таком порядке:

1. Этот файл (`00_REPLICA_MASTER_DOC_MAP_2026-03-26.md`).
2. `MEDIATOR_PERFORMANCE_DEEP_BRIEF_2026-03-26.md` (что делаем сейчас по лагам и декомпозиции).
3. `TECH_DEBT_BURNDOWN_NOW.md` (хвост техдолга и burn-list).
4. `ARCHITECTURE_AUDIT_Replica.md` (полный архитектурный контекст и история решений).

Остальные документы ниже идут как детализация по блокам.

## 2. Текущий сводный статус

| Блок | Статус | Главный документ |
|---|---|---|
| Stage 1 (MainForm migration + regression) | Closed | `Docs/ready/1_MAINFORM_MIGRATION_COMPLEX_RESEARCH_AND_PLAN.md` |
| Stage 2 (group-order + PostgreSQL) | Closed | `Docs/ready/2_STAGE2_CONSOLIDATED_AUDIT_AND_GO_NO_GO.md` |
| Stage 3-4 (LAN client-server + EF/API + auto-update) | Closed | `Docs/ready/3_STAGE3_STAGE4_CONSOLIDATED_GO_NO_GO_2026-03-20.md` |
| Stage 5 (security/auth cutover) | Completed | `5_STAGE5_SECURITY_AUTH_AND_CUTOVER_BRIEF_2026-03-26.md` |
| Наблюдаемость/SLO runbook | Completed | `Docs/ready/OPERATIONS_SLO_RUNBOOK.md` |
| Performance + Mediator migration | In progress (execution brief ready) | `MEDIATOR_PERFORMANCE_DEEP_BRIEF_2026-03-26.md` |
| Tech debt burn | In progress | `TECH_DEBT_BURNDOWN_NOW.md` |
| Installer/packaging | Planned (отдельный трек) | `INSTALLER_AND_DEPENDENCIES_PACKAGING_PLAN.md` |

## 3. Канонический набор документов (что считать источником истины)

### 3.1 Управление текущей работой (активные)

1. `00_REPLICA_MASTER_DOC_MAP_2026-03-26.md`  
   Единая навигация и статусы.

2. `MEDIATOR_PERFORMANCE_DEEP_BRIEF_2026-03-26.md`  
   План на ближайшие итерации: ускорение UI + переход на mediator-boundary.

3. `TECH_DEBT_BURNDOWN_NOW.md`  
   Burn-list техдолга (что дожимаем в первую очередь).

4. `ARCHITECTURE_AUDIT_Replica.md`  
   Полный архитектурный аудит и эволюция решений.

### 3.2 Операционная эксплуатация

1. `Docs/ready/OPERATIONS_SLO_RUNBOOK.md`  
   Пороги, деградация, первичные алерты, что делать по инцидентам.

2. `connection-sync-audit-2026-03-23.md`  
   Аудит путей подключения и синхронизации (LAN/API/PostgreSQL/fallback).

### 3.3 История завершенных этапов (reference)

1. `Docs/ready/1_MAINFORM_MIGRATION_COMPLEX_RESEARCH_AND_PLAN.md`
2. `Docs/ready/1_SINGLE_ORDER_REGRESSION_CHECKLIST.md`
3. `Docs/ready/2_MULTI_ORDER_LOGIC_AND_POSTGRESQL_PLAN.md`
4. `Docs/ready/2_STAGE2_CONSOLIDATED_AUDIT_AND_GO_NO_GO.md`
5. `Docs/ready/3_LAN_CLIENT_SERVER_BRIEF_STEP1.md`
6. `Docs/ready/3_STAGE3_STAGE4_CONSOLIDATED_GO_NO_GO_2026-03-20.md`
7. `Docs/ready/4_EF_MIGRATIONS_API_AND_AUTOUPDATE_ROLLOUT_PLAN.md`
8. `Docs/ready/4_STAGE4_RELEASE_RUNBOOK.md`
9. `Docs/ready/5_STRICT_AUTH_CUTOVER_CHECKLIST_2026-03-26.md`
10. `5_STAGE5_SECURITY_AUTH_AND_CUTOVER_BRIEF_2026-03-26.md`

## 4. Что сейчас в фокусе (коротко)

Текущий приоритет: производительность и декомпозиция `OrdersWorkspace`, затем mediator rollout.

Ближайший исполняемый план:

1. Итерации P0 (убрать sync-over-async и лишние UI-блокировки).
2. Уменьшить стоимость `RebuildOrdersGrid` (debounce + частичные обновления).
3. После стабилизации latency перевести write/read use-cases на mediator handlers.

Источник плана: `MEDIATOR_PERFORMANCE_DEEP_BRIEF_2026-03-26.md`.

## 5. Что пока не трогаем

Отдельный трек `installer/dependencies packaging` не смешиваем с текущей оптимизацией и декомпозицией.

Источник: `INSTALLER_AND_DEPENDENCIES_PACKAGING_PLAN.md`.

## 6. Правило на будущее (чтобы снова не утонуть в документах)

1. Любой новый документ сначала регистрируется в этой карте (раздел 2 или 3).
2. У каждого документа должен быть явный `Статус` (`Planned/In progress/Completed/Deprecated`).
3. Для каждого этапа один итоговый consolidated-файл в `Docs/ready`, остальные только как приложения.
