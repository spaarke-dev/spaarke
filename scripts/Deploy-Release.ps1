#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Master release orchestrator — deploys the full Spaarke platform to one or more environments.

.DESCRIPTION
    Orchestrates the complete production release pipeline:

      Phase 0: Pre-flight checks (git clean, branch, auth, BFF URL validation)
      Phase 1: Build all client components (calls Build-AllClientComponents.ps1)
      Per-environment loop (sequential):
        Phase 2: BFF API deployment (calls Deploy-BffApi.ps1)
        Phase 3: Dataverse solution import (calls Deploy-DataverseSolutions.ps1)
        Phase 4: Web resource deployment — customerId-driven (Gap 2 / FR-28)
                 Target env resolved from sprk_dataverseenvironment registry keyed
                 on -CustomerId (NOT from -EnvironmentUrl loop iterand). Calls
                 Deploy-AllWebResources.ps1 against the resolved customer env.
        Phase 5: Post-deploy validation (calls Validate-DeployedEnvironment.ps1)
      Phase 6: Tag release in git

    Each phase delegates to the existing deployment scripts — this script does NOT
    reimplement any deployment logic. Environments are processed sequentially; if
    one environment fails and -StopOnFailure is set (default), remaining environments
    are skipped.

    Environment configuration is read from config/environments.json.

    Phase 4 hardening (Gap 2 / FR-28 / §4D I1): -CustomerId is Mandatory. Phase 4
    resolves its target Dataverse environment via the sprk_dataverseenvironment
    registry (customer lookup on sprk_name / sprk_envaccountdomain). No hardcoded
    default tenant or environment is permitted in this script. Missing -CustomerId
    fails fast with a mandatory-parameter diagnostic before any deployment side-effect.

.PARAMETER EnvironmentUrl
    One or more Dataverse environment URLs or environment names from config/environments.json.
    Drives Phases 1/2/3/5 (build, BFF API, solutions, validation). Phase 4 target is
    resolved separately via -CustomerId + registry lookup (Gap 2 / FR-28).
    Examples: "https://{tenant}.crm.dynamics.com", "dev", "demo"

.PARAMETER CustomerId
    Customer identifier (lowercase alphanumeric + hyphens, 2-64 chars). MANDATORY.
    Drives Phase 4 (Web Resources) target env resolution: the sprk_dataverseenvironment
    registry is queried for a row where sprk_name eq '{CustomerId}' OR
    sprk_envaccountdomain eq '{CustomerId}'; the row's sprk_dataverseurl becomes the
    Phase 4 deployment target. Per §4D I1 / FR-28: NO hardcoded default; missing
    -CustomerId errors immediately.

.PARAMETER AdminEnvironmentUrl
    Dataverse URL of the platform admin environment hosting the sprk_dataverseenvironment
    registry. Defaults to $env:SPAARKE_ADMIN_DATAVERSE_URL. When neither is set, falls
    back to the first entry in config/environments.json marked isPlatformAdmin:true,
    otherwise the first non-template entry (with a warning).

.PARAMETER Version
    Release version tag in v{major}.{minor}.{patch} format (e.g., v1.2.0).
    If omitted, auto-suggests based on the latest git tag.

.PARAMETER SkipPhase
    Array of phase names to skip. Valid values: Build, BffApi, Solutions, WebResources, Validation.

.PARAMETER SkipBuild
    Shortcut for -SkipPhase Build. Skips the client component build phase.

.PARAMETER StopOnFailure
    Stop deploying to remaining environments if a deployment fails.
    Default: $true

.PARAMETER ClientSecret
    Service principal client secret for Dataverse solution import.
    If not provided, falls back to SPAARKE_SP_CLIENT_SECRET environment variable,
    then prompts interactively.

.EXAMPLE
    .\scripts\Deploy-Release.ps1 -EnvironmentUrl dev -CustomerId acme
    # Full release pipeline: Phases 1/2/3/5 target the dev env; Phase 4 (Web
    # Resources) targets whatever env the acme customer registry row points at.

.EXAMPLE
    .\scripts\Deploy-Release.ps1 -EnvironmentUrl dev, demo -CustomerId acme -Version v2.1.0
    # Phases 1/2/3/5 loop dev then demo; Phase 4 targets acme's registry-resolved env
    # (idempotent — same customer env each iteration). Tags as v2.1.0.

