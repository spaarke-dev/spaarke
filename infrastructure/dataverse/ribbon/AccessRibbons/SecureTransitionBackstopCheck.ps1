# infrastructure/dataverse/ribbon/AccessRibbons/SecureTransitionBackstopCheck.ps1
# ---------------------------------------------------------------------------
# The mechanical half of Make Secure's release gate (unified-access-control-r2 round 46 item 2, wired at the batch-4
# integration). Set-AccessRibbon.ps1 -Apply -SecureTransitionDeployed ships Make Secure only when the target BFF's
# Make Secure file backstop is ON: task 147's SecureChildReconciliationJob is registered and enabled every 2 minutes,
# and its latest completed run settled the pending Make Secure relocations in WRITE mode.
#
# Usage (dot-source, then):
#   $status   = Invoke-RestMethod "$BffBaseUrl/api/admin/jobs/secure-child-reconciliation/status" -Headers @{ Authorization = "Bearer $token" }
#   $failures = @(Test-SecureTransitionBackstop -Status $status)    # empty = PASS
# Read-only and offline: it only judges the status answer (tests/integration/auth/UnifiedAccessControl/
# SecureTransitionBackstopCheckTests.cs runs it over the answer shapes).
# ---------------------------------------------------------------------------

# No Set-StrictMode here: this file is dot-sourced, so it would change the CALLING script's mode.

function Test-SecureTransitionBackstop {
    param([Parameter(Mandatory)] $Status)

    $failures = New-Object System.Collections.Generic.List[string]
    if ($Status.jobId -ne 'secure-child-reconciliation') {
        $failures.Add("the status answer is for job '$($Status.jobId)', not secure-child-reconciliation.")
        return $failures.ToArray()
    }
    if ($Status.enabled -ne $true) {
        $failures.Add('the secure-child reconciliation job is not enabled - it is the Make Secure file backstop (round 46 item 2).')
    }
    if ("$($Status.cronSchedule)".Trim() -ne '*/2 * * * *') {
        $failures.Add("the job runs on '$($Status.cronSchedule)', not every 2 minutes ('*/2 * * * *').")
    }

    $latest = @($Status.recentRuns | Where-Object { $_ -and -not [string]::IsNullOrWhiteSpace($_.resultJson) }) |
        Select-Object -First 1
    if (-not $latest) {
        $failures.Add('the job has no completed run with a report yet - let it run (every 2 minutes), then retry.')
        return $failures.ToArray()
    }

    try { $report = $latest.resultJson | ConvertFrom-Json -ErrorAction Stop }
    catch { $failures.Add("the latest run's report is not JSON: $($_.Exception.Message)"); return $failures.ToArray() }

    $relocations = $report.makeSecureRelocations
    if (-not $relocations) {
        $failures.Add('the latest run reports no Make Secure file relocations - this BFF does not carry the wired backstop.')
    }
    elseif ($relocations.mode -ne 'write') {
        $failures.Add("the latest run settled Make Secure relocations in mode '$($relocations.mode)', not 'write' " +
            '(SecureChild:Reconciliation:RecentChangesWritesEnabled is the emergency stop; it must not be false).')
    }
    return $failures.ToArray()
}
