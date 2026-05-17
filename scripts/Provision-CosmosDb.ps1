<#
.SYNOPSIS
    Provision Cosmos DB for the Spaarke AI platform using cosmos-db.bicep

.DESCRIPTION
    Deploys (or updates) the Cosmos DB account and spaarke-ai database to the
    target resource group using a resource-group-scoped Bicep deployment.

    Resources deployed:
      - Cosmos DB account (serverless, RBAC-only, no master key auth in app code)
      - Database: spaarke-ai
      - Containers: sessions, prompts, audit, memory, feedback
        (with partition keys and TTL settings per task AIPU2-002)

    The script is idempotent — safe to run multiple times (Bicep is declarative).
    No secrets are committed or output — application access uses DefaultAzureCredential.

    Resource group mapping:
      dev     -> spe-infrastructure-westus2
      staging -> rg-spaarke-platform-staging  (update when staging RG is provisioned)
      prod    -> rg-spaarke-platform-prod

.PARAMETER Environment
    Target environment name (dev, staging, prod).

.PARAMETER Location
    Azure region for the deployment (default: westus2).

.PARAMETER AppServicePrincipalId
    Object ID of the App Service managed identity to grant Cosmos DB Built-in Data
    Contributor. If omitted, no RBAC assignment is created (can be added later).

.PARAMETER WhatIf
    Run az deployment group what-if to preview changes without applying them.

.EXAMPLE
    .\Provision-CosmosDb.ps1 -Environment dev -WhatIf
    # Preview what would be created in dev

.EXAMPLE
    .\Provision-CosmosDb.ps1 -Environment dev
    # Deploy Cosmos DB to dev environment

.EXAMPLE
    .\Provision-CosmosDb.ps1 -Environment dev -AppServicePrincipalId "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
    # Deploy and grant RBAC to the App Service managed identity
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dev', 'staging', 'prod')]
    [string]$Environment,

    [string]$Location = 'westus2',

    [string]$AppServicePrincipalId = '',

    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

# ============================================================================
# CONFIGURATION
# ============================================================================

# Resource group per environment
$ResourceGroupMap = @{
    dev     = 'spe-infrastructure-westus2'
    staging = 'rg-spaarke-platform-staging'
    prod    = 'rg-spaarke-platform-prod'
}

# Cosmos DB account name per environment (matches platform.bicep naming convention)
$AccountNameMap = @{
    dev     = 'spaarke-cosmos-dev'
    staging = 'spaarke-cosmos-staging'
    prod    = 'spaarke-cosmos-prod'
}

$ResourceGroupName  = $ResourceGroupMap[$Environment]
$CosmosAccountName  = $AccountNameMap[$Environment]
$BicepTemplate      = "$PSScriptRoot\..\infrastructure\bicep\modules\cosmos-db.bicep"
$DeploymentName     = "cosmos-db-$Environment-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$LogFile            = "$PSScriptRoot\..\logs\provision-cosmosdb-$Environment-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"

# ============================================================================
# LOGGING
# ============================================================================

function Write-Log {
    param(
        [string]$Message,
        [ValidateSet('INFO', 'WARN', 'ERROR', 'SUCCESS')]
        [string]$Level = 'INFO'
    )

    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $logEntry  = "[$timestamp] [$Level] $Message"

    switch ($Level) {
        'INFO'    { Write-Host $logEntry -ForegroundColor Gray }
        'WARN'    { Write-Host $logEntry -ForegroundColor Yellow }
        'ERROR'   { Write-Host $logEntry -ForegroundColor Red }
        'SUCCESS' { Write-Host $logEntry -ForegroundColor Green }
    }

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
    Write-Log 'Running pre-flight checks...'

    # Azure CLI installed
    az version 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Azure CLI is not installed or not in PATH. Install from https://aka.ms/installazurecli'
    }
    Write-Log '  Azure CLI: installed'

    # Logged in
    $account = az account show 2>&1 | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        throw "Not logged in to Azure. Run 'az login' first."
    }
    Write-Log "  Logged in as: $($account.user.name) (subscription: $($account.name))"

    # Bicep template exists
    if (-not (Test-Path $BicepTemplate)) {
        throw "Bicep module not found: $BicepTemplate"
    }
    Write-Log "  Bicep module: $BicepTemplate"

    # Resource group exists
    $rgExists = az group exists --name $ResourceGroupName 2>&1 | Out-String
    if ($rgExists.Trim() -ne 'true') {
        throw "Resource group '$ResourceGroupName' does not exist. Create it first or run Deploy-Platform.ps1."
    }
    Write-Log "  Resource group '$ResourceGroupName': exists"

    Write-Log 'Pre-flight checks passed' -Level SUCCESS
}