.EXAMPLE
    .\scripts\Deploy-Release.ps1 -EnvironmentUrl demo -CustomerId acme `
        -SkipBuild -SkipPhase BffApi,Solutions,Validation
    # H9 handler invocation pattern: only Phase 4 runs; target env comes from
    # the acme registry lookup; other phases skipped.

.EXAMPLE
    .\scripts\Deploy-Release.ps1 -EnvironmentUrl demo -CustomerId acme -WhatIf
    # Preview the full release pipeline without executing anything.

.NOTES
    Project: spaarke-production-release-procedure
    Task: PRPR-023
    Phase 4 hardening: customer-provisioning-orchestration-r1 task 013 (Gap 2 / FR-28)
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string[]]$EnvironmentUrl,

    # Gap 2 / FR-28 / §4D I1: -CustomerId is Mandatory. NO hardcoded default.
    # Missing value produces PowerShell's built-in mandatory-parameter diagnostic
    # (non-zero exit) BEFORE any deployment side-effect.
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9][a-z0-9-]{1,63}$')]
    [string]$CustomerId,

    [ValidatePattern('^v\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidateSet('Build', 'BffApi', 'Solutions', 'WebResources', 'Validation')]
    [string[]]$SkipPhase = @(),

    [switch]$SkipBuild,

    [bool]$StopOnFailure = $true,

    [string]$ClientSecret,

    # Platform admin Dataverse env hosting the sprk_dataverseenvironment registry.
    # Resolution order: explicit param -> $env:SPAARKE_ADMIN_DATAVERSE_URL ->
    # config/environments.json (isPlatformAdmin:true) -> first non-template entry.
    [string]$AdminEnvironmentUrl = $env:SPAARKE_ADMIN_DATAVERSE_URL
)

$ErrorActionPreference = "Stop"
$ScriptStartTime = Get-Date
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

# ─────────────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────────────

function Write-Phase {
    param([string]$Phase, [string]$Message)
    Write-Host ""
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
    Write-Host "  $Phase — $Message" -ForegroundColor Cyan
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
}

function Write-Step {
    param([string]$Message)
    Write-Host "  ► $Message" -ForegroundColor White
}

function Write-Ok {
    param([string]$Message)
    Write-Host "  ✓ $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "  ⚠ $Message" -ForegroundColor Yellow
}

function Write-Fail {
    param([string]$Message)
    Write-Host "  ✗ $Message" -ForegroundColor Red
}

function Get-ElapsedString {
    param([datetime]$Start)
    $elapsed = (Get-Date) - $Start
    if ($elapsed.TotalMinutes -ge 1) {
        return "{0:N0}m {1:N0}s" -f [math]::Floor($elapsed.TotalMinutes), $elapsed.Seconds
    }
    return "{0:N1}s" -f $elapsed.TotalSeconds
}

function Test-PhaseSkipped {
    param([string]$PhaseName)
    return ($SkipPhase -contains $PhaseName)
}

# ─────────────────────────────────────────────────────────────────────
# Environment Resolution
# ─────────────────────────────────────────────────────────────────────

$envConfigPath = Join-Path $RepoRoot "config/environments.json"
if (-not (Test-Path $envConfigPath)) {
    Write-Fail "Environment registry not found: $envConfigPath"
    exit 1
}

$envRegistry = (Get-Content $envConfigPath -Raw | ConvertFrom-Json).environments

function Resolve-Environment {
    param([string]$UrlOrName)

    # Try by name first (case-insensitive)
    $envNames = $envRegistry.PSObject.Properties | Where-Object { $_.Name -ne '_template' } | Select-Object -ExpandProperty Name
    foreach ($name in $envNames) {
        if ($name -ieq $UrlOrName) {
            $cfg = $envRegistry.$name
            return [PSCustomObject]@{
                Name          = $name
                DisplayName   = $cfg.displayName
                DataverseUrl  = $cfg.dataverseUrl.TrimEnd('/')
                BffApiUrl     = $cfg.bffApiUrl.TrimEnd('/')
                AppServiceName = $cfg.appServiceName
                AppServiceSlot = $cfg.appServiceSlot
                ResourceGroup = $cfg.resourceGroup
                KeyVaultName  = $cfg.keyVaultName
                TenantId      = $cfg.tenantId
                SpClientId    = $cfg.servicePrincipal.clientId
            }
        }
    }

    # Try by Dataverse URL match
    $normalizedUrl = $UrlOrName.TrimEnd('/')
    foreach ($name in $envNames) {
        $cfg = $envRegistry.$name
        if ($cfg.dataverseUrl.TrimEnd('/') -ieq $normalizedUrl) {
            return [PSCustomObject]@{
                Name          = $name
                DisplayName   = $cfg.displayName
                DataverseUrl  = $cfg.dataverseUrl.TrimEnd('/')
                BffApiUrl     = $cfg.bffApiUrl.TrimEnd('/')
                AppServiceName = $cfg.appServiceName
                AppServiceSlot = $cfg.appServiceSlot
                ResourceGroup = $cfg.resourceGroup
                KeyVaultName  = $cfg.keyVaultName
                TenantId      = $cfg.tenantId
                SpClientId    = $cfg.servicePrincipal.clientId
            }
        }
    }

    return $null
}

function Resolve-CustomerEnvironmentFromRegistry {
    <#
    .SYNOPSIS
        Resolves a customer's target Dataverse environment via the sprk_dataverseenvironment
        registry (Gap 2 / FR-28 / §4D I1 — no hardcoded tenant/env in provisioning scripts).
    .DESCRIPTION
        Queries the platform admin Dataverse env for a sprk_dataverseenvironment record
        matching the supplied CustomerId (against sprk_name OR sprk_envaccountdomain).
        Returns the record's sprk_dataverseurl (customer's environment URL) plus metadata.
        Fails fast (throws) if the registry lookup returns zero rows OR the row has an
        empty sprk_dataverseurl. Called by Phase 4 to determine its deployment target.
    #>
    param(
        [Parameter(Mandatory)][string]$CustomerId,
        [Parameter(Mandatory)][string]$AdminEnvUrl
    )

    $normalizedAdminUrl = $AdminEnvUrl.TrimEnd('/')
    Write-Step "Registry lookup: customerId='$CustomerId' via $normalizedAdminUrl"

    $tokenRaw = az account get-access-token --resource $normalizedAdminUrl --query accessToken -o tsv 2>&1
    if ($LASTEXITCODE -ne 0 -or -not $tokenRaw) {
        throw "Registry lookup failed: cannot acquire Dataverse access token for $normalizedAdminUrl (az exit=$LASTEXITCODE). Ensure 'az login' has been run with an account that has access to the admin Dataverse env."
    }

    $escapedId = $CustomerId.Replace("'", "''")
    $filter = "sprk_name eq '$escapedId' or sprk_envaccountdomain eq '$escapedId'"
    $select = "sprk_dataverseenvironmentid,sprk_name,sprk_dataverseurl,sprk_environmenttype,sprk_envaccountdomain"
    $encodedFilter = [System.Uri]::EscapeDataString($filter)
    $encodedSelect = [System.Uri]::EscapeDataString($select)
    $uri = "$normalizedAdminUrl/api/data/v9.2/sprk_dataverseenvironments?`$filter=$encodedFilter&`$select=$encodedSelect&`$top=1"

    $headers = @{
        'Authorization'    = "Bearer $tokenRaw"
        'Accept'           = 'application/json'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
    }

    try {
        $response = Invoke-RestMethod -Uri $uri -Headers $headers -Method GET -ErrorAction Stop
    } catch {
        throw "Registry lookup HTTP failure at $normalizedAdminUrl : $_"
    }

    if (-not $response.value -or $response.value.Count -eq 0) {
        throw "Registry lookup found NO sprk_dataverseenvironment row for customerId='$CustomerId' in $normalizedAdminUrl. Verify the customer registry record exists with sprk_name eq '$CustomerId' OR sprk_envaccountdomain eq '$CustomerId'. No hardcoded fallback — Gap 2 / FR-28 forbids default env."
    }

    $rec = $response.value[0]
    if ([string]::IsNullOrWhiteSpace($rec.sprk_dataverseurl)) {
        throw "Registry row for customerId='$CustomerId' has empty sprk_dataverseurl (registryId=$($rec.sprk_dataverseenvironmentid)). Cannot resolve Phase 4 target."
    }

    return [PSCustomObject]@{
        CustomerId      = $CustomerId
        RegistryId      = $rec.sprk_dataverseenvironmentid
        RegistryName    = $rec.sprk_name
        DataverseUrl    = $rec.sprk_dataverseurl.TrimEnd('/')
        EnvironmentType = $rec.sprk_environmenttype
        AccountDomain   = $rec.sprk_envaccountdomain
    }
}

