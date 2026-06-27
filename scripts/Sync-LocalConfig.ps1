<#
.SYNOPSIS
Materialize Key Vault secrets to a local config file based on spaarke-resources.yaml.

.DESCRIPTION
Reads config/spaarke-resources.yaml, finds all `kv:SECRET-NAME` references for a
given environment, fetches the values from that environment's Key Vault via Az CLI,
and writes them to config/secrets.local.json (gitignored).

Requires:
- Azure CLI installed and authenticated (run `az login` first)
- powershell-yaml module (auto-installed on first run if missing)
- Read access to the target Key Vault

.PARAMETER Environment
The environment to sync (dev | demo). Defaults to dev.

.PARAMETER ManifestPath
Path to the YAML manifest relative to repo root. Defaults to config/spaarke-resources.yaml.

.PARAMETER OutputPath
Path to write the secrets JSON relative to repo root. Defaults to config/secrets.local.json.

.EXAMPLE
./scripts/Sync-LocalConfig.ps1
./scripts/Sync-LocalConfig.ps1 -Environment demo
#>

[CmdletBinding()]
param(
    [ValidateSet('dev', 'demo')]
    [string]$Environment = 'dev',

    [string]$ManifestPath = 'config/spaarke-resources.yaml',

    [string]$OutputPath = 'config/secrets.local.json'
)

$ErrorActionPreference = 'Stop'

# Resolve repo root regardless of where script is called from
$repoRoot = (& git rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) { throw "Not in a git repository" }
Set-Location $repoRoot

# Ensure powershell-yaml is available
if (-not (Get-Module -ListAvailable -Name powershell-yaml)) {
    Write-Host "Installing powershell-yaml module..." -ForegroundColor Yellow
    Install-Module -Name powershell-yaml -Scope CurrentUser -Force -AllowClobber
}
Import-Module powershell-yaml -ErrorAction Stop

# Read manifest
if (-not (Test-Path $ManifestPath)) {
    throw "Manifest not found: $ManifestPath"
}
$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Yaml

if (-not $manifest.environments.ContainsKey($Environment)) {
    $avail = ($manifest.environments.Keys | Where-Object { $_ -notlike '_*' }) -join ', '
    throw "Environment '$Environment' not found. Available: $avail"
}
$envConfig = $manifest.environments[$Environment]
$kvName = $envConfig.keyvault

if (-not $kvName) {
    throw "Environment '$Environment' has no keyvault defined"
}

Write-Host "Syncing secrets for environment '$Environment' from Key Vault '$kvName'..." -ForegroundColor Cyan

# Verify Az CLI login + correct subscription
$account = $null
try {
    $account = az account show 2>$null | ConvertFrom-Json
} catch {}
if (-not $account) { throw "Not logged into Azure CLI. Run: az login" }
Write-Host "  Signed in as: $($account.user.name)" -ForegroundColor DarkGray
Write-Host "  Subscription: $($account.name) ($($account.id))" -ForegroundColor DarkGray

# Walk the env config to collect kv: references with their dotted paths
function Get-KvReferences {
    param(
        [Parameter(Mandatory)] $node,
        [string]$path = ''
    )
    $refs = @()
    if ($null -eq $node) { return $refs }

    if ($node -is [System.Collections.IDictionary]) {
        foreach ($key in $node.Keys) {
            $currentPath = if ($path) { "$path.$key" } else { "$key" }
            $value = $node[$key]
            if ($value -is [string] -and $value -like 'kv:*') {
                $refs += [PSCustomObject]@{
                    Path = $currentPath
                    SecretName = $value.Substring(3)
                }
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

$refs = Get-KvReferences -node $envConfig
if (-not $refs -or $refs.Count -eq 0) {
    Write-Warning "No kv: references found for environment '$Environment'"
    return
}

Write-Host "`nFound $($refs.Count) secret reference(s):" -ForegroundColor Cyan
$refs | ForEach-Object { Write-Host "  $($_.Path)`t->`t$($_.SecretName)" -ForegroundColor DarkGray }

# Fetch each secret
$secretsMap = [ordered]@{}
$failed = @()

foreach ($ref in $refs) {
    Write-Host "  Fetching $($ref.SecretName)..." -ForegroundColor DarkCyan -NoNewline
    $value = $null
    try {
        $value = az keyvault secret show --vault-name $kvName --name $ref.SecretName --query value -o tsv 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value)) {
            Write-Host " MISSING" -ForegroundColor Red
            $failed += $ref
            continue
        }
        $secretsMap[$ref.Path] = $value
        Write-Host " OK" -ForegroundColor Green
    } catch {
        Write-Host " ERROR: $($_.Exception.Message)" -ForegroundColor Red
        $failed += $ref
    }
}

# Write output
$result = [ordered]@{
    _metadata = [ordered]@{
        environment = $Environment
        keyvault = $kvName
        synced_at = (Get-Date -Format 'o')
        synced_by = $account.user.name
        subscription = $account.name
        warning = 'Gitignored — do not commit'
    }
    secrets = $secretsMap
}

$json = $result | ConvertTo-Json -Depth 10
Set-Content -Path $OutputPath -Value $json -Encoding UTF8 -NoNewline

Write-Host ""
Write-Host "Wrote $($secretsMap.Count) secrets to $OutputPath" -ForegroundColor Green
if ($failed.Count -gt 0) {
    Write-Warning "$($failed.Count) secret(s) failed to fetch:"
    $failed | ForEach-Object { Write-Warning "  $($_.Path) -> $($_.SecretName)" }
    Write-Warning "Verify secret names in $ManifestPath match actual KV secret names."
}
Write-Host "(File is gitignored — do not commit.)" -ForegroundColor Yellow
