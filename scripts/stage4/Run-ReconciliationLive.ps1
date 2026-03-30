param(
    [Parameter(Mandatory = $false)]
    [string]$PgSnapshotPath = "artifacts/reconciliation/snapshots/pg.snapshot.json",

    [Parameter(Mandatory = $false)]
    [string]$JsonSnapshotPath = "artifacts/reconciliation/snapshots/json.snapshot.json",

    [Parameter(Mandatory = $false)]
    [string]$ApiBaseUrl = "",

    [Parameter(Mandatory = $false)]
    [string]$ApiActor = "Administrator",

    [Parameter(Mandatory = $false)]
    [string]$ApiBearerToken = "",

    [Parameter(Mandatory = $false)]
    [string]$HistoryFilePath = "",

    [Parameter(Mandatory = $false)]
    [string]$SettingsFilePath = "",

    [Parameter(Mandatory = $false)]
    [string]$ReportOutputPath = "",

    [Parameter(Mandatory = $false)]
    [string]$JournalPath = "",

    [Parameter(Mandatory = $false)]
    [string]$ResponsibleActor = "task-scheduler",

    [Parameter(Mandatory = $false)]
    [int]$TimeoutSec = 30,

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    param([string]$ScriptPath)
    return (Resolve-Path (Join-Path (Split-Path -Parent $ScriptPath) "..\..")).Path
}

function Add-StringArgIfPresent {
    param(
        [System.Collections.Generic.List[string]]$ArgsList,
        [string]$Name,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return
    }

    $ArgsList.Add($Name)
    $ArgsList.Add($Value)
}

$repoRoot = Resolve-RepoRoot -ScriptPath $PSCommandPath
$prepareScriptPath = Join-Path $repoRoot "scripts/stage4/Prepare-ReconciliationSnapshots.ps1"
$journalScriptPath = Join-Path $repoRoot "scripts/stage4/Run-ReconciliationJournal.ps1"

if (-not (Test-Path $prepareScriptPath)) {
    throw "Prepare script not found: $prepareScriptPath"
}
if (-not (Test-Path $journalScriptPath)) {
    throw "Journal script not found: $journalScriptPath"
}

$prepareArgs = New-Object System.Collections.Generic.List[string]
$prepareArgs.Add("-NoProfile")
$prepareArgs.Add("-ExecutionPolicy")
$prepareArgs.Add("Bypass")
$prepareArgs.Add("-File")
$prepareArgs.Add($prepareScriptPath)
$prepareArgs.Add("-PgSnapshotPath")
$prepareArgs.Add($PgSnapshotPath)
$prepareArgs.Add("-JsonSnapshotPath")
$prepareArgs.Add($JsonSnapshotPath)
$prepareArgs.Add("-ApiActor")
$prepareArgs.Add($ApiActor)
$prepareArgs.Add("-TimeoutSec")
$prepareArgs.Add([string]$TimeoutSec)
Add-StringArgIfPresent -ArgsList $prepareArgs -Name "-ApiBaseUrl" -Value $ApiBaseUrl
Add-StringArgIfPresent -ArgsList $prepareArgs -Name "-ApiBearerToken" -Value $ApiBearerToken
Add-StringArgIfPresent -ArgsList $prepareArgs -Name "-HistoryFilePath" -Value $HistoryFilePath
Add-StringArgIfPresent -ArgsList $prepareArgs -Name "-SettingsFilePath" -Value $SettingsFilePath

$journalArgs = New-Object System.Collections.Generic.List[string]
$journalArgs.Add("-NoProfile")
$journalArgs.Add("-ExecutionPolicy")
$journalArgs.Add("Bypass")
$journalArgs.Add("-File")
$journalArgs.Add($journalScriptPath)
$journalArgs.Add("-PgSnapshotPath")
$journalArgs.Add($PgSnapshotPath)
$journalArgs.Add("-JsonSnapshotPath")
$journalArgs.Add($JsonSnapshotPath)
$journalArgs.Add("-ResponsibleActor")
$journalArgs.Add($ResponsibleActor)
Add-StringArgIfPresent -ArgsList $journalArgs -Name "-ReportOutputPath" -Value $ReportOutputPath
Add-StringArgIfPresent -ArgsList $journalArgs -Name "-JournalPath" -Value $JournalPath

if ($DryRun) {
    $prepareArgs += "-DryRun"
}

& powershell.exe @prepareArgs
$prepareExitCode = $LASTEXITCODE
if ($prepareExitCode -ne 0) {
    exit $prepareExitCode
}

if ($DryRun) {
    Write-Host "Dry run: prepare step completed. Journal step skipped by design."
    exit 0
}

& powershell.exe @journalArgs
exit $LASTEXITCODE