# ─────────────────────────────────────────────────────────────────────
# Merge SkipBuild into SkipPhase
# ─────────────────────────────────────────────────────────────────────

if ($SkipBuild -and -not (Test-PhaseSkipped 'Build')) {
    $SkipPhase += 'Build'
}

# ─────────────────────────────────────────────────────────────────────
# Resolve all target environments
# ─────────────────────────────────────────────────────────────────────

$targetEnvironments = @()
foreach ($urlOrName in $EnvironmentUrl) {
    $resolved = Resolve-Environment $urlOrName
    if (-not $resolved) {
        Write-Fail "Cannot resolve environment '$urlOrName' — not found in $envConfigPath"
        Write-Host "  Available environments:" -ForegroundColor Gray
        $envRegistry.PSObject.Properties | Where-Object { $_.Name -ne '_template' } | ForEach-Object {
            Write-Host "    - $($_.Name) ($($_.Value.dataverseUrl))" -ForegroundColor Gray
        }
        exit 1
    }
    $targetEnvironments += $resolved
}

# ─────────────────────────────────────────────────────────────────────
# Phase 0: Pre-flight Checks
# ─────────────────────────────────────────────────────────────────────

Write-Phase "Phase 0" "Pre-flight Checks"
$preflightFailed = $false

# 0a. Git status clean
Write-Step "Checking git working tree..."
$gitStatus = git -C $RepoRoot status --porcelain 2>&1
if ($gitStatus) {
    Write-Fail "Working tree is not clean. Commit or stash changes before releasing."
    $gitStatus | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
    $preflightFailed = $true
} else {
    Write-Ok "Working tree is clean"
}

