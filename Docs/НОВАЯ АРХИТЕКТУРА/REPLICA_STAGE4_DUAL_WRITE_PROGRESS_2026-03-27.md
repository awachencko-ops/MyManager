<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.

# Replica Stage 4 Progress (Dual-Write Execution)

Date: 2026-03-27  
Status: In progress

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

## Open Notes

1. Ранее падавшие run-workflow тесты восстановлены; полный `Verify` снова зелёный (`346/346`).

## Next Increment (planned)

1. Завести ежедневный execution journal для dual-write окна и прогонять Go/No-Go критерии.
2. Подключить CLI reconciliation в nightly/ops pipeline с сохранением артефакта отчёта.
