#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Two-pass test runner classifier for SDAP CI.

.DESCRIPTION
    Reads pass-1 TRX files, identifies failed tests, classifies them via the
    reliability registry (tests/.reliability-registry.json), and emits a
    retry decision via $env:GITHUB_OUTPUT.

    Three outcomes:
      - All tests passed in pass 1 → exit 0, retry_needed=false.
      - Any failure is NOT in the registry (Deterministic = real bug) → exit 1,
        the workflow step fails immediately. No retry.
      - All failures ARE in the registry (TimingSensitive or ConcurrencySensitive)
        → exit 0, retry_needed=true, retry_filter=<dotnet test filter for pass 2>.

    Pass 2 runs only those failed tests. If pass 2 also has failures, the workflow's
    "Final test verdict" step fails the build (treated as a real regression — two
    consecutive runs failed on different runners is no longer noise).

.PARAMETER TrxDirectory
    Directory containing pass-1 .trx file(s). Searched recursively.

.PARAMETER RegistryPath
    Path to the reliability registry JSON. Defaults to "tests/.reliability-registry.json".

.OUTPUTS
    Writes to $env:GITHUB_OUTPUT (when running in GitHub Actions):
      retry_needed = true|false
      retry_filter = "FullyQualifiedName~A|FullyQualifiedName~B" (dotnet test --filter syntax)
      summary      = Human-readable one-line summary

.EXAMPLE
    ./scripts/ci/classify-and-retry.ps1 -TrxDirectory ./TestResults/pass1
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$TrxDirectory,

    [string]$RegistryPath = "tests/.reliability-registry.json"
)

$ErrorActionPreference = 'Stop'

function Emit-GithubOutput {
    param([string]$Key, [string]$Value)
    if ($env:GITHUB_OUTPUT) {
        "$Key=$Value" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }
    Write-Host "::notice::$Key=$Value"
}

# --- Load reliability registry ----------------------------------------------
if (-not (Test-Path $RegistryPath)) {
    Write-Error "Reliability registry not found at: $RegistryPath"
    exit 2
}
$registry = Get-Content $RegistryPath -Raw | ConvertFrom-Json
$timingSensitive = @($registry.TimingSensitive)
$concurrencySensitive = @($registry.ConcurrencySensitive)
$knownFlakies = @($timingSensitive) + @($concurrencySensitive)
Write-Host "Loaded reliability registry:"
Write-Host "  TimingSensitive:      $($timingSensitive.Count)"
Write-Host "  ConcurrencySensitive: $($concurrencySensitive.Count)"
Write-Host "  Total registered:     $($knownFlakies.Count)"

# --- Find TRX files ---------------------------------------------------------
if (-not (Test-Path $TrxDirectory)) {
    Write-Warning "TRX directory not found: $TrxDirectory"
    Emit-GithubOutput -Key "retry_needed" -Value "false"
    Emit-GithubOutput -Key "summary" -Value "No TRX directory found"
    exit 0
}

$trxFiles = @(Get-ChildItem -Path $TrxDirectory -Filter "*.trx" -Recurse -ErrorAction SilentlyContinue)
if ($trxFiles.Count -eq 0) {
    Write-Warning "No TRX files found under $TrxDirectory"
    Emit-GithubOutput -Key "retry_needed" -Value "false"
    Emit-GithubOutput -Key "summary" -Value "No TRX files found"
    exit 0
}
Write-Host "Found $($trxFiles.Count) TRX file(s):"
$trxFiles | ForEach-Object { Write-Host "  - $($_.FullName)" }

# --- Parse each TRX, accumulate failures ------------------------------------
$ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
$failedTestNames = New-Object System.Collections.Generic.HashSet[string]
foreach ($trx in $trxFiles) {
    try {
        $xml = [xml](Get-Content $trx.FullName -Raw)
    } catch {
        Write-Warning "Failed to parse TRX file $($trx.FullName): $_"
        continue
    }
    $nsm = New-Object System.Xml.XmlNamespaceManager $xml.NameTable
    $nsm.AddNamespace("x", $ns)
    $failedNodes = $xml.SelectNodes("//x:UnitTestResult[@outcome='Failed']", $nsm)
    foreach ($r in $failedNodes) {
        $name = $r.GetAttribute("testName")
        if ($name) { [void]$failedTestNames.Add($name) }
    }
}

$failedCount = $failedTestNames.Count
if ($failedCount -eq 0) {
    Write-Host ""
    Write-Host "PASS 1 CLEAN: 0 failures across all TRX files"
    Emit-GithubOutput -Key "retry_needed" -Value "false"
    Emit-GithubOutput -Key "summary" -Value "All tests passed on first run"
    exit 0
}

Write-Host ""
Write-Host "PASS 1 had $failedCount failure(s):"
$failedTestNames | ForEach-Object { Write-Host "  - $_" }

# --- Classify each failure --------------------------------------------------
# A TRX 'testName' may be the FullyQualifiedName for a [Fact], OR include
# parameter values for a [Theory] (e.g. "Namespace.Class.Method(p1: \"v1\")").
# To handle both, we test each registry entry as a prefix of the failed name.
$retryEligible = New-Object System.Collections.Generic.List[string]
$deterministicFailures = New-Object System.Collections.Generic.List[string]

foreach ($name in $failedTestNames) {
    $isFlaky = $false
    foreach ($registered in $knownFlakies) {
        # Exact match OR registered name is a prefix (Theory case)
        if ($name -eq $registered -or $name.StartsWith("$registered(")) {
            $isFlaky = $true
            break
        }
    }
    if ($isFlaky) {
        $retryEligible.Add($name) | Out-Null
    } else {
        $deterministicFailures.Add($name) | Out-Null
    }
}

# --- Decision ---------------------------------------------------------------
if ($deterministicFailures.Count -gt 0) {
    Write-Host ""
    Write-Host "DETERMINISTIC FAILURES (not in reliability registry — real bugs):"
    $deterministicFailures | ForEach-Object { Write-Host "  - $_" }
    Emit-GithubOutput -Key "retry_needed" -Value "false"
    Emit-GithubOutput -Key "summary" -Value "$($deterministicFailures.Count) deterministic failure(s); $($retryEligible.Count) retry-eligible — failing build"
    Write-Host ""
    Write-Host "::error::Deterministic test failure(s) detected — failing the build (no retry). See output above for the failing test names."
    exit 1
}

# All failures are in the reliability registry → retry
Write-Host ""
Write-Host "ALL FAILURES are in the reliability registry — emitting pass-2 retry filter"
Write-Host "Retry-eligible tests:"
$retryEligible | ForEach-Object { Write-Host "  - $_" }

# dotnet test --filter syntax: FullyQualifiedName~A|FullyQualifiedName~B
# Using `~` (contains) instead of `=` so Theory parameterizations match.
$filter = ($retryEligible | ForEach-Object { "FullyQualifiedName~$_" }) -join "|"
Emit-GithubOutput -Key "retry_needed" -Value "true"
Emit-GithubOutput -Key "retry_filter" -Value $filter
Emit-GithubOutput -Key "summary" -Value "$($retryEligible.Count) timing/concurrency-sensitive failure(s) — retrying"
Write-Host ""
Write-Host "::notice::All failures are registry-tagged — running pass 2 with --filter `"$filter`""
exit 0