# 0b. On master or release branch
Write-Step "Checking branch..."
$branch = git -C $RepoRoot rev-parse --abbrev-ref HEAD 2>&1
if ($branch -notmatch '^(master|main|release/.*)$') {
    Write-Warn "Current branch '$branch' is not master or a release branch"
    Write-Host "    Expected: master, main, or release/*" -ForegroundColor Gray
    # Warning only — allow force deployment from feature branches during development
} else {
    Write-Ok "On branch: $branch"
}

# 0c. BFF URL validation — no /api suffix
Write-Step "Validating BFF API URLs in environment registry..."
$bffUrlIssues = @()
foreach ($env in $targetEnvironments) {
    if ($env.BffApiUrl -match '/api\s*$') {
        $bffUrlIssues += "$($env.Name): $($env.BffApiUrl) — MUST NOT end with /api"
    }
}
if ($bffUrlIssues.Count -gt 0) {
    Write-Fail "BFF URL validation failed (URLs must be host only, no /api suffix):"
    $bffUrlIssues | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    $preflightFailed = $true
} else {
    Write-Ok "All BFF API URLs are host-only (no /api suffix)"
}

# 0d. Azure CLI authenticated
Write-Step "Checking Azure CLI authentication..."
$azAccount = az account show --output json 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Azure CLI not authenticated. Run 'az login' first."
    $preflightFailed = $true
} else {
    $accountInfo = $azAccount | ConvertFrom-Json
    Write-Ok "Azure CLI: $($accountInfo.user.name) (tenant: $($accountInfo.tenantId))"
}

# 0e. PAC CLI authenticated
Write-Step "Checking PAC CLI authentication..."
$pacWho = pac org who 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Fail "PAC CLI not authenticated. Run 'pac auth create' first."
    $preflightFailed = $true
} else {
    Write-Ok "PAC CLI authenticated"
}

if ($preflightFailed) {
    Write-Host ""
    Write-Fail "Pre-flight checks failed. Fix the issues above before releasing."
    exit 1
}

Write-Ok "All pre-flight checks passed"

# ─────────────────────────────────────────────────────────────────────
# Version auto-suggest
# ─────────────────────────────────────────────────────────────────────

