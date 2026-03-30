<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.

# Replica Stage 4 Reconciliation Runbook

Date: 2026-03-30  
Status: Active

## Purpose

Операционный регламент запуска nightly reconciliation (`json vs pg`) и фиксации результата в Stage 4 execution journal.

## Workflow

GitHub Actions workflow:
`.github/workflows/stage4-reconciliation-nightly.yml`

Triggers:
1. `schedule` (daily, 09:15 Asia/Vladivostok).
2. `workflow_dispatch` (manual run).

## Required Repo Variables (for scheduled runs)

1. `REPLICA_PG_SNAPSHOT_PATH` — путь до snapshot PostgreSQL (JSON array of orders).
2. `REPLICA_JSON_SNAPSHOT_PATH` — путь до JSON snapshot (`array` или `Orders/orders` envelope).

Set with GitHub CLI:

```powershell
gh variable set REPLICA_PG_SNAPSHOT_PATH --body "artifacts/reconciliation/snapshots/pg.snapshot.json"
gh variable set REPLICA_JSON_SNAPSHOT_PATH --body "artifacts/reconciliation/snapshots/json.snapshot.json"
```

Check values:

```powershell
gh variable list
```

## Manual Run

Without custom inputs (uses repo vars/fallback):

```powershell
gh workflow run "Stage4 Reconciliation Nightly"
```

With explicit snapshot paths:

```powershell
gh workflow run "Stage4 Reconciliation Nightly" `
  -f pg_snapshot_path="artifacts/reconciliation/snapshots/pg.snapshot.json" `
  -f json_snapshot_path="artifacts/reconciliation/snapshots/json.snapshot.json"
```

Local operator command (CLI + auto-journal entry):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/stage4/Run-ReconciliationJournal.ps1 `
  -PgSnapshotPath "<pg_snapshot.json>" `
  -JsonSnapshotPath "<json_snapshot.json>" `
  -ResponsibleActor "<operator_name>"
```

## Result Interpretation

CLI exit codes:
1. `0` — zero-diff (`is_zero_diff=true`).
2. `2` — mismatches detected (workflow marks run failed after artifact upload).
3. `1` — execution error (invalid input/path/read/parse failure).

Published artifacts:
1. `stage4-reconciliation-report-<run_id>` — JSON report with summary and mismatch buckets.

Summary fields:
1. `missing_in_pg`
2. `missing_in_json`
3. `version_mismatch`
4. `payload_mismatch`
5. `is_zero_diff`

## Daily Operator Checklist

1. Убедиться, что workflow run завершён и artifact отчёт доступен.
2. Проверить summary (особенно `is_zero_diff`).
3. Зафиксировать результат в:
   - `Docs/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE4_EXECUTION_JOURNAL_2026-03-30.md`
4. Если любое mismatch-поле > 0:
   - создать incident,
   - остановить расширение rollout,
   - запустить root-cause/replay процедуру по Stage 4 checklist.
