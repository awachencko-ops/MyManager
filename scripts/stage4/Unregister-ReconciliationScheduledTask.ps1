param(
    [Parameter(Mandatory = $false)]
    [string]$TaskName = "Replica Stage4 Reconciliation Daily"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if (-not $existingTask) {
    Write-Host "Task '$TaskName' not found. Nothing to remove."
    exit 0
}

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
Write-Host "Task removed: $TaskName"
