#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Two-pass test runner classifier for SDAP CI.

.DESCRIPTION
    Reads pass-1 TRX files, identifies failed tests, and emits a retry decision
    via $env:GITHUB_OUTPUT.

    Two outcomes:
      - All tests passed in pass 1 → exit 0, retry_needed=false.
      - Any failures → exit 0, retry_needed=true, retry_filter=<dotnet test filter>.
        EVERY pass-1 failure is retried, registered or not.

    Pass 2 runs those tests on a fresh execution. If pass 2 also fails, the workflow's
    "Final test verdict" step fails the build — two consecutive failures on different
    runs is no longer noise.

    DETERMINISM IS MEASURED, NOT ASSUMED (changed 2026-08-26)
    ---------------------------------------------------------
    This script previously treated "not in the reliability registry" as a synonym for
    "deterministic — real bug" and failed the build immediately, with no retry. That
    inference does not hold, and in practice it was wrong far more often than right:

      - Run 33007649714 failed the build on ReAnalysisFlowTests.ReAnalysis_HappyPath.
        That test passes locally in 32s.
      - Runs 32871571761 and 32858914113 failed the build on six AuthorizationIntegration
        / SystemIntegration tests. All six pass locally in 2s.
      - A different set of tests was flagged on essentially every run.

    The mechanism could only ever recognise flakiness it had already been told about, so
    a NEW flake always presented as a real bug. Worse, a single unregistered failure
    suppressed the retry for the registered ones too (run 33007649714 logged
    "1 deterministic failure(s); 2 retry-eligible — failing build"), because the
    deterministic branch returned before the retry branch could run.

    The fix applies this file's own long-standing standard uniformly: a test is a real
    failure when it fails TWICE, on different runs. The registry no longer gates the
    retry decision — it is retained purely as reporting, to distinguish a known-flaky
    failure from a newly-observed one in the log.

    Cost: one extra pass on any run that has failures. Deliberately uncapped — a
    genuinely broken build still fails, one pass later, and capping would restore a
    version of the same false-confidence problem this change removes.

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

# --- Annotate each failure (REPORTING ONLY — does not gate the retry) --------
# A TRX 'testName' may be the FullyQualifiedName for a [Fact], OR include
# parameter values for a [Theory] (e.g. "Namespace.Class.Method(p1: \"v1\")").
# To handle both, we test each registry entry as a prefix of the failed name.
$knownFlaky = New-Object System.Collections.Generic.List[string]
$newlyObserved = New-Object System.Collections.Generic.List[string]

foreach ($name in $failedTestNames) {
    $isRegistered = $false
    foreach ($registered in $knownFlakies) {
        # Exact match OR registered name is a prefix (Theory case)
        if ($name -eq $registered -or $name.StartsWith("$registered(")) {
            $isRegistered = $true
            break
        }
    }
    if ($isRegistered) {
        $knownFlaky.Add($name) | Out-Null
    } else {
        $newlyObserved.Add($name) | Out-Null
    }
}

if ($knownFlaky.Count -gt 0) {
    Write-Host ""
    Write-Host "Known-flaky (in reliability registry):"
    $knownFlaky | ForEach-Object { Write-Host "  - $_" }
}
if ($newlyObserved.Count -gt 0) {
    Write-Host ""
    Write-Host "Newly-observed (not in registry) — retried like any other failure."
    Write-Host "If one of these fails pass 2 as well, it is a real failure and the build fails:"
    $newlyObserved | ForEach-Object { Write-Host "  - $_" }
}

# --- Build the pass-2 filter -------------------------------------------------
# Theory parameter values are stripped: a TRX name like
#   Namespace.Class.Method(endpoint: "/api/containers")
# is not valid inside `dotnet test --filter` — the parentheses, quotes and comma
# break filter parsing. Truncating at the first '(' yields the method FQN, which
# re-runs every case of that Theory. That is the intended granularity for a retry
# and it collapses sibling cases into one filter term.
function Get-FilterableName {
    param([string]$TestName)
    $paren = $TestName.IndexOf('(')
    if ($paren -ge 0) { return $TestName.Substring(0, $paren) }
    return $TestName
}

$filterNames = @(
    $failedTestNames |
        ForEach-Object { Get-FilterableName $_ } |
        Where-Object { $_ } |
        Sort-Object -Unique
)

# dotnet test --filter syntax: FullyQualifiedName~A|FullyQualifiedName~B
# Using `~` (contains) instead of `=` so Theory parameterizations match.
$filter = ($filterNames | ForEach-Object { "FullyQualifiedName~$_" }) -join "|"

Emit-GithubOutput -Key "retry_needed" -Value "true"
Emit-GithubOutput -Key "retry_filter" -Value $filter

# The verdict step re-reads this list and asserts every one of these methods actually
# EXECUTED in pass 2. A filter term can silently match nothing — the test was renamed,
# deleted, or carries a [Theory] DisplayName that differs from its FullyQualifiedName —
# and "did not re-run" must never be read as "passed". Without this, a vanished test
# turns a red build green, which is a worse failure than the one this script was
# rewritten to fix.
Emit-GithubOutput -Key "retry_methods" -Value ($filterNames -join ";")

Emit-GithubOutput -Key "summary" -Value "$failedCount pass-1 failure(s) ($($knownFlaky.Count) known-flaky, $($newlyObserved.Count) newly-observed) across $($filterNames.Count) test method(s) — retrying all"
Write-Host ""
Write-Host "::notice::Retrying all $($filterNames.Count) failed test method(s) — a failure is real only if it fails twice"
exit 0
