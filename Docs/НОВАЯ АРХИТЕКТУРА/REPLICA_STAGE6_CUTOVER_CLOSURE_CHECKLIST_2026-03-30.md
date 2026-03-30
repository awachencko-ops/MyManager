<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.

# Replica Stage 6 Closure Checklist

Date: 2026-03-30  
Status: Signed-off (Go)

## 1. Goal

Закрыть Stage 6 с подтверждением, что runtime-контур полностью работает через LAN PostgreSQL/API и legacy file-flow выведен из обычного runtime пути.

## 2. Mandatory Gates

1. API migration mode:
   - `ReplicaApi:Migration:DualWriteEnabled=false`.
2. Client runtime mode:
   - default `OrdersStorageBackend = LanPostgreSql`;
   - settings UI не предлагает `FileSystem` для runtime.
3. Legacy runtime file-flow:
   - в LAN runtime отключены bootstrap/mirror path для `history.json`.
4. Scheduler cleanup:
   - task `Replica Stage4 Reconciliation Daily` не зарегистрирована.
5. Regression:
   - verify pack green (`tests/Replica.VerifyTests`).

## 3. Verification Commands

```powershell
# 1) Full Stage 6 readiness snapshot
powershell -ExecutionPolicy Bypass -File "scripts/stage6/Get-CutoverReadinessStatus.ps1" `
  -RepoRoot "." `
  -OutJsonPath "artifacts/stage6/cutover-readiness.latest.json" `
  -FailOnRisk

# 2) Verify regression
dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj `
  -p:BaseOutputPath="C:\Users\user\Desktop\MyManager 1.0.1\artifacts\tmp\test-out\"
```

Expected:

1. Readiness: `ready_for_cutover` with `risk_count=0`.
2. Verify tests: all passed.

## 4. Rollback/Recovery Policy (Post-Stage6)

1. `history.json` не используется как runtime source-of-truth.
2. Любые file-based операции — только как explicit utility/recovery workflow.
3. Возврат к file-runtime mode не делается через UI; только через controlled incident protocol и отдельный change record.

## 5. Handoff Notes

1. Основной операционный health-check команды: `Get-CutoverReadinessStatus.ps1`.
2. Артефакт статуса: `artifacts/stage6/cutover-readiness.latest.json`.
3. Контрольный журнал этапа: `REPLICA_STAGE6_CUTOVER_PROGRESS_2026-03-30.md`.

## 6. Sign-off Block

1. Technical owner: Replica engineering (session sign-off)
2. Ops owner: Replica operations (session sign-off)
3. Date/time: 2026-03-30 14:24 (Asia/Vladivostok)
4. Decision:
   - [x] Go
   - [ ] No-Go
5. Notes:
   - All mandatory gates passed: readiness `ready_for_cutover` with `risk_count=0`, verify pack `349/349` green.
