<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.

# Replica Stage 4 Reconciliation Runbook

Date: 2026-03-30  
Status: Active

## Purpose

Операционный регламент ежедневной сверки `json vs pg` в режиме Dual-Write и фиксации результата в execution journal.

## Runtime Model (Primary)

Основной путь эксплуатации: **локально на сервере/рабочем ПК через Windows Task Scheduler**.

1. По расписанию запускается `scripts/stage4/Run-ReconciliationLive.ps1`.
2. `Run-ReconciliationLive.ps1` вызывает `Prepare-ReconciliationSnapshots.ps1`:
   - читает `history.json` (локальная рабочая таблица),
   - запрашивает API (`/api/orders`),
   - формирует актуальные snapshots `json vs pg`.
3. Затем запускается `Run-ReconciliationJournal.ps1`.
4. `Run-ReconciliationJournal.ps1` вызывает reconciliation CLI, формирует diff-report и дописывает запись в execution journal.

GitHub Actions в этом контуре не обязателен и рассматривается только как опциональный внешний fallback.

## Scripts

1. `scripts/stage4/Prepare-ReconciliationSnapshots.ps1`  
   Подготовка актуальных snapshots из API + `history.json`.
2. `scripts/stage4/Run-ReconciliationJournal.ps1`  
   Reconciliation + report + journal append.
3. `scripts/stage4/Run-ReconciliationLive.ps1`  
   Оркестрация prepare + journal для ежедневного запуска.
4. `scripts/stage4/Register-ReconciliationScheduledTask.ps1`  
   Setup script: регистрация ежедневной задачи Task Scheduler.
5. `scripts/stage4/Unregister-ReconciliationScheduledTask.ps1`  
   Удаление зарегистрированной задачи (при пересоздании/откате).

## One-Time Setup (Task Scheduler)

```powershell
powershell -ExecutionPolicy Bypass -File scripts/stage4/Register-ReconciliationScheduledTask.ps1 `
  -Mode LiveSources `
  -At "09:15" `
  -ResponsibleActor "task-scheduler"
```

Опциональные overrides для нестандартного окружения:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/stage4/Register-ReconciliationScheduledTask.ps1 `
  -Mode LiveSources `
  -ApiBaseUrl "http://localhost:5000/" `
  -HistoryFilePath "C:\Path\To\history.json" `
  -SettingsFilePath "C:\Path\To\settings.json" `
  -TimeoutSec 45 `
  -ApiPreflightPolicy "Fail" `
  -ResponsibleActor "task-scheduler" `
  -ForceRecreate
```

Проверить задачу:

```powershell
Get-ScheduledTask -TaskName "Replica Stage4 Reconciliation Daily"
Get-ScheduledTaskInfo -TaskName "Replica Stage4 Reconciliation Daily" | Select-Object LastRunTime,LastTaskResult,NextRunTime
```

Проверка конфигурации без регистрации задачи:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/stage4/Register-ReconciliationScheduledTask.ps1 -DryRun
```

Удаление задачи (если нужно пересоздать/откатить):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/stage4/Unregister-ReconciliationScheduledTask.ps1
```

## Manual Run (Operator)

```powershell
powershell -ExecutionPolicy Bypass -File scripts/stage4/Run-ReconciliationLive.ps1 `
  -ApiPreflightPolicy "Fail" `
  -ResponsibleActor "<operator_name>"
```

Fallback ручной run со статическими snapshots:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/stage4/Run-ReconciliationJournal.ps1 `
  -PgSnapshotPath "<pg_snapshot.json>" `
  -JsonSnapshotPath "<json_snapshot.json>" `
  -ResponsibleActor "<operator_name>"
```

## Result Interpretation

CLI exit codes:
1. `0` — zero-diff (`is_zero_diff=true`).
2. `2` — mismatches detected.
3. `1` — execution/runtime error.

Mismatch buckets:
1. `missing_in_pg`
2. `missing_in_json`
3. `version_mismatch`
4. `payload_mismatch`
5. `is_zero_diff`

Preflight policy:
1. `Warn` — при проблемном `/live` показывает warning и пытается продолжить.
2. `Fail` — при проблемном/недоступном API останавливает run до reconciliation шага.

## Daily Operator Checklist

1. Убедиться, что задача отработала (`LastTaskResult`).
2. Проверить доступность API (`/live`) и актуальность `history.json` пути в `settings.json` (если есть инцидент).
3. Проверить свежий reconciliation report в `artifacts/reconciliation/reports/`.
4. Проверить последнюю запись в:
   - `Docs/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE4_EXECUTION_JOURNAL_2026-03-30.md`
5. Если любое mismatch-поле > 0:
   - создать incident,
   - остановить расширение rollout,
   - выполнить root-cause/replay по Stage 4 checklist.
6. Если `LastTaskResult` non-zero:
   - выполнить ручной `Run-ReconciliationLive.ps1` и считать сообщение preflight,
   - проверить запуск API и `LanApiBaseUrl`,
   - повторно запустить scheduled task после восстановления API.

## Optional Fallback (GitHub Actions)

Если локальный scheduler временно недоступен, допустим fallback через:
`.github/workflows/stage4-reconciliation-nightly.yml`.

Этот fallback не заменяет основной локальный operational контур.
