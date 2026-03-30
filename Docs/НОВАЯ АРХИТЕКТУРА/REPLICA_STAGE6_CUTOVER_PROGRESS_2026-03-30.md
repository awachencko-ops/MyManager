<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.

# Replica Stage 6 Progress (Cutover + Legacy Flow Decommission)

Date: 2026-03-30  
Status: In progress

## Stage 6 Kickoff

1. Stage 5 formally closed (`REPLICA_STAGE5_CLEAN_ARCH_PROGRESS_2026-03-30.md` status set to `Done`).
2. Stage 6 execution tracking started in this document.

## Completed Increment: Cutover Readiness Status Script

1. Added script:
   - `scripts/stage6/Get-CutoverReadinessStatus.ps1`
2. Script checks current cutover blockers/risk signals:
   - `Replica.Api:Migration:DualWriteEnabled` flag in `Replica.Api/appsettings.json`,
   - client default storage backend (`OrdersStorageMode.FileSystem`) in `Models/AppSettings.cs`,
   - settings UI file-mode availability in `Forms/Settings/SettingsDialogForm.cs`,
   - Stage 4 scheduler task registration (`Replica Stage4 Reconciliation Daily`).
3. Script supports:
   - JSON output artifact (`-OutJsonPath`),
   - non-zero exit on risk (`-FailOnRisk`).

## First Execution Evidence

Command:

```powershell
powershell -ExecutionPolicy Bypass -File "scripts/stage6/Get-CutoverReadinessStatus.ps1" `
  -RepoRoot "." `
  -OutJsonPath "artifacts/stage6/cutover-readiness.latest.json"
```

Observed status:

1. `risk_detected`
2. Active risks (`3`):
   - client default storage mode is `FileSystem`,
   - settings UI still exposes file-system mode option,
   - Stage 4 scheduler task is still registered.
3. Non-risk signal:
   - API dual-write flag already disabled (`DualWriteEnabled=false`).

## Completed Increment: Storage Mode UI/Default Cutover

1. Runtime defaults switched to `LanPostgreSql`:
   - `Models/AppSettings.cs` (`OrdersStorageBackend` default),
   - `Features/Orders/UI/OrdersWorkspace/Core/OrdersWorkspaceForm.State.cs`,
   - `Features/Orders/Application/Services/OrdersHistoryRepositoryCoordinator.cs`.
2. Legacy user settings auto-migration added:
   - if persisted mode is `FileSystem`, it is normalized to `LanPostgreSql` during `AppSettings.Load()` normalization.
3. Settings UI no longer exposes `FileSystem` mode option:
   - `Forms/Settings/SettingsDialogForm.cs` now keeps only `LAN PostgreSQL` and locks selector.
4. Regression evidence:
   - `dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj` passed (`348/348`).

## Second Execution Evidence

Command:

```powershell
powershell -ExecutionPolicy Bypass -File "scripts/stage6/Get-CutoverReadinessStatus.ps1" `
  -RepoRoot "." `
  -OutJsonPath "artifacts/stage6/cutover-readiness.latest.json"
```

Observed status:

1. `risk_detected`
2. Active risks (`1`):
   - Stage 4 scheduler task is still registered.
3. Closed risks:
   - client default storage mode is no longer `FileSystem`,
   - settings UI no longer exposes file-system mode option.

## Completed Increment: Stage 4 Scheduler Decommission

1. Stage 4 daily task removed from Windows Task Scheduler:
   - `scripts/stage4/Unregister-ReconciliationScheduledTask.ps1` executed successfully.
2. Cutover readiness status is now green:
   - `ready_for_cutover`,
   - `risk_count = 0`.

## Third Execution Evidence

Command:

```powershell
powershell -ExecutionPolicy Bypass -File "scripts/stage4/Unregister-ReconciliationScheduledTask.ps1"
powershell -ExecutionPolicy Bypass -File "scripts/stage6/Get-CutoverReadinessStatus.ps1" `
  -RepoRoot "." `
  -OutJsonPath "artifacts/stage6/cutover-readiness.latest.json"
```

Observed status:

1. `ready_for_cutover`
2. Active risks (`0`):
   - none.

## Next Increment (planned)

1. Finalize Stage 6 rollback/recovery wording (file-based path only as explicit utility/recovery flow, not runtime mode).
2. Prepare Stage 6 closure checklist and handoff notes.
