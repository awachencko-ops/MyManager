param(
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

function Resolve-PathFromRepo {
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

function Get-FirstExistingPath {
    param([string[]]$Candidates)
    foreach ($candidate in ($Candidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    return ""
}

function Get-PropValue {
    param(
        [object]$Source,
        [string[]]$Names,
        [object]$Default = $null
    )

    if ($null -eq $Source) {
        return $Default
    }

    foreach ($name in $Names) {
        $property = $Source.PSObject.Properties[$name]
        if ($null -ne $property) {
            return $property.Value
        }
    }

    return $Default
}

$repoRoot = Resolve-RepoRoot -ScriptPath $PSCommandPath

$settingsCandidates = @()
if (-not [string]::IsNullOrWhiteSpace($SettingsFilePath)) {
    $settingsCandidates += (Resolve-PathFromRepo -RepoRoot $repoRoot -Candidate $SettingsFilePath)
}
$settingsCandidates += (Join-Path $repoRoot "settings.json")
$settingsCandidates += (Join-Path $repoRoot "bin/Debug/net8.0-windows/settings.json")
$settingsCandidates += (Join-Path $repoRoot "bin/Release/net8.0-windows/settings.json")
$resolvedSettingsPath = Get-FirstExistingPath -Candidates $settingsCandidates

$settings = $null
if (-not [string]::IsNullOrWhiteSpace($resolvedSettingsPath)) {
    $settingsRaw = Get-Content -Path $resolvedSettingsPath -Raw -Encoding UTF8
    $settings = $settingsRaw | ConvertFrom-Json
}

$resolvedApiBaseUrl = $ApiBaseUrl
if ([string]::IsNullOrWhiteSpace($resolvedApiBaseUrl) -and $null -ne $settings) {
    $resolvedApiBaseUrl = [string](Get-PropValue -Source $settings -Names @("LanApiBaseUrl") -Default "")
}
if ([string]::IsNullOrWhiteSpace($resolvedApiBaseUrl)) {
    $resolvedApiBaseUrl = "http://localhost:5000/"
}

$resolvedHistoryPath = $HistoryFilePath
if ([string]::IsNullOrWhiteSpace($resolvedHistoryPath) -and $null -ne $settings) {
    $settingsHistoryPath = [string](Get-PropValue -Source $settings -Names @("HistoryFilePath") -Default "")
    if (-not [string]::IsNullOrWhiteSpace($settingsHistoryPath)) {
        if ([System.IO.Path]::IsPathRooted($settingsHistoryPath)) {
            $resolvedHistoryPath = $settingsHistoryPath
        }
        elseif (-not [string]::IsNullOrWhiteSpace($resolvedSettingsPath)) {
            $settingsDirectory = Split-Path -Parent $resolvedSettingsPath
            $resolvedHistoryPath = Join-Path $settingsDirectory $settingsHistoryPath
        }
    }
}

if ([string]::IsNullOrWhiteSpace($resolvedHistoryPath)) {
    $historyCandidates = @(
        (Join-Path $repoRoot "bin/Debug/net8.0-windows/history.json"),
        (Join-Path $repoRoot "bin/Release/net8.0-windows/history.json"),
        (Join-Path $repoRoot "bin/Debug/net7.0-windows/history.json"),
        (Join-Path $repoRoot "bin/Release/net7.0-windows/history.json")
    )
    $resolvedHistoryPath = Get-FirstExistingPath -Candidates $historyCandidates
}
else {
    $resolvedHistoryPath = Resolve-PathFromRepo -RepoRoot $repoRoot -Candidate $resolvedHistoryPath
}

if ([string]::IsNullOrWhiteSpace($resolvedHistoryPath) -or -not (Test-Path $resolvedHistoryPath)) {
    throw "History file not found. Provide -HistoryFilePath explicitly."
}

$headers = @{
    "Accept" = "application/json"
}
if (-not [string]::IsNullOrWhiteSpace($ApiBearerToken)) {
    $headers["Authorization"] = "Bearer $ApiBearerToken"
}
else {
    $headers["X-Current-User"] = $ApiActor
}

$ordersUrl = "$($resolvedApiBaseUrl.TrimEnd('/'))/api/orders"
$pgOrders = Invoke-RestMethod -Method Get -Uri $ordersUrl -Headers $headers -TimeoutSec $TimeoutSec

$historyRaw = Get-Content -Path $resolvedHistoryPath -Raw -Encoding UTF8
$historyData = $historyRaw | ConvertFrom-Json

$historyOrders = @()
if ($historyData -is [System.Array]) {
    $historyOrders = @($historyData)
}
elseif ($null -ne $historyData.PSObject.Properties["Orders"]) {
    $historyOrders = @($historyData.Orders)
}
elseif ($null -ne $historyData.PSObject.Properties["orders"]) {
    $historyOrders = @($historyData.orders)
}
else {
    throw "History JSON does not contain an orders array."
}

$pgOrderNumberByInternalId = @{}
foreach ($pgOrder in @($pgOrders)) {
    $internalId = [string](Get-PropValue -Source $pgOrder -Names @("internalId", "InternalId") -Default "")
    $orderNumber = [string](Get-PropValue -Source $pgOrder -Names @("orderNumber", "OrderNumber") -Default "")
    if ([string]::IsNullOrWhiteSpace($internalId) -or [string]::IsNullOrWhiteSpace($orderNumber)) {
        continue
    }

    $pgOrderNumberByInternalId[$internalId.Trim()] = $orderNumber
}

$patchedInternalIds = New-Object System.Collections.Generic.List[string]
foreach ($historyOrder in $historyOrders) {
    if ($null -eq $historyOrder) {
        continue
    }

    $internalId = [string](Get-PropValue -Source $historyOrder -Names @("InternalId", "internalId") -Default "")
    if ([string]::IsNullOrWhiteSpace($internalId)) {
        continue
    }
    $internalId = $internalId.Trim()

    if (-not $pgOrderNumberByInternalId.ContainsKey($internalId)) {
        continue
    }

    $currentId = [string](Get-PropValue -Source $historyOrder -Names @("Id", "id", "OrderNumber", "orderNumber") -Default "")
    if (-not [string]::IsNullOrWhiteSpace($currentId)) {
        continue
    }

    $newOrderNumber = [string]$pgOrderNumberByInternalId[$internalId]
    if ([string]::IsNullOrWhiteSpace($newOrderNumber)) {
        continue
    }

    if ($null -ne $historyOrder.PSObject.Properties["Id"]) {
        $historyOrder.Id = $newOrderNumber
    }
    elseif ($null -ne $historyOrder.PSObject.Properties["id"]) {
        $historyOrder.id = $newOrderNumber
    }
    elseif ($null -ne $historyOrder.PSObject.Properties["OrderNumber"]) {
        $historyOrder.OrderNumber = $newOrderNumber
    }
    elseif ($null -ne $historyOrder.PSObject.Properties["orderNumber"]) {
        $historyOrder.orderNumber = $newOrderNumber
    }
    else {
        $historyOrder | Add-Member -NotePropertyName "Id" -NotePropertyValue $newOrderNumber
    }

    $patchedInternalIds.Add($internalId)
}

Write-Host "Resolved history path: $resolvedHistoryPath"
Write-Host "Found patch candidates: $($patchedInternalIds.Count)"
if ($patchedInternalIds.Count -gt 0) {
    Write-Host ("Patched internal ids: " + (($patchedInternalIds | Sort-Object) -join ", "))
}

if ($DryRun) {
    Write-Host "Dry run: no files changed."
    exit 0
}

if ($patchedInternalIds.Count -eq 0) {
    Write-Host "No changes were required."
    exit 0
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupPath = "$resolvedHistoryPath.bak-$timestamp"
Copy-Item -LiteralPath $resolvedHistoryPath -Destination $backupPath -Force

$jsonOutput = $historyData | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($resolvedHistoryPath, $jsonOutput, (New-Object System.Text.UTF8Encoding($true)))

Write-Host "Backup created: $backupPath"
Write-Host "History file updated."
