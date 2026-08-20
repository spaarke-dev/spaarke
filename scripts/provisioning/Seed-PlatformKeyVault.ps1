<#
.SYNOPSIS
    Seed the L2 platform-controlplane Key Vault (sprk-controlplane-{env}-kv)
    with the 5 secrets its App Service KV-reference app-settings resolve --
    idempotent, never-overwrite, sentinel-aware.

.DESCRIPTION
    customer-provisioning-orchestration-r1 -- post-authoring audit defect #9
    (Wave G-8 Batch 4).

    THE GAP THIS CLOSES (T1-family silent fail):
      infrastructure/bicep/platform-controlplane.bicep + its two App Service
      modules (modules/controlplane-app-service.bicep,
      modules/controlplane-worker-app-service.bicep) wire @Microsoft.KeyVault
      references into L2 app-settings for FIVE platform-KV secrets -- but
      NOTHING seeded those secrets. Handler H4 seeds CUSTOMER vaults
      (sprk-{env}-kv via the canonical-secret-catalog generated seeder), never
      the platform-controlplane vault. On a fresh stamp every KV ref therefore
      fails to resolve, App Service passes the LITERAL
      '@Microsoft.KeyVault(...)' string through as the setting value, options
      Validate() sees a non-empty string and SUCCEEDS, and the failure
      surfaces much later as garbage-credential errors downstream.

    THE 5 SECRETS (names MUST match the Bicep modules' KV-reference
    SecretName= values exactly):
      1. Dataverse-ClientSecret   (BINDING never-delete) -- deliberately
                                  seeded with a SENTINEL, never a real value,
                                  unless -DataverseClientSecret is explicitly
                                  passed: platform-controlplane.bicep's param
                                  note says "keep the dummy KV binding; do NOT
                                  seed a real secret (r3 MUST-NOT-reintroduce-
                                  S2S rule)" pending C1.4.
      2. BFF-API-ClientSecret     (BINDING never-delete) -- real value from
                                  -BffApiClientSecret, else sentinel; the
                                  Worker's EnvVarValuesOptions.Validate()
                                  fail-fasts at boot until the real value is
                                  populated.
      3. AzureOpenAI-Endpoint     -- real value from -AzureOpenAiEndpoint,
                                  else sentinel (only H12c's Model1Shared
                                  branch consults it; not boot-blocking).
      4. Sidecar-Shared-Secret    -- from -SidecarSharedSecret, else a
                                  freshly GENERATED random GUID (the sidecar
                                  container + Worker both resolve the SAME KV
                                  secret, so a generated value is immediately
                                  self-consistent).
      5. Exchange-Connect-Cert    -- base64 cert bytes from
                                  -ExchangeConnectCert, else sentinel (cert
                                  material can only come from an operator OOB
                                  ceremony).

    NEVER-DELETE / NEVER-OVERWRITE GUARD (BINDING):
      If a secret ALREADY EXISTS in the vault -- with ANY value -- this script
      SKIPS it and reports SKIPPED. It never overwrites, never deletes. This
      makes re-runs safe (idempotent) and protects the BINDING never-delete
      pair (Dataverse-ClientSecret, BFF-API-ClientSecret) per
      scripts/canonical-secret-catalog/manifest.yaml.

    OUTPUT:
      Per-secret status (CREATED / SKIPPED / FAILED / WHATIF) + a summary
      table + the follow-up ceremony list (which sentinels an operator must
      replace with real values out-of-band). Exit 0 when nothing failed;
      exit 1 on any FAILED secret; exit 2 on pre-flight failure.

    SECRET HYGIENE: secret VALUES are never echoed -- only names + value
    provenance (parameter / generated / sentinel) appear in output.

.PARAMETER Environment
    Target environment name (dev, staging, prod -- matching
    platform-controlplane.bicep's @allowed values, which drive the vault
    naming). Default: dev.

.PARAMETER KeyVaultName
    Target Key Vault name. Default: sprk-controlplane-{Environment}-kv
    (platform-controlplane.bicep convention).

