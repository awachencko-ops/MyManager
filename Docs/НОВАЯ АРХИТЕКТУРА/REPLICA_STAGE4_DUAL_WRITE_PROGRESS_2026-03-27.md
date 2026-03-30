<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.

# Replica Stage 4 Progress (Dual-Write Execution)

Date: 2026-03-30  
Status: Done (historical, closed by Stage 6 cutover)

## Completed Increment: API dual-write scaffolding

1. Введён конфиг-контракт миграции `ReplicaApi:Migration`:
   - `DualWriteEnabled`,
   - `ShadowWriteFailurePolicy` (`WarnOnly`/`FailCommand`),
   - `ShadowHistoryFilePath`.
2. Добавлен MediatR behavior `ReplicaApiDualWriteShadowBehavior<,>`:
   - применяется только к `IReplicaApiWriteCommand`,
   - выполняется только при успешном primary write (`StoreOperationResult.IsSuccess`),
   - поддерживает политики `WarnOnly` и `FailCommand`.
3. Добавлен shadow writer слой:
   - `IReplicaApiHistoryShadowWriter`,
   - `NoOpReplicaApiHistoryShadowWriter`,
   - `FileReplicaApiHistoryShadowWriter` (write mirror в `history.shadow.json`).
4. Добавлена экспозиция migration-режима в `/live` и `/health`.
5. Регистрация pipeline обновлена:
   - включён dual-write behavior,
   - добавлены defaults для migration options и logging fallback в pipeline registration.
6. Добавлено integration coverage для Stage 4:
   - проверка `CreateOrder` с `DualWriteEnabled=true` создаёт и наполняет `history.shadow.json`,
   - проверка `DualWriteEnabled=false` не создаёт shadow-файл.
7. Добавлен reconciliation starter artifact (`json vs pg`) с машинно-читаемым diff output:
   - `ReplicaApiReconciliationReportBuilder` (bucket-логика `missing_in_pg`, `missing_in_json`, `version_mismatch`, `payload_mismatch`),
   - file I/O helper `ReplicaApiReconciliationReportIo` (чтение snapshot + запись отчёта),
   - CLI-инструмент `tools/Replica.Reconciliation.Cli` для запуска из терминала.
8. Подключен основной операционный контур reconciliation через Windows Task Scheduler:
   - добавлен setup-script `scripts/stage4/Register-ReconciliationScheduledTask.ps1`,
   - добавлен remove-script `scripts/stage4/Unregister-ReconciliationScheduledTask.ps1`,
   - daily запуск выполняется локально на сервере/ПК без зависимости от GitHub.
9. Заведён ежедневный execution journal Stage 4:
   - документ `REPLICA_STAGE4_EXECUTION_JOURNAL_2026-03-30.md`,
   - добавлена стартовая запись и шаблон daily-отметок.
10. Операционные snapshot paths задаются в Task Scheduler setup:
   - `-PgSnapshotPath` и `-JsonSnapshotPath` передаются в registration script,
   - в режиме `LiveSources` snapshots пересобираются автоматически из API + `history.json`.
11. Добавлен runbook Stage 4 reconciliation:
   - документ `REPLICA_STAGE4_RECONCILIATION_RUNBOOK_2026-03-30.md`,
   - описаны setup/run/verification шаги для Task Scheduler и operator checklist.
12. Добавлен операционный helper-script для daily журнала:
   - `scripts/stage4/Run-ReconciliationJournal.ps1`,
   - выполняет reconciliation CLI и автоматически дописывает entry в execution journal.
13. GitHub workflow оставлен как optional fallback и не является основным operational маршрутом.
14. Task Scheduler setup выполнен и проверен:
   - registered task: `Replica Stage4 Reconciliation Daily` (`09:15`, daily),
   - manual start succeeded (`LastTaskResult=0`),
   - execution journal получил автоматическую запись от `task-scheduler`.
15. Добавлен live execution-chain для реальных источников:
   - `scripts/stage4/Prepare-ReconciliationSnapshots.ps1`,
   - `scripts/stage4/Run-ReconciliationLive.ps1`,
   - registration script поддерживает `-Mode LiveSources` и переключён на этот режим.
