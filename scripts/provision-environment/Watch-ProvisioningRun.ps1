<#
.SYNOPSIS
  /provision-environment Steps 4a, 4b and the Step 5 batch dispatch — polls GET /api/runs/{runId}?customerId= .

.DESCRIPTION
  Polls every -PollSeconds (10) for up to -MaxMinutes (default 9, so one call fits a 10-minute tool timeout; run it in
  the background with a larger value for long waits). The ?customerId= query parameter is MANDATORY
  (TryValidateRouteAndPartition returns 400 without it).
  401 → re-acquire the L2 token and retry; 3 consecutive 401s → stop (Fallback F2).
  5xx / timeout → retry once after 30 s; a second failure → exit 9 (Fallback F3 — the run continues in L2).
  Reconciler liveness (EXEC-05): 3 identical polls outside H2a / H8 / H12a → check /healthz (F3 on failure). There is
  no nudge for a stuck Running run, and /resume is for Failed runs only (T186 L18).

  Exit codes (the original Step 4b/5 codes, plus 9 and 10):
    0  Completed                       → Step 6
    2  Failed (batch: after onFailedPolicy — autoResumeOnce resumes once, then runs/{runId}-failed.json)
    3  Quarantined (runs/{runId}-quarantine.json; never auto-cleared)
    4  WaitingOnGate (interactive: Step 5; batch: onManualGatePolicy — waitAndExit writes runs/{runId}-gate.json +
       runs/{runId}-WAITING.md; pollUntilTimeout waits 30 min, or 25 h for H8 SPE replication; failFast exits now)
    5  Cancelled (sprk_currentrunid released)
    9  L2 unreachable after one retry (Fallback F3)
    10 poll window elapsed, run still NotStarted/Running — call again
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$SessionFile,
  [int]$MaxMinutes = 9,
  [int]$PollSeconds = 10
)

Import-Module (Join-Path $PSScriptRoot 'ProvisionEnvironment.psm1') -Force
$s = Read-ProvisionSession -SessionFile $SessionFile
$isBatch = $s.mode -eq 'batch'
$i = Get-ProvisionIntake -Session $s
$cpEnv = $s.controlPlaneEnv; $runId = $s.runId; $customerId = [string]$i['customerId']
if (-not $runId) { Stop-Provision '[skill] No runId in the session — Step 4.0 has not run.' }
$policies = Get-BatchPolicies -Intake $i
$l2Base = Get-L2Base -Env $cpEnv
$encodedCustomerId = [Uri]::EscapeDataString($customerId)   # Step 1a already enforced the standard; escape defensively
$runUri = "$l2Base/api/runs/$runId`?customerId=$encodedCustomerId"

function Get-Run {
  for ($attempt = 1; $attempt -le 2; $attempt++) {
    try { return Invoke-L2Request -Env $cpEnv -Method GET -Uri $runUri -RunId $runId }
    catch {
      if ("$_" -match 'consecutive 401s|az context lost') { Stop-Provision "[skill] $_ (Fallback F2)" }
      if ($attempt -eq 1) { Write-Warning "L2 GET failed ($($_.Exception.Message)) — retrying once in 30 s (Fallback F3)."; Start-Sleep -Seconds 30; continue }
      Write-Host "🚨 L2 API UNREACHABLE — Endpoint: $runUri — Response: $($_.Exception.Message). The run continues in L2 — your work is NOT lost. Follow Fallback F3 (references/fallback-matrix.md)." -ForegroundColor Red
      exit 9
    }
  }
}
function Get-PendingGateIds($run) {
  if (-not $run.gateStates) { return @() }
  return @($run.gateStates.PSObject.Properties | Where-Object { "$($_.Value.status)" -in 'Pending', '0' } | ForEach-Object { $_.Name })
}

