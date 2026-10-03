<#
.SYNOPSIS
RETIRED 2026-10-03 (customer-provisioning-orchestration-r1 task 248) - do not run;
it throws immediately. Formerly: H0 preflight verifying that the SPE owning-app
certificate was bootstrapped in Key Vault and at least 24h old.

.DESCRIPTION
============================================================================
RETIRED 2026-10-03 (customer-provisioning-orchestration-r1 task 248)
============================================================================
This script checks a certificate that no longer exists in the design. Do not
run it - it throws immediately.

The L2 control plane no longer signs in as an SPE container type's owning app
with a certificate. Owner decision D16 (2026-10-02): it uses a managed-identity
federated identity credential (MI-FIC, ADR-028 A4) - the owning app trusts the
L2 Worker's user-assigned managed identity, and nothing is stored in Key Vault.
The `SPE-OwnerCert-Pfx` secret this script looked for was never created and is
no longer part of the design, and there is nothing to bootstrap or age.

H0 now runs `SpeOwnerCredentialProbe` (check name `SpeOwnerCredential`): owner
entry configured -> owning-app token obtained through the federated credential
-> container type registration GET. Rejection codes `spe-owner-not-configured`,
`spe-owner-token-failed`, `spe-container-type-not-registered`. The former
`spe-cert-bootstrap-missing` code and the 24h age gate no longer exist.

Set-up procedure: docs/guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md
The description and body below are preserved for provenance only.
============================================================================

Former description:
Queries `az keyvault secret show --vault-name <vault> --name <secret>` and
confirms:
  (1) The secret EXISTS (returns 200 / non-null).
  (2) Its `attributes.created` timestamp is AT LEAST MinAgeHours in the past
      (default 24h — the Microsoft-documented replication window for SPE
      container-type auth after certificate rotation).

If either condition fails, H8 (SPE container-type provisioning) will fail with
403 "public client not allowed" per §4B trap catalog even though H4 wrote the
cert successfully. H0 surfaces this UP-FRONT.

Returns the shared preflight PSCustomObject contract:
    Result / CheckName / Headroom / Diagnostic

.PARAMETER KeyVaultName
KV that holds the SPE cert secret (PFX-encoded base64, per
scripts/common/Get-SpeConfidentialClientToken.ps1 convention).

.PARAMETER CertSecretName
Name of the secret in KV. Default 'SPE-OwnerCert-Pfx' matches the T6 helper
convention.

.PARAMETER MinAgeHours
Minimum age in hours the secret must be to have completed SPE replication.
Default 24 per FR-11 T6.

.PARAMETER SecretShowJsonPath
(Test-mode escape hatch) — path to a pre-captured JSON file containing the
`az keyvault secret show` output. When set, skips live `az` call.

.OUTPUTS
[PSCustomObject] with Result, CheckName, Headroom, Diagnostic.

.EXAMPLE
$r = & ./Test-SpeCertBootstrap.ps1 -KeyVaultName $env:SPE_KV_NAME
if ($r.Result -eq 'Fail') { throw $r.Diagnostic }

.EXAMPLE
$r = & ./Test-SpeCertBootstrap.ps1 -KeyVaultName does-not-exist
# Simulated fail path — non-existent KV → Result = Fail naming the cert URI.

.NOTES
This check does NOT download the cert (secret hygiene per T6). It only reads
the secret's `attributes.created` timestamp via the KV data-plane show call.
The caller MUST have `keys/get` + `secrets/get` KV data-plane RBAC on the vault.

Escalation: if `az keyvault secret show` no longer returns
`attributes.created` (or drops the `id` field used for the diagnostic URI),
STOP and escalate per root CLAUDE.md §6.
#>

[CmdletBinding()]
[OutputType([PSCustomObject])]
param(
    # Was Mandatory; made optional when the script was retired (task 248) so that
    # invoking it reaches the RETIRED throw below instead of prompting for a vault.
    [Parameter()][string]$KeyVaultName,

    [Parameter()][string]$CertSecretName = 'SPE-OwnerCert-Pfx',

    [Parameter()][int]$MinAgeHours = 24,

    [Parameter()][string]$SecretShowJsonPath
)

throw @"

RETIRED (2026-10-03 task 248): Test-SpeCertBootstrap checks an owning-app
certificate that no longer exists in the design. The L2 control plane signs in
as the SPE container type's owning app through a managed-identity federated
identity credential (MI-FIC) trusting the L2 Worker UAMI — there is no
certificate, no Key Vault secret and nothing to bootstrap.

H0 now runs SpeOwnerCredentialProbe (check 'SpeOwnerCredential'). To verify the
setup, dispatch a provisioning run and read its H0 result, or follow:
    docs/guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md (Step 8)

The script body below is preserved for provenance only.
"@

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$checkName = 'SpeCertBootstrap'

function New-PreflightResult {
    param(
        [ValidateSet('Pass','Fail')][string]$Result,
        [hashtable]$Headroom,
        [string]$Diagnostic
    )
    return [PSCustomObject]@{
        Result     = $Result
        CheckName  = $checkName
        Headroom   = $Headroom
        Diagnostic = $Diagnostic
    }
}