.PARAMETER DataverseClientSecret
    Optional real value for Dataverse-ClientSecret. LEAVE UNSET in normal
    operation -- the sentinel 'pending-oob-population' is the CORRECT
    steady-state per the r3 MUST-NOT-reintroduce-S2S rule (see
    platform-controlplane.bicep's dataverseClientSecretName param note);
    passing a real value emits a warning but is honored.

.PARAMETER BffApiClientSecret
    Optional real value for BFF-API-ClientSecret (the shared multitenant BFF
    app-registration client secret). If unset, the sentinel
    'pending-oob-population' is seeded and an operator MUST replace it
    out-of-band before the Worker can pass EnvVarValuesOptions validation.

.PARAMETER AzureOpenAiEndpoint
    Optional real value for AzureOpenAI-Endpoint (shared-platform Azure
    OpenAI resource endpoint, e.g. https://{name}.openai.azure.com/). If
    unset, the sentinel 'pending-oob-population' is seeded.

.PARAMETER SidecarSharedSecret
    Optional value for Sidecar-Shared-Secret (the X-Sidecar-Auth per-boot
    shared secret). If unset, a random GUID is GENERATED and seeded -- both
    the Worker and the sidecar container resolve this same KV secret, so a
    generated value is self-consistent without operator follow-up.

.PARAMETER ExchangeConnectCert
    Optional base64-encoded certificate bytes for Exchange-Connect-Cert. If
    unset, the sentinel 'pending-oob-population' is seeded and an operator
    MUST replace it with the real cert material out-of-band before the
    Exchange sidecar can connect.

.PARAMETER DryRun
    Preview mode. Sets $WhatIfPreference = $true for the whole run so every
    `az keyvault secret set` is skipped and reported as "What if:" --
    existence probes (read-only) still run so the CREATED-vs-SKIPPED
    prediction is accurate. Equivalent to -WhatIf; provided as a named switch
    for parity with the other provisioning scripts
    (Deploy-ControlPlane.ps1, Grant-ControlPlaneIdentity.ps1).

.EXAMPLE
    # Dev: seed all 5 (sentinels + generated sidecar secret)
    .\Seed-PlatformKeyVault.ps1

.EXAMPLE
    # Dev: seed with a real OpenAI endpoint + real BFF client secret
    .\Seed-PlatformKeyVault.ps1 `
        -AzureOpenAiEndpoint 'https://spaarke-openai-dev.openai.azure.com/' `
        -BffApiClientSecret $env:BFF_API_CLIENT_SECRET

.EXAMPLE
    # Preview a prod run without touching the vault
    .\Seed-PlatformKeyVault.ps1 -Environment prod -WhatIf

.NOTES
    Project:      customer-provisioning-orchestration-r1
    Origin:       post-authoring audit 2026-08-20, defect #9 (Wave G-8 Batch 4)
    Siblings:     scripts/canonical-secret-catalog/generated/
                  Seed-CustomerKeyVault.generated.ps1 (CUSTOMER vaults --
                  generated from manifest.yaml; this script covers the
                  PLATFORM-controlplane vault, which is deliberately NOT in
                  that per-customer catalog).
    Idempotent:   Yes -- existing secrets are always SKIPPED, never
                  overwritten (BINDING never-delete guard).
#>

#Requires -Version 7.0

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('dev', 'staging', 'prod')]
    [string]$Environment = 'dev',

    [Parameter(Mandatory = $false)]
    [string]$KeyVaultName,

    [Parameter(Mandatory = $false)]
    [string]$DataverseClientSecret,

    [Parameter(Mandatory = $false)]
    [string]$BffApiClientSecret,

    [Parameter(Mandatory = $false)]
    [string]$AzureOpenAiEndpoint,

    [Parameter(Mandatory = $false)]
    [string]$SidecarSharedSecret,

    [Parameter(Mandatory = $false)]
    [string]$ExchangeConnectCert,

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# -DryRun is sugar over the built-in -WhatIf mechanism (same idiom as
# Deploy-ControlPlane.ps1): the single mutating call below is gated behind
# $PSCmdlet.ShouldProcess().
if ($DryRun) {
    $WhatIfPreference = $true
}

if (-not $KeyVaultName) { $KeyVaultName = "sprk-controlplane-$Environment-kv" }

$SentinelValue = 'pending-oob-population'

# -----------------------------------------------------------------------------
# Console helpers (style parity with Deploy-ControlPlane.ps1)
# -----------------------------------------------------------------------------

function Write-Header {
    param([Parameter(Mandatory)][string]$Title)
    Write-Host ''
    Write-Host ('=' * 78) -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host ('=' * 78) -ForegroundColor Cyan
    Write-Host ''
}

function Write-Section { param([Parameter(Mandatory)][string]$Title) Write-Host ''; Write-Host "  === $Title ===" -ForegroundColor Cyan }
function Write-Step    { param([string]$D) Write-Host "  [STEP] $D" -ForegroundColor Yellow }
function Write-Success { param([string]$M) Write-Host "  [OK] $M" -ForegroundColor Green }
function Write-Info    { param([string]$M) Write-Host "  [--] $M" -ForegroundColor Gray }
function Write-Warn    { param([string]$M) Write-Host "  [!!] $M" -ForegroundColor DarkYellow }
function Write-Skip    { param([string]$M) Write-Host "  [SKIP] $M" -ForegroundColor DarkCyan }
function Write-Fail    { param([string]$M) Write-Host "  [FAIL] $M" -ForegroundColor Red }

# -----------------------------------------------------------------------------
# Banner
# -----------------------------------------------------------------------------

Write-Header 'SEED PLATFORM-CONTROLPLANE KEY VAULT (audit defect #9)'

if ($DryRun -or $WhatIfPreference) {
    Write-Host '  *** DRY RUN / -WhatIf MODE - no secret will be written ***' -ForegroundColor Magenta
    Write-Host ''
}

Write-Host "  Environment:   $Environment" -ForegroundColor Gray
Write-Host "  Key Vault:     $KeyVaultName" -ForegroundColor Gray
Write-Host '  Guard:         existing secrets are SKIPPED, never overwritten (BINDING)' -ForegroundColor Gray
Write-Host ''

if ($DataverseClientSecret) {
    Write-Warn 'A REAL -DataverseClientSecret was passed. Per the r3 MUST-NOT-reintroduce-S2S rule (platform-controlplane.bicep dataverseClientSecretName param note), the platform vault should normally carry only the sentinel until C1.4 lands. Proceeding because the operator was explicit.'
}

# -----------------------------------------------------------------------------
# Pre-flight
# -----------------------------------------------------------------------------

Write-Section 'PRE-FLIGHT'

if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Fail "PowerShell 7+ required. Detected: $($PSVersionTable.PSVersion)."
    exit 2
}
Write-Success "PowerShell version: $($PSVersionTable.PSVersion)"

