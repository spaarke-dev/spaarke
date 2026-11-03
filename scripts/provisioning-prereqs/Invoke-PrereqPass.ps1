<#
.SYNOPSIS
  /provision-environment Steps 0.5a–0.5b–0.5d and 1e-ter — iterates scripts/provisioning-prereqs/prereqs.yaml.

.DESCRIPTION
  -Pass Env       (Step 0.5) once_per_tenant + once_per_subscription, plus once_per_env when the control-plane env is
                  known (batch: always, from the intake; interactive: only when the operator gave it as an argument).
  -Pass Customer  (Step 1e-ter) once_per_customer, with the intake tokens added to the same token map. In interactive
                  mode it also runs once_per_env when Step 0.5 could not (env unknown then) — see MOVE-MAP defect D4.

  PASS/FAIL IS THE RECIPE'S EXIT CODE (task 206 contract; validate.ps1 lints it). Recipes run under `bash -c`.
  Any failed prerequisite → the Step 0.5d checklist + exit 1 (HARD STOP; nothing has been written yet).
  Skipped entirely when the session has skipExternalPrereqs (-SkipStep0_5 / intake.skipExternalPrereqs) — recorded
  for Step 7. Silent skip is FORBIDDEN: if powershell-yaml cannot be installed, the operator checks each prerequisite
  by hand per docs/guides/PROVISIONING-PREREQUISITES.md and sets skipExternalPrereqs explicitly.

  This file is the RUNTIME token map (formerly inline in SKILL.md Step 0.5b). Adding a {token} to prereqs.yaml
  REQUIRES extending $prereqTokens / $customerTokens below AND (per_env or invariant) spaarke-constants.yaml AND
  context-defaults.*.json (validate.ps1 cross-checks the last one).
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$SessionFile,
  [Parameter(Mandatory)][ValidateSet('Env', 'Customer')][string]$Pass
)

Import-Module (Join-Path $PSScriptRoot '..\provision-environment\ProvisionEnvironment.psm1') -Force
$s = Read-ProvisionSession -SessionFile $SessionFile
$isBatch = $s.mode -eq 'batch'
$cpEnv = $s.controlPlaneEnv   # control-plane env (dev | demo | prod) — called $env in the original inline code and messages

if ($s.skipExternalPrereqs) {
  Write-Warning "[skill] skipExternalPrereqs is set — prerequisite pass '$Pass' NOT run. The operator attests every applicable prerequisite was verified by hand; Step 7 records this."
  $pr = $s.prereqResults; if (-not $pr) { $pr = @{} }; $pr[$Pass.ToLowerInvariant()] = 'skipped'
  Update-ProvisionSession -SessionFile $SessionFile -Values @{ prereqResults = $pr } | Out-Null
  exit 0
}

# --- 0.5a. YAML parser + environment fail-fast (COMP-14) ---------------------------------------------------------
try { Import-SpaarkeYaml } catch {
  Stop-Provision "[skill-config] powershell-yaml is unavailable and could not be installed ($($_.Exception.Message)). Check each prerequisite by hand per docs/guides/PROVISIONING-PREREQUISITES.md and set skipExternalPrereqs (-SkipStep0_5) explicitly — silent skip is FORBIDDEN."
}
if ($isBatch) {
  if ([string]::IsNullOrWhiteSpace($cpEnv)) {
    Stop-Provision "[skill-config] Step 0.5a HARD STOP (COMP-14): batch-mode `$env is null/empty after Step 1.0 read of intake.controlPlaneEnv. This means the intake.json passed schema validation with a null/empty controlPlaneEnv field OR the Step 1.0 batch loader dropped it. Correct the intake and rerun. Silent-skip of once_per_env prereqs is FORBIDDEN in batch mode."
  }
  if ($cpEnv -notin @('dev', 'demo', 'prod')) {
    Stop-Provision "[skill-config] Step 0.5a HARD STOP (COMP-14): batch-mode `$env='$cpEnv' is not one of the valid values (dev|demo|prod) per intake.schema.json. spaarke-constants.yaml per_env_constants.$cpEnv lookup would return null; PLX-13 sanity check would emit a confusing 'containerTypeId is null' error. Correct the intake and rerun."
  }
  if ($Pass -eq 'Env') { Write-Host "  [PASS] Batch-mode env='$cpEnv' — Step 0.5b will iterate once_per_tenant + once_per_subscription + once_per_env prereqs" -ForegroundColor Green }
} else {
  if ([string]::IsNullOrWhiteSpace($cpEnv)) {
    if ($Pass -eq 'Customer') { Stop-Provision '[skill] Step 1e-ter needs the control-plane env (Step 1d). Record it with Set-ProvisionSession.ps1 -Env and rerun.' }
    Write-Host "  [INFO] Interactive-mode env not yet assigned (Step 1d has not run); Step 0.5b will skip once_per_env prereqs. They run in the Step 1e-ter pass once `$env is known." -ForegroundColor Cyan
  } elseif ($cpEnv -notin @('dev', 'demo', 'prod')) {
    Stop-Provision "[skill-config] Step 0.5a HARD STOP: `$env='$cpEnv' is not one of (dev|demo|prod). Correct the CLI arg and rerun."
  }
}

