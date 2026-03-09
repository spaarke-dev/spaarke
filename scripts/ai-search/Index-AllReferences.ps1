# Index-AllReferences.ps1
# Batch index all KNW-*.md golden reference documents into the spaarke-rag-references index.
#
# Usage:
#   .\Index-AllReferences.ps1
#   .\Index-AllReferences.ps1 -DryRun
#   .\Index-AllReferences.ps1 -SourceDir "path\to\knowledge-sources"
#   .\Index-AllReferences.ps1 -Pattern "KNW-001*"  # Index specific files

param(
    [string]$SourceDir = "",
    [string]$Pattern = "KNW-*.md",
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [switch]$SkipDataverse,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Default source directory: look for KNW files in the repo's knowledge-sources folder
if (-not $SourceDir) {
    $repoRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
    $SourceDir = Join-Path $repoRoot "projects\ai-spaarke-platform-enhancements-r1\notes\design\knowledge-sources"
}

Write-Host ""
Write-Host "=== Batch Index All References ===" -ForegroundColor Cyan
Write-Host "Source: $SourceDir"
Write-Host "Pattern: $Pattern"
if ($DryRun) {
    Write-Host "Mode: DRY RUN" -ForegroundColor Yellow
} else {
    Write-Host "Mode: LIVE" -ForegroundColor Green
}
Write-Host ""

# Find files
$files = Get-ChildItem -Path $SourceDir -Filter $Pattern -ErrorAction SilentlyContinue
if (-not $files -or $files.Count -eq 0) {
    Write-Host "No files matching '$Pattern' found in: $SourceDir" -ForegroundColor Red
    exit 1
}

Write-Host "Found $($files.Count) file(s) to index:" -ForegroundColor Gray
foreach ($f in $files) {
    Write-Host "  - $($f.Name)" -ForegroundColor Gray
}
Write-Host ""

# Process each file
$results = @()
$startTime = Get-Date

foreach ($file in $files) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Processing: $($file.Name)" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    $params = @{
        FilePath = $file.FullName
        EnvironmentUrl = $EnvironmentUrl
    }
    if ($SkipDataverse) { $params['SkipDataverse'] = $true }
    if ($DryRun) { $params['DryRun'] = $true }

    try {
        & (Join-Path $ScriptDir "Add-ReferenceToIndex.ps1") @params

        $results += @{
            File = $file.Name
            Status = "Success"
        }
    } catch {
        Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
        $results += @{
            File = $file.Name
            Status = "Error"
            Message = $_.Exception.Message
        }
    }
}

$endTime = Get-Date
$duration = $endTime - $startTime

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Batch Indexing Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$successCount = ($results | Where-Object { $_.Status -eq "Success" }).Count
$errorCount = ($results | Where-Object { $_.Status -eq "Error" }).Count

foreach ($r in $results) {
    if ($r.Status -eq "Success") {
        Write-Host "  OK  $($r.File)" -ForegroundColor Green
    } else {
        Write-Host "  ERR $($r.File): $($r.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Results: $successCount succeeded, $errorCount failed (of $($files.Count) total)" -ForegroundColor $(if ($errorCount -gt 0) { "Yellow" } else { "Green" })
Write-Host "Duration: $($duration.ToString('mm\:ss'))" -ForegroundColor Gray
Write-Host ""