# Capture full output BEFORE any Select-Object (see Deploy-ControlPlane.ps1's
# pre-flight note on stale $LASTEXITCODE with early-terminating pipelines).
$azProbeLines = az --version 2>$null
if ($LASTEXITCODE -ne 0 -or -not $azProbeLines) {
    Write-Fail 'Azure CLI (az) not found on PATH. Install: https://learn.microsoft.com/cli/azure/install-azure-cli'
    exit 2
}
Write-Success "Azure CLI available: $($azProbeLines | Select-Object -First 1)"

$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Fail "Azure CLI is not authenticated. Run 'az login' with Key Vault Secrets Officer (or equivalent set-secret rights) on '$KeyVaultName' first."
    exit 2
}
Write-Success "Authenticated as '$($account.user.name)' (subscription: $($account.name))"

$vaultProbe = az keyvault show --name $KeyVaultName --query 'name' --output tsv 2>$null
if ($LASTEXITCODE -ne 0 -or -not $vaultProbe) {
    Write-Fail "Key Vault '$KeyVaultName' not found / not reachable. Deploy platform-controlplane.bicep for '$Environment' first (this script seeds an EXISTING vault; it does not create one)."
    exit 2
}
Write-Success "Key Vault reachable: $KeyVaultName"

# -----------------------------------------------------------------------------
# Secret plan -- name + value + PROVENANCE (values are never echoed).
# Names MUST match the Bicep modules' KV-reference SecretName= values exactly:
#   modules/controlplane-app-service.bicep        -> Dataverse-ClientSecret
#   modules/controlplane-worker-app-service.bicep -> Dataverse-ClientSecret,
#       BFF-API-ClientSecret, AzureOpenAI-Endpoint, Sidecar-Shared-Secret,
#       Exchange-Connect-Cert
# -----------------------------------------------------------------------------

