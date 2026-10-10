<#
.SYNOPSIS
    Complete deployment of the Spaarke M365 Copilot agent to a target environment.
    Runs all configuration steps in sequence: entity descriptions, glossary,
    synonyms, and generates the Teams app package ready for upload.

.DESCRIPTION
    This script is the single entry point for deploying the Copilot agent
    to any Spaarke environment. It orchestrates:
    1. Dataverse entity descriptions - teaches Copilot the data model
    2. Copilot glossary terms and synonyms - maps user vocabulary
    3. App package generation - rendered from the T257 template (scripts/copilot-agent), ready for upload

    Include this in the deployment runbook for every new environment.

.PARAMETER EnvironmentUrl
    Target Dataverse environment URL.
    Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApiUrl
    BFF API base URL for the target environment.
    Default: https://spaarke-bff-dev.azurewebsites.net

.PARAMETER BotAppId
    The app manifest id of this environment's existing catalog app (kept so an upload updates it).
    Default: f257a0a9-1061-4f9b-8918-3ad056fe90db (dev). Customer packages derive theirs from the customerId instead
    (scripts/copilot-agent/Render-CopilotAgentPackage.ps1).

.PARAMETER BffAppId
    BFF API Entra app registration ID.
    Default: 1e40baad-e065-4aea-a8d4-4b7ab273458c

.PARAMETER AuthConfigId
    The auth config (OAuth registration) id the plugin's auth.reference_id points to. Default: dev's existing one.

.PARAMETER TenantId
    Spaarke's Entra tenant id: the OAuth authorize/token authority. Required, no default
    (§4D I1: a script never defaults a tenant). Spaarke's tenant is a221a95e-6abc-4434-aecc-e48338a1b2f2.

.PARAMETER BffScopeName
    The BFF app's delegated scope the agent requests. Default access_as_user: the dev app 1e40baad exposes it and dev's
    auth config was registered with it. Customer BFF apps expose only user_impersonation (H3).

.PARAMETER Version
    App version for the manifest. Empty (default) = the template version in src/solutions/CopilotAgent. Must be
    higher than the uploaded one for an update.

.PARAMETER OutputPath
    Path for the generated Teams app package ZIP.
    Default: ./spaarke-copilot-agent.zip

.PARAMETER SkipDataverse
    Skip Dataverse configuration steps - useful when repackaging only.

.EXAMPLE
    .\Deploy-CopilotAgent.ps1 -TenantId <Spaarke tenant id>
    .\Deploy-CopilotAgent.ps1 -TenantId <Spaarke tenant id> -SkipDataverse -Version "1.0.11"
    # Spaarke's own environments only. Customer packages: scripts/copilot-agent/Render-CopilotAgentPackage.ps1
#>

#Requires -Version 7.3
param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [string]$BffApiUrl = "https://spaarke-bff-dev.azurewebsites.net",
    [string]$BotAppId = "f257a0a9-1061-4f9b-8918-3ad056fe90db",
    [string]$BffAppId = "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    [string]$AuthConfigId = "YTIyMWE5NWUtNmFiYy00NDM0LWFlY2MtZTQ4MzM4YTFiMmYyIyM3ZmFjM2E2Zi1mZDYwLTQ4MTQtYTEzNC1kMTlkNzIwN2E1ZGY=",
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [string]$BffScopeName = "access_as_user",
    [string]$Version = "",
    [string]$OutputPath = "./spaarke-copilot-agent.zip",
    [switch]$SkipDataverse
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$copilotDir = Join-Path $repoRoot "src/solutions/CopilotAgent"

Write-Host "`n================================================================" -ForegroundColor Cyan
Write-Host "  Spaarke Copilot Agent Deployment" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Environment:  $EnvironmentUrl"
Write-Host "  BFF API:      $BffApiUrl"
Write-Host "  Bot App ID:   $BotAppId"
Write-Host "  BFF App ID:   $BffAppId"
Write-Host "  Version:      $(if ($Version) { $Version } else { '(template version)' })"
Write-Host "  Output:       $OutputPath"
Write-Host "================================================================`n"

# ============================================================================
# STEP 1: Configure Dataverse for Copilot
# ============================================================================

