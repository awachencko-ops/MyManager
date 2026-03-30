param(
    [Parameter(Mandatory = $false)]
    [string]$TaskName = "Replica Stage4 Reconciliation Daily",

    [Parameter(Mandatory = $false)]
    [string]$ApiBaseUrl = "",

    [Parameter(Mandatory = $false)]
    [string]$SettingsFilePath = "",

    [Parameter(Mandatory = $false)]
    [string]$ReportDirectory = "artifacts/reconciliation/reports",

    [Parameter(Mandatory = $false)]
    [string]$JournalPath = "",

    [Parameter(Mandatory = $false)]
    [int]$TimeoutSec = 5,

    [Parameter(Mandatory = $false)]
    [switch]$FailOnRisk
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

function Get-LiveStatus {
    param(
        [string]$ResolvedApiBaseUrl,
        [int]$TimeoutSec
    )

    $liveUrl = "$($ResolvedApiBaseUrl.TrimEnd('/'))/live"
    try {
        $payload = Invoke-RestMethod -Method Get -Uri $liveUrl -TimeoutSec $TimeoutSec
        return [ordered]@{
            reachable = $true
            url = $liveUrl
            status = [string](Get-PropValue -Source $payload -Names @("status", "Status") -Default "")
            ready = (Get-PropValue -Source $payload -Names @("ready", "Ready") -Default $null)
            slo = [string](Get-SloText -LivePayload $payload)
            error = ""
        }
    }
    catch {
        return [ordered]@{
            reachable = $false
            url = $liveUrl
            status = ""
            ready = $null
            slo = ""
            error = $_.Exception.Message
        }
    }
}

function Get-SchedulerStatus {
    param([string]$TaskName)

    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        return [ordered]@{
            exists = $false
            state = ""
            last_run_time = $null
            last_task_result = $null
            next_run_time = $null
        }
    }

    $info = Get-ScheduledTaskInfo -TaskName $TaskName
    return [ordered]@{
        exists = $true
        state = [string]$task.State
        last_run_time = [string]$info.LastRunTime
        last_task_result = $info.LastTaskResult
        next_run_time = [string]$info.NextRunTime
    }
}

function Get-LatestReportStatus {
    param(
        [string]$RepoRoot,
        [string]$ReportDirectory
    )

    $resolvedReportDirectory = Resolve-PathFromRepo -RepoRoot $RepoRoot -Candidate $ReportDirectory
    if ([string]::IsNullOrWhiteSpace($resolvedReportDirectory) -or -not (Test-Path $resolvedReportDirectory)) {
        return [ordered]@{
            exists = $false
            path = ""
            generated_at_utc = ""
            summary = $null
            error = "Report directory not found."
        }
    }

    $latest = Get-ChildItem -Path $resolvedReportDirectory -File -Filter "*.json" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $latest) {
        return [ordered]@{
            exists = $false
            path = ""
            generated_at_utc = ""
            summary = $null
            error = "No report files found."
        }
    }

    try {
        $report = Get-Content -Path $latest.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        $summaryRaw = Get-PropValue -Source $report -Names @("summary") -Default $null
        $summary = $null
        if ($null -ne $summaryRaw) {
            $summary = [pscustomobject][ordered]@{
                missing_in_pg = [int](Get-PropValue -Source $summaryRaw -Names @("missing_in_pg") -Default 0)
                missing_in_json = [int](Get-PropValue -Source $summaryRaw -Names @("missing_in_json") -Default 0)
                version_mismatch = [int](Get-PropValue -Source $summaryRaw -Names @("version_mismatch") -Default 0)
                payload_mismatch = [int](Get-PropValue -Source $summaryRaw -Names @("payload_mismatch") -Default 0)
                is_zero_diff = [bool](Get-PropValue -Source $summaryRaw -Names @("is_zero_diff") -Default $false)
            }
        }

        return [ordered]@{
            exists = $true
            path = $latest.FullName
            generated_at_utc = [string](Get-PropValue -Source $report -Names @("generated_at_utc") -Default "")
            summary = $summary
            error = ""
        }
    }
    catch {
        return [ordered]@{
            exists = $true
            path = $latest.FullName
            generated_at_utc = ""
            summary = $null
            error = "Failed to parse report JSON: $($_.Exception.Message)"
        }
    }
}

