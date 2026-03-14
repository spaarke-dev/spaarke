<#
.SYNOPSIS
    Deploy shared Spaarke platform infrastructure using platform.bicep

.DESCRIPTION
    Deploys or updates all shared platform resources to rg-spaarke-platform-{env}
    using the subscription-scoped platform.bicep template.

    Resources deployed:
      - App Service Plan + App Service (Sprk.Bff.Api) with staging slot
      - Azure OpenAI (GPT-4o, GPT-4o-mini, text-embedding-3-large)
      - AI Search (Standard2, 2 replicas)
      - Document Intelligence (S0)
      - App Insights + Log Analytics
      - Platform Key Vault

    The script is idempotent — safe to run multiple times (FR-10).
    No secrets are passed as parameters — all secrets use Key Vault (FR-08).

    Called by: deploy-platform.yml GitHub Actions workflow
    Can also be run manually by developers/operators.

.PARAMETER EnvironmentName
    Target environment name (dev, staging, prod). Used in resource naming.

.PARAMETER Location
    Azure region for deployment (default: westus2)

.PARAMETER ParameterFile
    Path to .bicepparam file for the deployment. If not specified,
    defaults to infrastructure/bicep/platform-{EnvironmentName}.bicepparam

.PARAMETER WhatIf
    Run az deployment what-if instead of actual deployment.
    Shows what would change without making any modifications.

.EXAMPLE
    .\Deploy-Platform.ps1 -EnvironmentName prod
    # Deploy platform to production using default parameter file

.EXAMPLE
    .\Deploy-Platform.ps1 -EnvironmentName prod -WhatIf
    # Preview changes without deploying

.EXAMPLE
    .\Deploy-Platform.ps1 -EnvironmentName staging -ParameterFile ./custom-params.bicepparam
    # Deploy to staging with custom parameter file

.EXAMPLE
    .\Deploy-Platform.ps1 -EnvironmentName prod -Location eastus2
    # Deploy to a different region
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dev', 'staging', 'prod')]
    [string]$EnvironmentName,

    [string]$Location = 'westus2',

    [string]$ParameterFile,

    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

# ============================================================================
# CONFIGURATION
# ============================================================================

$BicepTemplate = "$PSScriptRoot\..\infrastructure\bicep\platform.bicep"
$DeploymentName = "platform-$EnvironmentName-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$ResourceGroupName = "rg-spaarke-platform-$EnvironmentName"
$LogFile = "$PSScriptRoot\..\logs\deploy-platform-$EnvironmentName-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"

# Default parameter file if not specified
if (-not $ParameterFile) {
    $ParameterFile = "$PSScriptRoot\..\infrastructure\bicep\platform-$EnvironmentName.bicepparam"
}

# ============================================================================
# LOGGING (NFR-05: Log all operations — who, what, when, which environment)
# ============================================================================

function Write-Log {
    param(
        [string]$Message,
        [ValidateSet('INFO', 'WARN', 'ERROR', 'SUCCESS')]
        [string]$Level = 'INFO'
    )

    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $logEntry = "[$timestamp] [$Level] $Message"

    # Console output with color
    switch ($Level) {
        'INFO'    { Write-Host $logEntry -ForegroundColor Gray }
        'WARN'    { Write-Host $logEntry -ForegroundColor Yellow }
        'ERROR'   { Write-Host $logEntry -ForegroundColor Red }
        'SUCCESS' { Write-Host $logEntry -ForegroundColor Green }
    }

    # File output
    $logDir = Split-Path $LogFile -Parent
    if (-not (Test-Path $logDir)) {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    }
    Add-Content -Path $LogFile -Value $logEntry
}

# ============================================================================
# PRE-FLIGHT CHECKS
# ============================================================================

function Test-Prerequisites {
    Write-Log "Running pre-flight checks..."

    # Check Azure CLI is installed
    $azVersion = az version 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI is not installed or not in PATH. Install from https://aka.ms/installazurecli"
    }
    Write-Log "  Azure CLI: installed"

    # Check logged in
    $account = az account show 2>&1 | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        throw "Not logged in to Azure. Run 'az login' first."
    }
    Write-Log "  Logged in as: $($account.user.name) (subscription: $($account.name))"

    # Check Bicep template exists
    if (-not (Test-Path $BicepTemplate)) {
        throw "Bicep template not found: $BicepTemplate"
    }
    Write-Log "  Bicep template: $BicepTemplate"

    # Check parameter file exists
    if (-not (Test-Path $ParameterFile)) {
        throw "Parameter file not found: $ParameterFile. Create it or specify -ParameterFile."
    }
    Write-Log "  Parameter file: $ParameterFile"

    Write-Log "Pre-flight checks passed" -Level SUCCESS
}