$certUri = "https://$KeyVaultName.vault.azure.net/secrets/$CertSecretName"

try {
    # -- Fetch secret metadata ---------------------------------------------
    if ($SecretShowJsonPath) {
        if (-not (Test-Path $SecretShowJsonPath)) {
            $r = New-PreflightResult Fail @{
                keyVault    = $KeyVaultName
                secretName  = $CertSecretName
                certUri     = $certUri
                minAgeHours = $MinAgeHours
            } "Test-mode SecretShowJsonPath '$SecretShowJsonPath' not found."
            $r | Write-Output
            exit 2
        }
        $secretJson = Get-Content -Raw -Path $SecretShowJsonPath
        $secretExists = $true
    }
    else {
        # `az keyvault secret show` uses --vault-name + --name.
        # Capture stderr — a "not found" is an expected fail-path here, not a hard error.
        $secretJson = az keyvault secret show --vault-name $KeyVaultName --name $CertSecretName --output json 2>&1
        $secretExists = ($LASTEXITCODE -eq 0)
    }

    if (-not $secretExists) {
        $r = New-PreflightResult Fail @{
            keyVault    = $KeyVaultName
            secretName  = $CertSecretName
            certUri     = $certUri
            minAgeHours = $MinAgeHours
            observed    = 'not-found'
        } "SPE cert-bootstrap MISSING: no secret '$CertSecretName' in vault '$KeyVaultName' (URI: $certUri). Run scripts/common cert-bootstrap helper OR verify H4 KV-seed handler ran cleanly. Note: even after bootstrap, wait ${MinAgeHours}h for SPE replication before proceeding to H8."
        $r | Write-Output
        exit 1
    }

    try { $secret = $secretJson | ConvertFrom-Json -ErrorAction Stop }
    catch {
        $r = New-PreflightResult Fail @{
            keyVault   = $KeyVaultName
            secretName = $CertSecretName
            certUri    = $certUri
        } "Failed to parse az keyvault secret show output as JSON. Escalate per README (API drift?). Error: $_"
        $r | Write-Output
        exit 5
    }

    # Verify shape
    if (-not $secret) {
        $r = New-PreflightResult Fail @{
            keyVault   = $KeyVaultName
            secretName = $CertSecretName
            certUri    = $certUri
        } "az keyvault secret show returned empty payload for URI $certUri. Escalate per README."
        $r | Write-Output
        exit 6
    }

    # attributes.created is required
    $createdRaw = $null
    if ($secret.PSObject.Properties.Name -contains 'attributes' -and $secret.attributes) {
        $attrs = $secret.attributes
        if ($attrs.PSObject.Properties.Name -contains 'created') {
            $createdRaw = $attrs.created
        }
    }

    if (-not $createdRaw) {
        $r = New-PreflightResult Fail @{
            keyVault   = $KeyVaultName
            secretName = $CertSecretName
            certUri    = $certUri
        } "az keyvault secret show output missing attributes.created — API shape may have drifted. Escalate per README."
        $r | Write-Output
        exit 7
    }

    try { $created = ([datetime]$createdRaw).ToUniversalTime() }
    catch {
        $r = New-PreflightResult Fail @{
            keyVault    = $KeyVaultName
            secretName  = $CertSecretName
            certUri     = $certUri
            createdRaw  = $createdRaw
        } "Failed to parse attributes.created '$createdRaw' as datetime. Escalate per README."
        $r | Write-Output
        exit 8
    }

    $now      = (Get-Date).ToUniversalTime()
    $ageHours = [math]::Round(($now - $created).TotalHours, 2)

    $headroom = @{
        keyVault      = $KeyVaultName
        secretName    = $CertSecretName
        certUri       = $certUri
        observedAgeHours = $ageHours
        requiredAgeHours = $MinAgeHours
        createdUtc    = $created.ToString('o')
    }

    if ($ageHours -ge $MinAgeHours) {
        $r = New-PreflightResult Pass $headroom `
            "SPE cert-bootstrap OK: '$CertSecretName' in '$KeyVaultName' is ${ageHours}h old (>= ${MinAgeHours}h replication requirement)."
        $r | Write-Output
        exit 0
    }

    $r = New-PreflightResult Fail $headroom `
        "SPE cert-bootstrap NOT YET REPLICATED: '$CertSecretName' in '$KeyVaultName' is only ${ageHours}h old (URI: $certUri), replication requires ${MinAgeHours}h per FR-11 T6. Wait $([math]::Ceiling($MinAgeHours - $ageHours))h more before proceeding, OR verify H4 cert-seed timestamp is intentional."
    $r | Write-Output
    exit 1
}
catch {
    $r = New-PreflightResult Fail @{
        keyVault   = $KeyVaultName
        secretName = $CertSecretName
        certUri    = $certUri
    } "Unhandled error in Test-SpeCertBootstrap: $($_.Exception.Message)"
    $r | Write-Output
    exit 99
}