$secretPlan = @(
    [pscustomobject]@{
        Name       = 'Dataverse-ClientSecret'
        Value      = if ($DataverseClientSecret) { $DataverseClientSecret } else { $SentinelValue }
        Provenance = if ($DataverseClientSecret) { 'parameter' } else { 'sentinel' }
        FollowUp   = if ($DataverseClientSecret) { $null } else { 'Sentinel is the CORRECT steady-state (r3 MUST-NOT-reintroduce-S2S; dummy binding kept pending C1.4). No action needed.' }
        Note       = 'BINDING never-delete. Dummy KV binding per platform-controlplane.bicep param note.'
    }
    [pscustomobject]@{
        Name       = 'BFF-API-ClientSecret'
        Value      = if ($BffApiClientSecret) { $BffApiClientSecret } else { $SentinelValue }
        Provenance = if ($BffApiClientSecret) { 'parameter' } else { 'sentinel' }
        FollowUp   = if ($BffApiClientSecret) { $null } else { 'Operator MUST replace with the real shared BFF app-reg client secret out-of-band -- Worker EnvVarValuesOptions.Validate() fail-fasts at boot until then.' }
        Note       = 'BINDING never-delete. Shared multitenant BFF app-reg secret (H7 credential provisioning source).'
    }
    [pscustomobject]@{
        Name       = 'AzureOpenAI-Endpoint'
        Value      = if ($AzureOpenAiEndpoint) { $AzureOpenAiEndpoint } else { $SentinelValue }
        Provenance = if ($AzureOpenAiEndpoint) { 'parameter' } else { 'sentinel' }
        FollowUp   = if ($AzureOpenAiEndpoint) { $null } else { 'Operator MUST replace with the shared-platform Azure OpenAI endpoint before any Model1Shared H12c run.' }
        Note       = 'Consumed by H12c Model1Shared branch (RuntimeReferences__SharedPlatformOpenAiEndpoint); not boot-blocking.'
    }
    [pscustomobject]@{
        Name       = 'Sidecar-Shared-Secret'
        Value      = if ($SidecarSharedSecret) { $SidecarSharedSecret } else { [guid]::NewGuid().ToString() }
        Provenance = if ($SidecarSharedSecret) { 'parameter' } else { 'generated' }
        FollowUp   = $null
        Note       = 'X-Sidecar-Auth shared secret. Worker AND sidecar container both resolve this same KV secret, so a generated value is self-consistent.'
    }
    [pscustomobject]@{
        Name       = 'Exchange-Connect-Cert'
        Value      = if ($ExchangeConnectCert) { $ExchangeConnectCert } else { $SentinelValue }
        Provenance = if ($ExchangeConnectCert) { 'parameter' } else { 'sentinel' }
        FollowUp   = if ($ExchangeConnectCert) { $null } else { 'Operator MUST replace with real base64 cert bytes out-of-band before the Exchange ApplicationAccessPolicy sidecar can connect.' }
        Note       = 'Base64 Exchange app-only certificate for the DS-1b sidecar.'
    }
)

# -----------------------------------------------------------------------------
# Seed loop -- existence probe (read-only) -> set (ShouldProcess-gated) ->
# read-back verification. Existing secrets are ALWAYS skipped (never
# overwritten -- BINDING).
# -----------------------------------------------------------------------------

Write-Section 'SEED SECRETS'

$created = New-Object System.Collections.Generic.List[string]
$skipped = New-Object System.Collections.Generic.List[string]
$failed  = New-Object System.Collections.Generic.List[string]
$whatIfPlanned = New-Object System.Collections.Generic.List[string]

