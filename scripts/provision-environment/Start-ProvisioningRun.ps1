<#
.SYNOPSIS
  /provision-environment Step 4.0 — issues THE single POST /api/runs that mutates L2 state.

.DESCRIPTION
  Refuses unless Step 3 recorded the confirmation phrase (Confirm-ProvisionGate.ps1) and Step 1f recorded the
  environmentId. CreateRunRequest (RunsEndpoints.cs) accepts EXACTLY customerId, environmentId, tenancyModel, profile,
  nonSecretParameters — no top-level tenantId, no mode (Wave 0 Decisions 1, 3, 6).

  nonSecretParameters is a CLOSED SET (task 245a): L2 accepts ONLY the keys in IntakeParameterCatalog
  (src/server/services/Sprk.Provisioning.ControlPlane.Core/Models/IntakeParameterCatalog.cs). Any other key — a typo,
  `notes`, a value some handler produces — is a 400 'intake-unknown-key'. Adding a key here means adding it to that
  catalog AND declaring which handler reads it. NEVER send the skill-local batch policies (mcpDisconnectPolicy,
  acknowledgeUpgradeMode, onFailedPolicy, onQuarantinedPolicy, onManualGatePolicy, postmortemFile) nor
  customerOutboundB2BAttested (PRQ-C-14). costEnvelopePolicy is GONE (T229; L2 refuses it).

  L2 returns 202 within ~100 ms; the reconciler picks up H0 within ~5 s and auto-advances the DAG. Records runId and
  prints RUN_ID=<id>. A 409 means another run holds sprk_currentrunid for this customer — never start a second one.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$SessionFile)

Import-Module (Join-Path $PSScriptRoot 'ProvisionEnvironment.psm1') -Force
$s = Read-ProvisionSession -SessionFile $SessionFile
$i = Get-ProvisionIntake -Session $s
$cpEnv = $s.controlPlaneEnv

if ($s.runId) { Stop-Provision "[skill] Step 4.0 refused: this session already started run $($s.runId). Poll it (Watch-ProvisioningRun.ps1); never POST twice." }
if ($s.confirmationPhrase -ne $ConfirmationLiteral) { Stop-Provision '[skill] Step 4.0 refused: the Step 3 confirmation gate has not been passed (Confirm-ProvisionGate.ps1).' }
if (-not ($s.environmentId -as [guid])) { Stop-Provision '[skill] Step 4.0 refused: no environmentId — Step 1f must create and verify the placeholder (L2 returns 400 without it).' }

# --- T228: the customer's own subscription and Dataverse environment — required for EVERY model. NEVER default either
# — in particular never `az account show`, which is the operator's subscription (ISH-02 used to do exactly that).
if ([string]::IsNullOrWhiteSpace([string]$i['subscriptionId']) -or [string]::IsNullOrWhiteSpace([string]$i['dataverseEnvUrl'])) {
  Stop-Provision '[skill] Step 4.0 HARD STOP: intake must carry subscriptionId AND dataverseEnvUrl (Step 1b-bis; T228). L2 returns 400 subscription-id-required / dataverse-env-url-invalid otherwise. Correct the intake and rerun.'
}
# --- T229: the cost tier + estimate — required for every model (L2 400s quota-cost-envelope-* otherwise) ---
if ([string]::IsNullOrWhiteSpace([string]$i['tier']) -or $null -eq $i['estimatedMonthlyUsd']) {
  Stop-Provision '[skill] Step 4.0 HARD STOP: intake must carry tier AND estimatedMonthlyUsd (Step 1b-ter; T229).'
}

$constants = Get-SpaarkeConstants
$containerTypeId = $constants.per_env_constants.$cpEnv.containerTypeId   # H4b writes SharePointEmbedded__ContainerTypeId; H8 uses it (T226)
$operatorUpn = az ad signed-in-user show --query userPrincipalName -o tsv   # NEVER from the intake (NFR-11 spoof risk)
$resolvedOpenAiLocation = if ($i['openAiRegion']) { $i['openAiRegion'] } else { 'westus3' }   # Bicep param is openAiLocation; intake field is openAiRegion
# Batch: SHA-256 of the intake that authorized the run (BAT-03 audit trail). Interactive: null.
$intakeFileSha256 = if ($s.mode -eq 'batch') { $s.intakeFileSha256 } else { $null }
$users = @($i['users'])