if (-not $Version) {
    Write-Step "Auto-suggesting version from latest git tag..."
    $latestTag = git -C $RepoRoot describe --tags --abbrev=0 2>&1
    if ($LASTEXITCODE -eq 0 -and $latestTag -match '^v(\d+)\.(\d+)\.(\d+)$') {
        $major = [int]$Matches[1]
        $minor = [int]$Matches[2]
        $patch = [int]$Matches[3] + 1
        $Version = "v$major.$minor.$patch"
        Write-Ok "Latest tag: $latestTag — suggesting: $Version"
    } else {
        $Version = "v1.0.0"
        Write-Ok "No previous tags found — suggesting: $Version"
    }

    if (-not $WhatIfPreference) {
        Write-Host ""
        $confirm = Read-Host "  Use version $Version? (Y/n)"
        if ($confirm -and $confirm -inotin @('y', 'yes', '')) {
            $Version = Read-Host "  Enter version (v{major}.{minor}.{patch})"
            if ($Version -notmatch '^v\d+\.\d+\.\d+$') {
                Write-Fail "Invalid version format. Expected v{major}.{minor}.{patch}"
                exit 1
            }
        }
    }
}

# ─────────────────────────────────────────────────────────────────────
# Resolve client secret for Dataverse solutions
# ─────────────────────────────────────────────────────────────────────

if (-not (Test-PhaseSkipped 'Solutions')) {
    if (-not $ClientSecret) {
        $ClientSecret = $env:SPAARKE_SP_CLIENT_SECRET
    }
    if (-not $ClientSecret -and -not $WhatIfPreference) {
        Write-Host ""
        $secureSecret = Read-Host "  Enter service principal client secret for Dataverse" -AsSecureString
        $ClientSecret = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureSecret)
        )
        if (-not $ClientSecret) {
            Write-Fail "Client secret is required for Dataverse solution deployment."
            exit 1
        }
    }
}

# ─────────────────────────────────────────────────────────────────────
# Print Release Plan
# ─────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "┌─────────────────────────────────────────────────────────────────┐" -ForegroundColor Magenta
Write-Host "│  RELEASE PLAN                                                   │" -ForegroundColor Magenta
Write-Host "├─────────────────────────────────────────────────────────────────┤" -ForegroundColor Magenta
Write-Host "│  Version:      $Version" -ForegroundColor Magenta
Write-Host "│  Environments: $($targetEnvironments.Count)" -ForegroundColor Magenta
foreach ($env in $targetEnvironments) {
    Write-Host "│    • $($env.DisplayName) ($($env.DataverseUrl))" -ForegroundColor Magenta
}
Write-Host "│  Skipped:      $(if ($SkipPhase.Count -eq 0) { 'none' } else { $SkipPhase -join ', ' })" -ForegroundColor Magenta
Write-Host "│  StopOnFail:   $StopOnFailure" -ForegroundColor Magenta
Write-Host "└─────────────────────────────────────────────────────────────────┘" -ForegroundColor Magenta

# ─────────────────────────────────────────────────────────────────────
# Phase 4 target — resolve customer env via sprk_dataverseenvironment registry
# (Gap 2 / FR-28 / §4D I1 — no hardcoded default; customerId-driven).
# Resolved ONCE up front so all loop iterations share the same customer target
# (Phase 4 is idempotent per customer). Skipped only if WebResources is skipped.
# ─────────────────────────────────────────────────────────────────────

