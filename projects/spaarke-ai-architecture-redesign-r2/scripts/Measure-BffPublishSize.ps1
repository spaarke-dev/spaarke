<#
.SYNOPSIS
    Publish-size verification harness for Sprk.Bff.Api (ADR-029 / NFR-01).

.DESCRIPTION
    Operationalizes the "BFF Publish-Size Per-Task Verification Rule (NFR-01)"
    in .claude/constraints/azure-deployment.md and ADR-029 (BFF publish hygiene).

    In one invocation this script:
      1. (Optionally) runs a fresh `dotnet publish -c Release` of Sprk.Bff.Api
         to deploy/api-publish/ (per azure-deployment.md "Publish & Packaging").
      2. Measures compressed size TWO ways: including PDBs (the actual deploy
         lineage shipped by Deploy-BffApi.ps1) and excluding PDBs (source-hygiene
         lineage) -- mirrors the dual figure ADR-029 records.
      3. Diffs the incl-PDB figure against the last recorded baseline entry in
         the project's publish-size-baseline.json.
      4. Classifies the result against all three ADR-029 / NFR-01 thresholds:
           - >= 60 MB compressed (incl PDB)         -> HARD STOP
           - >= 55 MB compressed (incl PDB)          -> ARCHITECTURE REVIEW
           - delta vs prior baseline >= +5 MB         -> JUSTIFY IN PR
         plus the CVE gate (CLAUDE.md section 10 bullet 5 "no NEW HIGH CVE"):
           - scan failed / unrecognizable output      -> ESCALATE (never clean)
           - HIGH/CRITICAL not in prior baseline set  -> HARD STOP (new CVE)
           - HIGH/CRITICAL already in prior set       -> WARN (pre-existing)
      5. Prints a copy-pasteable report block (matches the CLAUDE.md section 10
         citation format: "BFF Hygiene section 10 + NFR-01 verified: publish
         size = X MB, delta = Y MB, no new HIGH CVEs").
      6. Optionally appends the measurement to publish-size-baseline.json as
         a new baseline history entry (-RecordBaseline).

    This script does NOT modify Sprk.Bff.Api.csproj or add any NuGet package --
    it only measures the existing publish output. It is safe to re-run any
    number of times; only -RecordBaseline persists state.

.PARAMETER RepoRoot
    Root of the spaarke repo (or worktree). Default: auto-detected via
    `git rev-parse --show-toplevel`.

.PARAMETER PublishDir
    Directory `dotnet publish` writes to (relative to RepoRoot).
    Default: deploy/api-publish (per azure-deployment.md convention -- outside
    the project source tree, avoids recursive artifact nesting).

.PARAMETER SkipPublish
    Skip the `dotnet publish` step and measure whatever is already in
    PublishDir. Use when you already published moments ago and just want to
    re-measure / re-classify.

.PARAMETER SkipCveCheck
    Skip the `dotnet list package --vulnerable --include-transitive` check
    (azure-deployment.md rule 5 / bff-extensions.md section B). Runs by
    default; can be slow on a cold NuGet cache, so this is provided to speed
    up quick iterative re-measurements.

.PARAMETER TaskId
    Task id label to record on this measurement (e.g. "074"). Default: "ad-hoc".

.PARAMETER Notes
    Free-text note to attach to a recorded baseline entry.

.PARAMETER BaselineJson
    Path to the baseline history JSON (relative to RepoRoot). Default:
    projects/spaarke-ai-architecture-redesign-r2/notes/publish-size-baseline.json

.PARAMETER RecordBaseline
    If set, appends this measurement to BaselineJson as a new history entry
    and updates the "current" pointer. Without this switch the script is
    read-only (measure + report only) -- the default, so ad-hoc runs during
    investigation don't silently mutate the baseline ledger.

.EXAMPLE
    .\Measure-BffPublishSize.ps1
    # Fresh publish + measure + classify against the last recorded baseline.
    # Prints the report; does not persist anything.

.EXAMPLE
    .\Measure-BffPublishSize.ps1 -TaskId 074 -Notes "audit-container re-key" -RecordBaseline
    # Fresh publish + measure + classify + append as a new baseline entry
    # attributed to task 074. Use this once per BFF-touching task, right
    # before merge, per the NFR-01 per-task verification rule.

.EXAMPLE
    .\Measure-BffPublishSize.ps1 -SkipPublish -SkipCveCheck
    # Re-measure the existing deploy/api-publish/ output without re-publishing
    # or re-running the CVE scan (fast iteration while tuning the harness).

.NOTES
    Source of truth for the rule this automates:
      - .claude/constraints/azure-deployment.md "BFF Publish-Size Per-Task
        Verification Rule (NFR-01)"
      - docs/adr/ADR-029-bff-publish-hygiene.md "Publish-Size Baseline Ratchet"
      - root CLAUDE.md section 10 item 4
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$PublishDir    = 'deploy/api-publish',
    [switch]$SkipPublish,
    [switch]$SkipCveCheck,
    [string]$TaskId        = 'ad-hoc',
    [string]$Notes         = '',
    [string]$BaselineJson  = 'projects/spaarke-ai-architecture-redesign-r2/notes/publish-size-baseline.json',
    [switch]$RecordBaseline
)