$runRequest = [ordered]@{
  customerId          = $i['customerId']
  environmentId       = $s.environmentId          # Step 1f
  tenancyModel        = $i['tenancyModel']        # Model1 | Model2 (case-sensitive — T223/T224)
  profile             = $i['profile']             # Model1 → spaarke-hosted-model2, Model2 → customer-owned-model2 (Step 1e)
  nonSecretParameters = [ordered]@{
    tenantId                    = $i['tenantId']              # I1 invariant per Wave 0 Decision 1
    subscriptionId              = $i['subscriptionId']        # T228 — H1/H2a/H2b/H4/H4b/H8/H9/H13/H14
    dataverseEnvUrl             = $i['dataverseEnvUrl']       # T228 — H5 adopts it
    openAiLocation              = $resolvedOpenAiLocation
    confirmationAcknowledgment  = $s.confirmationPhrase       # verbatim "proceed with provisioning"
    intakeFileSha256            = $intakeFileSha256
    region                      = $i['region']                # primary platform region — distinct from openAiLocation
    tier                        = $i['tier']                  # T229 — smb | enterprise | dedicated
    # T229: a STRING via [decimal], which never prints an exponent; nonSecretParameters is a string map and a JSON
    # number fails request binding (400 before CreateRun runs; found 2026-10-01 T245c review).
    estimatedMonthlyUsd         = ([decimal]"$($i['estimatedMonthlyUsd'])").ToString([cultureinfo]::InvariantCulture)
    operatorUpn                 = $operatorUpn
    containerTypeId             = $containerTypeId
    identityPreset              = $i['identityPreset']        # H11 — userprov-missing/invalid-identity-preset
    usersJson                   = (ConvertTo-Json -InputObject @($users) -Compress -Depth 4)   # always a JSON array (do NOT add -AsArray: it double-nests)
    environmentSecurityGroupId  = $i['environmentSecurityGroupId']   # H11 (T232) — required for B2BGuest; null for NativeAccount
    exchangePolicyScopeGroupId  = $i['exchangePolicyScopeGroupId']   # H14a — h14a-missing-policy-scope-group-id
    communicationDefaultMailbox = $i['communicationDefaultMailbox']  # H4 → KV Communication-DefaultMailbox
    customerWorkforceTenantIds  = (ConvertTo-Json -InputObject @($i['customerWorkforceTenantIds']) -Compress)   # T255 — workforce-tenants-* codes
    displayName                 = $i['displayName']           # T259 — REQUIRED: H10 names the customer's business unit with it
    # environmentName may be omitted (L2 stores 'prod'); dev | staging | prod only.
  }
}
# T254 — OPTIONAL OpenAI spend limit: sent only when set; absent = no limit. 400 quota-openai-monthly-limit-invalid otherwise.
if (-not [string]::IsNullOrWhiteSpace("$($i['openAiMonthlyLimitUsd'])")) {
  $runRequest.nonSecretParameters.openAiMonthlyLimitUsd = ([decimal]"$($i['openAiMonthlyLimitUsd'])").ToString([cultureinfo]::InvariantCulture)
}
# T218b — package type: sent only when the owner instructed unmanaged; absent = managed.
if (-not [string]::IsNullOrWhiteSpace("$($i['solutionPackageType'])")) { $runRequest.nonSecretParameters.solutionPackageType = "$($i['solutionPackageType'])" }
# T256 — H7b dry run: sent only when true, exact lower case (400 secure_setup.dry_run_invalid otherwise).
if ($i['secureRecordSetupDryRun'] -eq $true) { $runRequest.nonSecretParameters.secureRecordSetupDryRun = 'true' }

$body = $runRequest | ConvertTo-Json -Depth 5
$l2Base = Get-L2Base -Env $cpEnv
try {
  $response = Invoke-L2Request -Env $cpEnv -Method POST -Uri "$l2Base/api/runs" -Body $body
} catch {
  $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { $null }
  $detail = if ($_.ErrorDetails) { $_.ErrorDetails.Message } else { $_.Exception.Message }
  if ($code -eq 403) { Stop-Provision "[skill] Step 4.0: L2 returned 403 — the operator lacks the Operator app-role on api://spaarke.com/provisioning-controlplane-$cpEnv (Reader passes Step 0c but not this POST). $detail" }
  if ($code -eq 409) { Stop-Provision "[skill] Step 4.0: L2 returned 409 — a run already holds sprk_currentrunid for this customer. Do NOT start a second run; resume/poll the existing one. $detail" }
  if ($code -ge 500 -or $null -eq $code) { Stop-Provision "[skill] Step 4.0: L2 unreachable / $code — see Fallback F3 (references/fallback-matrix.md). $detail" }
  Stop-Provision "[skill] Step 4.0: L2 refused the run (HTTP $code). The errorCode names the rule broken. $detail"
}
$runId = $response.runId   # { runId, customerId, status:"NotStarted", location:"/api/runs/{runId}?customerId=..." }
Update-ProvisionSession -SessionFile $SessionFile -Values @{ runId = $runId } | Out-Null
Write-Host "RUN_ID=$runId"
exit 0