function Get-LatestJournalEntry {
    param(
        [string]$RepoRoot,
        [string]$JournalPath
    )

    $resolvedJournalPath = ""
    if (-not [string]::IsNullOrWhiteSpace($JournalPath)) {
        $resolvedJournalPath = Resolve-PathFromRepo -RepoRoot $RepoRoot -Candidate $JournalPath
    }
    else {
        $resolvedJournalPath = Get-ChildItem -Path (Join-Path $RepoRoot "Docs") -Recurse -File -Filter "REPLICA_STAGE4_EXECUTION_JOURNAL_*.md" |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }

    if ([string]::IsNullOrWhiteSpace($resolvedJournalPath) -or -not (Test-Path $resolvedJournalPath)) {
        return [ordered]@{
            exists = $false
            path = ""
            heading = ""
            decision = ""
            error = "Journal file not found."
        }
    }

    $lines = Get-Content -Path $resolvedJournalPath -Encoding UTF8
    $headingIndices = @()
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -like "### *") {
            $headingIndices += $i
        }
    }

    if ($headingIndices.Count -eq 0) {
        return [ordered]@{
            exists = $true
            path = $resolvedJournalPath
            heading = ""
            decision = ""
            error = "No journal entries found."
        }
    }

    $startIndex = $headingIndices[-1]
    $endIndex = $lines.Count - 1
    $heading = $lines[$startIndex]
    $decisionLine = ""
    for ($i = $startIndex; $i -le $endIndex; $i++) {
        if ($lines[$i] -match "^\s*4\.\s*Decision:\s*(.+)\s*$") {
            $decisionLine = $Matches[1]
            break
        }
    }

    return [ordered]@{
        exists = $true
        path = $resolvedJournalPath
        heading = $heading
        decision = $decisionLine
        error = ""
    }
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
    try {
        $settingsRaw = Get-Content -Path $resolvedSettingsPath -Raw -Encoding UTF8
        $settings = $settingsRaw | ConvertFrom-Json
    }
    catch {
        $settings = $null
    }
}

$resolvedApiBaseUrl = $ApiBaseUrl
if ([string]::IsNullOrWhiteSpace($resolvedApiBaseUrl) -and $null -ne $settings) {
    $resolvedApiBaseUrl = [string](Get-PropValue -Source $settings -Names @("LanApiBaseUrl") -Default "")
}
if ([string]::IsNullOrWhiteSpace($resolvedApiBaseUrl)) {
    $resolvedApiBaseUrl = "http://localhost:5000/"
}

$liveStatus = Get-LiveStatus -ResolvedApiBaseUrl $resolvedApiBaseUrl -TimeoutSec $TimeoutSec
$schedulerStatus = Get-SchedulerStatus -TaskName $TaskName
$reportStatus = Get-LatestReportStatus -RepoRoot $repoRoot -ReportDirectory $ReportDirectory
$journalEntry = Get-LatestJournalEntry -RepoRoot $repoRoot -JournalPath $JournalPath

$overall = "OK"
$risks = New-Object System.Collections.Generic.List[string]

if (-not [bool]$liveStatus.reachable) {
    $overall = "ATTENTION"
    $risks.Add("API is unreachable: $($liveStatus.error)")
}
else {
    $ready = $liveStatus.ready
    $statusText = [string]$liveStatus.status
    $sloText = [string]$liveStatus.slo
    if (($null -ne $ready -and -not [bool]$ready) -or $statusText -match "(?i)degraded" -or (-not [string]::IsNullOrWhiteSpace($sloText) -and $sloText -notmatch "(?i)ok|healthy|ready")) {
        if ($overall -ne "ATTENTION") {
            $overall = "WARNING"
        }
        $risks.Add("API health flags are not ideal: ready=$ready, status='$statusText', slo='$sloText'.")
    }
}

if (-not [bool]$schedulerStatus.exists) {
    $overall = "ATTENTION"
    $risks.Add("Scheduled task '$TaskName' not found.")
}
else {
    $lastTaskResult = $schedulerStatus.last_task_result
    if ($null -ne $lastTaskResult -and [int]$lastTaskResult -ne 0) {
        $overall = "ATTENTION"
        $risks.Add("Scheduled task last result is non-zero: $lastTaskResult.")
    }
}

