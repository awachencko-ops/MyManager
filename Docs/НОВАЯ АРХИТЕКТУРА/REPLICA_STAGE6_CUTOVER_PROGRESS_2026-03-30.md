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

## Next Increment (planned)

1. Switch client default backend to API mode for production profile.
2. Remove/deactivate file-system mode from settings UI and runtime composition (keep only recovery/import utility path).
3. Decommission Stage 4 daily scheduler path after final go/no-go confirmation.
