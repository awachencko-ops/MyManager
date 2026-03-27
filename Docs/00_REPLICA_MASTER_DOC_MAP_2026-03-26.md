<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.
# Replica: Master Doc Map (Single Source of Truth)

Дата актуализации: 2026-03-27  
Статус: `Active`

## 1. Точка входа

Открывать в таком порядке:

1. `Docs/00_REPLICA_MASTER_DOC_MAP_2026-03-26.md`
2. `Docs/REPLICA_DESIGN_SYSTEM_2026-03-27.md`
3. `Docs/НОВАЯ АРХИТЕКТУРА/REPLICA_SERVICE_FIRST_ROADMAP_2026-03-26.md`

## 2. Сводный статус

| Блок | Статус | Главный документ |
|---|---|---|
| New Architecture roadmap (Service-First / Push-Pull / MediatR) | In progress | `Docs/НОВАЯ АРХИТЕКТУРА/REPLICA_SERVICE_FIRST_ROADMAP_2026-03-26.md` |
| UI Design System | Active | `Docs/REPLICA_DESIGN_SYSTEM_2026-03-27.md` |
| Legacy docs set (active + ready, до пересборки плана) | Archived | `Docs/archive/2026-03-26_pre_new_architecture/` |

## 3. Структура папок

### 3.1 `Docs/НОВАЯ АРХИТЕКТУРА` (текущий целевой контур)

1. `REPLICA_SERVICE_FIRST_ROADMAP_2026-03-26.md`
2. `REPLICA_STAGE1_SECURITY_PROGRESS_2026-03-27.md`
3. `REPLICA_STAGE2_COMMAND_BUS_PROGRESS_2026-03-27.md`
4. `REPLICA_STAGE3_SIGNALR_PROGRESS_2026-03-27.md`

### 3.2 `Docs/archive/2026-03-26_pre_new_architecture` (архив согласованного переноса)

1. `active/*` — ранее рабочие документы (аудиты/планы)
2. `ready/*` — ранее закрытые этапы/runbook

### 3.3 `Docs/archive` (исторические архивы)

1. `CATALOGIZATION_MEMO.md`
2. `UNIVERSAL_LAN_GROUPORDER_IMPLEMENTATION_BRIEF.md`
3. `2026-03-26_pre_new_architecture/*`

## 4. Правило ведения документации

1. Текущая рабочая архитектурная рамка хранится в `Docs/НОВАЯ АРХИТЕКТУРА`.
2. Документы прежнего контура не удаляются, а переносятся в `Docs/archive/*`.
3. После закрытия каждого нового этапа допускается публикация consolidated-doc в новом контуре или архивирование в отдельный dated-bundle.