16. Добавлен операторский preflight-check API перед reconciliation:
   - `Prepare-ReconciliationSnapshots.ps1` проверяет API через `/live` и `/api/orders`,
   - добавлена политика preflight `Warn/Fail` (по умолчанию `Warn`),
   - при недоступном API выводится человекочитаемое сообщение и reconciliation шаг останавливается с `exit code 1`,
   - `Register-ReconciliationScheduledTask.ps1` поддерживает `-ApiPreflightPolicy` и `-TimeoutSec` для daily-задачи.
17. Исправлена ложная массовая рассинхронизация в reconciliation I/O:
   - чтение snapshots переведено на case-insensitive JSON десериализацию,
   - добавлен verify-тест для `camelCase` snapshot payload,
   - результат live-run после фикса: `missing_in_pg=0`, `missing_in_json=0`, осталось `payload_mismatch=1`.
18. Закрыт remaining payload mismatch по `OrderNumber`:
   - добавлен recovery-script `scripts/stage4/Repair-HistoryOrderNumbersFromApi.ps1` (API->history backfill + auto-backup),
   - для `internal_id=412296ad2a4249779be4f4a7d524c012` выполнен backfill `Id="тест 1"` в `history.json`,
   - повторные manual/scheduled live-runs подтверждают `is_zero_diff=true`.
19. Добавлен one-shot операторский статус Stage 4:
   - `scripts/stage4/Get-ReconciliationOpsStatus.ps1` агрегирует API `/live`, Scheduler status, latest reconciliation report и latest journal entry,
   - поддержан режим `-FailOnRisk` (exit code `2`) для быстрого gate-check.

## Test Evidence

1. `dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj --filter "ReplicaApiDualWriteIntegrationTests|ReplicaApiMigrationConfigurationTests|ReplicaApiDualWriteShadowBehaviorTests"`  
   Result: passed (`9/9`).
2. `dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj --filter "SignalRPushIntegrationTests|LanOrderPushClientTests|MediatRPushNotificationsBehaviorTests|LanPushPressureAlertEvaluatorTests|ReplicaApiObservabilityTests|DiagnosticsControllerTests"`  
   Result: passed (`41/41`).
3. `dotnet test tests/Replica.UiSmokeTests/Replica.UiSmokeTests.csproj --filter "MainFormCoreRegressionTests|MainFormSmokeTests"`  
   Result: passed (`35/35`).
4. `dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj`  
   Result: passed (`346/346`).
5. `dotnet run --project tools/Replica.Reconciliation.Cli -- --pg <pg_snapshot.json> --json <json_snapshot.json> --out <reconciliation-report.json>`  
   Result: validated (zero-diff report generated, exit code `0`).
6. `dotnet run --project tools/Replica.Reconciliation.Cli -- --pg <pg_snapshot.json> --json <json_snapshot.json> --out <reconciliation-report.json>` (mismatch sample)  
   Result: validated (non-zero diff report generated, exit code `2`).
7. `powershell -ExecutionPolicy Bypass -File scripts/stage4/Run-ReconciliationJournal.ps1 -ResponsibleActor "codex-local"`  
   Result: validated (report generated + journal entry appended).
8. `powershell -ExecutionPolicy Bypass -File scripts/stage4/Register-ReconciliationScheduledTask.ps1 -DryRun`  
   Result: validated (Task Scheduler action/paths resolved without side effects).
9. `powershell -ExecutionPolicy Bypass -File scripts/stage4/Register-ReconciliationScheduledTask.ps1 -ForceRecreate` + `Start-ScheduledTask`  
   Result: validated (task registered and manual run completed with `LastTaskResult=0`).
10. `powershell -ExecutionPolicy Bypass -File scripts/stage4/Prepare-ReconciliationSnapshots.ps1 -DryRun`  
   Result: validated (API/history/settings paths resolved, snapshot output paths resolved).
11. `powershell -ExecutionPolicy Bypass -File scripts/stage4/Run-ReconciliationLive.ps1 -DryRun`  
   Result: validated (prepare step invoked successfully, journal step skipped by design).