$phase4CustomerEnv = $null
if (-not (Test-PhaseSkipped 'WebResources')) {
    $resolvedAdminUrl = $AdminEnvironmentUrl
    if (-not $resolvedAdminUrl) {
        $adminEntry = $envRegistry.PSObject.Properties |
            Where-Object { $_.Name -ne '_template' -and $_.Value.PSObject.Properties['isPlatformAdmin'] -and $_.Value.isPlatformAdmin -eq $true } |
            Select-Object -First 1
        if ($adminEntry) {
            $resolvedAdminUrl = $adminEntry.Value.dataverseUrl
        }
    }
    if (-not $resolvedAdminUrl) {
        $firstEntry = $envRegistry.PSObject.Properties |
            Where-Object { $_.Name -ne '_template' } |
            Select-Object -First 1
        if ($firstEntry) {
            $resolvedAdminUrl = $firstEntry.Value.dataverseUrl
            Write-Warn "AdminEnvironmentUrl not set; falling back to '$($firstEntry.Name)' ($resolvedAdminUrl) as registry lookup source. Set -AdminEnvironmentUrl or `$env:SPAARKE_ADMIN_DATAVERSE_URL for explicit control."
        }
    }
    if (-not $resolvedAdminUrl) {
        Write-Fail "Cannot resolve admin Dataverse env URL for registry lookup. Provide -AdminEnvironmentUrl or set `$env:SPAARKE_ADMIN_DATAVERSE_URL, or add an entry to config/environments.json."
        exit 1
    }

    if ($WhatIfPreference) {
        Write-Phase "Phase 4 Preflight" "Registry lookup (WhatIf) — customerId=$CustomerId adminEnv=$resolvedAdminUrl"
        $phase4CustomerEnv = [PSCustomObject]@{
            CustomerId      = $CustomerId
            RegistryId      = '<whatif>'
            RegistryName    = "<whatif:$CustomerId>"
            DataverseUrl    = "<whatif:resolved-from-registry>"
            EnvironmentType = $null
            AccountDomain   = $CustomerId
        }
        Write-Ok "WhatIf: registry lookup would resolve customerId='$CustomerId'"
    } else {
        Write-Phase "Phase 4 Preflight" "Resolve customer env from registry"
        try {
            $phase4CustomerEnv = Resolve-CustomerEnvironmentFromRegistry `
                -CustomerId $CustomerId `
                -AdminEnvUrl $resolvedAdminUrl
            Write-Ok "Registry resolved: customerId='$CustomerId' -> env=$($phase4CustomerEnv.DataverseUrl) (registryId=$($phase4CustomerEnv.RegistryId))"
        } catch {
            Write-Fail "Phase 4 preflight failed: $_"
            exit 1
        }
    }
}

# ─────────────────────────────────────────────────────────────────────
# Phase 1: Build
# ─────────────────────────────────────────────────────────────────────

if (Test-PhaseSkipped 'Build') {
    Write-Phase "Phase 1" "Build — SKIPPED"
} else {
    Write-Phase "Phase 1" "Build All Client Components"
    $buildStart = Get-Date

    $buildScript = Join-Path $ScriptDir "Build-AllClientComponents.ps1"
    if (-not (Test-Path $buildScript)) {
        Write-Fail "Build script not found: $buildScript"
        exit 1
    }

    if ($PSCmdlet.ShouldProcess("All client components", "Build")) {
        & $buildScript
        if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
            Write-Fail "Build failed (exit code: $LASTEXITCODE)"
            exit 1
        }
        Write-Ok "Build completed in $(Get-ElapsedString $buildStart)"
    }
}

# ─────────────────────────────────────────────────────────────────────
# Per-environment deployment loop (Phases 2–5)
# ─────────────────────────────────────────────────────────────────────

$envResults = @()

