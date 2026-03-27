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

## Test Evidence

1. `dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj --filter "ReplicaApiDualWriteIntegrationTests|ReplicaApiMigrationConfigurationTests|ReplicaApiDualWriteShadowBehaviorTests"`  
   Result: passed (`9/9`).
2. `dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj --filter "SignalRPushIntegrationTests|LanOrderPushClientTests|MediatRPushNotificationsBehaviorTests|LanPushPressureAlertEvaluatorTests|ReplicaApiObservabilityTests|DiagnosticsControllerTests"`  
   Result: passed (`41/41`).
3. `dotnet test tests/Replica.UiSmokeTests/Replica.UiSmokeTests.csproj --filter "MainFormCoreRegressionTests|MainFormSmokeTests"`  
   Result: passed (`35/35`).

## Open Notes

1. Полный `Replica.VerifyTests` в текущей ветке имеет 2 падения вне Stage 4 scope:
   - `OrderRunCommandServiceTests.PrepareAndBeginAsync_WhenNoRunnableOrders_ReturnsNoRunnable`,
   - `OrderRunWorkflowOrchestrationServiceTests.PrepareStartAsync_WhenNoRunnableOrders_DoesNotCallLanGateway`.
2. Эти падения не относятся к dual-write изменениям и требуют отдельного разбора в run-workflow контуре.

## Next Increment (planned)

1. Добавить stage-4 integration coverage для end-to-end dual-write path (primary success + shadow mirror verification).
2. Подготовить reconciliation starter artifact (`json vs pg`) с машинно-читаемым diff output.
3. Завести ежедневный execution journal для dual-write окна и прогонять Go/No-Go критерии.