# ============================================================================
# DEPLOYMENT
# ============================================================================

function Deploy-Platform {
    param([bool]$IsWhatIf)

    if ($IsWhatIf) {
        Write-Log "Running deployment what-if (preview mode)..." -Level WARN
        Write-Log "  No resources will be created or modified."

        $output = az deployment sub what-if `
            --location $Location `
            --template-file $BicepTemplate `
            --parameters $ParameterFile `
            --name $DeploymentName 2>&1 | Out-String

        if ($LASTEXITCODE -ne 0) {
            Write-Log "What-if failed:" -Level ERROR
            Write-Log $output -Level ERROR
            throw "Deployment what-if failed. See output above."
        }

        Write-Host ""
        Write-Host "=== What-If Results ===" -ForegroundColor Cyan
        Write-Host $output
        Write-Log "What-if completed successfully" -Level SUCCESS
        return $null
    }

    Write-Log "Starting Bicep deployment..."
    Write-Log "  Deployment name: $DeploymentName"
    Write-Log "  Target scope: subscription"
    Write-Log "  Resource group: $ResourceGroupName (created by Bicep)"
    Write-Log "  Location: $Location"
    Write-Log "  This may take 10-20 minutes for a full deployment..."

    $output = az deployment sub create `
        --location $Location `
        --template-file $BicepTemplate `
        --parameters $ParameterFile `
        --name $DeploymentName `
        --output json 2>&1 | Out-String

    if ($LASTEXITCODE -ne 0) {
        Write-Log "Deployment failed:" -Level ERROR
        Write-Log $output -Level ERROR
        throw "Bicep deployment failed. See log for details: $LogFile"
    }

    $deployResult = $output | ConvertFrom-Json
    Write-Log "Bicep deployment completed successfully" -Level SUCCESS

    return $deployResult
}

# ============================================================================
# POST-DEPLOYMENT VALIDATION
# ============================================================================

function Test-DeploymentOutputs {
    param($DeployResult)

    Write-Log "Validating deployment outputs..."

    $outputs = $DeployResult.properties.outputs

    # Verify critical outputs exist
    $requiredOutputs = @(
        'resourceGroupName',
        'apiUrl',
        'keyVaultName',
        'openAiEndpoint',
        'aiSearchEndpoint',
        'docIntelligenceEndpoint',
        'appInsightsName'
    )

    foreach ($outputName in $requiredOutputs) {
        $value = $outputs.$outputName.value
        if (-not $value) {
            Write-Log "  Missing output: $outputName" -Level ERROR
            throw "Deployment output '$outputName' is missing or empty."
        }
        Write-Log "  $outputName = $value"
    }

    Write-Log "All deployment outputs present" -Level SUCCESS
    return $outputs
}

function Test-ResourceHealth {
    param($Outputs)

    Write-Log "Validating resource health..."

    $rgName = $Outputs.resourceGroupName.value

    # Check resource group exists
    $rgExists = az group exists --name $rgName 2>&1 | Out-String
    if ($rgExists.Trim() -ne 'true') {
        throw "Resource group '$rgName' does not exist after deployment."
    }
    Write-Log "  Resource group '$rgName': exists" -Level SUCCESS

    # Check App Service health endpoint
    $apiUrl = $Outputs.apiUrl.value
    $healthUrl = "$apiUrl/healthz"
    Write-Log "  Checking App Service health: $healthUrl"

    $maxRetries = 6
    $retryCount = 0
    $healthy = $false

    while ($retryCount -lt $maxRetries -and -not $healthy) {
        try {
            $response = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 10
            if ($response -eq "Healthy" -or $response.status -eq "Healthy") {
                $healthy = $true
                Write-Log "  App Service health check: passed" -Level SUCCESS
            }
        }
        catch {
            $retryCount++
            if ($retryCount -lt $maxRetries) {
                Write-Log "  Waiting for App Service... (attempt $retryCount/$maxRetries)" -Level WARN
                Start-Sleep -Seconds 10
            }
        }
    }

    if (-not $healthy) {
        Write-Log "  App Service health check failed after $maxRetries attempts" -Level WARN
        Write-Log "  This may be expected for first-time deployments (no code deployed yet)" -Level WARN
    }

    # Check Key Vault is accessible
    $kvName = $Outputs.keyVaultName.value
    Write-Log "  Checking Key Vault '$kvName'..."
    $kvShow = az keyvault show --name $kvName 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
        Write-Log "  Key Vault '$kvName': accessible" -Level SUCCESS
    } else {
        Write-Log "  Key Vault '$kvName': not accessible (may need RBAC)" -Level WARN
    }

    # Check OpenAI account
    $openAiName = $Outputs.openAiName.value
    Write-Log "  Checking Azure OpenAI '$openAiName'..."
    $oaiShow = az cognitiveservices account show --name $openAiName --resource-group $rgName 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
        Write-Log "  Azure OpenAI '$openAiName': provisioned" -Level SUCCESS
    } else {
        Write-Log "  Azure OpenAI '$openAiName': check failed" -Level WARN
    }

    # Check AI Search
    $searchName = $Outputs.aiSearchName.value
    Write-Log "  Checking AI Search '$searchName'..."
    $searchShow = az search service show --name $searchName --resource-group $rgName 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
        Write-Log "  AI Search '$searchName': provisioned" -Level SUCCESS
    } else {
        Write-Log "  AI Search '$searchName': check failed" -Level WARN
    }

    Write-Log "Resource health validation complete" -Level SUCCESS
}

# ============================================================================
# MAIN EXECUTION
# ============================================================================

$startTime = Get-Date

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Spaarke Platform Deployment" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

Write-Log "Deployment initiated"
Write-Log "  Operator: $([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)"
Write-Log "  Environment: $EnvironmentName"
Write-Log "  Location: $Location"
Write-Log "  WhatIf: $WhatIf"
Write-Log "  Timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss UTC' -AsUTC)"
Write-Log "  Log file: $LogFile"

try {
    # Step 1: Pre-flight checks
    Write-Host ""
    Write-Host "[1/4] Pre-flight checks" -ForegroundColor Yellow
    Test-Prerequisites

    # Step 2: Deploy Bicep template (or what-if)
    Write-Host ""
    if ($WhatIf) {
        Write-Host "[2/4] Running what-if preview" -ForegroundColor Yellow
    } else {
        Write-Host "[2/4] Deploying Bicep template" -ForegroundColor Yellow
    }
    $deployResult = Deploy-Platform -IsWhatIf $WhatIf

    if ($WhatIf) {
        # WhatIf mode — skip validation steps
        Write-Host ""
        Write-Host "[3/4] Skipped (what-if mode)" -ForegroundColor Gray
        Write-Host "[4/4] Skipped (what-if mode)" -ForegroundColor Gray
    } else {
        # Step 3: Validate deployment outputs
        Write-Host ""
        Write-Host "[3/4] Validating deployment outputs" -ForegroundColor Yellow
        $outputs = Test-DeploymentOutputs -DeployResult $deployResult

        # Step 4: Validate resource health
        Write-Host ""
        Write-Host "[4/4] Validating resource health" -ForegroundColor Yellow
        Test-ResourceHealth -Outputs $outputs
    }

    # Summary
    $elapsed = (Get-Date) - $startTime
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host "  Deployment Complete" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host ""

    if (-not $WhatIf) {
        Write-Log "Deployment summary:" -Level SUCCESS
        Write-Log "  Resource Group: $ResourceGroupName" -Level SUCCESS
        Write-Log "  API URL: $($outputs.apiUrl.value)" -Level SUCCESS
        Write-Log "  Key Vault: $($outputs.keyVaultName.value)" -Level SUCCESS
        Write-Log "  OpenAI: $($outputs.openAiEndpoint.value)" -Level SUCCESS
        Write-Log "  AI Search: $($outputs.aiSearchEndpoint.value)" -Level SUCCESS
        Write-Log "  Doc Intelligence: $($outputs.docIntelligenceEndpoint.value)" -Level SUCCESS
    }

    Write-Log "Total time: $($elapsed.ToString('mm\:ss'))" -Level SUCCESS
    Write-Log "Log file: $LogFile" -Level INFO
}
catch {
    Write-Log "DEPLOYMENT FAILED: $($_.Exception.Message)" -Level ERROR
    Write-Log "Stack trace: $($_.ScriptStackTrace)" -Level ERROR
    Write-Log "Log file: $LogFile" -Level ERROR

    $elapsed = (Get-Date) - $startTime
    Write-Log "Failed after: $($elapsed.ToString('mm\:ss'))" -Level ERROR

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Red
    Write-Host "  Deployment Failed" -ForegroundColor Red
    Write-Host "============================================================" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  See log: $LogFile" -ForegroundColor Yellow
    Write-Host ""

    exit 1
}
