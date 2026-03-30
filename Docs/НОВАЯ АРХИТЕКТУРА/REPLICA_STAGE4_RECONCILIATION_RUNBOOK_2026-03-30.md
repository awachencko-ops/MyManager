<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.

# Replica Stage 4 Reconciliation Runbook

Date: 2026-03-30  
Status: Active

## Purpose

Операционный регламент ежедневной сверки `json vs pg` в режиме Dual-Write и фиксации результата в execution journal.

## Runtime Model (Primary)

Основной путь эксплуатации: **локально на сервере/рабочем ПК через Windows Task Scheduler**.

1. По расписанию запускается `scripts/stage4/Run-ReconciliationJournal.ps1`.
2. Скрипт вызывает reconciliation CLI.
3. CLI строит JSON-отчёт с mismatch buckets.
4. Скрипт автоматически дописывает запись в execution journal.

GitHub Actions в этом контуре не обязателен и рассматривается только как опциональный внешний fallback.

## Scripts

1. `scripts/stage4/Run-ReconciliationJournal.ps1`  
   Daily-run script: reconciliation + report + journal append.
2. `scripts/stage4/Register-ReconciliationScheduledTask.ps1`  
   Setup script: регистрация ежедневной задачи Task Scheduler.

## One-Time Setup (Task Scheduler)

```powershell
powershell -ExecutionPolicy Bypass -File scripts/stage4/Register-ReconciliationScheduledTask.ps1 `
  -PgSnapshotPath "<real_pg_snapshot.json>" `
  -JsonSnapshotPath "<real_json_snapshot.json>" `
  -At "09:15" `
  -ResponsibleActor "task-scheduler"
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
powershell -ExecutionPolicy Bypass -File scripts/stage4/Run-ReconciliationJournal.ps1 `
  -PgSnapshotPath "<real_pg_snapshot.json>" `
  -JsonSnapshotPath "<real_json_snapshot.json>" `
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

## Daily Operator Checklist

1. Убедиться, что задача отработала (`LastTaskResult`).
2. Проверить свежий reconciliation report в `artifacts/reconciliation/reports/`.
3. Проверить последнюю запись в:
   - `Docs/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE4_EXECUTION_JOURNAL_2026-03-30.md`
4. Если любое mismatch-поле > 0:
   - создать incident,
   - остановить расширение rollout,
   - выполнить root-cause/replay по Stage 4 checklist.

## Optional Fallback (GitHub Actions)

Если локальный scheduler временно недоступен, допустим fallback через:
`.github/workflows/stage4-reconciliation-nightly.yml`.

Этот fallback не заменяет основной локальный operational контур.