12. `powershell -ExecutionPolicy Bypass -File scripts/stage4/Register-ReconciliationScheduledTask.ps1 -Mode LiveSources -ForceRecreate`  
   Result: validated (scheduled task action switched to `Run-ReconciliationLive.ps1`).
13. `powershell -ExecutionPolicy Bypass -File scripts/stage4/Run-ReconciliationLive.ps1 -ApiPreflightPolicy Fail` (при недоступном API)  
   Result: validated (preflight blocks run with clear operator message, `exit code 1`).
14. `powershell -ExecutionPolicy Bypass -File scripts/stage4/Register-ReconciliationScheduledTask.ps1 -Mode LiveSources -ApiPreflightPolicy Fail -TimeoutSec 45 -ForceRecreate` + `Start-ScheduledTask`  
   Result: validated (task action includes preflight policy/timeout; task fails fast when API is unreachable).
15. `dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj --filter "ReplicaApiReconciliationReportBuilderTests"`  
   Result: passed (`3/3`) including `camelCase` snapshot coverage.
16. `powershell -ExecutionPolicy Bypass -File scripts/stage4/Run-ReconciliationLive.ps1 -ApiPreflightPolicy Fail -ResponsibleActor "codex-local"` (при доступном API)  
   Result: validated (`missing_in_pg=0`, `missing_in_json=0`, `payload_mismatch=1`, `exit code 2`).
17. `Start-ScheduledTask -TaskName "Replica Stage4 Reconciliation Daily"` (при доступном API)  
   Result: validated (`LastTaskResult=2`, report/journal created by scheduler with same single payload mismatch).
18. `powershell -ExecutionPolicy Bypass -File scripts/stage4/Repair-HistoryOrderNumbersFromApi.ps1 -DryRun` (при доступном API)  
   Result: validated (`1` patch candidate detected, no file changes).
19. `powershell -ExecutionPolicy Bypass -File scripts/stage4/Repair-HistoryOrderNumbersFromApi.ps1` (при доступном API)  
   Result: validated (history backup created + targeted backfill applied).
20. `powershell -ExecutionPolicy Bypass -File scripts/stage4/Run-ReconciliationLive.ps1 -ApiPreflightPolicy Fail -ResponsibleActor "codex-local"` (post-repair)  
   Result: validated (`missing_in_pg=0`, `missing_in_json=0`, `payload_mismatch=0`, `is_zero_diff=true`, `exit code 0`).
21. `Start-ScheduledTask -TaskName "Replica Stage4 Reconciliation Daily"` (post-repair, API reachable)  
   Result: validated (`LastTaskResult=0`, zero-diff report and journal entry created).
22. `powershell -ExecutionPolicy Bypass -File scripts/stage4/Get-ReconciliationOpsStatus.ps1`  
   Result: validated (one-shot status output generated with API/Scheduler/report/journal sections).
23. `powershell -ExecutionPolicy Bypass -File scripts/stage4/Get-ReconciliationOpsStatus.ps1 -FailOnRisk`  
   Result: validated (returns `exit code 2` when risk detected, e.g. API unreachable).

## Open Notes

1. Ранее падавшие run-workflow тесты восстановлены; полный `Verify` снова зелёный (`346/346`).
2. Основной daily-контур перенесён на локальный Task Scheduler; GitHub workflow используется только как fallback.
3. При текущей политике `ApiPreflightPolicy=Fail` non-zero `LastTaskResult` при недоступном API считается ожидаемым сигналом для оператора.
4. Recovery-script `Repair-HistoryOrderNumbersFromApi.ps1` доступен как точечный remediation tool для `OrderNumber` gaps в `history.json`.
5. Daily operator quick-check автоматизирован через `Get-ReconciliationOpsStatus.ps1`.

## Next Increment (planned)

1. Продолжить ежедневный live reconciliation monitoring через Task Scheduler и execution journal.
2. При следующем non-zero diff выполнять recovery/incindent workflow из runbook (preflight check -> report triage -> targeted remediation).
