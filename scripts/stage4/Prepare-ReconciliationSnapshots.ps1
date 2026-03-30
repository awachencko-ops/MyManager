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
    [ValidateSet("Warn", "Fail")]
    [string]$ApiPreflightPolicy = "Warn",

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

function Convert-OrdersToJsonArray {
    param([object]$Orders)

    $normalized = @($Orders)
    if ($normalized.Count -eq 0) {
        return "[]"
    }

    if ($normalized.Count -eq 1) {
        $singleJson = $normalized[0] | ConvertTo-Json -Depth 50
        return "[`n$singleJson`n]"
    }

    return ($normalized | ConvertTo-Json -Depth 50)
}

function Get-ExceptionStatusCode {
    param([System.Exception]$Exception)

    if ($null -eq $Exception -or $null -eq $Exception.Response) {
        return $null
    }

    try {
        return [int]$Exception.Response.StatusCode
    }
    catch {
        return $null
    }
}

function Get-ExceptionMessage {
    param([System.Exception]$Exception)

    if ($null -eq $Exception) {
        return "unknown error"
    }

    if ($null -ne $Exception.InnerException -and -not [string]::IsNullOrWhiteSpace($Exception.InnerException.Message)) {
        return $Exception.InnerException.Message
    }

    return $Exception.Message
}

function Get-SloText {
    param([object]$LivePayload)

    $slo = Get-PropValue -Source $LivePayload -Names @("slo", "Slo") -Default $null
    if ($null -eq $slo) {
        return ""
    }

    if ($slo -is [string]) {
        return $slo
    }

    $sloStatus = Get-PropValue -Source $slo -Names @("status", "Status") -Default ""
    if (-not [string]::IsNullOrWhiteSpace([string]$sloStatus)) {
        return [string]$sloStatus
    }

    return [string]$slo
}

function Invoke-ReplicaApiGet {
    param(
        [string]$Uri,
        [hashtable]$Headers,
        [int]$TimeoutSec,
        [switch]$Allow404
    )

    try {
        return Invoke-RestMethod -Method Get -Uri $Uri -Headers $Headers -TimeoutSec $TimeoutSec
    }
    catch {
        $statusCode = Get-ExceptionStatusCode -Exception $_.Exception
        if ($Allow404 -and $statusCode -eq 404) {
            return $null
        }

        $message = Get-ExceptionMessage -Exception $_.Exception
        throw "API request failed for '$Uri': $message"
    }
}

function Test-ReplicaApiPreflight {
    param(
        [string]$ApiBaseUrl,
        [hashtable]$Headers,
        [int]$TimeoutSec,
        [string]$Policy
    )

    $baseUrl = $ApiBaseUrl.TrimEnd("/")
    $liveUrl = "$baseUrl/live"
    $ordersUrl = "$baseUrl/api/orders"

    $livePayload = Invoke-ReplicaApiGet -Uri $liveUrl -Headers $Headers -TimeoutSec $TimeoutSec -Allow404
    if ($null -eq $livePayload) {
        Write-Warning "Preflight: endpoint '/live' returned 404. Continuing with '/api/orders' probe."
    }
    else {
        $ready = Get-PropValue -Source $livePayload -Names @("ready", "Ready") -Default $null
        $status = [string](Get-PropValue -Source $livePayload -Names @("status", "Status") -Default "")
        $sloText = [string](Get-SloText -LivePayload $livePayload)

        $isReadyProblem = ($null -ne $ready -and -not [bool]$ready)
        $isDegradedProblem = ($status -match "(?i)degraded")
        $isSloProblem = (-not [string]::IsNullOrWhiteSpace($sloText) -and $sloText -notmatch "(?i)ok|healthy|ready")

        if ($isReadyProblem -or $isDegradedProblem -or $isSloProblem) {
            $humanWarning = "API is reachable but health flags are not ideal: ready=$ready, status='$status', slo='$sloText'."
            if ($Policy -eq "Fail") {
                throw "$humanWarning Preflight policy 'Fail' blocks snapshot creation."
            }

            Write-Warning "$humanWarning Continuing by preflight policy 'Warn'."
        }
        else {
            Write-Host "Preflight /live: ok (ready=$ready, status='$status', slo='$sloText')."
        }
    }

    return (Invoke-ReplicaApiGet -Uri $ordersUrl -Headers $Headers -TimeoutSec $TimeoutSec)
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
    Write-Host "ApiPreflightPolicy: $ApiPreflightPolicy"
    Write-Host "HistoryFilePath: $resolvedHistoryPath"
    Write-Host "SettingsFilePath: $resolvedSettingsPath"
    Write-Host "PgSnapshotPath: $resolvedPgSnapshotPath"
    Write-Host "JsonSnapshotPath: $resolvedJsonSnapshotPath"
    exit 0
}

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedPgSnapshotPath)) | Out-Null
[System.IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedJsonSnapshotPath)) | Out-Null

$headers = @{
    "Accept" = "application/json"
}
if (-not [string]::IsNullOrWhiteSpace($ApiBearerToken)) {
    $headers["Authorization"] = "Bearer $ApiBearerToken"
}
else {
    $headers["X-Current-User"] = $ApiActor
}

try {
    $pgOrders = Test-ReplicaApiPreflight -ApiBaseUrl $resolvedApiBaseUrl -Headers $headers -TimeoutSec $TimeoutSec -Policy $ApiPreflightPolicy
}
catch {
    Write-Host (@(
        "Failed to prepare snapshot from API."
        "Check that Replica.Api is running and reachable at '$resolvedApiBaseUrl'."
        "Also verify LanApiBaseUrl in settings.json and port/network availability."
        "Technical reason: $($_.Exception.Message)"
    ) -join " ")
    exit 1
}

$normalizedPgOrders = Normalize-OrdersArray -Source $pgOrders
$pgJson = Convert-OrdersToJsonArray -Orders $normalizedPgOrders
[System.IO.File]::WriteAllText($resolvedPgSnapshotPath, $pgJson, (New-Object System.Text.UTF8Encoding($true)))

$historyRaw = Get-Content -Path $resolvedHistoryPath -Raw -Encoding UTF8
$historyParsed = $historyRaw | ConvertFrom-Json
$normalizedHistory = Normalize-OrdersArray -Source $historyParsed
$jsonSnapshot = Convert-OrdersToJsonArray -Orders $normalizedHistory
[System.IO.File]::WriteAllText($resolvedJsonSnapshotPath, $jsonSnapshot, (New-Object System.Text.UTF8Encoding($true)))

Write-Host "Snapshots prepared."
Write-Host "PG snapshot: $resolvedPgSnapshotPath"
Write-Host "JSON snapshot: $resolvedJsonSnapshotPath"
Write-Host ("PG orders count: " + @($normalizedPgOrders).Count)
Write-Host ("JSON orders count: " + @($normalizedHistory).Count)