# --- 0.5b. Token map (SKILL-08 + PLX-01..14) ---------------------------------------------------------------------
$repoRoot = Get-ProvisionRepoRoot
$constants = Get-SpaarkeConstants
$intake = if ($s.intakeFile) { Get-ProvisionIntake -Session $s } else { @{} }
$openAiRegion = $intake['openAiRegion']

$graphAppId = $constants.microsoft_constants.graphAppId
$customerManagementGroupId = $constants.management_groups.customers.id   # T262 (PRQ-S-06) — tenant-wide, not per env
$subId = az account show --query id -o tsv   # the operator's current subscription — once_per_env / once_per_tenant checks only; the customer pass uses {stampSubscriptionId} (T228)
$openAiRegionResolved = if ($openAiRegion) { $openAiRegion } else { 'westus3' }  # canonical Spaarke split per operator memory

# Everything below needs $cpEnv. Without it once_per_env is skipped, so these stay $null and no az call runs with an
# empty resource name (T207).
$l2UamiPrincipalId = $null; $l2UamiClientId = $null; $l2UamiSpId = $null; $sbNamespace = $null
$artifactsStorage = $null; $acrId = $null; $kvResourceId = $null; $containerTypeId = $null; $adminDvUrl = $null
if ($cpEnv) {
  $l2UamiName        = $constants.name_templates.l2UamiName -replace '\{env\}', $cpEnv
  $platformRg        = $constants.name_templates.platformResourceGroup -replace '\{env\}', $cpEnv
  $l2UamiJson        = az identity show -g $platformRg -n $l2UamiName -o json | ConvertFrom-Json
  $l2UamiPrincipalId = $l2UamiJson.principalId
  $l2UamiClientId    = $l2UamiJson.clientId
  $l2UamiSpId        = az ad sp show --id $l2UamiClientId --query id -o tsv
  $sbNamespace       = $constants.name_templates.sbNamespace -replace '\{env\}', $cpEnv
  $artifactsStorage  = az storage account show -g $platformRg -n ($constants.name_templates.artifactsStorageName -replace '\{env\}', $cpEnv) --query id -o tsv 2>$null
  $acrId             = az acr show -g $platformRg -n ($constants.name_templates.acrName -replace '\{env\}', $cpEnv) --query id -o tsv 2>$null
  $kvResourceId      = az keyvault show -g $platformRg -n ($constants.name_templates.platformKvName -replace '\{env\}', $cpEnv) --query id -o tsv 2>$null
  $containerTypeId   = $constants.per_env_constants.$cpEnv.containerTypeId
  $adminDvUrl        = $constants.name_templates.registryDvUrl.$cpEnv
  # Sanity: per_env_constants that require operator population MUST be set
  if (-not $containerTypeId) {
    Stop-Provision "[skill-config] scripts/provisioning-prereqs/spaarke-constants.yaml per_env_constants.$cpEnv.containerTypeId is null. Operator MUST populate before Step 0.5 iteration. See docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md §2.4 for how to obtain the SPE container-type GUID."
  }
}
# Task 245b / 248: the L2 Worker must also carry this container type's OWNING app
# (SpeContainerOptions__ContainerTypeOwners__{i}__ContainerTypeId / __OwnerAppId — Bicep param speContainerTypeOwners).
# L2 signs in as the owning app through the federated identity credential on it that trusts the Worker UAMI (MI-FIC,
# owner decision D16) — there is no certificate or secret to bootstrap. H0's SpeOwnerCredential check rejects the run
# before anything is created with one of three Resumable codes:
#   spe-owner-not-configured          → add the owner entry to the Worker configuration (not the intake);
#   spe-owner-token-failed            → the owning app's FIC / consent (SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md);
#   spe-container-type-not-registered → register the container type as the owning app (same runbook).