# ============================================================================
# DEPLOYMENT
# ============================================================================

function Deploy-CosmosDb {
    param([bool]$IsWhatIf)

    # Build parameter overrides for the CLI
    $params = @(
        "accountName=$CosmosAccountName"
        "location=$Location"
        'databaseName=spaarke-ai'
    )

    if (-not [string]::IsNullOrEmpty($AppServicePrincipalId)) {
        $params += "appServicePrincipalId=$AppServicePrincipalId"
    }

    if ($IsWhatIf) {
        Write-Log 'Running deployment what-if (preview mode)...' -Level WARN
        Write-Log '  No resources will be created or modified.'

        $paramArgs = $params | ForEach-Object { "--parameters", $_ }

        $output = az deployment group what-if `
            --resource-group $ResourceGroupName `
            --template-file $BicepTemplate `
            @paramArgs `
            --name $DeploymentName 2>&1 | Out-String

        if ($LASTEXITCODE -ne 0) {
            Write-Log 'What-if failed:' -Level ERROR
            Write-Log $output -Level ERROR
            throw 'Deployment what-if failed. See output above.'
        }

        Write-Host ''
        Write-Host '=== What-If Results ===' -ForegroundColor Cyan
        Write-Host $output
        Write-Log 'What-if completed successfully' -Level SUCCESS
        return $null
    }

    Write-Log 'Starting Cosmos DB Bicep deployment...'
    Write-Log "  Deployment name:  $DeploymentName"
    Write-Log "  Resource group:   $ResourceGroupName"
    Write-Log "  Cosmos account:   $CosmosAccountName"
    Write-Log "  Database:         spaarke-ai"
    Write-Log "  Location:         $Location"
    Write-Log '  Containers:       sessions, prompts, audit, memory, feedback'
    Write-Log '  This may take 3-8 minutes for first-time Cosmos DB provisioning...'

    $paramArgs = $params | ForEach-Object { '--parameters', $_ }

    $output = az deployment group create `
        --resource-group $ResourceGroupName `
        --template-file $BicepTemplate `
        @paramArgs `
        --name $DeploymentName `
        --output json 2>&1 | Out-String

    if ($LASTEXITCODE -ne 0) {
        Write-Log 'Deployment failed:' -Level ERROR
        Write-Log $output -Level ERROR
        throw "Bicep deployment failed. See log: $LogFile"
    }

    $deployResult = $output | ConvertFrom-Json
    Write-Log 'Bicep deployment completed successfully' -Level SUCCESS
    return $deployResult
}

# ============================================================================
# POST-DEPLOYMENT VALIDATION
# ============================================================================

function Test-CosmosDeployment {
    param($DeployResult)

    Write-Log 'Validating Cosmos DB deployment...'

    $outputs = $DeployResult.properties.outputs

    # Verify outputs
    $requiredOutputs = @('accountName', 'accountEndpoint', 'databaseName')
    foreach ($outputName in $requiredOutputs) {
        $value = $outputs.$outputName.value
        if (-not $value) {
            Write-Log "  Missing output: $outputName" -Level ERROR
            throw "Deployment output '$outputName' is missing or empty."
        }
        Write-Log "  $outputName = $value"
    }

    $accountName = $outputs.accountName.value

    # Verify Cosmos DB account exists and is serverless
    Write-Log "  Verifying Cosmos account '$accountName'..."
    $cosmosShow = az cosmosdb show --name $accountName --resource-group $ResourceGroupName --output json 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        Write-Log "  Could not retrieve Cosmos DB account (may need a moment to propagate)" -Level WARN
    } else {
        $cosmosJson = $cosmosShow | ConvertFrom-Json
        $isServerless = $cosmosJson.capabilities | Where-Object { $_.name -eq 'EnableServerless' }
        if ($isServerless) {
            Write-Log "  Capacity mode: Serverless (confirmed)" -Level SUCCESS
        } else {
            Write-Log "  WARNING: Serverless capability not detected in show output" -Level WARN
        }
    }

    # Verify all 5 containers exist
    Write-Log "  Verifying containers in database 'spaarke-ai'..."
    $containersJson = az cosmosdb sql container list `
        --account-name $accountName `
        --database-name 'spaarke-ai' `
        --resource-group $ResourceGroupName `
        --output json 2>&1 | Out-String

    if ($LASTEXITCODE -ne 0) {
        Write-Log '  Could not list containers (permissions or propagation delay)' -Level WARN
    } else {
        $containers = ($containersJson | ConvertFrom-Json) | ForEach-Object { $_.name }
        $expected   = @('sessions', 'prompts', 'audit', 'memory', 'feedback')
        foreach ($name in $expected) {
            if ($containers -contains $name) {
                Write-Log "  Container '$name': present" -Level SUCCESS
            } else {
                Write-Log "  Container '$name': MISSING" -Level ERROR
            }
        }
    }

    Write-Log 'Validation complete' -Level SUCCESS
}

# ============================================================================
# MAIN EXECUTION
# ============================================================================

$startTime = Get-Date

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  Spaarke Cosmos DB Provisioning' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ''

Write-Log 'Provisioning initiated'
Write-Log "  Operator:    $([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)"
Write-Log "  Environment: $Environment"
Write-Log "  Resource RG: $ResourceGroupName"
Write-Log "  Account:     $CosmosAccountName"
Write-Log "  Location:    $Location"
Write-Log "  WhatIf:      $WhatIf"
Write-Log "  Timestamp:   $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss UTC' -AsUTC)"
Write-Log "  Log file:    $LogFile"

try {
    # Step 1: Pre-flight
    Write-Host ''
    Write-Host '[1/3] Pre-flight checks' -ForegroundColor Yellow
    Test-Prerequisites

    # Step 2: Deploy / what-if
    Write-Host ''
    if ($WhatIf) {
        Write-Host '[2/3] Running what-if preview' -ForegroundColor Yellow
    } else {
        Write-Host '[2/3] Deploying Cosmos DB' -ForegroundColor Yellow
    }
    $deployResult = Deploy-CosmosDb -IsWhatIf $WhatIf

    # Step 3: Validate
    if ($WhatIf) {
        Write-Host ''
        Write-Host '[3/3] Skipped (what-if mode)' -ForegroundColor Gray
    } else {
        Write-Host ''
        Write-Host '[3/3] Validating deployment' -ForegroundColor Yellow
        Test-CosmosDeployment -DeployResult $deployResult
    }

    $elapsed = (Get-Date) - $startTime

    Write-Host ''
    Write-Host '============================================================' -ForegroundColor Green
    Write-Host '  Provisioning Complete' -ForegroundColor Green
    Write-Host '============================================================' -ForegroundColor Green
    Write-Host ''

    if (-not $WhatIf) {
        $outputs = $deployResult.properties.outputs
        Write-Log "Cosmos DB endpoint: $($outputs.cosmosEndpoint.value ?? $outputs.accountEndpoint.value)" -Level SUCCESS
        Write-Log "Database:           spaarke-ai (5 containers)" -Level SUCCESS
        Write-Log "RBAC:               DefaultAzureCredential / Managed Identity" -Level SUCCESS
        Write-Log "No connection strings stored — keys not used by application code." -Level SUCCESS
    }

    Write-Log "Total time: $($elapsed.ToString('mm\:ss'))" -Level SUCCESS
    Write-Log "Log file: $LogFile" -Level INFO
}
catch {
    Write-Log "PROVISIONING FAILED: $($_.Exception.Message)" -Level ERROR
    Write-Log "Stack trace: $($_.ScriptStackTrace)" -Level ERROR
    Write-Log "Log file: $LogFile" -Level ERROR

    $elapsed = (Get-Date) - $startTime
    Write-Log "Failed after: $($elapsed.ToString('mm\:ss'))" -Level ERROR

    Write-Host ''
    Write-Host '============================================================' -ForegroundColor Red
    Write-Host '  Provisioning Failed' -ForegroundColor Red
    Write-Host '============================================================' -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  See log: $LogFile" -ForegroundColor Yellow
    Write-Host ''

    exit 1
}
