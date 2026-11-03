<#
.SYNOPSIS
  /provision-environment Steps 4b and 5 — the two operator-requested L2 actions on an existing run.

.DESCRIPTION
  -Action Resume   POST /api/runs/{id}/resume?customerId=  — ONLY for a Failed run, and only after the operator said
                   "yes" (interactive). L2 re-takes the customer guard and dispatches currentPhase; any other status is a
                   409 run-not-resumable (T186 L18). Never for a secure_setup.dry_run end (it repeats the dry run).
  -Action Advance  POST /api/runs/{id}/gates/{gateId}/advance?customerId=  — after the operator says the gate action is
                   done. L2 RE-VERIFIES the gate's condition; if it still fails the run stays WaitingOnGate. NEVER advance
                   by trusting the operator's assertion, and never use /resume for a gate.
  clear-quarantine is deliberately NOT offered: it needs a human-accountable reason and is never run on the
  operator's behalf.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$SessionFile,
  [Parameter(Mandatory)][ValidateSet('Resume', 'Advance')][string]$Action,
  [string]$GateId
)

Import-Module (Join-Path $PSScriptRoot 'ProvisionEnvironment.psm1') -Force
$s = Read-ProvisionSession -SessionFile $SessionFile
$i = Get-ProvisionIntake -Session $s
$cpEnv = $s.controlPlaneEnv; $runId = $s.runId
if (-not $runId) { Stop-Provision '[skill] No runId in the session — Step 4.0 has not run.' }
$enc = [Uri]::EscapeDataString([string]$i['customerId'])
$l2Base = Get-L2Base -Env $cpEnv

$uri = if ($Action -eq 'Resume') { "$l2Base/api/runs/$runId/resume`?customerId=$enc" } else {
  if (-not $GateId) { Stop-Provision '[skill] -Action Advance needs -GateId (the pending gate id from the run).' }
  "$l2Base/api/runs/$runId/gates/$([Uri]::EscapeDataString($GateId))/advance`?customerId=$enc"
}
try {
  $r = Invoke-L2Request -Env $cpEnv -Method POST -Uri $uri -RunId $runId
} catch {
  $detail = if ($_.ErrorDetails) { $_.ErrorDetails.Message } else { $_.Exception.Message }
  Stop-Provision "[skill] $Action on run $runId failed: $detail"
}
Write-Host "$Action accepted for run $runId. Poll again with Watch-ProvisioningRun.ps1."
if ($r) { $r | ConvertTo-Json -Depth 5 }
exit 0