if (-not $SkipDataverse) {
    Write-Host "[1/4] Configuring Dataverse entity descriptions..." -ForegroundColor Cyan
    & "$scriptDir/Update-CopilotEntityDescriptions.ps1" -EnvironmentUrl $EnvironmentUrl
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) {
        Write-Host "  Warning: Entity description update had errors - continuing" -ForegroundColor Yellow
    }

    Write-Host "`n[2/4] Configuring Copilot glossary and synonyms..." -ForegroundColor Cyan
    $glossaryScript = Join-Path $scriptDir "Configure-CopilotKnowledge.ps1"
    if (Test-Path $glossaryScript) {
        & $glossaryScript -EnvironmentUrl $EnvironmentUrl
        if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) {
            Write-Host "  Warning: Glossary configuration had errors - continuing" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  Skipped: Configure-CopilotKnowledge.ps1 not found" -ForegroundColor Yellow
    }
} else {
    Write-Host "[1/4] Skipped: Dataverse configuration" -ForegroundColor Yellow
    Write-Host "[2/4] Skipped: Copilot glossary and synonyms" -ForegroundColor Yellow
}

# ============================================================================
# STEP 2: Generate the app package (T257: the same template + renderer customer packages use)
# ============================================================================

Write-Host "`n[3/4] Building the app package..." -ForegroundColor Cyan

# This path serves Spaarke's OWN environments (dev/demo/internal; design note t257 Q4). It keeps their existing catalog
# app id (-BotAppId) and auth config; customer packages come from scripts/copilot-agent/Render-CopilotAgentPackage.ps1.
Import-Module (Join-Path $scriptDir "copilot-agent/CopilotAgentPackage.psm1") -Force
$templateDir = Join-Path ([System.IO.Path]::GetTempPath()) "copilot-template-$(Get-Date -Format 'yyyyMMddHHmmss')"
try {
    $template = New-CopilotAgentTemplate -SourceFolder $copilotDir -OutputFolder $templateDir
    $values = Get-CopilotAgentRenderValues -ManifestId $BotAppId -BffBaseUrl $BffApiUrl -BffAppId $BffAppId `
        -AuthConfigId $AuthConfigId -SpaarkeTenantId $TenantId -BffScopeName $BffScopeName
    $rendered = Invoke-CopilotAgentRender -Template $template.ZipPath -Values $values -OutputPath $OutputPath -Version $Version
} finally {
    Remove-Item $templateDir -Recurse -Force -ErrorAction SilentlyContinue
}
$resolvedOutput = $rendered.ZipPath
$Version = $rendered.Version

Write-Host "  Package created: $resolvedOutput" -ForegroundColor Green
Write-Host "  Version: $Version (template $($template.Version))"

# ============================================================================
# STEP 3: Instructions
# ============================================================================

Write-Host "`n[4/4] Next steps..." -ForegroundColor Cyan
Write-Host ""
Write-Host "  Upload the package in Spaarke's tenant (this environment's agent only - never a customer's):"
Write-Host "    Microsoft 365 admin center > Agents > All agents > Upload custom agent (assign to Spaarke staff)"
Write-Host ""
Write-Host "  For updates: Integrated apps > 'Spaarke AI' > Update > select $resolvedOutput"
Write-Host "  Customer agents: docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md section 7.12 (installed by the customer's IT)"
Write-Host ""
Write-Host "  After upload, configure in Copilot Studio:"
Write-Host "    1. Open MDA app in App Designer"
Write-Host "    2. Click '...' > 'Configure in Copilot Studio'"
Write-Host "    3. Add Dataverse tables as knowledge sources"
Write-Host "    4. Add glossary terms and synonyms"
Write-Host "    5. Publish the agent"
Write-Host ""
Write-Host "  See: docs/guides/COPILOT-KNOWLEDGE-CONFIGURATION-GUIDE.md"
Write-Host ""

Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Deployment Complete" -ForegroundColor Cyan
Write-Host "  Package: $resolvedOutput" -ForegroundColor Cyan
Write-Host "  Version: $Version" -ForegroundColor Cyan
Write-Host "================================================================`n" -ForegroundColor Cyan