$manifest = Get-Content (Join-Path $repoRoot 'scripts/provisioning-prereqs/prereqs.yaml') -Raw | ConvertFrom-Yaml

# `bash` on a Windows PowerShell PATH can be the WSL stub ("no installed distributions") — in System32 OR in
# %LOCALAPPDATA%\Microsoft\WindowsApps (seen 2026-10-09). Prefer Git for Windows' own bash; elsewhere the PATH bash.
$gitBash = Join-Path (Split-Path (Split-Path (Get-Command git).Source)) 'bin/bash.exe'
$bashExe = if (Test-Path $gitBash) { $gitBash } else {
  (Get-Command bash -All -ErrorAction SilentlyContinue | Where-Object { $_.Source -notmatch 'System32|WindowsApps' } | Select-Object -First 1).Source }
if (-not $bashExe) { Stop-Provision '[skill-config] No usable bash (Git Bash on Windows; bash on Linux/macOS).' }
$env:MSYS_NO_PATHCONV = '1'   # Git Bash rewrites a leading /subscriptions/... argument

$prereqTokens = [ordered]@{
  env = $cpEnv; openAiRegion = $openAiRegionResolved; region = $openAiRegionResolved; subId = $subId; sub = $subId
  l2UamiPrincipalId = $l2UamiPrincipalId; l2UamiClientId = $l2UamiClientId; l2UamiSpId = $l2UamiSpId
  graphAppId = $graphAppId; customerManagementGroupId = $customerManagementGroupId; sbNamespace = $sbNamespace; artifactsStorageId = $artifactsStorage; acrId = $acrId
  kvResourceId = $kvResourceId; containerTypeId = $containerTypeId; adminDvUrl = $adminDvUrl
}
# Tokens that must not be empty when a recipe uses them. artifactsStorageId / acrId / kvResourceId are exempt:
# E-01..E-04 and E-10 report "not found" themselves. customerOutboundB2BAttested is never here — false is valid.
$mustResolveTokens = @('env', 'l2UamiPrincipalId', 'l2UamiClientId', 'l2UamiSpId', 'sbNamespace', 'containerTypeId', 'adminDvUrl', 'graphAppId', 'customerManagementGroupId',
  'customerId', 'stampSubscriptionId', 'stampEnvironment', 'dvUrl', 'exchangePolicyScopeGroupId', 'environmentSecurityGroupId',
  'customerWorkforceTenantIds')

function Invoke-PrereqScopes([string[]]$Scopes, $Tokens) {
  $passResults = @()
  foreach ($prereq in $manifest.prereqs) {
    if ($prereq.scope -notin $Scopes) { continue }
    # Retired entries keep their id but carry no check_recipe; `bash -c ""` exits 0 and would report a pass.
    if ($prereq.status -eq 'retired') { continue }

    $recipe = $prereq.check_recipe.cli
    foreach ($t in $Tokens.Keys) { $recipe = $recipe -replace ('\{' + $t + '\}'), [string]$Tokens[$t] }

    # PLX-14 author-time sanity check: a surviving {token} means the token map is out of date vs the manifest.
    if ($recipe -match '\{[a-zA-Z_][a-zA-Z_0-9]*\}') {
      Write-Host "[skill-config] Recipe for $($prereq.id) references unresolved placeholder '$($Matches[0])'. Extend the token map in scripts/provisioning-prereqs/Invoke-PrereqPass.ps1 (Step 0.5b) with a derivation for this token, or verify it belongs in spaarke-constants.yaml per_env_constants.$cpEnv.*." -ForegroundColor Red
      $passResults += [ordered]@{ Id = $prereq.id; Name = $prereq.name; Scope = $prereq.scope; Passed = $false; ExitCode = -1; Output = "[skill-config] unresolved placeholder: $($Matches[0])"; Consequence = $prereq.consequence_of_absence; Remediation = $prereq.remediation }
      continue
    }
    # A token that resolved to NOTHING is as bad as one left literal: `az ... -n ""` fails cryptically or matches everything (T207).
    $emptyTok = $mustResolveTokens | Where-Object { $Tokens.Contains($_) -and -not $Tokens[$_] -and $prereq.check_recipe.cli -match ('\{' + $_ + '\}') } | Select-Object -First 1
    if ($emptyTok) {
      Write-Host "[skill-config] Recipe for $($prereq.id) uses {$emptyTok}, which resolved to an empty value." -ForegroundColor Red
      $passResults += [ordered]@{ Id = $prereq.id; Name = $prereq.name; Scope = $prereq.scope; Passed = $false; ExitCode = -1; Output = "[skill-config] empty token: $emptyTok"; Consequence = $prereq.consequence_of_absence; Remediation = $prereq.remediation }
      continue
    }

    Write-Host "  [CHECK] $($prereq.id) $($prereq.name)" -ForegroundColor Yellow
    $output = & $bashExe -c $recipe 2>&1 | Out-String
    $exitCode = $LASTEXITCODE
    $passResults += [ordered]@{
      Id = $prereq.id; Name = $prereq.name; Scope = $prereq.scope; Passed = ($exitCode -eq 0); ExitCode = $exitCode
      Output = $output.Trim(); Consequence = $prereq.consequence_of_absence; Remediation = $prereq.remediation
    }
  }
  return , $passResults
}