if (-not [bool]$reportStatus.exists) {
    if ($overall -ne "ATTENTION") {
        $overall = "WARNING"
    }
    $risks.Add("Latest report is unavailable: $($reportStatus.error)")
}
elseif (-not [string]::IsNullOrWhiteSpace([string]$reportStatus.error)) {
    $overall = "ATTENTION"
    $risks.Add("Latest report parse error: $($reportStatus.error)")
}
else {
    $summary = $reportStatus.summary
    if ($null -eq $summary) {
        $overall = "ATTENTION"
        $risks.Add("Latest report summary is missing.")
    }
    else {
        $isZeroDiff = [bool](Get-PropValue -Source $summary -Names @("is_zero_diff") -Default $false)
        if (-not $isZeroDiff) {
            $overall = "ATTENTION"
            $risks.Add("Latest reconciliation report is not zero-diff.")
        }
    }
}

$result = [ordered]@{
    generated_at_local = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    overall_status = $overall
    api_base_url = $resolvedApiBaseUrl
    api_live = $liveStatus
    scheduled_task = $schedulerStatus
    latest_report = $reportStatus
    latest_journal_entry = $journalEntry
    risks = @($risks)
}

Write-Host "Replica Stage4 Ops Status @ $($result.generated_at_local)"
Write-Host "Overall: $($result.overall_status)"
Write-Host ""
Write-Host "API:"
Write-Host "  Url: $($result.api_live.url)"
Write-Host "  Reachable: $($result.api_live.reachable)"
Write-Host "  Status: $($result.api_live.status)"
Write-Host "  Ready: $($result.api_live.ready)"
Write-Host "  SLO: $($result.api_live.slo)"
if (-not [string]::IsNullOrWhiteSpace([string]$result.api_live.error)) {
    Write-Host "  Error: $($result.api_live.error)"
}
Write-Host ""
Write-Host "Scheduler:"
Write-Host "  Task: $TaskName"
Write-Host "  Exists: $($result.scheduled_task.exists)"
Write-Host "  State: $($result.scheduled_task.state)"
Write-Host "  LastRunTime: $($result.scheduled_task.last_run_time)"
Write-Host "  LastTaskResult: $($result.scheduled_task.last_task_result)"
Write-Host "  NextRunTime: $($result.scheduled_task.next_run_time)"
Write-Host ""
Write-Host "Latest Report:"
Write-Host "  Path: $($result.latest_report.path)"
Write-Host "  GeneratedAtUtc: $($result.latest_report.generated_at_utc)"
if ($null -ne $result.latest_report.summary) {
    Write-Host "  missing_in_pg: $($result.latest_report.summary.missing_in_pg)"
    Write-Host "  missing_in_json: $($result.latest_report.summary.missing_in_json)"
    Write-Host "  version_mismatch: $($result.latest_report.summary.version_mismatch)"
    Write-Host "  payload_mismatch: $($result.latest_report.summary.payload_mismatch)"
    Write-Host "  is_zero_diff: $($result.latest_report.summary.is_zero_diff)"
}
if (-not [string]::IsNullOrWhiteSpace([string]$result.latest_report.error)) {
    Write-Host "  Error: $($result.latest_report.error)"
}
Write-Host ""
Write-Host "Latest Journal Entry:"
Write-Host "  Path: $($result.latest_journal_entry.path)"
Write-Host "  Heading: $($result.latest_journal_entry.heading)"
Write-Host "  Decision: $($result.latest_journal_entry.decision)"
if (-not [string]::IsNullOrWhiteSpace([string]$result.latest_journal_entry.error)) {
    Write-Host "  Error: $($result.latest_journal_entry.error)"
}
Write-Host ""
if ($result.risks.Count -eq 0) {
    Write-Host "Risks: none"
}
else {
    Write-Host "Risks:"
    foreach ($risk in $result.risks) {
        Write-Host "  - $risk"
    }
}

if ($FailOnRisk -and $result.overall_status -ne "OK") {
    exit 2
}

exit 0
