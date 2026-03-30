param(
    [Parameter(Mandatory = $false)]
    [string]$TaskName = "Replica Stage4 Reconciliation Daily",

    [Parameter(Mandatory = $false)]
    [string]$At = "09:15",

    [Parameter(Mandatory = $false)]
    [string]$PgSnapshotPath = "artifacts/reconciliation/snapshots/pg.snapshot.json",

    [Parameter(Mandatory = $false)]
    [string]$JsonSnapshotPath = "artifacts/reconciliation/snapshots/json.snapshot.json",

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

    if ([System.IO.Path]::IsPathRooted($Candidate)) {
        return (Resolve-Path $Candidate).Path
    }

    return (Resolve-Path (Join-Path $RepoRoot $Candidate)).Path
}

function Require-File {
    param([string]$PathToCheck)
    if (-not (Test-Path $PathToCheck)) {
        throw "Required file not found: $PathToCheck"
    }
}

$repoRoot = Resolve-RepoRoot -ScriptPath $PSCommandPath
$runScriptPath = Resolve-PathFromRepo -RepoRoot $repoRoot -Candidate "scripts/stage4/Run-ReconciliationJournal.ps1"
$resolvedPgPath = Resolve-PathFromRepo -RepoRoot $repoRoot -Candidate $PgSnapshotPath
$resolvedJsonPath = Resolve-PathFromRepo -RepoRoot $repoRoot -Candidate $JsonSnapshotPath

Require-File -PathToCheck $runScriptPath
Require-File -PathToCheck $resolvedPgPath
Require-File -PathToCheck $resolvedJsonPath

try {
    $triggerTime = [DateTime]::ParseExact($At, "HH:mm", [System.Globalization.CultureInfo]::InvariantCulture)
}
catch {
    throw "Invalid -At value '$At'. Expected HH:mm (e.g. 09:15)."
}

$psArgs = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", "`"$runScriptPath`"",
    "-PgSnapshotPath", "`"$resolvedPgPath`"",
    "-JsonSnapshotPath", "`"$resolvedJsonPath`"",
    "-ResponsibleActor", "`"$ResponsibleActor`""
) -join " "

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
    Write-Host "RunScript: $runScriptPath"
    Write-Host "PgSnapshotPath: $resolvedPgPath"
    Write-Host "JsonSnapshotPath: $resolvedJsonPath"
    Write-Host "ResponsibleActor: $ResponsibleActor"
    Write-Host "Action: powershell.exe $psArgs"
    if ($existingTask) {
        Write-Host "Existing task found."
    } else {
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