foreach ($secret in $secretPlan) {
    # NEVER-OVERWRITE GUARD (BINDING): probe for existence first. `az keyvault
    # secret show` exits non-zero for a missing secret; stderr is suppressed
    # because "not found" is an EXPECTED branch here, not an error. A vault
    # that is unreachable/unauthorized fails loudly at the subsequent SET
    # (and pre-flight already probed vault reachability).
    $existing = az keyvault secret show `
        --vault-name $KeyVaultName `
        --name $secret.Name `
        --query 'name' `
        --output tsv 2>$null

    if ($LASTEXITCODE -eq 0 -and $existing) {
        Write-Skip "$($secret.Name) -- already exists; NOT overwritten (never-delete/never-overwrite guard)."
        $skipped.Add($secret.Name) | Out-Null
        continue
    }

    if ($PSCmdlet.ShouldProcess("$KeyVaultName/$($secret.Name)", "az keyvault secret set (provenance: $($secret.Provenance))")) {
        Write-Step "Creating $($secret.Name) (provenance: $($secret.Provenance))"
        az keyvault secret set `
            --vault-name $KeyVaultName `
            --name $secret.Name `
            --value $secret.Value `
            --tags "source=Seed-PlatformKeyVault" "provenance=$($secret.Provenance)" "environment=$Environment" `
            --output none 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Fail "$($secret.Name) -- az keyvault secret set failed (exit $LASTEXITCODE)."
            $failed.Add($secret.Name) | Out-Null
            continue
        }

        # Read-back verification (name + enabled only -- the value is never
        # echoed).
        $verify = az keyvault secret show `
            --vault-name $KeyVaultName `
            --name $secret.Name `
            --query '{name:name,enabled:attributes.enabled}' `
            --output json 2>$null | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or -not $verify -or $verify.name -ne $secret.Name -or $verify.enabled -ne $true) {
            Write-Fail "$($secret.Name) -- post-set verification FAILED (secret not readable/enabled after set)."
            $failed.Add($secret.Name) | Out-Null
            continue
        }

        Write-Success "$($secret.Name) -- CREATED + verified ($($secret.Provenance))."
        $created.Add($secret.Name) | Out-Null
    }
    else {
        Write-Info "$($secret.Name) -- WOULD be created (provenance: $($secret.Provenance)); skipped by -WhatIf/-DryRun."
        $whatIfPlanned.Add($secret.Name) | Out-Null
    }
}

# -----------------------------------------------------------------------------
# Summary
# -----------------------------------------------------------------------------

Write-Header 'SUMMARY'
Write-Host "  Key Vault:  $KeyVaultName" -ForegroundColor Gray
Write-Host "  CREATED:    $(if ($created.Count) { $created -join ', ' } else { '(none)' })" -ForegroundColor $(if ($created.Count) { 'Green' } else { 'Gray' })
Write-Host "  SKIPPED:    $(if ($skipped.Count) { $skipped -join ', ' } else { '(none)' }) (already existed -- never overwritten)" -ForegroundColor $(if ($skipped.Count) { 'DarkCyan' } else { 'Gray' })
if ($whatIfPlanned.Count) {
    Write-Host "  WHATIF:     $($whatIfPlanned -join ', ') (would be created on a real run)" -ForegroundColor Magenta
}
Write-Host "  FAILED:     $(if ($failed.Count) { $failed -join ', ' } else { '(none)' })" -ForegroundColor $(if ($failed.Count) { 'Red' } else { 'Gray' })
Write-Host ''

$followUps = $secretPlan | Where-Object { $_.FollowUp -and ($created.Contains($_.Name) -or $whatIfPlanned.Contains($_.Name)) }
if ($followUps) {
    Write-Host '  FOLLOW-UP CEREMONY (out-of-band operator actions):' -ForegroundColor Yellow
    foreach ($fu in $followUps) {
        Write-Host "    - $($fu.Name): $($fu.FollowUp)" -ForegroundColor Yellow
    }
    Write-Host ''
}

if ($failed.Count -gt 0) {
    Write-Fail "$($failed.Count) secret(s) FAILED to seed. Fix + re-run (safe: existing secrets are skipped)."
    exit 1
}

if ($DryRun -or $WhatIfPreference) {
    Write-Success 'Dry run complete -- re-run without -WhatIf/-DryRun to apply.'
}
else {
    Write-Success 'Platform Key Vault seeding complete.'
}
exit 0