$ErrorActionPreference = 'Stop'

# --- Resolve repo root -------------------------------------------------
if (-not $RepoRoot) {
    try {
        $RepoRoot = (git rev-parse --show-toplevel 2>$null).Trim()
    } catch {
        $RepoRoot = $null
    }
    if (-not $RepoRoot) {
        $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
    }
}
$RepoRoot = (Resolve-Path $RepoRoot).Path

$csproj        = Join-Path $RepoRoot 'src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj'
$publishAbs    = Join-Path $RepoRoot $PublishDir
$baselineAbs   = Join-Path $RepoRoot $BaselineJson

if (-not (Test-Path $csproj)) {
    Write-Error "Sprk.Bff.Api.csproj not found at $csproj -- run from the repo root or pass -RepoRoot."
    exit 1
}

Write-Host '==============================================================='
Write-Host 'BFF Publish-Size Verification Harness (ADR-029 / NFR-01)'
Write-Host '==============================================================='
Write-Host "Repo root    : $RepoRoot"
Write-Host "Csproj       : $csproj"
Write-Host "Publish dir  : $publishAbs"
Write-Host "Task id      : $TaskId"
Write-Host ''

# --- Step 1: publish (fresh) --------------------------------------------
if (-not $SkipPublish) {
    if (Test-Path $publishAbs) {
        Write-Host "Cleaning stale publish dir: $publishAbs"
        Remove-Item -Path $publishAbs -Recurse -Force
    }
    Write-Host 'Running: dotnet publish -c Release ...'
    Push-Location $RepoRoot
    try {
        & dotnet publish -c Release $csproj -o $publishAbs
        if ($LASTEXITCODE -ne 0) {
            Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
            exit $LASTEXITCODE
        }
    } finally {
        Pop-Location
    }
    Write-Host ''
} else {
    Write-Host 'Skipping publish (using existing output) per -SkipPublish.'
    if (-not (Test-Path $publishAbs)) {
        Write-Error "No existing publish output at $publishAbs -- omit -SkipPublish or publish first."
        exit 1
    }
}

# --- Step 2: measure -----------------------------------------------------
$allFiles  = Get-ChildItem -Path $publishAbs -Recurse -File
$pdbFiles  = $allFiles | Where-Object { $_.Extension -eq '.pdb' }
$nonPdb    = $allFiles | Where-Object { $_.Extension -ne '.pdb' }

$uncompressedInclPdbBytes = ($allFiles | Measure-Object -Property Length -Sum).Sum
$uncompressedExclPdbBytes = ($nonPdb   | Measure-Object -Property Length -Sum).Sum

function New-CompressedSizeBytes {
    param(
        [Parameter(Mandatory)] [System.IO.FileInfo[]]$Files,
        [Parameter(Mandatory)] [string]$SourceRoot
    )
    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    $tempZip = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "bff-publish-size-$([guid]::NewGuid()).zip")
    $fs  = [System.IO.File]::Open($tempZip, [System.IO.FileMode]::Create)
    $archive = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in $Files) {
            $relPath = $f.FullName.Substring($SourceRoot.Length).TrimStart('\', '/').Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $relPath, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally {
        $archive.Dispose()
        $fs.Dispose()
    }
    $size = (Get-Item $tempZip).Length
    Remove-Item $tempZip -Force
    return $size
}

$sourceRootNorm = (Resolve-Path $publishAbs).Path
Write-Host 'Compressing (incl. PDBs -- canonical deploy lineage)...'
$compressedInclPdbBytes = New-CompressedSizeBytes -Files $allFiles -SourceRoot $sourceRootNorm
Write-Host 'Compressing (excl. PDBs -- source-hygiene lineage)...'
$compressedExclPdbBytes = New-CompressedSizeBytes -Files $nonPdb -SourceRoot $sourceRootNorm

$mb = { param($bytes) [Math]::Round($bytes / 1MB, 2) }

