<#
.SYNOPSIS
    Decides whether a pcf-scripts build actually succeeded, from its OUTPUT.

.DESCRIPTION
    `pcf-scripts build` (and therefore `npm run build:prod` in every PCF) EXITS 0 WHEN THE WEBPACK
    BUILD FAILS. Its taskRunner logs `[build] Failed:` and `[pcf-1033] [Error] An error occurred
    compiling or bundling the control.` and then returns without rethrowing, so the process exit
    code stays 0. Any script that trusts `$LASTEXITCODE` after a PCF build will treat a failed build
    as a success and can package or deploy a stale `out/` bundle.

    Found 2026-10-04 (spaarke-ontology-platform-r1 tasks 092 / 093b): the nightly CI workflow
    reported 17 of 18 PCFs passing when 9 had failed. `.github/workflows/pcf-build-prod-nightly.yml`
    applies the same rule as this module; keep the two in step.

    The rule:
      * FAIL if the exit code is non-zero, OR the output contains `[build] Failed`, OR a
        `compiled with N error(s)` line, OR `[pcf-1033]`.
      * PASS only if the output contains `[build] Succeeded` and none of the above.
      * Anything else is UNKNOWN, which callers must treat as a failure.

.EXAMPLE
    Import-Module "$PSScriptRoot/PcfBuildResult.psm1"
    $out = npm run build:prod 2>&1
    $r = Get-PcfBuildResult -Output $out -ExitCode $LASTEXITCODE
    if (-not $r.Succeeded) { throw "PCF build failed: $($r.Reason)" }
#>

function Get-PcfBuildResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [object[]] $Output,
        [Parameter(Mandatory)] [int] $ExitCode
    )

    # Strip ANSI colour codes first: webpack colours the "N error" token separately, which would
    # otherwise split the "compiled with N error(s)" marker.
    $ansi = "\x1b\[[0-9;]*m"
    $lines = @($Output | ForEach-Object { "$_" -replace $ansi, '' })
    $text = $lines -join "`n"

    $failMarkers = @()
    if ($ExitCode -ne 0) { $failMarkers += "exit code $ExitCode" }
    if ($text -match '\[build\] Failed') { $failMarkers += '[build] Failed' }
    if ($text -match 'compiled with \d+ errors?') { $failMarkers += $Matches[0] }
    if ($text -match '\[pcf-1033\]') { $failMarkers += '[pcf-1033]' }

    $succeededMarker = $text -match '\[build\] Succeeded'

    if ($failMarkers.Count -gt 0) {
        $status = 'Failed'
        $reason = $failMarkers -join '; '
    }
    elseif ($succeededMarker) {
        $status = 'Succeeded'
        $reason = '[build] Succeeded'
    }
    else {
        $status = 'Unknown'
        $reason = 'no [build] Succeeded or failure marker in the output'
    }

    # First few error lines, so a caller can show WHY without dumping the whole log.
    $excerpt = @($lines |
        Where-Object { $_ -match 'ERROR in|error TS\d+|Module not found|Module build failed|Cannot find module|\[pcf-1033\]' } |
        Select-Object -First 6)

    [pscustomobject]@{
        Succeeded = ($status -eq 'Succeeded')
        Status    = $status
        Reason    = $reason
        Excerpt   = $excerpt
    }
}

Export-ModuleMember -Function Get-PcfBuildResult
