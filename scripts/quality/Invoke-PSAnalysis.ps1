<#
.SYNOPSIS
    Runs PSScriptAnalyzer on PowerShell scripts with project-specific settings.

.DESCRIPTION
    Wrapper script for PSScriptAnalyzer that uses the repository's
    PSScriptAnalyzerSettings.psd1 configuration. Supports local developer
    use (text output) and CI integration (XML output with exit codes).

.PARAMETER Path
    Directory or file to analyze. Defaults to scripts/.

.PARAMETER Severity
    Severity levels to include. Defaults to Warning and Error.

.PARAMETER OutputFormat
    Output format: Text (console), XML (NUnit for CI). Defaults to Text.

.PARAMETER FailOnError
    When set, exits with code 1 if any Error-severity findings are found.
    Use in CI to fail the build on critical issues.

.EXAMPLE
    ./scripts/quality/Invoke-PSAnalysis.ps1
    # Analyze scripts/ directory with default settings

.EXAMPLE
    ./scripts/quality/Invoke-PSAnalysis.ps1 -Path scripts/ -Severity Warning,Error
    # Analyze with specific severity filter

.EXAMPLE
    ./scripts/quality/Invoke-PSAnalysis.ps1 -FailOnError -OutputFormat XML
    # CI mode: XML output, fail on errors
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Path = "scripts/",

    [Parameter()]
    [ValidateSet('Information', 'Warning', 'Error')]
    [string[]]$Severity = @('Warning', 'Error'),

    [Parameter()]
    [ValidateSet('Text', 'XML')]
    [string]$OutputFormat = 'Text',

    [Parameter()]
    [switch]$FailOnError
)

$ErrorActionPreference = 'Stop'

# Find repository root (where PSScriptAnalyzerSettings.psd1 lives)
$repoRoot = git rev-parse --show-toplevel 2>$null
if (-not $repoRoot) {
    $repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSCommandPath))
}

$settingsFile = Join-Path $repoRoot 'PSScriptAnalyzerSettings.psd1'

# Ensure PSScriptAnalyzer is installed
if (-not (Get-Module PSScriptAnalyzer -ListAvailable)) {
    Write-Host "Installing PSScriptAnalyzer..." -ForegroundColor Yellow
    Install-Module PSScriptAnalyzer -Scope CurrentUser -Force -SkipPublisherCheck
}

Import-Module PSScriptAnalyzer -ErrorAction Stop

# Resolve path
$analysisPath = if ([System.IO.Path]::IsPathRooted($Path)) {
    $Path
} else {
    Join-Path $repoRoot $Path
}

if (-not (Test-Path $analysisPath)) {
    Write-Error "Path not found: $analysisPath"
    exit 1
}

Write-Host "`n=== PSScriptAnalyzer Report ===" -ForegroundColor Cyan
Write-Host "Path:     $analysisPath"
Write-Host "Settings: $settingsFile"
Write-Host "Severity: $($Severity -join ', ')"
Write-Host "==============================`n" -ForegroundColor Cyan

# Run analysis
$params = @{
    Path        = $analysisPath
    Recurse     = $true
    Severity    = $Severity
}

if (Test-Path $settingsFile) {
    $params['Settings'] = $settingsFile
} else {
    Write-Warning "Settings file not found at $settingsFile — using default rules"
}

$results = Invoke-ScriptAnalyzer @params

# Output results
if ($OutputFormat -eq 'XML') {
    # NUnit XML format for CI systems
    $xmlPath = Join-Path $repoRoot 'psscriptanalyzer-results.xml'
    $results | Export-Clixml -Path $xmlPath
    Write-Host "Results exported to: $xmlPath"
} else {
    if ($results.Count -gt 0) {
        $results | Format-Table -Property Severity, RuleName, ScriptName, Line, Message -AutoSize -Wrap
    }
}

# Summary
$errorCount = ($results | Where-Object { $_.Severity -eq 'Error' }).Count
$warningCount = ($results | Where-Object { $_.Severity -eq 'Warning' }).Count
$infoCount = ($results | Where-Object { $_.Severity -eq 'Information' }).Count

Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "Errors:       $errorCount" -ForegroundColor $(if ($errorCount -gt 0) { 'Red' } else { 'Green' })
Write-Host "Warnings:     $warningCount" -ForegroundColor $(if ($warningCount -gt 0) { 'Yellow' } else { 'Green' })
Write-Host "Information:  $infoCount" -ForegroundColor Gray
Write-Host "Total:        $($results.Count)"
Write-Host "===============`n" -ForegroundColor Cyan

# Exit code for CI
if ($FailOnError -and $errorCount -gt 0) {
    Write-Host "FAILED: $errorCount error(s) found." -ForegroundColor Red
    exit 1
}

Write-Host "Analysis complete." -ForegroundColor Green
exit 0
