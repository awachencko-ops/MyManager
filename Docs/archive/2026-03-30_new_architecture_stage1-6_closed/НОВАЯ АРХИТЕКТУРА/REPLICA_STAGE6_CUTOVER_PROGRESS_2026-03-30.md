<!-- DOC_ENCODING_REQUIREMENT_UTF8 -->
> Требование кодировки: все файлы документации (`*.md`) в этом репозитории хранятся только в `UTF-8 with BOM`, окончания строк — `LF`.

# Replica Stage 6 Progress (Cutover + Legacy Flow Decommission)

Date: 2026-03-30  
Status: Done

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

## Completed Increment: Runtime Legacy File-Flow Decommission

1. Removed LAN runtime bootstrap/mirror interaction with `history.json`:
   - `Features/Orders/Application/Services/OrdersHistoryRepositoryCoordinator.cs`
   - LAN mode no longer:
     - bootstraps PostgreSQL from `history.json` during load,
     - mirrors PostgreSQL snapshot to `history.json` during load/save.
2. Stage 6 readiness script strengthened:
   - added check `lan_runtime_legacy_file_flow`,
   - risk is raised if coordinator still contains runtime bootstrap/mirror methods.
3. Regression evidence:
   - `dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj` passed (`349/349`).
4. Integration coverage update:
   - LAN coordinator integration expectation switched to “primary without runtime file sync”,
   - added explicit integration case for “no bootstrap from file in LAN mode”.

## Fourth Execution Evidence

Command:

```powershell
dotnet test tests/Replica.VerifyTests/Replica.VerifyTests.csproj `
  -p:BaseOutputPath="C:\Users\user\Desktop\MyManager 1.0.1\artifacts\tmp\test-out\"

powershell -ExecutionPolicy Bypass -File "scripts/stage6/Get-CutoverReadinessStatus.ps1" `
  -RepoRoot "." `
  -OutJsonPath "artifacts/stage6/cutover-readiness.latest.json"
```

Observed status:

1. Tests: `349/349` passed.
2. Readiness: `ready_for_cutover`.
3. Active risks (`0`):
   - none.
4. New check state:
   - `lan_runtime_legacy_file_flow = OK`.

## Completed Increment: Stage 6 Closure Checklist Publication

1. Added final closure/go-no-go document:
   - `Docs/archive/2026-03-30_new_architecture_stage1-6_closed/НОВАЯ АРХИТЕКТУРА/REPLICA_STAGE6_CUTOVER_CLOSURE_CHECKLIST_2026-03-30.md`.
2. Master doc map updated to include Stage 6 closure checklist as active handoff artifact.

## Go/No-Go Walkthrough Snapshot

1. Checklist command set executed (readiness + verify tests).
2. Current technical gate result:
   - `ready_for_cutover`,
   - `risk_count=0`,
   - verify pack `349/349` green.
3. Decision state:
   - technical gate passed,
   - awaiting owner sign-off in closure checklist.

## Completed Increment: Final Sign-off and Stage Closure

1. Sign-off decision recorded in closure checklist:
   - `REPLICA_STAGE6_CUTOVER_CLOSURE_CHECKLIST_2026-03-30.md` -> `Signed-off (Go)`.
2. Stage 6 execution status closed as `Done`.
3. Final gate snapshot:
   - readiness `ready_for_cutover`, `risk_count=0`,
   - verify tests `349/349` passed.

## Next Increment (planned)

1. Start post-Stage6 roadmap block (next architecture stage execution document).