$compressedInclPdbMb   = & $mb $compressedInclPdbBytes
$compressedExclPdbMb   = & $mb $compressedExclPdbBytes
$uncompressedInclPdbMb = & $mb $uncompressedInclPdbBytes
$fileCount             = $allFiles.Count

# Runtimes tree check (ADR-029 rule 1 regression guard -- should always be 0)
$runtimesDir = Join-Path $publishAbs 'runtimes'
$runtimesFileCount = 0
if (Test-Path $runtimesDir) {
    $runtimesFileCount = (Get-ChildItem -Path $runtimesDir -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
}

# Sourcemap check (ADR-029 rule 2 regression guard -- should always be 0)
$sourcemapCount = ($allFiles | Where-Object { $_.Extension -eq '.map' } | Measure-Object).Count

Write-Host ''
Write-Host '--- Measurement ------------------------------------------------'
Write-Host ("Compressed (incl. PDBs)   : {0} MB" -f $compressedInclPdbMb)
Write-Host ("Compressed (excl. PDBs)   : {0} MB" -f $compressedExclPdbMb)
Write-Host ("Uncompressed (incl. PDBs) : {0} MB" -f $uncompressedInclPdbMb)
Write-Host ("File count                : {0}" -f $fileCount)
Write-Host ("runtimes/ file count      : {0} (expect 0 per ADR-029 rule 1)" -f $runtimesFileCount)
Write-Host ("Sourcemap (.map) count    : {0} (expect 0 per ADR-029 rule 2)" -f $sourcemapCount)
Write-Host ''

# --- Step 3: CVE check (optional) ----------------------------------------
# Gating semantics (root CLAUDE.md section 10 bullet 5: "no NEW HIGH-severity CVE"):
#   - A failed or unrecognizable scan is NEVER reported as clean. It becomes an
#     explicit SCAN FAILED state and classifies as ESCALATE.
#   - HIGH/CRITICAL findings are parsed into a structured {package, version,
#     severity, advisory} set and diffed (Step 4) against the CVE set recorded
#     in the prior baseline entry:
#       * finding NOT in the prior set -> NEW CVE     -> HARD STOP (do not merge)
#       * finding IN the prior set     -> pre-existing -> WARN (accepted-risk, tracked)
#   NOTE: `dotnet list package --vulnerable` exits 0 even when vulnerabilities
#   are found, so scan validity is judged by recognizable output content AND
#   exit code -- not exit code alone.
$cveSummary    = 'SKIPPED'
$cveScanFailed = $false
$cveFindings   = @()   # HIGH/CRITICAL findings from this scan
if (-not $SkipCveCheck) {
    Write-Host 'Running: dotnet list package --vulnerable --include-transitive ...'
    Push-Location $RepoRoot
    try {
        $cveOutput = & dotnet list $csproj package --vulnerable --include-transitive 2>&1 | Out-String
        $cveExitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    $outputRecognized = ($cveOutput -match 'has the following vulnerable packages') -or
                        ($cveOutput -match 'has no vulnerable packages')
    if (-not $outputRecognized) {
        # Command failed, produced garbage, or produced nothing -- state is UNKNOWN.
        $cveScanFailed = $true
        $cveSummary = "SCAN FAILED (dotnet list exit code $cveExitCode; output not recognizable) -- vulnerability state UNKNOWN, never treat as clean"
        Write-Host "  $cveSummary" -ForegroundColor Red
        if ($cveOutput) { ($cveOutput -split "`r?`n" | Select-Object -First 10) | ForEach-Object { Write-Host "    $_" } }
    } else {
        # Parse finding lines: "> <package> [<requested>] <resolved> <severity> <advisory-url>"
        # (top-level rows have Requested+Resolved columns; transitive rows only Resolved --
        #  the last token before the severity is always the resolved version).
        $findingLines = $cveOutput -split "`r?`n" | Where-Object { $_ -match '^\s*>' }
        foreach ($line in $findingLines) {
            if ($line -match '^\s*>\s*(?<pkg>\S+)(?<mid>(?:\s+\S+)*?)\s+(?<sev>Critical|High|Moderate|Low)\s+(?<adv>\S+)\s*$') {
                $sev = $Matches['sev']
                if ($sev -in @('High', 'Critical')) {
                    $midTokens = @(($Matches['mid'] -split '\s+') | Where-Object { $_ })
                    $cveFindings += [pscustomobject][ordered]@{
                        package  = $Matches['pkg']
                        version  = if ($midTokens.Count -gt 0) { $midTokens[-1] } else { '' }
                        severity = $sev
                        advisory = $Matches['adv']
                    }
                }
            }
        }
        $cveFindings = @($cveFindings)
        if ($cveFindings.Count -gt 0) {
            $cveSummary = "HIGH/CRITICAL FOUND ($($cveFindings.Count)): " +
                (($cveFindings | ForEach-Object { "$($_.package)@$($_.version) [$($_.severity)]" }) -join ', ')
            Write-Host "  $cveSummary" -ForegroundColor Yellow
            $cveFindings | ForEach-Object { Write-Host "    $($_.package) $($_.version) $($_.severity) $($_.advisory)" }
        } else {
            $cveSummary = 'No HIGH/CRITICAL vulnerabilities found'
            Write-Host "  $cveSummary"
        }
    }
    Write-Host ''
}

# --- Step 4: diff vs baseline + classify ---------------------------------
$baseline = $null
$priorInclPdbMb = $null
$priorSource = $null
if (Test-Path $baselineAbs) {
    $baseline = Get-Content $baselineAbs -Raw | ConvertFrom-Json
    if ($baseline.current) {
        $priorInclPdbMb = $baseline.current.compressed_incl_pdb_mb
        $priorSource    = $baseline.current.source
    }
}

$deltaMb = $null
if ($null -ne $priorInclPdbMb) {
    $deltaMb = [Math]::Round($compressedInclPdbMb - $priorInclPdbMb, 2)
}

# Thresholds (ADR-029 / NFR-01)
$hardStopMb        = 60
$archReviewMb      = 55
$singleTaskDeltaMb = 5

$classifications = New-Object System.Collections.ArrayList
if ($compressedInclPdbMb -ge $hardStopMb) {
    [void]$classifications.Add("HARD STOP: compressed size $compressedInclPdbMb MB >= $hardStopMb MB ceiling. Do NOT merge. Roll back or extract per ADR-029.")
} elseif ($compressedInclPdbMb -ge $archReviewMb) {
    [void]$classifications.Add("ARCHITECTURE REVIEW: compressed size $compressedInclPdbMb MB >= $archReviewMb MB. Escalate before merging.")
}
if ($null -ne $deltaMb -and $deltaMb -ge $singleTaskDeltaMb) {
    [void]$classifications.Add("JUSTIFY IN PR: delta vs prior baseline (+$deltaMb MB) >= +$singleTaskDeltaMb MB single-task threshold.")
}
if ($classifications.Count -eq 0) {
    [void]$classifications.Add('OK: within all ADR-029 / NFR-01 size thresholds.')
}

# --- CVE gating: diff this scan's HIGH/CRITICAL set vs the prior baseline ---
# Rule (root CLAUDE.md section 10 bullet 5): "no NEW HIGH-severity CVE". Pre-existing
# HIGHs already recorded in the baseline are accepted-risk (tracked separately) and
# WARN; a HIGH/CRITICAL absent from the prior baseline's cve_findings set HARD-STOPs.
$priorCveFindings    = @()
$priorCveSetRecorded = $false
if ($baseline) {
    # Most recent recorded entry with a non-null cve_findings set: prefer `current`,
    # else walk history backwards (entries recorded with -SkipCveCheck or a failed
    # scan carry cve_findings = null and are skipped -- they never assert "clean").
    $candidates = @()
    if ($baseline.current) { $candidates += $baseline.current }
    if ($baseline.history) { $candidates += @($baseline.history)[-1..-(@($baseline.history).Count)] }
    foreach ($cand in $candidates) {
        if (($cand.PSObject.Properties.Name -contains 'cve_findings') -and ($null -ne $cand.cve_findings)) {
            $priorCveFindings    = @($cand.cve_findings)
            $priorCveSetRecorded = $true
            break
        }
    }
}

$newCveFindings         = @()
$preExistingCveFindings = @()
$cveVerdict             = 'n/a (CVE scan skipped)'
if (-not $SkipCveCheck) {
    if ($cveScanFailed) {
        $cveVerdict = 'SCAN FAILED -- vulnerability state UNKNOWN'
        [void]$classifications.Add('ESCALATE: CVE scan failed -- vulnerability state UNKNOWN (never treat as clean). Fix the scan and re-run before merge (CLAUDE.md section 10 bullet 5).')
    } elseif ($cveFindings.Count -eq 0) {
        $cveVerdict = 'no HIGH/CRITICAL CVEs'
    } elseif (-not $priorCveSetRecorded) {
        $cveVerdict = "$($cveFindings.Count) HIGH/CRITICAL found; prior baseline has no recorded CVE set -- cannot distinguish new vs pre-existing"
        [void]$classifications.Add("ESCALATE: $($cveFindings.Count) HIGH/CRITICAL CVE(s) found but the prior baseline entry records no cve_findings set. Review each finding manually, then -RecordBaseline to establish the tracked set (CLAUDE.md section 10 bullet 5).")
    } else {
        foreach ($f in $cveFindings) {
            $known = $priorCveFindings | Where-Object {
                $_.package -eq $f.package -and $_.version -eq $f.version -and $_.severity -eq $f.severity
            }
            if ($known) { $preExistingCveFindings += $f } else { $newCveFindings += $f }
        }
        $newCveFindings         = @($newCveFindings)
        $preExistingCveFindings = @($preExistingCveFindings)
        if ($newCveFindings.Count -gt 0) {
            $newList = ($newCveFindings | ForEach-Object { "$($_.package)@$($_.version) [$($_.severity)] $($_.advisory)" }) -join ', '
            $cveVerdict = "$($newCveFindings.Count) NEW HIGH/CRITICAL CVE(s): $newList"
            [void]$classifications.Add("HARD STOP: $($newCveFindings.Count) NEW HIGH/CRITICAL CVE(s) not in the prior baseline ($newList). Do NOT merge until resolved or explicitly accepted + re-baselined (CLAUDE.md section 10 bullet 5).")
        } else {
            $cveVerdict = "no NEW HIGH/CRITICAL CVEs ($($preExistingCveFindings.Count) pre-existing persist)"
        }
        if ($preExistingCveFindings.Count -gt 0) {
            $preList = ($preExistingCveFindings | ForEach-Object { "$($_.package)@$($_.version) [$($_.severity)]" }) -join ', '
            [void]$classifications.Add("WARN: $($preExistingCveFindings.Count) pre-existing HIGH/CRITICAL CVE(s) persist ($preList) -- accepted-risk, tracked in baseline; not a merge blocker by itself.")
        }
    }
}

Write-Host '--- Classification -----------------------------------------------'
foreach ($c in $classifications) { Write-Host "  - $c" }
Write-Host ''

# --- Step 5: report block -------------------------------------------------
$deltaStr = if ($null -ne $deltaMb) { "{0:+0.00;-0.00;0.00}" -f $deltaMb } else { 'n/a (no prior baseline recorded)' }
$reportLine = "BFF Hygiene section 10 + NFR-01 verified: publish size = $compressedInclPdbMb MB (incl. PDBs; $compressedExclPdbMb MB excl.), delta = $deltaStr MB vs prior ($priorSource), CVE check: $cveSummary; new-CVE verdict: $cveVerdict."

Write-Host '--- Copy-paste report line (PR description / task notes) --------'
Write-Host $reportLine
Write-Host ''

# --- Step 6: optionally record as new baseline entry ----------------------
if ($RecordBaseline) {
    if (-not $baseline) {
        Write-Error "Baseline file not found at $baselineAbs -- cannot -RecordBaseline against a missing ledger. Create it first (see notes/publish-size-report-template.md)."
        exit 1
    }
    $entry = [ordered]@{
        date                     = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
        source                   = "spaarke-ai-architecture-redesign-r2 task $TaskId"
        task_id                  = $TaskId
        compressed_incl_pdb_mb   = $compressedInclPdbMb
        compressed_excl_pdb_mb   = $compressedExclPdbMb
        uncompressed_incl_pdb_mb = $uncompressedInclPdbMb
        file_count               = $fileCount
        runtimes_file_count      = $runtimesFileCount
        sourcemap_count          = $sourcemapCount
        delta_vs_prior_mb        = $deltaMb
        classification           = ($classifications -join ' | ')
        cve_check                = $cveSummary
        # cve_findings is the tracked HIGH/CRITICAL set future runs diff against.
        # Null (NOT empty array) when the scan was skipped or failed -- a skipped/
        # failed scan must never establish an empty set as a "clean" baseline.
        # (unary comma keeps single-element/empty arrays as JSON arrays -- an
        #  `if` expression would otherwise unwrap them to a scalar / $null)
        cve_findings             = if (-not $SkipCveCheck -and -not $cveScanFailed) { , @($cveFindings) } else { $null }
        cve_new_findings         = if (-not $SkipCveCheck -and -not $cveScanFailed) { , @($newCveFindings) } else { $null }
        cve_verdict              = $cveVerdict
        notes                    = $Notes
    }
    $historyArray = @($baseline.history) + @($entry)
    $baseline.history = $historyArray
    $baseline.current = $entry
    ($baseline | ConvertTo-Json -Depth 10) | Set-Content -Path $baselineAbs -Encoding utf8
    Write-Host "Recorded new baseline entry (task $TaskId) to $BaselineJson"
    Write-Host ''
}

Write-Host '==============================================================='
Write-Host 'Done.'
Write-Host '==============================================================='
