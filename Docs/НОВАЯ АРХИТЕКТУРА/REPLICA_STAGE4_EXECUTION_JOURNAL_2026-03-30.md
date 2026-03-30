<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.

# Replica Stage 4 Execution Journal

Date: 2026-03-30  
Status: Active

## Purpose

Ежедневный журнал выполнения dual-write окна Stage 4: контроль reconcile-отчётов, backup freshness и Go/No-Go сигналов.

## Daily Entry Template

1. Date/Time (local)
2. Responsible actor
3. Backups:
   - history.json immutable copy status
   - pg snapshot status
4. Reconciliation summary:
   - missing_in_pg
   - missing_in_json
   - version_mismatch
   - payload_mismatch
   - is_zero_diff
5. Decision:
   - Continue / Pause rollout
6. Notes / incident links

## Entries

### 2026-03-30 18:20 (Asia/Vladivostok)

1. Responsible actor: codex-assisted update.
2. Backups:
   - `history.json` immutable copy: pending operational run.
   - pg snapshot: pending operational run.
3. Reconciliation tooling:
   - Nightly workflow added: `.github/workflows/stage4-reconciliation-nightly.yml`.
   - CLI integrated: `tools/Replica.Reconciliation.Cli`.
   - Baseline sample snapshots prepared in `artifacts/reconciliation/snapshots/`.
4. Initial result (sample run):
   - missing_in_pg = 0
   - missing_in_json = 0
   - version_mismatch = 0
   - payload_mismatch = 0
   - is_zero_diff = true
5. Decision: Continue rollout preparation.
6. Notes: перейти на реальные snapshot inputs через repo vars `REPLICA_PG_SNAPSHOT_PATH` и `REPLICA_JSON_SNAPSHOT_PATH`.

### 2026-03-30 11:05 (Asia/Vladivostok)

1. Responsible actor: codex-local
2. Backups:
   - history.json immutable copy: available.
   - pg snapshot: available.
3. Reconciliation summary:
   - missing_in_pg = 0
   - missing_in_json = 0
   - version_mismatch = 0
   - payload_mismatch = 0
   - is_zero_diff = true
4. Decision: Continue rollout preparation.
5. Notes: report_path=C:\Users\user\Desktop\MyManager 1.0.1\artifacts\reconciliation\reports\reconciliation-20260330-110536.json; cli_exit_code=0.

### 2026-03-30 11:07 (Asia/Vladivostok)

1. Responsible actor: codex-local
2. Backups:
   - history.json immutable copy: available.
   - pg snapshot: available.
3. Reconciliation summary:
   - missing_in_pg = 0
   - missing_in_json = 0
   - version_mismatch = 0
   - payload_mismatch = 0
   - is_zero_diff = true
4. Decision: Continue rollout preparation.
5. Notes: report_path=C:\Users\user\Desktop\MyManager 1.0.1\artifacts\reconciliation\reports\reconciliation-20260330-110720.json; cli_exit_code=0.

### 2026-03-30 11:35 (Asia/Vladivostok)

1. Responsible actor: codex-assisted update.
2. Operational mode update:
   - Task Scheduler назначен основным ежедневным маршрутом reconciliation.
   - GitHub Actions переведён в optional fallback режим.
3. Tooling:
   - added `scripts/stage4/Register-ReconciliationScheduledTask.ps1`,
   - added `scripts/stage4/Unregister-ReconciliationScheduledTask.ps1`,
   - setup command dry-run validated (`-DryRun`).
4. Decision: Continue rollout preparation.
5. Notes: следующий шаг — зарегистрировать daily задачу на production host с реальными snapshot путями.

### 2026-03-30 11:38 (Asia/Vladivostok)

1. Responsible actor: task-scheduler
2. Backups:
   - history.json immutable copy: available.
   - pg snapshot: available.
3. Reconciliation summary:
   - missing_in_pg = 0
   - missing_in_json = 0
   - version_mismatch = 0
   - payload_mismatch = 0
   - is_zero_diff = true
4. Decision: Continue rollout preparation.
5. Notes: report_path=C:\Users\user\Desktop\MyManager 1.0.1\artifacts\reconciliation\reports\reconciliation-20260330-113819.json; cli_exit_code=0.
