[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [string]$OutJsonPath = "",
    [switch]$FailOnRisk
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot))
{
    $RepoRoot = Join-Path $PSScriptRoot "..\.."
}

$resolvedRepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$checks = New-Object System.Collections.Generic.List[object]

function Add-Check
{
    param(
        [string]$Name,
        [bool]$IsRisk,
        [string]$Message,
        [object]$Details
    )

    $checks.Add([PSCustomObject]@{
        name = $Name
        is_risk = $IsRisk
        message = $Message
        details = $Details
    }) | Out-Null
}

$apiSettingsPath = Join-Path $resolvedRepoRoot "Replica.Api\appsettings.json"
if (Test-Path -LiteralPath $apiSettingsPath)
{
    $apiSettings = Get-Content -LiteralPath $apiSettingsPath -Raw | ConvertFrom-Json
    $migrationSection = $apiSettings.ReplicaApi.Migration
    $dualWriteEnabled = [bool]$migrationSection.DualWriteEnabled
    $shadowPolicy = [string]$migrationSection.ShadowWriteFailurePolicy
    Add-Check `
        -Name "api_dual_write_flag" `
        -IsRisk $dualWriteEnabled `
        -Message ($(if ($dualWriteEnabled) { "ReplicaApi:Migration:DualWriteEnabled=true (cutover risk)." } else { "ReplicaApi:Migration:DualWriteEnabled=false." })) `
        -Details ([PSCustomObject]@{
            path = $apiSettingsPath
            dual_write_enabled = $dualWriteEnabled
            shadow_write_failure_policy = $shadowPolicy
        })
}
else
{
    Add-Check `
        -Name "api_dual_write_flag" `
        -IsRisk $true `
        -Message "Replica.Api/appsettings.json not found." `
        -Details ([PSCustomObject]@{ path = $apiSettingsPath })
}

$appSettingsSourcePath = Join-Path $resolvedRepoRoot "Models\AppSettings.cs"
if (Test-Path -LiteralPath $appSettingsSourcePath)
{
    $appSettingsSource = Get-Content -LiteralPath $appSettingsSourcePath -Raw
    $fileSystemDefault = [bool]($appSettingsSource -match "OrdersStorageBackend\s*\{[^\}]+\}\s*=\s*OrdersStorageMode\.FileSystem")
    Add-Check `
        -Name "client_default_storage_mode" `
        -IsRisk $fileSystemDefault `
        -Message ($(if ($fileSystemDefault) { "Client default storage mode is FileSystem (history.json)." } else { "Client default storage mode is not FileSystem." })) `
        -Details ([PSCustomObject]@{
            path = $appSettingsSourcePath
            file_system_default = $fileSystemDefault
        })
}
else
{
    Add-Check `
        -Name "client_default_storage_mode" `
        -IsRisk $true `
        -Message "Models/AppSettings.cs not found." `
        -Details ([PSCustomObject]@{ path = $appSettingsSourcePath })
}

$settingsDialogPath = Join-Path $resolvedRepoRoot "Forms\Settings\SettingsDialogForm.cs"
if (Test-Path -LiteralPath $settingsDialogPath)
{
    $dialogSource = Get-Content -LiteralPath $settingsDialogPath -Raw
    $hasFileModeOption = [bool]($dialogSource -match "OrdersStorageMode\.FileSystem")
    Add-Check `
        -Name "settings_ui_filesystem_mode_option" `
        -IsRisk $hasFileModeOption `
        -Message ($(if ($hasFileModeOption) { "Settings UI still exposes FileSystem mode." } else { "Settings UI does not expose FileSystem mode." })) `
        -Details ([PSCustomObject]@{
            path = $settingsDialogPath
            has_file_mode_option = $hasFileModeOption
        })
}
else
{
    Add-Check `
        -Name "settings_ui_filesystem_mode_option" `
        -IsRisk $true `
        -Message "Forms/Settings/SettingsDialogForm.cs not found." `
        -Details ([PSCustomObject]@{ path = $settingsDialogPath })
}

$stage4TaskName = "Replica Stage4 Reconciliation Daily"
try
{
    $stage4Task = Get-ScheduledTask -TaskName $stage4TaskName -ErrorAction Stop
    Add-Check `
        -Name "stage4_scheduler_task_registered" `
        -IsRisk $true `
        -Message "Stage 4 scheduler task is still registered." `
        -Details ([PSCustomObject]@{
            task_name = $stage4Task.TaskName
            state = [string]$stage4Task.State
        })
}
catch
{
    Add-Check `
        -Name "stage4_scheduler_task_registered" `
        -IsRisk $false `
        -Message "Stage 4 scheduler task is not registered." `
        -Details ([PSCustomObject]@{
            task_name = $stage4TaskName
        })
}

$riskChecks = @($checks | Where-Object { $_.is_risk })
$status = if ($riskChecks.Count -eq 0) { "ready_for_cutover" } else { "risk_detected" }

$result = [PSCustomObject]@{
    evaluated_at_local = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ss")
    repo_root = $resolvedRepoRoot
    status = $status
    risk_count = $riskChecks.Count
    checks = $checks
}

$json = $result | ConvertTo-Json -Depth 8
if (-not [string]::IsNullOrWhiteSpace($OutJsonPath))
{
    $outPath = if ([System.IO.Path]::IsPathRooted($OutJsonPath))
    {
        $OutJsonPath
    }
    else
    {
        Join-Path $resolvedRepoRoot $OutJsonPath
    }

    $outDirectory = Split-Path -Parent $outPath
    if (-not [string]::IsNullOrWhiteSpace($outDirectory))
    {
        New-Item -ItemType Directory -Path $outDirectory -Force | Out-Null
    }

    Set-Content -LiteralPath $outPath -Value $json -Encoding UTF8
    Write-Host "Cutover readiness JSON saved: $outPath"
}

Write-Host "Stage6 Cutover Readiness: $status (risks=$($riskChecks.Count))"
foreach ($check in $checks)
{
    $prefix = if ($check.is_risk) { "[RISK]" } else { "[OK]" }
    Write-Host "$prefix $($check.name): $($check.message)"
}

if ($FailOnRisk -and $riskChecks.Count -gt 0)
{
    exit 1
}
