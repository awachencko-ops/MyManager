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

function Convert-ToInt64Safe {
    param([object]$Value)
    if ($null -eq $Value) {
        return 0L
    }

    try {
        return [long]$Value
    }
    catch {
        return 0L
    }
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

function Normalize-OrdersArray {
    param([object]$Source)

    if ($null -eq $Source) {
        return @()
    }

    $ordersSource = $null
    if ($Source -is [System.Array]) {
        $ordersSource = $Source
    }
    elseif ($null -ne $Source.PSObject.Properties["Orders"]) {
        $ordersSource = $Source.Orders
    }
    elseif ($null -ne $Source.PSObject.Properties["orders"]) {
        $ordersSource = $Source.orders
    }
    else {
        $ordersSource = @()
    }

    $normalizedOrders = New-Object System.Collections.Generic.List[object]
    foreach ($order in @($ordersSource)) {
        if ($null -eq $order) { continue }

        $items = New-Object System.Collections.Generic.List[object]
        foreach ($item in @((Get-PropValue -Source $order -Names @("Items", "items") -Default @()))) {
            if ($null -eq $item) { continue }

            $items.Add([ordered]@{
                ItemId = [string](Get-PropValue -Source $item -Names @("ItemId", "itemId") -Default "")
                Version = Convert-ToInt64Safe (Get-PropValue -Source $item -Names @("Version", "StorageVersion", "version", "storageVersion") -Default 0)
                SequenceNo = [int](Convert-ToInt64Safe (Get-PropValue -Source $item -Names @("SequenceNo", "sequenceNo") -Default 0))
                ClientFileLabel = [string](Get-PropValue -Source $item -Names @("ClientFileLabel", "clientFileLabel") -Default "")
                Variant = [string](Get-PropValue -Source $item -Names @("Variant", "variant") -Default "")
                SourcePath = [string](Get-PropValue -Source $item -Names @("SourcePath", "sourcePath") -Default "")
                PreparedPath = [string](Get-PropValue -Source $item -Names @("PreparedPath", "preparedPath") -Default "")
                PrintPath = [string](Get-PropValue -Source $item -Names @("PrintPath", "printPath") -Default "")
                FileStatus = [string](Get-PropValue -Source $item -Names @("FileStatus", "fileStatus") -Default "")
                LastReason = [string](Get-PropValue -Source $item -Names @("LastReason", "lastReason") -Default "")
                UpdatedAt = (Get-PropValue -Source $item -Names @("UpdatedAt", "updatedAt") -Default $null)
            })
        }

        $normalizedOrders.Add([ordered]@{
            InternalId = [string](Get-PropValue -Source $order -Names @("InternalId", "internalId") -Default "")
            OrderNumber = [string](Get-PropValue -Source $order -Names @("OrderNumber", "orderNumber", "Id", "id") -Default "")
            Status = [string](Get-PropValue -Source $order -Names @("Status", "status") -Default "")
            Version = Convert-ToInt64Safe (Get-PropValue -Source $order -Names @("Version", "StorageVersion", "version", "storageVersion") -Default 0)
            UserName = [string](Get-PropValue -Source $order -Names @("UserName", "userName") -Default "")
            Keyword = [string](Get-PropValue -Source $order -Names @("Keyword", "keyword") -Default "")
            FolderName = [string](Get-PropValue -Source $order -Names @("FolderName", "folderName") -Default "")
            CreatedById = [string](Get-PropValue -Source $order -Names @("CreatedById", "createdById") -Default "")
            CreatedByUser = [string](Get-PropValue -Source $order -Names @("CreatedByUser", "createdByUser") -Default "")
            ManagerOrderDate = (Get-PropValue -Source $order -Names @("ManagerOrderDate", "managerOrderDate", "OrderDate", "orderDate") -Default $null)
            ArrivalDate = (Get-PropValue -Source $order -Names @("ArrivalDate", "arrivalDate") -Default $null)
            Items = $items
        })
    }

    return $normalizedOrders
}

$repoRoot = Resolve-RepoRoot -ScriptPath $PSCommandPath

$resolvedPgSnapshotPath = Resolve-PathFromRepo -RepoRoot $repoRoot -Candidate $PgSnapshotPath
$resolvedJsonSnapshotPath = Resolve-PathFromRepo -RepoRoot $repoRoot -Candidate $JsonSnapshotPath

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

if ($DryRun) {
    Write-Host "Dry run. No files written."
    Write-Host "ApiBaseUrl: $resolvedApiBaseUrl"
    Write-Host "HistoryFilePath: $resolvedHistoryPath"
    Write-Host "SettingsFilePath: $resolvedSettingsPath"
    Write-Host "PgSnapshotPath: $resolvedPgSnapshotPath"
    Write-Host "JsonSnapshotPath: $resolvedJsonSnapshotPath"
    exit 0
}

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedPgSnapshotPath)) | Out-Null
[System.IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedJsonSnapshotPath)) | Out-Null

$ordersUrl = "$($resolvedApiBaseUrl.TrimEnd('/'))/api/orders"
$headers = @{
    "Accept" = "application/json"
}
if (-not [string]::IsNullOrWhiteSpace($ApiBearerToken)) {
    $headers["Authorization"] = "Bearer $ApiBearerToken"
}
else {
    $headers["X-Current-User"] = $ApiActor
}

$pgOrders = Invoke-RestMethod -Method Get -Uri $ordersUrl -Headers $headers -TimeoutSec $TimeoutSec
$pgJson = $pgOrders | ConvertTo-Json -Depth 50
[System.IO.File]::WriteAllText($resolvedPgSnapshotPath, $pgJson, (New-Object System.Text.UTF8Encoding($true)))

$historyRaw = Get-Content -Path $resolvedHistoryPath -Raw -Encoding UTF8
$historyParsed = $historyRaw | ConvertFrom-Json
$normalizedHistory = Normalize-OrdersArray -Source $historyParsed
$jsonSnapshot = $normalizedHistory | ConvertTo-Json -Depth 50
[System.IO.File]::WriteAllText($resolvedJsonSnapshotPath, $jsonSnapshot, (New-Object System.Text.UTF8Encoding($true)))

Write-Host "Snapshots prepared."
Write-Host "PG snapshot: $resolvedPgSnapshotPath"
Write-Host "JSON snapshot: $resolvedJsonSnapshotPath"
Write-Host ("PG orders count: " + @($pgOrders).Count)
Write-Host ("JSON orders count: " + @($normalizedHistory).Count)
