param(
    [Parameter(Mandatory = $false)]
    [string]$PgSnapshotPath = "artifacts/reconciliation/snapshots/pg.snapshot.json",

    [Parameter(Mandatory = $false)]
    [string]$JsonSnapshotPath = "artifacts/reconciliation/snapshots/json.snapshot.json",

    [Parameter(Mandatory = $false)]
    [string]$ReportOutputPath = "",

    [Parameter(Mandatory = $false)]
    [string]$JournalPath = "",

    [Parameter(Mandatory = $false)]
    [string]$ResponsibleActor = ""
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

function Normalize-Lf {
    param([string]$Text)
    return ($Text -replace "`r`n", "`n" -replace "`r", "`n")
}

if ([string]::IsNullOrWhiteSpace($ResponsibleActor)) {
    $ResponsibleActor = if ([string]::IsNullOrWhiteSpace($env:USERNAME)) { "unknown-operator" } else { $env:USERNAME }
}

$repoRoot = Resolve-RepoRoot -ScriptPath $PSCommandPath
Push-Location $repoRoot
try {
    $resolvedPgPath = Resolve-PathFromRepo -RepoRoot $repoRoot -Candidate $PgSnapshotPath
    $resolvedJsonPath = Resolve-PathFromRepo -RepoRoot $repoRoot -Candidate $JsonSnapshotPath

    if ([string]::IsNullOrWhiteSpace($ReportOutputPath)) {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $ReportOutputPath = "artifacts/reconciliation/reports/reconciliation-$timestamp.json"
    }

    $resolvedReportPath = if ([System.IO.Path]::IsPathRooted($ReportOutputPath)) {
        $ReportOutputPath
    } else {
        Join-Path $repoRoot $ReportOutputPath
    }
    $reportDirectory = Split-Path -Parent $resolvedReportPath
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        [System.IO.Directory]::CreateDirectory($reportDirectory) | Out-Null
    }

    if ([string]::IsNullOrWhiteSpace($JournalPath)) {
        $resolvedJournalPath = Get-ChildItem -Path (Join-Path $repoRoot "Docs") -Recurse -File -Filter "REPLICA_STAGE4_EXECUTION_JOURNAL_*.md" |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1 -ExpandProperty FullName
        if ([string]::IsNullOrWhiteSpace($resolvedJournalPath)) {
            throw "Journal file could not be auto-resolved. Pass -JournalPath explicitly."
        }
    } else {
        $resolvedJournalPath = if ([System.IO.Path]::IsPathRooted($JournalPath)) {
            $JournalPath
        } else {
            Join-Path $repoRoot $JournalPath
        }
    }
    if (-not (Test-Path $resolvedJournalPath)) {
        throw "Journal file not found: $resolvedJournalPath"
    }

    dotnet run --project tools/Replica.Reconciliation.Cli -- --pg $resolvedPgPath --json $resolvedJsonPath --out $resolvedReportPath
    $cliExitCode = $LASTEXITCODE

    if (-not (Test-Path $resolvedReportPath)) {
        throw "Reconciliation report was not created: $resolvedReportPath"
    }

    $report = Get-Content -Path $resolvedReportPath -Raw | ConvertFrom-Json
    $summary = $report.summary
    $isZeroDiff = [bool]$summary.is_zero_diff
    $isZeroDiffText = if ($isZeroDiff) { "true" } else { "false" }
    $decision = if ($isZeroDiff) { "Continue rollout preparation." } else { "Pause expansion rollout and start incident workflow." }

    $timezoneLabel = [System.TimeZoneInfo]::Local.StandardName
    $nowLocal = Get-Date
    try {
        $vladivostokTz = [System.TimeZoneInfo]::FindSystemTimeZoneById("Vladivostok Standard Time")
        $nowLocal = [System.TimeZoneInfo]::ConvertTime((Get-Date), $vladivostokTz)
        $timezoneLabel = "Asia/Vladivostok"
    }
    catch {
        # Fallback to host-local timezone when Vladivostok TZ id is unavailable.
        $timezoneLabel = [System.TimeZoneInfo]::Local.StandardName
    }
    $timestampLine = $nowLocal.ToString("yyyy-MM-dd HH:mm")

    $entry = @"
### $timestampLine ($timezoneLabel)

1. Responsible actor: $ResponsibleActor
2. Backups:
   - history.json immutable copy: $(if (Test-Path $resolvedJsonPath) { "available" } else { "missing" }).
   - pg snapshot: $(if (Test-Path $resolvedPgPath) { "available" } else { "missing" }).
3. Reconciliation summary:
   - missing_in_pg = $($summary.missing_in_pg)
   - missing_in_json = $($summary.missing_in_json)
   - version_mismatch = $($summary.version_mismatch)
   - payload_mismatch = $($summary.payload_mismatch)
   - is_zero_diff = $isZeroDiffText
4. Decision: $decision
5. Notes: report_path=$resolvedReportPath; cli_exit_code=$cliExitCode.
"@

    $journalContent = [System.IO.File]::ReadAllText($resolvedJournalPath, [System.Text.Encoding]::UTF8)
    $journalContent = Normalize-Lf -Text $journalContent
    if (-not $journalContent.EndsWith("`n")) {
        $journalContent += "`n"
    }

    $journalContent += "`n" + (Normalize-Lf -Text $entry) + "`n"

    $utf8Bom = New-Object System.Text.UTF8Encoding($true)
    [System.IO.File]::WriteAllText($resolvedJournalPath, $journalContent, $utf8Bom)

    Write-Host "Journal updated: $resolvedJournalPath"
    Write-Host "Report: $resolvedReportPath"
    Write-Host "CLI exit code: $cliExitCode"

    if ($cliExitCode -eq 1) {
        exit 1
    }

    if ($cliExitCode -eq 2) {
        exit 2
    }

    exit 0
}
finally {
    Pop-Location
}