for ($i = 0; $i -lt $targetEnvironments.Count; $i++) {
    $env = $targetEnvironments[$i]
    $envStart = Get-Date
    $envNum = $i + 1
    $envFailed = $false

    Write-Host ""
    Write-Host "╔═════════════════════════════════════════════════════════════════╗" -ForegroundColor Yellow
    Write-Host "║  Environment $envNum/$($targetEnvironments.Count): $($env.DisplayName)" -ForegroundColor Yellow
    Write-Host "║  $($env.DataverseUrl)" -ForegroundColor Yellow
    Write-Host "╚═════════════════════════════════════════════════════════════════╝" -ForegroundColor Yellow

    # ── Phase 2: BFF API ──────────────────────────────────────────

    if (Test-PhaseSkipped 'BffApi') {
        Write-Phase "Phase 2" "BFF API — SKIPPED"
    } else {
        Write-Phase "Phase 2" "BFF API → $($env.AppServiceName)"
        $phaseStart = Get-Date

        $bffScript = Join-Path $ScriptDir "Deploy-BffApi.ps1"
        if (-not (Test-Path $bffScript)) {
            Write-Fail "BFF deploy script not found: $bffScript"
            $envFailed = $true
        } else {
            $bffParams = @{
                Environment       = "production"
                ResourceGroupName = $env.ResourceGroup
                AppServiceName    = $env.AppServiceName
                UseSlotDeploy     = $true
                SkipBuild         = $true  # Already built in Phase 1
            }

            if ($PSCmdlet.ShouldProcess("$($env.AppServiceName) (slot: $($env.AppServiceSlot))", "Deploy BFF API")) {
                try {
                    & $bffScript @bffParams
                    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
                        throw "Deploy-BffApi.ps1 exited with code $LASTEXITCODE"
                    }
                    Write-Ok "BFF API deployed in $(Get-ElapsedString $phaseStart)"
                } catch {
                    Write-Fail "BFF API deployment failed: $_"
                    $envFailed = $true
                }
            }
        }
    }

    # ── Phase 3: Dataverse Solutions ──────────────────────────────

    if ($envFailed -and $StopOnFailure) {
        Write-Phase "Phase 3" "Dataverse Solutions — SKIPPED (previous phase failed)"
    } elseif (Test-PhaseSkipped 'Solutions') {
        Write-Phase "Phase 3" "Dataverse Solutions — SKIPPED"
    } else {
        Write-Phase "Phase 3" "Dataverse Solutions → $($env.DataverseUrl)"
        $phaseStart = Get-Date

        $solScript = Join-Path $ScriptDir "Deploy-DataverseSolutions.ps1"
        if (-not (Test-Path $solScript)) {
            Write-Fail "Solutions deploy script not found: $solScript"
            $envFailed = $true
        } else {
            $solParams = @{
                EnvironmentUrl = $env.DataverseUrl
                TenantId       = $env.TenantId
                ClientId       = $env.SpClientId
                ClientSecret   = $ClientSecret
            }

            if ($PSCmdlet.ShouldProcess("$($env.DataverseUrl)", "Import Dataverse solutions")) {
                try {
                    & $solScript @solParams
                    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
                        throw "Deploy-DataverseSolutions.ps1 exited with code $LASTEXITCODE"
                    }
                    Write-Ok "Solutions deployed in $(Get-ElapsedString $phaseStart)"
                } catch {
                    Write-Fail "Solution deployment failed: $_"
                    $envFailed = $true
                }
            }
        }
    }

    # ── Phase 4: Web Resources (Gap 2 / FR-28 — customerId-driven) ──
    # Target env comes from the sprk_dataverseenvironment registry (resolved
    # up front as $phase4CustomerEnv), NOT from $env.DataverseUrl. -CustomerId
    # is mandatory; no hardcoded default env is permitted (§4D I1).

    if ($envFailed -and $StopOnFailure) {
        Write-Phase "Phase 4" "Web Resources — SKIPPED (previous phase failed)"
    } elseif (Test-PhaseSkipped 'WebResources') {
        Write-Phase "Phase 4" "Web Resources — SKIPPED"
    } else {
        # $phase4CustomerEnv is guaranteed non-null here (resolved before the loop;
        # failure would have exited already).
        $phase4TargetUrl = $phase4CustomerEnv.DataverseUrl
        Write-Phase "Phase 4" "Web Resources -> $phase4TargetUrl"
        Write-Host "  Phase 4 target: customerId=$CustomerId env=$phase4TargetUrl" -ForegroundColor Gray
        $phaseStart = Get-Date

        $wrScript = Join-Path $ScriptDir "Deploy-AllWebResources.ps1"
        if (-not (Test-Path $wrScript)) {
            Write-Fail "Web resources deploy script not found: $wrScript"
            $envFailed = $true
        } else {
            $wrParams = @{
                DataverseUrl = $phase4TargetUrl
            }

            if ($PSCmdlet.ShouldProcess($phase4TargetUrl, "Deploy web resources (customerId=$CustomerId)")) {
                try {
                    & $wrScript @wrParams
                    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
                        throw "Deploy-AllWebResources.ps1 exited with code $LASTEXITCODE"
                    }
                    Write-Ok "Web resources deployed in $(Get-ElapsedString $phaseStart)"
                } catch {
                    Write-Fail "Web resource deployment failed: $_"
                    $envFailed = $true
                }
            }
        }
    }

    # ── Phase 5: Validation ───────────────────────────────────────

    if ($envFailed -and $StopOnFailure) {
        Write-Phase "Phase 5" "Validation — SKIPPED (previous phase failed)"
    } elseif (Test-PhaseSkipped 'Validation') {
        Write-Phase "Phase 5" "Validation — SKIPPED"
    } else {
        Write-Phase "Phase 5" "Validate → $($env.DisplayName)"
        $phaseStart = Get-Date

        $valScript = Join-Path $ScriptDir "Validate-DeployedEnvironment.ps1"
        if (-not (Test-Path $valScript)) {
            Write-Fail "Validation script not found: $valScript"
            $envFailed = $true
        } else {
            $valParams = @{
                DataverseUrl = $env.DataverseUrl
                BffApiUrl    = $env.BffApiUrl
            }

            if ($PSCmdlet.ShouldProcess("$($env.DisplayName)", "Validate deployed environment")) {
                try {
                    & $valScript @valParams
                    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
                        throw "Validate-DeployedEnvironment.ps1 exited with code $LASTEXITCODE"
                    }
                    Write-Ok "Validation passed in $(Get-ElapsedString $phaseStart)"
                } catch {
                    Write-Fail "Validation failed: $_"
                    $envFailed = $true
                }
            }
        }
    }

    # ── Record result ─────────────────────────────────────────────

    $envResults += [PSCustomObject]@{
        Environment = $env.DisplayName
        Url         = $env.DataverseUrl
        Status      = if ($envFailed) { "FAILED" } else { "OK" }
        Duration    = Get-ElapsedString $envStart
    }

    if ($envFailed -and $StopOnFailure -and ($i -lt $targetEnvironments.Count - 1)) {
        Write-Host ""
        Write-Warn "Stopping — StopOnFailure is enabled and $($env.DisplayName) failed."
        Write-Warn "Remaining environments will not be deployed."
        # Mark remaining environments as skipped
        for ($j = $i + 1; $j -lt $targetEnvironments.Count; $j++) {
            $envResults += [PSCustomObject]@{
                Environment = $targetEnvironments[$j].DisplayName
                Url         = $targetEnvironments[$j].DataverseUrl
                Status      = "SKIPPED"
                Duration    = "-"
            }
        }
        break
    }
}

