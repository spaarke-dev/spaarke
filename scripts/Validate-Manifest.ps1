<#
.SYNOPSIS
Validate spaarke-resources.yaml against actual Azure state — bi-directional drift check.

.DESCRIPTION
Runs three checks per environment:
  1. References → Vault   — every `kv:NAME` in the manifest exists as a KV secret
  2. Vault → References   — every KV secret is accounted for (referenced or in `keyvault_inventory`)
  3. Subscription sanity  — current az CLI subscription matches the env's expected subscription

Outputs a structured report and exits non-zero on hard drift (referenced secret missing in KV).

Requires:
- Azure CLI authenticated (`az login`)
- powershell-yaml module (auto-installed if missing)

.PARAMETER Environment
The environment to validate (dev | demo). Defaults to dev.

.EXAMPLE
./scripts/Validate-Manifest.ps1
./scripts/Validate-Manifest.ps1 -Environment demo
#>

[CmdletBinding()]
param(
    [ValidateSet('dev', 'demo')]
    [string]$Environment = 'dev',

    [string]$ManifestPath = 'config/spaarke-resources.yaml'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (& git rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) { throw "Not in a git repository" }
Set-Location $repoRoot

if (-not (Get-Module -ListAvailable -Name powershell-yaml)) {
    Install-Module -Name powershell-yaml -Scope CurrentUser -Force -AllowClobber
}
Import-Module powershell-yaml -ErrorAction Stop

if (-not (Test-Path $ManifestPath)) { throw "Manifest not found: $ManifestPath" }
$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Yaml

if (-not $manifest.environments.ContainsKey($Environment)) {
    throw "Environment '$Environment' not found"
}
$envConfig = $manifest.environments[$Environment]
$kvName = $envConfig.keyvault

Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host "Validating manifest for environment: $Environment" -ForegroundColor Cyan
Write-Host "Key Vault: $kvName" -ForegroundColor Cyan
Write-Host "===============================================================" -ForegroundColor Cyan

# Walk the env config to collect kv: references
function Get-KvReferences {
    param([Parameter(Mandatory)] $node, [string]$path = '')
    $refs = @()
    if ($null -eq $node) { return $refs }
    if ($node -is [System.Collections.IDictionary]) {
        foreach ($key in $node.Keys) {
            $currentPath = if ($path) { "$path.$key" } else { "$key" }
            $value = $node[$key]
            if ($value -is [string] -and $value -like 'kv:*') {
                $refs += [PSCustomObject]@{ Path = $currentPath; SecretName = $value.Substring(3) }
            } elseif ($value -is [System.Collections.IDictionary] -or $value -is [System.Collections.IList]) {
                $refs += Get-KvReferences -node $value -path $currentPath
            }
        }
    } elseif ($node -is [System.Collections.IList]) {
        for ($i = 0; $i -lt $node.Count; $i++) {
            $refs += Get-KvReferences -node $node[$i] -path "$path[$i]"
        }
    }
    return $refs
}

$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) { throw "Not logged into Azure CLI. Run: az login" }

# ---------------------------------------------------------------------------
# CHECK 1: References → Vault (every kv:NAME exists as a real secret)
# ---------------------------------------------------------------------------
Write-Host "`n[1/3] References → Vault" -ForegroundColor Yellow
$refs = Get-KvReferences -node $envConfig
$refSecretNames = $refs.SecretName | Sort-Object -Unique
Write-Host "  Manifest declares $($refs.Count) reference(s) ($($refSecretNames.Count) unique secret names)" -ForegroundColor DarkGray

$missingFromVault = @()
$presentInVault = @()
foreach ($name in $refSecretNames) {
    $result = az keyvault secret show --vault-name $kvName --name $name --query name -o tsv 2>$null
    if ($LASTEXITCODE -eq 0 -and $result) {
        $presentInVault += $name
    } else {
        $missingFromVault += $name
    }
}
Write-Host "  Present in vault: $($presentInVault.Count)" -ForegroundColor Green
if ($missingFromVault.Count -gt 0) {
    Write-Host "  MISSING from vault: $($missingFromVault.Count)" -ForegroundColor Red
    $missingFromVault | ForEach-Object { Write-Host "    - $_" -ForegroundColor Red }
}

# ---------------------------------------------------------------------------
# CHECK 2: Vault → References (every real secret is accounted for)
# ---------------------------------------------------------------------------
Write-Host "`n[2/3] Vault → References" -ForegroundColor Yellow
$actualSecrets = az keyvault secret list --vault-name $kvName --query "[].name" -o tsv 2>$null
if ($LASTEXITCODE -ne 0) { throw "Failed to list secrets in vault $kvName" }
$actualSecretNames = $actualSecrets -split "`n" | Where-Object { $_ } | Sort-Object -Unique
Write-Host "  Vault contains $($actualSecretNames.Count) secret(s)" -ForegroundColor DarkGray

# Pull the documented inventory (if present) — flatten into a single set of "known" secrets
$inventory = $envConfig.spe.keyvault_inventory
if (-not $inventory) { $inventory = $envConfig.keyvault_inventory }
$accountedNames = @()
$accountedNames += $refSecretNames
if ($inventory) {
    foreach ($category in @('referenced', 'not_secret_but_stored_as_secret')) {
        if ($inventory[$category]) { $accountedNames += $inventory[$category] }
    }
    if ($inventory['duplicates']) {
        foreach ($pair in $inventory['duplicates']) { $accountedNames += $pair }
    }
}
$accountedNames = $accountedNames | Sort-Object -Unique

$unaccounted = $actualSecretNames | Where-Object { $_ -notin $accountedNames }
if ($unaccounted.Count -eq 0) {
    Write-Host "  All vault secrets accounted for (referenced or in keyvault_inventory)" -ForegroundColor Green
} else {
    Write-Host "  UNACCOUNTED secrets in vault (add to keyvault_inventory or reference): $($unaccounted.Count)" -ForegroundColor Yellow
    $unaccounted | ForEach-Object { Write-Host "    - $_" -ForegroundColor Yellow }
}

# ---------------------------------------------------------------------------
# CHECK 3: Subscription sanity
# ---------------------------------------------------------------------------
Write-Host "`n[3/3] Subscription sanity" -ForegroundColor Yellow
$expectedSubId = $manifest.subscriptions.spe.id
if ($account.id -eq $expectedSubId) {
    Write-Host "  Current az subscription matches manifest (spe = $expectedSubId)" -ForegroundColor Green
} else {
    Write-Host "  WARNING: Current az subscription ($($account.id)) != manifest.subscriptions.spe.id ($expectedSubId)" -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# Summary + exit code
# ---------------------------------------------------------------------------
Write-Host "`n===============================================================" -ForegroundColor Cyan
$hardDrift = $missingFromVault.Count
$softDrift = $unaccounted.Count
Write-Host "  Hard drift (manifest -> vault MISSING): $hardDrift" -ForegroundColor $(if ($hardDrift -eq 0) { 'Green' } else { 'Red' })
Write-Host "  Soft drift (vault -> manifest UNACCOUNTED): $softDrift" -ForegroundColor $(if ($softDrift -eq 0) { 'Green' } else { 'Yellow' })
Write-Host "===============================================================" -ForegroundColor Cyan

if ($hardDrift -gt 0) { exit 1 }
exit 0
