<#
.SYNOPSIS
H0 preflight: verify SPE (SharePoint Embedded) confidential-client certificate
is bootstrapped in Key Vault AND is old enough for the 24h Microsoft-side
replication to have completed (per FR-11 T6 lead-time item).

.DESCRIPTION
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
Name of the secret in KV. Default 'spe-owner-cert-pfx' matches the T6 helper
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
    [Parameter(Mandatory=$true)][string]$KeyVaultName,

    [Parameter()][string]$CertSecretName = 'spe-owner-cert-pfx',

    [Parameter()][int]$MinAgeHours = 24,

    [Parameter()][string]$SecretShowJsonPath
)

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
