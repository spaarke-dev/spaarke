<#
.SYNOPSIS
  /provision-environment Step 6a — operator-side reconciliation of the sprk_dataverseenvironment registry row.

.DESCRIPTION
  H13 writes Cosmos FIRST (Completed), then PATCHes the registry best-effort (MED#10) — so the row may be in any of four
  states (green / columns failed / setupstatus failed / both failed). This step covers all four, and it NEVER throws
  before Step 6b writes the handoff report (Bucket B HIGH#10): failures set registryStale in the session instead.

  -Action Plan        GET the run; build the promoted columns from the SAME sources H13 uses
                      (H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady); print them for
                      mcp__dataverse__update_record and store them in the session. -ObservedSetupStatus (from the MCP read
                      of the row) adds sprk_setupstatus = 2 (Ready) when it is not already Ready — never re-written when
                      Ready (RU cost + audit noise). Resolves environmentId from the session or -EnvironmentId; an
                      unresolvable id marks the registry stale.
  -Action ApplyWebApi Fallback F1 B — observe sprk_setupstatus and PATCH the same columns through the Web API. A failure
                      marks the registry stale (exit 0 — Step 6b must still run).
  -Action MarkStale   records -Diagnostic (e.g. the MCP lookup failed) — Step 6b/6c then write the stale artifacts.

  NEVER written here: sprk_tenantid (I1 — set once at Step 1f), sprk_currentrunid (released only by
  ICustomerRunGuard.ReleaseAsync — single-writer invariant, Bucket B HIGH#6/#7). On drift in any set-once column,
  HARD STOP + escalate — do not overwrite blindly.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$SessionFile,
  [Parameter(Mandatory)][ValidateSet('Plan', 'ApplyWebApi', 'MarkStale')][string]$Action,
  [string]$ObservedSetupStatus,
  [string]$EnvironmentId,
  [string]$Diagnostic
)

Import-Module (Join-Path $PSScriptRoot 'ProvisionEnvironment.psm1') -Force
$s = Read-ProvisionSession -SessionFile $SessionFile
$i = Get-ProvisionIntake -Session $s
$cpEnv = $s.controlPlaneEnv; $runId = $s.runId; $customerId = [string]$i['customerId']

function Set-Stale([string]$diag) {
  Update-ProvisionSession -SessionFile $SessionFile -Values @{ registryStale = $true; registryStaleDiagnostic = $diag } | Out-Null
  Write-Warning "Step 6a — registry STALE; the diagnostic is written AFTER the handoff report (Step 6c). Diagnostic: $diag"
  Write-Host 'REGISTRY=stale'
  exit 0
}

if ($Action -eq 'MarkStale') { Set-Stale ($(if ($Diagnostic) { $Diagnostic } else { 'registry update failed (no diagnostic given)' })) }

if ($Action -eq 'Plan') {
  $run = Invoke-L2Request -Env $cpEnv -Method GET -Uri "$(Get-L2Base -Env $cpEnv)/api/runs/$runId`?customerId=$([Uri]::EscapeDataString($customerId))" -RunId $runId
  $isv = $run.interStepState
  # sprk_solutionversion — ImportedSolutionSet (T218b): "SpaarkeMaster {version} ({managed|unmanaged})" from H6's
  # importedSolutions SpaarkeMaster entry ($null when absent or versionless).
  $deployedSolutionVer = $null
  $pkg = @($isv.importedSolutions) | Where-Object { $_ -and "$($_.solutionUniqueName)".Trim() -ieq 'SpaarkeMaster' } | Select-Object -First 1
  if ($pkg -and -not [string]::IsNullOrWhiteSpace("$($pkg.version)")) {
    $deployedSolutionVer = "SpaarkeMaster $("$($pkg.version)".Trim()) ($(if ($pkg.isManaged) { 'managed' } else { 'unmanaged' }))"
  }
  $fields = [ordered]@{
    sprk_provisionedon        = ([datetimeoffset]$run.completedOn).ToString('o')   # run.CompletedOn
    sprk_bffversion           = $isv.bffBuildId                                    # H9 — the build it deployed
    sprk_solutionversion      = $deployedSolutionVer                               # H6 (T218b)
    sprk_azuresubscriptionid  = $run.parameters.nonSecret.subscriptionId
    sprk_resourcegroupname    = $isv.resourceGroupName                             # H2a
    sprk_appservicename       = $isv.appServiceName                                # H2a
    sprk_keyvaultname         = $isv.keyVaultName                                  # H2a (the CUSTOMER vault)
    sprk_bffappid             = $isv.bffAppRegId                                   # H3 (T257)
    sprk_containertypeid      = (Get-SpaarkeConstants).per_env_constants.$cpEnv.containerTypeId
    sprk_ClientCacheBustToken = $runId                                             # new per deploy / upgrade, stable across retries
  }
  if ($ObservedSetupStatus -and -not (Test-SetupStatusReady $ObservedSetupStatus)) {
    $fields.sprk_setupstatus = 2   # Ready (EnvironmentSetupStatus; option-set INTEGER — the label 'Ready' is refused by the Web API)
    Write-Warning "Step 6a MED#10 recovery: observed sprk_setupstatus='$ObservedSetupStatus' — including it in the recovery PATCH (H13's server-side setupstatus PATCH did not land)."
  }
  $envId = if ($EnvironmentId) { $EnvironmentId } else { $s.environmentId }
  Update-ProvisionSession -SessionFile $SessionFile -Values @{ registryFields = $fields; registryCompletedOn = $fields.sprk_provisionedon; registryBffVersion = $fields.sprk_bffversion; registrySolutionVersion = $deployedSolutionVer } | Out-Null
  if (-not ($envId -match '^[0-9a-fA-F-]{36}$')) {
    Set-Stale "environmentId could not be resolved for customerId=$customerId — value='$envId' does not match GUID shape"
  }
  Update-ProvisionSession -SessionFile $SessionFile -Values @{ environmentId = $envId } | Out-Null
  Write-Host "mcp__dataverse__update_record — entity sprk_dataverseenvironment, record $envId, fields:"
  $fields | ConvertTo-Json -Depth 4
  exit 0
}

# ApplyWebApi — Fallback F1 B
$fields = $s.registryFields
if (-not $fields) { Stop-Provision '[skill] Run -Action Plan first (it builds the columns from the run record).' }
$envId = $s.environmentId
try {
  $observed = $null
  try {
    $row = Invoke-RegistryWebApi -Env $cpEnv -Method GET -RelativeUri "sprk_dataverseenvironments($envId)?`$select=sprk_setupstatus,sprk_provisionedon"
    $observed = $row.sprk_setupstatus
  } catch {
    # Non-fatal: if the read failed, include sprk_setupstatus (safe default: H13 may have missed it).
    Write-Warning "Step 6a pre-observe read failed; defaulting to include sprk_setupstatus in the recovery PATCH. Diagnostic: $($_.Exception.Message)"
  }
  $body = @{}
  foreach ($k in $fields.Keys) { if ($k -ne 'sprk_setupstatus') { $body[$k] = $fields[$k] } }
  if (-not (Test-SetupStatusReady $observed)) { $body.sprk_setupstatus = 2 }
  Invoke-RegistryWebApi -Env $cpEnv -Method PATCH -RelativeUri "sprk_dataverseenvironments($envId)" -Body $body | Out-Null
  Write-Host "  [PASS] Registry row $envId reconciled through the Web API fallback (setupstatus $(if ($body.ContainsKey('sprk_setupstatus')) { 'set to Ready' } else { 'already Ready' }))." -ForegroundColor Green
  Write-Host 'REGISTRY=ok'
  exit 0
} catch {
  Set-Stale "BOTH Dataverse MCP AND raw Web API fallback failed (Bucket B HIGH#10). Web API: $($_.Exception.Message). Manual recovery required via the Web API PATCH or the Portal — see runs/$runId-registry-stale.md."
}
