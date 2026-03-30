param(
    [Parameter(Mandatory = $false)]
    [string]$TaskName = "Replica Stage4 Reconciliation Daily",

    [Parameter(Mandatory = $false)]
    [string]$At = "09:15",

    [Parameter(Mandatory = $false)]
    [ValidateSet("LiveSources", "StaticSnapshots")]
    [string]$Mode = "LiveSources",

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
    [ValidateRange(1, 600)]
    [int]$TimeoutSec = 30,

    [Parameter(Mandatory = $false)]
    [ValidateSet("Warn", "Fail")]
    [string]$ApiPreflightPolicy = "Warn",

    [Parameter(Mandatory = $false)]
    [string]$ResponsibleActor = "task-scheduler",

    [Parameter(Mandatory = $false)]
    [switch]$ForceRecreate,

    [Parameter(Mandatory = $false)]
    [switch]$RunNow,

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    param([string]$ScriptPath)
    return (Resolve-Path (Join-Path (Split-Path -Parent $ScriptPath) "..\..")).Path
}

function Resolve-PathFromRepo {
    param(
        [string]$RepoRoot,
        [string]$Candidate
    )

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return ""
    }

    if ([System.IO.Path]::IsPathRooted($Candidate)) {
        return (Resolve-Path $Candidate).Path
    }

    return (Resolve-Path (Join-Path $RepoRoot $Candidate)).Path
}

function Resolve-PathCandidateFromRepo {
    param(
        [string]$RepoRoot,
        [string]$Candidate
    )

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return ""
    }

    if ([System.IO.Path]::IsPathRooted($Candidate)) {
        return [System.IO.Path]::GetFullPath($Candidate)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Candidate))
}

function Require-File {
    param([string]$PathToCheck)
    if (-not (Test-Path $PathToCheck)) {
        throw "Required file not found: $PathToCheck"
    }
}

$repoRoot = Resolve-RepoRoot -ScriptPath $PSCommandPath
$runJournalScriptPath = Resolve-PathFromRepo -RepoRoot $repoRoot -Candidate "scripts/stage4/Run-ReconciliationJournal.ps1"
$runLiveScriptPath = Resolve-PathFromRepo -RepoRoot $repoRoot -Candidate "scripts/stage4/Run-ReconciliationLive.ps1"

Require-File -PathToCheck $runJournalScriptPath
Require-File -PathToCheck $runLiveScriptPath

$resolvedPgPathForInput = if ($Mode -eq "StaticSnapshots") {
    Resolve-PathFromRepo -RepoRoot $repoRoot -Candidate $PgSnapshotPath
}
else {
    Resolve-PathCandidateFromRepo -RepoRoot $repoRoot -Candidate $PgSnapshotPath
}

$resolvedJsonPathForInput = if ($Mode -eq "StaticSnapshots") {
    Resolve-PathFromRepo -RepoRoot $repoRoot -Candidate $JsonSnapshotPath
}
else {
    Resolve-PathCandidateFromRepo -RepoRoot $repoRoot -Candidate $JsonSnapshotPath
}

if ($Mode -eq "StaticSnapshots") {
    Require-File -PathToCheck $resolvedPgPathForInput
    Require-File -PathToCheck $resolvedJsonPathForInput
}

try {
    $triggerTime = [DateTime]::ParseExact($At, "HH:mm", [System.Globalization.CultureInfo]::InvariantCulture)
}
catch {
    throw "Invalid -At value '$At'. Expected HH:mm (e.g. 09:15)."
}

if ($Mode -eq "LiveSources") {
    $targetScriptPath = $runLiveScriptPath
    $psArgs = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$targetScriptPath`"",
        "-PgSnapshotPath", "`"$resolvedPgPathForInput`"",
        "-JsonSnapshotPath", "`"$resolvedJsonPathForInput`"",
        "-ApiBaseUrl", "`"$ApiBaseUrl`"",
        "-ApiActor", "`"$ApiActor`"",
        "-ApiBearerToken", "`"$ApiBearerToken`"",
        "-HistoryFilePath", "`"$HistoryFilePath`"",
        "-SettingsFilePath", "`"$SettingsFilePath`"",
        "-TimeoutSec", "`"$TimeoutSec`"",
        "-ApiPreflightPolicy", "`"$ApiPreflightPolicy`"",
        "-ResponsibleActor", "`"$ResponsibleActor`""
    ) -join " "
}
else {
    $targetScriptPath = $runJournalScriptPath
    $psArgs = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$targetScriptPath`"",
        "-PgSnapshotPath", "`"$resolvedPgPathForInput`"",
        "-JsonSnapshotPath", "`"$resolvedJsonPathForInput`"",
        "-ResponsibleActor", "`"$ResponsibleActor`""
    ) -join " "
}

$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $psArgs
$trigger = New-ScheduledTaskTrigger -Daily -At $triggerTime
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew

$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

if ($DryRun) {
    Write-Host "Dry run. No changes applied."
    Write-Host "TaskName: $TaskName"
    Write-Host "At: $At"
    Write-Host "Mode: $Mode"
    Write-Host "Script: $targetScriptPath"
    Write-Host "PgSnapshotPath: $resolvedPgPathForInput"
    Write-Host "JsonSnapshotPath: $resolvedJsonPathForInput"
    Write-Host "ResponsibleActor: $ResponsibleActor"
    if ($Mode -eq "LiveSources") {
        Write-Host "ApiBaseUrl: $ApiBaseUrl"
        Write-Host "ApiActor: $ApiActor"
        Write-Host "HistoryFilePath: $HistoryFilePath"
        Write-Host "SettingsFilePath: $SettingsFilePath"
        Write-Host "TimeoutSec: $TimeoutSec"
        Write-Host "ApiPreflightPolicy: $ApiPreflightPolicy"
    }
    Write-Host "Action: powershell.exe $psArgs"
    if ($existingTask) {
        Write-Host "Existing task found."
    }
    else {
        Write-Host "Task does not exist yet."
    }
    exit 0
}

if ($existingTask) {
    if (-not $ForceRecreate) {
        throw "Task '$TaskName' already exists. Use -ForceRecreate to replace it."
    }

    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Description "Replica Stage4 reconciliation + journal append"

Write-Host "Scheduled task registered: $TaskName"
Write-Host "Next run time:"
Get-ScheduledTaskInfo -TaskName $TaskName | Select-Object LastRunTime, LastTaskResult, NextRunTime | Format-Table -AutoSize

if ($RunNow) {
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "Task started manually: $TaskName"
}
