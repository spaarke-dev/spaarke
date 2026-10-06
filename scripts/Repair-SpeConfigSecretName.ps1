#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Makes ONE SPE container-type config's Key Vault secret name conform to the BFF's allow-list: stores the config's
    owning-app client secret under a conforming name in the BFF Key Vault(s) and points the config at it
    (unified-access-control-r2 task 165, owner round 41 item 4). Dry run by default; -Apply writes; -Verify checks.
    It NEVER deletes the config, and it never prints a secret value.

.DESCRIPTION
    sprk_specontainertypeconfig.sprk_keyvaultsecretname names the Key Vault secret the BFF reads, app-only, to act as the
    config's owning app. Since round 35 item 3 the BFF resolves ONLY names under one pinned prefix
    (scripts/common/SpeConfigSecretNamePolicy.ps1, equal to the C# rule). A config naming anything else — dev config
    68f9a952… "Spaarke SPE Model 1 Owner" stores the literal 'null' — is refused by every route that would use its
    credential (409 'spe.admin.deny.config_secret_name_not_allowed'), and the container-binding backfill cannot list its
    containers. scripts/Test-SpeConfigSecretNames.ps1 -Verify lists such configs; this script repairs one.

    WHAT -Apply DOES, in order (each step re-runnable; a repeat run settles what is already done):
      1. The secret VALUE. If any target vault already holds -SecretName, that value is used (all holders must agree)
         and NOTHING is minted — so a repeat run never adds a second credential. Otherwise it comes from exactly one of:
           -MintClientSecret   adds a NEW client secret to the config's owning app registration (Graph addPassword —
                               an Entra write; the app's existing credentials are kept), valid -SecretYears years. Its
                               keyId is printed so it can be removed later; the value never is.
           -SourceKeyVaultName / -SourceSecretName   copies an existing secret.
      2. Stores the value under -SecretName in every -KeyVaultName that lacks it (an existing secret is never
         overwritten).
      3. PATCHes the config's sprk_keyvaultsecretname to -SecretName, with If-Match on the ETag it read (a config changed
         since the read is refused, not overwritten).

    -Verify (read-only) exits 0 only when: the config names -SecretName and it conforms; every -KeyVaultName holds it,
    all with the same value; and that value authenticates as the config's owning app (a client-credentials token request
    for Microsoft Graph — no write). Otherwise it exits 1.

    Which vaults: the BFF reads from its KeyVaultUri app setting (dev 2026-10-04: spaarke-spekvcert); the container-
    binding backfill reads from the vault an operator passes it (the gate uses sprk-prod-kv). Pass both.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER ConfigId
    The sprk_specontainertypeconfig id to repair.

.PARAMETER SecretName
    The conforming secret name to store and point the config at, e.g. spe-owning-app-model1-owner. Refused (exit 2)
    before anything is read when it does not conform.

.PARAMETER KeyVaultName
    The vault(s) to store the secret in (one or more).

.PARAMETER MintClientSecret
    -Apply only: when no target vault already holds -SecretName, add a new client secret to the owning app.

.PARAMETER SecretYears
    Lifetime of a minted secret, in years (1 or 2). Default 1.

.PARAMETER SourceKeyVaultName
    -Apply only: copy the value from this vault (with -SourceSecretName) instead of minting.

.PARAMETER SourceSecretName
    -Apply only: the secret to copy from -SourceKeyVaultName.

