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

### 2026-03-30 11:52 (Asia/Vladivostok)

1. Responsible actor: codex-assisted update.
2. Operational mode update:
   - Task Scheduler action switched to `Run-ReconciliationLive.ps1` (`Mode=LiveSources`),
   - live chain now uses API + local `history.json` to prepare snapshots before reconciliation.
3. Verification:
   - `Prepare-ReconciliationSnapshots.ps1 -DryRun` passed,
   - `Run-ReconciliationLive.ps1 -DryRun` passed,
   - `Register-ReconciliationScheduledTask.ps1 -Mode LiveSources -ForceRecreate` passed.
4. Decision: Continue rollout preparation.
5. Notes: `http://localhost:5000/live` was unreachable during this check; first successful live scheduled run will be зафиксирован после старта API.

### 2026-03-30 11:59 (Asia/Vladivostok)

1. Responsible actor: codex-assisted update.
2. Operational mode update:
   - preflight policy switched to `Fail` for scheduled reconciliation task,
   - task action now includes `-ApiPreflightPolicy "Fail"` and `-TimeoutSec "45"`.
3. Verification:
   - manual `Run-ReconciliationLive.ps1 -ApiPreflightPolicy Fail` returns clear preflight error text on API outage,
   - scheduled task manual start completed with non-zero result (`LastTaskResult=267009`) while API remained unreachable.
4. Decision: Continue rollout preparation.
5. Notes: current non-zero task result is expected until Replica.Api becomes reachable; after API startup run scheduled task again and record first successful live run.

### 2026-03-30 12:03 (Asia/Vladivostok)

1. Responsible actor: codex-local
2. Backups:
   - history.json immutable copy: available.
   - pg snapshot: available.
3. Reconciliation summary:
   - missing_in_pg = 12
   - missing_in_json = 12
   - version_mismatch = 0
   - payload_mismatch = 0
   - is_zero_diff = false
4. Decision: Pause expansion rollout and start incident workflow.
5. Notes: report_path=C:\Users\user\Desktop\MyManager 1.0.1\artifacts\reconciliation\reports\reconciliation-20260330-120342.json; cli_exit_code=2.

### 2026-03-30 12:04 (Asia/Vladivostok)

1. Responsible actor: task-scheduler
2. Backups:
   - history.json immutable copy: available.
   - pg snapshot: available.
3. Reconciliation summary:
   - missing_in_pg = 12
   - missing_in_json = 12
   - version_mismatch = 0
   - payload_mismatch = 0
   - is_zero_diff = false
4. Decision: Pause expansion rollout and start incident workflow.
5. Notes: report_path=C:\Users\user\Desktop\MyManager 1.0.1\artifacts\reconciliation\reports\reconciliation-20260330-120414.json; cli_exit_code=2.

### 2026-03-30 12:06 (Asia/Vladivostok)

1. Responsible actor: codex-local
2. Backups:
   - history.json immutable copy: available.
   - pg snapshot: available.
3. Reconciliation summary:
   - missing_in_pg = 0
   - missing_in_json = 0
   - version_mismatch = 0
   - payload_mismatch = 1
   - is_zero_diff = false
4. Decision: Pause expansion rollout and start incident workflow.
5. Notes: report_path=C:\Users\user\Desktop\MyManager 1.0.1\artifacts\reconciliation\reports\reconciliation-20260330-120649.json; cli_exit_code=2.

### 2026-03-30 12:07 (Asia/Vladivostok)

1. Responsible actor: task-scheduler
2. Backups:
   - history.json immutable copy: available.
   - pg snapshot: available.
3. Reconciliation summary:
   - missing_in_pg = 0
   - missing_in_json = 0
   - version_mismatch = 0
   - payload_mismatch = 1
   - is_zero_diff = false
4. Decision: Pause expansion rollout and start incident workflow.
5. Notes: report_path=C:\Users\user\Desktop\MyManager 1.0.1\artifacts\reconciliation\reports\reconciliation-20260330-120753.json; cli_exit_code=2.

### 2026-03-30 12:08 (Asia/Vladivostok)

1. Responsible actor: codex-assisted update.
2. Reconciliation incident analysis:
   - root cause of previous `missing_in_pg=12`/`missing_in_json=12` was case-sensitive snapshot deserialization on `camelCase` API payload,
   - parser fixed to case-insensitive mode and verify coverage added.
3. Post-fix verification:
   - manual + scheduled live runs now show `missing_in_pg=0` and `missing_in_json=0`,
   - remaining diff is single `payload_mismatch=1` (`OrderNumber`) for `internal_id=412296ad2a4249779be4f4a7d524c012`.
4. Decision: Pause expansion rollout and continue root-cause/remediation for remaining payload mismatch.
5. Notes: next action is targeted data repair/alignment for `OrderNumber` and confirmation run to zero-diff.
