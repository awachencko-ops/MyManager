<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.

# Replica Stage 4 Checklist (Dual-Write + Reconciliation)

Date: 2026-03-27  
Status: Done (historical baseline, closed by Stage 6 cutover)

## Purpose

Подготовить безопасный переход на Stage 4 (controlled dual-write `PostgreSQL -> history.json shadow`) без риска потери данных и с явными критериями Go/No-Go.

## Scope

1. Backend dual-write policy.
2. Reconciliation pipeline (`json vs pg`) и acceptance criteria.
3. Daily operation checklist на период dual-write окна.
4. Rollback triggers и rollback protocol.

## Preconditions (must be true before Stage 4 start)

1. Stage 1-3 baseline green в целевых тест-паках.
2. API write path работает через MediatR command pipeline.
3. SignalR push и reconnect-resync verified.
4. `AppSettings`/runtime у операторов синхронизированы (включая LAN Push tuning).
5. Backup-процедуры подтверждены (см. Runbook section).

## Configuration/Flags Contract

1. Добавить feature-flag режима миграции (пример: `ReplicaApi:Migration:DualWriteEnabled`).
2. Добавить toggle для fail-policy shadow-write:
   - `WarnOnly` (recommended at rollout start),
   - `FailCommand` (only after stabilization window).
3. Добавить трассировку dual-write outcome:
   - `correlation_id`,
   - command name,
   - primary write result,
   - shadow write result,
   - latency split (`primary_ms`, `shadow_ms`).

## Dual-Write Policy

1. Primary source of truth: PostgreSQL.
2. Shadow target: `history.json` mirror (write-after-primary).
3. If primary write fails:
   - command fails,
   - shadow write is skipped.
4. If primary write succeeds, shadow write fails:
   - command outcome depends on fail-policy (`WarnOnly` by default),
   - mandatory alert + audit marker.
5. Every dual-write operation must be idempotent by command/idempotency key.

## Reconciliation Pipeline

## Snapshot Inputs

1. PostgreSQL snapshot export (`orders`, `items`, `versions`, `updated_at`).
2. `history.json` normalized snapshot.

## Comparison Keys

1. Order key: `InternalId`.
2. Item key: `InternalId + ItemId`.
3. Version consistency: `StorageVersion` or mapped equivalent.

## Reconciliation Outputs

1. `missing_in_pg`.
2. `missing_in_json`.
3. `version_mismatch`.
4. `payload_mismatch` (critical fields only).
5. Daily report artifact with totals + sample rows.

## Acceptance Criteria

1. `missing_in_pg = 0`.
2. `missing_in_json = 0`.
3. `version_mismatch = 0`.
4. `payload_mismatch = 0`.
5. 3 consecutive daily runs with zero diff before Stage 5/6 cutover decision.

## Daily Ops Checklist (during dual-write window)

1. Validate backup freshness:
   - latest `history.json` immutable copy,
   - latest `pg_dump` snapshot.
2. Check dual-write telemetry:
   - shadow write failures trend,
   - latency overhead trend.
3. Run reconciliation job and publish report.
4. If any mismatch > 0:
   - create incident,
   - pause expansion rollout,
   - start root-cause and replay plan.
5. Log status in execution journal with date/time and responsible actor.

## Go/No-Go Gate

## Go

1. Test packs green (verify/ui-smoke targeted).
2. Reconciliation zero-diff for 3 consecutive days.
3. Shadow-write failure rate below agreed SLO threshold.
4. Rollback drill result documented and approved.

## No-Go

1. Any non-zero reconciliation mismatch.
2. Unbounded shadow-write failure growth.
3. Missing backups or stale backup snapshots.
4. Undiagnosed data consistency incident in the last 24h.

## Rollback Protocol (minimum)

1. Freeze mutating traffic (maintenance mode / write gate).
2. Disable dual-write flag.
3. Restore last known-good state according to approved recovery runbook.
4. Reconcile restored state before reopening writes.
5. Publish incident summary with timeline and corrective actions.

## Test Plan for Stage 4 kick-off

1. Unit:
   - dual-write policy behavior (`primary fail`, `shadow fail`, `warn-only`).
2. Integration:
   - end-to-end write with dual-write enabled,
   - replay/idempotency under retries.
3. Verification:
   - reconciliation parser and diff engine with synthetic mismatches.
4. Smoke:
   - operator-visible status remains human-readable in tray/tooltip.

## Handoff Notes

1. Этот документ является рабочим checklist артефактом для старта Stage 4.
2. Все изменения Stage 4 логируются в roadmap execution log отдельными инкрементами.
