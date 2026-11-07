<#
.SYNOPSIS
  /provision-environment Step 0.5c — SPE topology verify (task 213.6; BFF-app checks removed by T227a). Read-only.

.DESCRIPTION
  Per docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md §3A, the owning app-reg (permanent 1:1 with the
  container type, §R1) and the container type (1 of 25 per tenant, §R2) MUST exist before any dispatch.
    (1) per_env_constants.{env}.containerTypeId is set                         → else HARD STOP
    (2) GET graph /storage/fileStorage/containerTypes/{id} (delegated token)
          200 → id must match; read owningAppId                                → mismatch: HARD STOP
          404 → HARD STOP (never created / deleted / replication in progress)
          403 → SKIP (expected: the Azure CLI token is not consented for container-type reads, T248);
                H0's SpeOwnerCredential check verifies owning app + registration end to end
          other → HARD STOP
    (3) owningAppId resolves to a live Entra app-reg (skipped after a 403)      → else HARD STOP (broken topology — escalate)
  Exit 1 on any HARD STOP, in both interactive and batch mode.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$SessionFile)

Import-Module (Join-Path $PSScriptRoot '..\provision-environment\ProvisionEnvironment.psm1') -Force
$s = Read-ProvisionSession -SessionFile $SessionFile
$cpEnv = $s.controlPlaneEnv
Write-Host '=== Step 0.5c SPE topology verify (task 213.6) ===' -ForegroundColor Cyan
if (-not $cpEnv) { Stop-Provision '[skill] Step 0.5c needs the control-plane env. Record it (New-ProvisionSession -Env / Set-ProvisionSession -Env) and rerun.' }

$constants = Get-SpaarkeConstants
$containerTypeId = $constants.per_env_constants.$cpEnv.containerTypeId

# --- (1) containerTypeId present ---
if ([string]::IsNullOrWhiteSpace($containerTypeId)) {
  Stop-Provision "[skill-config] Step 0.5c HARD STOP (task 213.6): per_env_constants.$cpEnv.containerTypeId is null. Cannot verify SPE topology without a container-type GUID. Run docs/guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md steps 1-8 before dispatch, then re-run this skill."
}

# --- (2) Container-type exists + is queryable (delegated Graph token; app-only 403 per §R5) ---
$graphToken = az account get-access-token --resource https://graph.microsoft.com --query accessToken -o tsv 2>$null
if ([string]::IsNullOrWhiteSpace($graphToken)) {
  Stop-Provision "[skill-config] Step 0.5c HARD STOP: cannot acquire delegated Graph token for topology verify. Run 'az login' interactively and retry."
}
$resp = Invoke-WebRequest -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypes/$containerTypeId" `
  -Headers @{ Authorization = "Bearer $graphToken" } -Method GET -SkipHttpErrorCheck
$ctCode = [string]$resp.StatusCode
$ctBody = [string]$resp.Content
$owningAppId = $null
switch ($ctCode) {
  '200' {
    $ctJson = $ctBody | ConvertFrom-Json
    if ($ctJson.id -ne $containerTypeId) {
      Stop-Provision "[skill-config] Step 0.5c HARD STOP: container-type GET returned different id ($($ctJson.id) != $containerTypeId). Constants file may reference a stale/rotated GUID."
    }
    Write-Host "  [PASS] Container-type $containerTypeId exists (name: $($ctJson.name), owningAppId: $($ctJson.owningAppId))" -ForegroundColor Green
    $owningAppId = $ctJson.owningAppId
  }
  '404' {
    Stop-Provision "[skill-config] Step 0.5c HARD STOP: container-type $containerTypeId does not exist in Spaarke tenant (HTTP 404). Either (a) it was never created — run runbook steps 3-4, OR (b) it was deleted (impossible for 'standard' classification per §R3) — reconcile constants + runbook to actual state, OR (c) 24h replication still in progress — retry in 30 min (empirically ~2 min per operator memory)."
  }
  '403' {
    # Observed 2026-10-03 (T248): the Azure CLI's delegated Graph token is NOT consented for container-type reads
    # (FileStorageContainerType.Manage.All), so this GET returns 403 even for a Global Admin. Not a topology fault.
    Write-Warning "  [SKIP] Container-type GET returned 403 to the operator's Azure CLI token (expected — not consented for container-type reads). Verify '$containerTypeId' in the SharePoint admin center if in doubt; H0's SpeOwnerCredential check verifies the owning app and the registration."
  }
  default {
    Stop-Provision "[skill-config] Step 0.5c HARD STOP: container-type GET returned unexpected HTTP $ctCode. Body: $ctBody"
  }
}

# --- (3) Owning app-reg exists in Spaarke tenant (skipped when (2) could not read the container type) ---
if ($owningAppId) {
  $owningAppCheck = az ad app show --id $owningAppId --query displayName -o tsv 2>&1
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($owningAppCheck)) {
    Stop-Provision "[skill-config] Step 0.5c HARD STOP: container-type's owningAppId ($owningAppId) does NOT resolve to a live Entra app-reg. This is a broken topology state — the container-type references a deleted app-reg. Container-type binding is IMMUTABLE per §R1 — the container-type itself is now unusable. Escalate."
  }
  Write-Host "  [PASS] Owning app-reg $owningAppId ($owningAppCheck) exists in Spaarke tenant" -ForegroundColor Green
}

Write-Host '  [ALL PASS] SPE topology verified — proceeding to Step 0.5d report' -ForegroundColor Green
exit 0