# ─────────────────────────────────────────────────────────────────────
# Phase 6: Tag Release
# ─────────────────────────────────────────────────────────────────────

$anyFailed = $envResults | Where-Object { $_.Status -eq 'FAILED' }

if ($anyFailed) {
    Write-Phase "Phase 6" "Tag Release — SKIPPED (deployment failures detected)"
} else {
    Write-Phase "Phase 6" "Tag Release: $Version"

    if ($PSCmdlet.ShouldProcess("$Version", "Create and push git tag")) {
        try {
            git -C $RepoRoot tag -a $Version -m "Release $Version"
            if ($LASTEXITCODE -ne 0) { throw "git tag failed (exit code: $LASTEXITCODE)" }
            Write-Ok "Created tag: $Version"

            git -C $RepoRoot push origin $Version
            if ($LASTEXITCODE -ne 0) { throw "git push tag failed (exit code: $LASTEXITCODE)" }
            Write-Ok "Pushed tag to origin"
        } catch {
            Write-Fail "Tag creation failed: $_"
        }
    }
}

# ─────────────────────────────────────────────────────────────────────
# Summary Report
# ─────────────────────────────────────────────────────────────────────

$totalElapsed = Get-ElapsedString $ScriptStartTime

Write-Host ""
Write-Host "┌─────────────────────────────────────────────────────────────────┐" -ForegroundColor Cyan
Write-Host "│  RELEASE SUMMARY                                                │" -ForegroundColor Cyan
Write-Host "├─────────────────────────────────────────────────────────────────┤" -ForegroundColor Cyan
Write-Host "│  Version: $Version                                              " -ForegroundColor Cyan
Write-Host "│  Total:   $totalElapsed                                         " -ForegroundColor Cyan
Write-Host "├─────────────────────────────────────────────────────────────────┤" -ForegroundColor Cyan

foreach ($result in $envResults) {
    $statusColor = switch ($result.Status) {
        "OK"      { "Green" }
        "FAILED"  { "Red" }
        "SKIPPED" { "Yellow" }
    }
    $statusIcon = switch ($result.Status) {
        "OK"      { "✓" }
        "FAILED"  { "✗" }
        "SKIPPED" { "○" }
    }
    Write-Host "│  $statusIcon $($result.Environment.PadRight(20)) $($result.Status.PadRight(10)) $($result.Duration)" -ForegroundColor $statusColor
}

Write-Host "└─────────────────────────────────────────────────────────────────┘" -ForegroundColor Cyan

if ($anyFailed) {
    Write-Host ""
    Write-Fail "Release completed with failures. Review the output above."
    exit 1
} else {
    Write-Host ""
    Write-Ok "Release $Version completed successfully!"
    exit 0
}