$deadline = (Get-Date).AddMinutes($MaxMinutes)
$lastKey = $null; $sameCount = 0; $lastPhase = $null
while ($true) {
  $run = Get-Run
  $status = [string]$run.status; $phase = [string]$run.currentPhase
  if ($phase -ne $lastPhase) { Write-Host ("[{0}] status={1} phase={2} completed={3}" -f (Get-Date -Format 'HH:mm:ss'), $status, $phase, @($run.completedPhases).Count); $lastPhase = $phase }

  # EXEC-05 reconciler liveness
  $key = "$status|$phase|$(@($run.completedPhases).Count)"
  if ($key -eq $lastKey) { $sameCount++ } else { $sameCount = 0; $lastKey = $key }
  if ($sameCount -ge 3 -and $status -eq 'Running' -and $phase -notin 'H2a', 'H8', 'H12a') {
    $h = try { Invoke-WebRequest -Uri "$l2Base/healthz" -TimeoutSec 10 -SkipHttpErrorCheck } catch { $null }
    if (-not $h -or $h.StatusCode -ne 200) { Write-Host "🚨 L2 /healthz not healthy while the run sits at $phase — Fallback F3." -ForegroundColor Red; exit 9 }
    $sameCount = 0
  }

  switch ($status) {
    'Completed' { Write-Host "STATUS=Completed"; exit 0 }
    'Cancelled' { Write-Warning 'Run cancelled. sprk_currentrunid released.'; Write-Host 'STATUS=Cancelled'; exit 5 }
    'Quarantined' {
      $diagPath = Write-ProvisionDiagnostic -Path "runs/$runId-quarantine.json" -Data @{
        runId = $runId; customerId = $customerId; finalStatus = 'Quarantined'
        quarantineReason = $run.quarantine.reason; quarantinedByHandler = $run.quarantine.quarantinedByHandler
        remedy = 'Manually invoke QuarantineClearService via a separate maintenance workflow — batch mode never auto-clears quarantine per BAT-07 rationale (unattended runs must not silently clear operator-required state).'
      }
      Write-Host "STATUS=Quarantined"
      Stop-Provision "[skill] HARD STOP (BAT-07, Quarantined): run $runId requires manual QuarantineClearService intervention. Diagnostic: $diagPath. Postmortem will still be written via Step 7." 3
    }
    'Failed' {
      $isDryRunEnd = "$($run.errorDetail)" -match 'secure_setup\.dry_run'
      if ($isDryRunEnd) { Write-Host "Run ended Failed with secure_setup.dry_run — the expected end of an H7b dry run. Show the plan from gate h7b-secure-setup-plan; never resume it (resume repeats the dry run)." -ForegroundColor Cyan }
      if ($isBatch) {
        if ($policies.onFailedPolicy -eq 'autoResumeOnce' -and -not $s.alreadyResumed -and -not $isDryRunEnd) {
          Write-Host "  [BATCH] onFailedPolicy=autoResumeOnce → POST /api/runs/$runId/resume?customerId=$encodedCustomerId (single attempt)" -ForegroundColor Yellow
          Invoke-L2Request -Env $cpEnv -Method POST -Uri "$l2Base/api/runs/$runId/resume`?customerId=$encodedCustomerId" -RunId $runId | Out-Null
          $s = Update-ProvisionSession -SessionFile $SessionFile -Values @{ alreadyResumed = $true }
          break   # leaves the switch only; the loop sleeps and polls again
        }
        $diagPath = Write-ProvisionDiagnostic -Path "runs/$runId-failed.json" -Data @{
          runId = $runId; customerId = $customerId; finalStatus = 'Failed'; policy = $policies.onFailedPolicy
          currentPhase = $run.currentPhase; failedHandlers = @($run.failedHandlers); errorDetail = $run.errorDetail
        }
        Write-Host 'STATUS=Failed'
        Stop-Provision "[skill] Batch HARD STOP (BAT-07, onFailedPolicy=$($policies.onFailedPolicy)): run $runId ended Failed at phase $($run.currentPhase). Diagnostic: $diagPath" 2
      }
      Write-Host "❌ Run FAILED at phase $($run.currentPhase). Rejection: $($run.errorDetail)"
      Write-Host 'STATUS=Failed'
      exit 2
    }
    'WaitingOnGate' {
      $gateIds = Get-PendingGateIds $run
      $gateId = ($gateIds | Select-Object -First 1)
      if (-not $isBatch) {
        Write-Host "🔔 MANUAL GATE — pending gate(s): $($gateIds -join ', ') (phase $phase). $($run.errorDetail)"
        Write-Host 'STATUS=WaitingOnGate'
        exit 4
      }
      $gatePath = Write-ProvisionDiagnostic -Path "runs/$runId-gate.json" -Data @{
        runId = $runId; customerId = $customerId; gateId = $gateId; pendingGateIds = $gateIds; handler = $phase
        reason = $run.errorDetail; detected = (Get-Date -Format 'o')
      }
      switch ($policies.onManualGatePolicy) {
        'waitAndExit' {
          $waitingMd = @"
# Run $runId — WAITING at manual gate

- **Gate**: $gateId (handler: $phase)
- **Reason**: $($run.errorDetail)
- **Instructions**: see .claude/skills/provision-environment/references/manual-gates.md
- **Detected**: $(Get-Date -Format 'o')

## Resume
After clearing the gate condition, rerun the skill with:
    /provision-environment $customerId --batch <original-intake.json> --resume $runId
"@
          $waitingPath = "runs/$runId-WAITING.md"
          Set-Content -Path $waitingPath -Value $waitingMd -Encoding utf8
          Write-Warning "[skill] Batch WAITING (BAT-08, onManualGatePolicy=waitAndExit): run $runId hit gate '$gateId'. Wrote $waitingPath + $gatePath. Exit 4."
          exit 4
        }
        'pollUntilTimeout' {
          # Bucket B MED#13: H8 SPE container-type replication gets the 25 h fallback (MS's documented 24 h SLO; near-instant
          # in practice per feedback_spe_container_timing); every other gate gets 30 min, then Fallback F3 escalation.
          $isSpeReplication = ($gateId -eq 'spe-replication') -or ($phase -eq 'H8') -or (@($gateIds | Where-Object { $_ -like 'h8-*' }).Count -gt 0)
          $capMinutes = if ($isSpeReplication) { 1500 } else { 30 }
          $capLabel = if ($isSpeReplication) { '25h SPE-replication fallback' } else { '30 min' }
          if (-not $s.gateWaitStartedAt -or $s.gateWaitGateId -ne $gateId) {
            $s = Update-ProvisionSession -SessionFile $SessionFile -Values @{ gateWaitStartedAt = (Get-Date -Format 'o'); gateWaitGateId = $gateId }
            Write-Host "[skill] Batch onManualGatePolicy=pollUntilTimeout — polling gate '$gateId' for clear ($capLabel hard cap)..." -ForegroundColor Cyan
          }
          if ((Get-Date) -gt ([datetimeoffset]$s.gateWaitStartedAt).LocalDateTime.AddMinutes($capMinutes)) {
            Write-Warning "[skill] Batch WAITING (BAT-08, pollUntilTimeout hit $capLabel cap): run $runId still at gate '$gateId'. Exit 4."
            exit 4
          }
        }
        'failFast' {
          Write-Warning "[skill] Batch WAITING (BAT-08, onManualGatePolicy=failFast): run $runId hit gate '$gateId' — exiting immediately without poll. Exit 4."
          exit 4
        }
        default { Stop-Provision "[skill] Batch HARD STOP: unknown onManualGatePolicy '$($policies.onManualGatePolicy)'." }
      }
    }
    default { }   # NotStarted / Running — keep polling
  }

  if ((Get-Date) -ge $deadline) { Write-Host "STATUS=$status (poll window elapsed — call again)"; exit 10 }
  Start-Sleep -Seconds $PollSeconds
}
