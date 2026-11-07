<#
.SYNOPSIS
  /provision-environment Step 2 — client-side cost-envelope pre-check (BAT-10; T229). No server mutation.

.DESCRIPTION
  H0 applies the same rule server-side and is the authority; there is no override in either place. These are the
  BUILT-IN ceilings (H0Options.DefaultCeilingsUsd). If the L2 deployment configures
  H0__TierMonthlyCostCeilingsUsd__{tier}, change the table below to match — otherwise this check would stop an
  estimate H0 accepts. Over the ceiling → runs/pre-dispatch-cost-overrun.json + exit 1.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$SessionFile)

Import-Module (Join-Path $PSScriptRoot 'ProvisionEnvironment.psm1') -Force
$s = Read-ProvisionSession -SessionFile $SessionFile
$i = Get-ProvisionIntake -Session $s
$tier = [string]$i['tier']; $estimatedMonthlyUsd = $i['estimatedMonthlyUsd']; $customerId = $i['customerId']

$tierCap = switch -CaseSensitive ($tier) { 'smb' { 700 } 'enterprise' { 2500 } 'dedicated' { 5000 } default { $null } }
if ($null -eq $tierCap) { Stop-Provision "[skill] Step 2 HARD STOP: tier '$tier' is not smb | enterprise | dedicated (Step 1b-ter)." }
if ([decimal]"$estimatedMonthlyUsd" -gt $tierCap) {
  $diagPath = Write-ProvisionDiagnostic -Path 'runs/pre-dispatch-cost-overrun.json' -Data @{ runId = 'pre-dispatch'; customerId = $customerId; estimated = $estimatedMonthlyUsd; cap = $tierCap; tier = $tier }
  Stop-Provision "[skill] Step 2 HARD STOP (BAT-10): estimated `$$estimatedMonthlyUsd/mo exceeds tier '$tier' cap `$$tierCap/mo. Choose the tier that covers the stamp (Step 1b-ter) and rerun. Diagnostic: $diagPath"
}
Write-Host "  [PASS] Estimated cost: `$$estimatedMonthlyUsd/mo (within tier '$tier' ceiling `$$tierCap/mo)" -ForegroundColor Green
exit 0