.PARAMETER TenantId
    The tenant the owning app authenticates in (-Verify's token check). Default: the signed-in az account's tenant.

.PARAMETER Apply
    Perform the writes. Without it (and without -Verify) the script only prints its plan.

.PARAMETER Verify
    Read-only check; exit 0 when the config is repaired, 1 otherwise.

.EXAMPLE
    # dry run, then apply (mint), then verify — dev config 68f9a952 (owner round 41 item 4)
    .\Repair-SpeConfigSecretName.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -ConfigId 68f9a952-44bf-f111-a05b-3833c5e9614d -SecretName spe-owning-app-model1-owner -KeyVaultName spaarke-spekvcert,sprk-prod-kv
    .\Repair-SpeConfigSecretName.ps1 ... -MintClientSecret -Apply
    .\Repair-SpeConfigSecretName.ps1 ... -Verify
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$EnvironmentUrl,
    [Parameter(Mandatory)] [string]$ConfigId,
    [Parameter(Mandatory)] [string]$SecretName,
    [Parameter(Mandatory)] [string[]]$KeyVaultName,
    [switch]$MintClientSecret,
    [ValidateRange(1, 2)] [int]$SecretYears = 1,
    [string]$SourceKeyVaultName,
    [string]$SourceSecretName,
    [string]$TenantId,
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')

# ── THE rule: the ONE PowerShell copy of the BFF's allow-list, pinned equal to the C# one by Spaarke.ArchTests. ───────
. (Join-Path $PSScriptRoot 'common/SpeConfigSecretNamePolicy.ps1')

function Stop-Input([string]$Message) { Write-Host "INVALID INPUT: $Message" -ForegroundColor Red; exit 2 }

# ── Inputs are validated before anything is read. ────────────────────────────────────────────────────────────────
$configGuid = [guid]::Empty
if (-not [guid]::TryParse($ConfigId, [ref]$configGuid) -or $configGuid -eq [guid]::Empty) { Stop-Input "-ConfigId '$ConfigId' is not a GUID." }
if (-not (Test-SpeConfigSecretNameAllowed $SecretName)) {
    Stop-Input "-SecretName '$SecretName' does not conform: it must start with '$SpeConfigSecretNamePrefix' and use letters, digits and hyphens only (127 characters in all)."
}
$vaults = @($KeyVaultName | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
if ($vaults.Count -eq 0) { Stop-Input '-KeyVaultName names no vault.' }
if ($Apply -and $Verify) { Stop-Input 'Use -Apply OR -Verify, not both.' }
$copySource = -not [string]::IsNullOrWhiteSpace($SourceKeyVaultName) -or -not [string]::IsNullOrWhiteSpace($SourceSecretName)
if ($copySource -and ([string]::IsNullOrWhiteSpace($SourceKeyVaultName) -or [string]::IsNullOrWhiteSpace($SourceSecretName))) {
    Stop-Input 'Copying needs BOTH -SourceKeyVaultName and -SourceSecretName.'
}
if ($MintClientSecret -and $copySource) { Stop-Input 'Choose ONE value source: -MintClientSecret OR -SourceKeyVaultName/-SourceSecretName.' }

# ── Tokens (the operator's own az login) ──────────────────────────────────────────────────────────────────────────
function Get-Token([string]$Resource) {
    $t = az account get-access-token --resource $Resource --query accessToken -o tsv 2>$null
    if (-not $t) { throw "No token for $Resource. Run 'az login' and retry." }
    return $t
}
if (-not $TenantId) { $TenantId = az account show --query tenantId -o tsv 2>$null }

$dvHeaders = @{
    Authorization      = "Bearer $(Get-Token $EnvironmentUrl)"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
}

# ── Key Vault data plane (REST: a secret value is never on a command line or in the output) ──────────────────────
$kvToken = $null
function Get-KvHeaders {
    if (-not $script:kvToken) { $script:kvToken = Get-Token 'https://vault.azure.net' }
    return @{ Authorization = "Bearer $script:kvToken" }
}
function Test-VaultHoldsSecret([string]$Vault, [string]$Name) {
    $names = @(az keyvault secret list --vault-name $Vault --query '[].name' -o tsv 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "Could not list the secrets of vault '$Vault' (names only)." }
    return [bool]($names | Where-Object { [string]::Equals($_, $Name, [System.StringComparison]::OrdinalIgnoreCase) })
}
function Read-VaultSecret([string]$Vault, [string]$Name) {
    $r = Invoke-RestMethod -Method Get -Uri "https://$Vault.vault.azure.net/secrets/$([uri]::EscapeDataString($Name))?api-version=7.4" -Headers (Get-KvHeaders)
    return [string]$r.value
}
function Write-VaultSecret([string]$Vault, [string]$Name, [string]$Value) {
    $body = @{ value = $Value; contentType = 'SPE owning-app client secret (task 165 Repair-SpeConfigSecretName.ps1)' } | ConvertTo-Json
    Invoke-RestMethod -Method Put -Uri "https://$Vault.vault.azure.net/secrets/$([uri]::EscapeDataString($Name))?api-version=7.4" `
        -Headers (Get-KvHeaders) -ContentType 'application/json' -Body $body | Out-Null
}

# ── 1. The config (read-only) ────────────────────────────────────────────────────────────────────────────────────
$configUri = "$EnvironmentUrl/api/data/v9.2/sprk_specontainertypeconfigs($configGuid)"
try {
    $config = Invoke-RestMethod -Method Get -Headers $dvHeaders `
        -Uri "$($configUri)?`$select=sprk_specontainertypeconfigid,sprk_name,sprk_keyvaultsecretname,sprk_owningappid,statecode"
}
catch {
    Write-Host "Config $configGuid could not be read: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
$etag = $config.'@odata.etag'
$owningApp = [guid]::Empty
if (-not [guid]::TryParse([string]$config.sprk_owningappid, [ref]$owningApp) -or $owningApp -eq [guid]::Empty) {
    Write-Host "Config $configGuid has no owning app id (sprk_owningappid) — nothing can be repaired." -ForegroundColor Red
    exit 1
}
$current = [string]$config.sprk_keyvaultsecretname
$currentShown = if ([string]::IsNullOrEmpty($current)) { '(empty)' } else { "'$current'" }
$state = if ([int]$config.statecode -eq 0) { 'active' } else { 'inactive' }

Write-Host ''
Write-Host "SPE config secret-name repair — $EnvironmentUrl" -ForegroundColor Cyan
Write-Host "  Config      $configGuid ($($config.sprk_name), $state)"
Write-Host "  Owning app  $owningApp"
Write-Host "  Stored name $currentShown — $(if (Test-SpeConfigSecretNameAllowed $current) { 'conforms' } else { 'does NOT conform' })"
Write-Host "  Target name '$SecretName' in vault(s): $($vaults -join ', ')"

$holders = @($vaults | Where-Object { Test-VaultHoldsSecret $_ $SecretName })
$lacking = @($vaults | Where-Object { $holders -notcontains $_ })

# ── -Verify (read-only) ──────────────────────────────────────────────────────────────────────────────────────────
if ($Verify) {
    $failures = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::Equals($current, $SecretName, [System.StringComparison]::OrdinalIgnoreCase)) {
        $failures.Add("the config names $currentShown, not '$SecretName'")
    }
    foreach ($v in $lacking) { $failures.Add("vault '$v' does not hold '$SecretName'") }
    if ($holders.Count -gt 0) {
        $values = @($holders | ForEach-Object { Read-VaultSecret $_ $SecretName })
        if (@($values | Select-Object -Unique).Count -gt 1) {
            $failures.Add("the vaults hold DIFFERENT values under '$SecretName'")
        }
        try {
            Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" -ContentType 'application/x-www-form-urlencoded' `
                -Body @{ client_id = "$owningApp"; client_secret = $values[0]; scope = 'https://graph.microsoft.com/.default'; grant_type = 'client_credentials' } | Out-Null
            Write-Host "  OK  the stored value authenticates as owning app $owningApp (tenant $TenantId)." -ForegroundColor Green
        }
        catch {
            $failures.Add("the stored value does NOT authenticate as owning app $owningApp in tenant $TenantId")
        }
    }

    if ($failures.Count -gt 0) {
        foreach ($f in $failures) { Write-Host "  FAIL $f" -ForegroundColor Red }
        exit 1
    }

    Write-Host "  VERIFIED — the config names a conforming secret every vault holds, and it authenticates." -ForegroundColor Green
    exit 0
}

# ── Plan (dry run) / Apply ───────────────────────────────────────────────────────────────────────────────────────
$source = if ($holders.Count -gt 0) {
    "the value already in vault '$($holders[0])' (nothing is minted)"
} elseif ($MintClientSecret) {
    "a NEW client secret on app $owningApp (Graph addPassword — an Entra write; existing credentials kept), $SecretYears year(s)"
} elseif ($copySource) {
    "a copy of '$SourceSecretName' from vault '$SourceKeyVaultName'"
} else {
    $null
}

Write-Host ''
Write-Host "  PLAN  value source: $(if ($source) { $source } else { 'NONE — pass -MintClientSecret, or -SourceKeyVaultName/-SourceSecretName' })"
foreach ($v in $holders) { Write-Host "  PLAN  vault '$v' already holds '$SecretName' — kept, never overwritten" }
foreach ($v in $lacking) { Write-Host "  PLAN  STORE '$SecretName' in vault '$v'" }
if ([string]::Equals($current, $SecretName, [System.StringComparison]::OrdinalIgnoreCase)) {
    Write-Host "  PLAN  the config already names '$SecretName' — no PATCH"
} else {
    Write-Host "  PLAN  PATCH config $configGuid sprk_keyvaultsecretname $currentShown -> '$SecretName' (If-Match its ETag)"
}

if (-not $Apply) {
    Write-Host ''
    Write-Host 'DRY RUN — nothing was written. Re-run with -Apply, then with -Verify.' -ForegroundColor Yellow
    exit 0
}

if (-not $source) {
    Write-Host 'Nothing to store: no target vault holds the secret, and no value source was given.' -ForegroundColor Red
    exit 2
}

# 1. The value (held in memory only).
if ($holders.Count -gt 0) {
    $values = @($holders | ForEach-Object { Read-VaultSecret $_ $SecretName })
    if (@($values | Select-Object -Unique).Count -gt 1) {
        Write-Host "The vaults already holding '$SecretName' disagree on its value — settle that by hand first." -ForegroundColor Red
        exit 1
    }
    $value = $values[0]
}
elseif ($MintClientSecret) {
    $graphHeaders = @{ Authorization = "Bearer $(Get-Token 'https://graph.microsoft.com')" }
    $body = @{
        passwordCredential = @{
            displayName = "BFF SPE admin — $SecretName (task 165 Repair-SpeConfigSecretName.ps1)"
            endDateTime = (Get-Date).ToUniversalTime().AddYears($SecretYears).ToString('o')
        }
    } | ConvertTo-Json -Depth 3
    $minted = Invoke-RestMethod -Method Post -Uri "https://graph.microsoft.com/v1.0/applications(appId='$owningApp')/addPassword" `
        -Headers $graphHeaders -ContentType 'application/json' -Body $body
    $value = [string]$minted.secretText
    Write-Host "  DONE  minted a client secret on app $owningApp — keyId $($minted.keyId), expires $($minted.endDateTime) (value not shown)" -ForegroundColor Green
}
else {
    $value = Read-VaultSecret $SourceKeyVaultName $SourceSecretName
}
if ([string]::IsNullOrEmpty($value)) {
    Write-Host 'The secret value is empty — nothing was stored.' -ForegroundColor Red
    exit 1
}

# 2. Store it where it is missing (never overwrite).
foreach ($v in $lacking) {
    Write-VaultSecret $v $SecretName $value
    Write-Host "  DONE  stored '$SecretName' in vault '$v'" -ForegroundColor Green
}
$value = $null

# 3. Point the config at it (If-Match: a config changed since the read is refused, not overwritten).
if (-not [string]::Equals($current, $SecretName, [System.StringComparison]::OrdinalIgnoreCase)) {
    $patchHeaders = $dvHeaders.Clone()
    $patchHeaders['If-Match'] = $etag
    try {
        Invoke-RestMethod -Method Patch -Uri $configUri -Headers $patchHeaders -ContentType 'application/json' `
            -Body (@{ sprk_keyvaultsecretname = $SecretName } | ConvertTo-Json) | Out-Null
        Write-Host "  DONE  config $configGuid now names '$SecretName' (was $currentShown)" -ForegroundColor Green
    }
    catch {
        Write-Host "  FAILED to PATCH the config (changed since it was read, or no write privilege): $($_.Exception.Message)" -ForegroundColor Red
        Write-Host '         The secret IS stored; re-run -Apply to point the config at it (nothing is minted again).' -ForegroundColor Yellow
        exit 1
    }
}

Write-Host ''
Write-Host 'APPLIED. Now run with -Verify, then scripts/Test-SpeConfigSecretNames.ps1 -Verify.' -ForegroundColor Green
exit 0
