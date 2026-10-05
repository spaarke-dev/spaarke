#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Lists the SPE container-type configs whose Key Vault secret name is outside the BFF's allow-list (unified-access-
    control-r2 task 165, owner round 35 item 3). READ-ONLY: it never writes anything.

.DESCRIPTION
    sprk_specontainertypeconfig.sprk_keyvaultsecretname names the Key Vault secret the BFF reads, app-only, to act as a
    config's owning app. Since round 35 item 3 the BFF resolves ONLY names under ONE pinned prefix
    ($SpeConfigSecretNamePrefix below — MUST equal Sprk.Bff.Api.Services.SpeAdmin.SpeConfigSecretNamePolicy.RequiredPrefix;
    SpeAdminContainerBindingGuardTests pins both). A config naming anything else is refused: config POST/PUT answer 400,
    every route that would use its credential answers 409 'spe.admin.deny.config_secret_name_not_allowed', and the
    secret is never read.

    This script lists every config that does not conform — names only, never a secret value. Renaming is a MANUAL GATE
    for an operator: put the owning app's client secret into the BFF Key Vault under a conforming name
    (e.g. spe-owning-app-<customer>), then set the config's sprk_keyvaultsecretname to it (SPE Admin app -> Settings ->
    the config, which PUTs /api/spe/configs/{id}; or a Dataverse PATCH), then re-run with -Verify.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER Verify
    Exit 1 when ANY config (active or inactive) does not conform; exit 0 otherwise.

.EXAMPLE
    .\Test-SpeConfigSecretNames.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')

# ── THE pinned prefix. MUST equal SpeConfigSecretNamePolicy.RequiredPrefix (SpeAdminContainerBindingGuardTests). ─────
$SpeConfigSecretNamePrefix = 'spe-owning-app-'

# Key Vault's own name rule after the prefix (letters, digits, hyphens; 127 characters in all), case-insensitive — the
# same expression the BFF builds.
$allowed = '^' + [regex]::Escape($SpeConfigSecretNamePrefix) + '[A-Za-z0-9-]{1,' + (127 - $SpeConfigSecretNamePrefix.Length) + '}$'

function Test-SecretNameAllowed {
    param([AllowNull()][AllowEmptyString()][string]$Name)
    if ([string]::IsNullOrEmpty($Name)) { return $false }
    return [regex]::IsMatch($Name, $allowed, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
}

$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{ Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }

$uri = "$EnvironmentUrl/api/data/v9.2/sprk_specontainertypeconfigs?`$select=sprk_specontainertypeconfigid,sprk_name,sprk_keyvaultsecretname,statecode,_sprk_businessunit_value"
$configs = [System.Collections.Generic.List[object]]::new()
while ($uri) {
    $page = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
    $configs.AddRange(@($page.value))
    $uri = $page.'@odata.nextLink'
}

$nonConforming = @($configs | Where-Object { -not (Test-SecretNameAllowed $_.sprk_keyvaultsecretname) })

Write-Host ''
Write-Host "SPE config Key Vault secret names — allowed prefix '$SpeConfigSecretNamePrefix' — $EnvironmentUrl"
Write-Host "Configs: $($configs.Count)  Conforming: $($configs.Count - $nonConforming.Count)  NOT conforming: $($nonConforming.Count)"
foreach ($c in $nonConforming) {
    $state = if ([int]$c.statecode -eq 0) { 'active' } else { 'inactive' }
    $stored = if ($null -eq $c.sprk_keyvaultsecretname) { '(empty)' } else { "'$($c.sprk_keyvaultsecretname)'" }
    Write-Host "  NOT-CONFORMING $($c.sprk_specontainertypeconfigid) ($($c.sprk_name), $state): secret name $stored — the BFF refuses this config (409) until it is renamed."
}

if ($Verify) {
    exit ([int]($nonConforming.Count -gt 0))
}
exit 0