$pr = $s.prereqResults; if (-not $pr) { $pr = @{} }
if ($Pass -eq 'Env') {
  $scopes = @('once_per_tenant', 'once_per_subscription')
  if ($cpEnv) { $scopes += 'once_per_env' }
  $results = Invoke-PrereqScopes -Scopes $scopes -Tokens $prereqTokens
  $pr['envScopes'] = $scopes
} else {
  # Step 1e-ter — the customer (intake) tokens, then once_per_customer with the same function.
  $customerTokens = [ordered]@{}
  foreach ($k in $prereqTokens.Keys) { $customerTokens[$k] = $prereqTokens[$k] }
  $customerTokens.customerId                 = $intake['customerId']
  $customerTokens.stampSubscriptionId        = $intake['subscriptionId']                  # Step 1b-bis (T228)
  $customerTokens.stampEnvironment           = 'prod'                                     # L2's default environmentName (owner D6); the intake has no override
  $customerTokens.dvUrl                      = $intake['dataverseEnvUrl']                 # Step 1b-bis — canonical, trailing slash
  $customerTokens.exchangePolicyScopeGroupId = $intake['exchangePolicyScopeGroupId']      # Step 1e-bis (PRQ-C-08)
  $customerTokens.environmentSecurityGroupId = $intake['environmentSecurityGroupId']      # Step 1e-bis (PRQ-C-10; B2BGuest)
  $customerTokens.customerWorkforceTenantIds = (@($intake['customerWorkforceTenantIds']) -join ' ')   # PRQ-C-13; T255 — space-separated for the recipe's for-loop
  $customerTokens.customerOutboundB2BAttested = if ($intake['identityPreset'] -ceq 'B2BGuest') { ([string][bool]$intake['customerOutboundB2BAttested']).ToLowerInvariant() } else { 'notApplicable' }   # PRQ-C-14
  $scopes = @('once_per_customer')
  # Interactive: Step 0.5 ran without the env, so once_per_env never ran — run it here, still before Step 1f writes.
  if (-not $isBatch -and -not (@($pr['envScopes']) -contains 'once_per_env')) { $scopes = @('once_per_env') + $scopes }
  $results = Invoke-PrereqScopes -Scopes $scopes -Tokens $customerTokens
  $pr['customerScopes'] = $scopes
}

# --- 0.5d. Report + HARD STOP ------------------------------------------------------------------------------------
Write-Host ''
Write-Host 'EXTERNAL PREREQUISITES (from scripts/provisioning-prereqs/prereqs.yaml)'
foreach ($r in $results) {
  if ($r.Passed) { Write-Host "  [PASS] $($r.Id) $($r.Name)" -ForegroundColor Green; continue }
  Write-Host "  [FAIL] $($r.Id) $($r.Name)" -ForegroundColor Red
  Write-Host "    Output:      $($r.Output)"
  Write-Host "    Consequence: $($r.Consequence)"
  Write-Host "    Remediation: $($r.Remediation) See docs/guides/PROVISIONING-PREREQUISITES.md#$($r.Id)."
}
$pr[$Pass.ToLowerInvariant()] = @($results | ForEach-Object { [ordered]@{ id = $_.Id; scope = $_.Scope; passed = $_.Passed; exitCode = $_.ExitCode } })
Update-ProvisionSession -SessionFile $SessionFile -Values @{ prereqResults = $pr } | Out-Null

if (@($results | Where-Object { -not $_.Passed }).Count -gt 0) {
  Stop-Provision '    HARD STOP — resolve every failed prerequisite before proceeding.'
}
Write-Host "  [ALL PASS] $(@($results).Count) prerequisite(s) in scope(s) $($scopes -join ', ')." -ForegroundColor Green
exit 0
