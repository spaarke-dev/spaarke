# ============================================================================
# Redis Infrastructure Provisioning Script
# ============================================================================
# Purpose: Provision Azure Redis Cache for SDAP distributed cache
# ADR Compliance: ADR-004 (Idempotency), ADR-009 (Redis-First Caching)
# Sprint: 4, Task: 4.1
# ============================================================================

<#
.SYNOPSIS
    Provisions Azure Redis Cache and configures App Service for SDAP API.

.DESCRIPTION
    This script provisions the necessary Redis infrastructure for distributed
    caching in production/staging environments. It includes:
    - Azure Redis Cache (Basic C0 tier for dev/staging, Standard C1 for production)
    - Connection string stored in Azure Key Vault
    - App Service configuration settings
    - Network security (firewall rules)
    - Validation tests

.PARAMETER Environment
    Target environment: 'dev', 'staging', or 'production'

.PARAMETER ResourceGroup
    Azure resource group name

.PARAMETER Location
    Azure region (e.g., 'eastus', 'westus2')

.PARAMETER RedisName
    Name for the Redis cache instance (must be globally unique)

.PARAMETER KeyVaultName
    Name of the Key Vault for storing connection strings

.PARAMETER AppServiceName
    Name of the App Service to configure

.EXAMPLE
    .\provision-redis.ps1 -Environment "dev" -ResourceGroup "sdap-rg" -Location "eastus" -RedisName "sdap-redis-dev" -KeyVaultName "sdap-kv" -AppServiceName "sdap-api-dev"

.NOTES
    Prerequisites:
    - Azure CLI installed and authenticated (az login)
    - Appropriate permissions: Contributor on resource group, Key Vault Administrator
    - PowerShell 7.0+
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('dev', 'staging', 'production')]
    [string]$Environment,

    [Parameter(Mandatory=$true)]
    [string]$ResourceGroup,

    [Parameter(Mandatory=$true)]
    [string]$Location,

    [Parameter(Mandatory=$true)]
    [string]$RedisName,

    [Parameter(Mandatory=$true)]
    [string]$KeyVaultName,

    [Parameter(Mandatory=$true)]
    [string]$AppServiceName
)

# ============================================================================
# Configuration
# ============================================================================

$ErrorActionPreference = "Stop"

# Environment-specific configurations
$SkuMap = @{
    'dev'        = @{ Sku = 'Basic'; VmSize = 'C0' }  # 250MB, ~$16/month
    'staging'    = @{ Sku = 'Basic'; VmSize = 'C1' }  # 1GB, ~$50/month
    'production' = @{ Sku = 'Standard'; VmSize = 'C1' } # 1GB with replication, ~$100/month
}

$InstanceNameMap = @{
    'dev'        = 'sdap-dev:'
    'staging'    = 'sdap-staging:'
    'production' = 'sdap-prod:'
}

$Config = $SkuMap[$Environment]
$InstanceName = $InstanceNameMap[$Environment]

# ============================================================================
# Helper Functions
# ============================================================================

function Write-Step {
    param([string]$Message)
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "========================================`n" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✅ $Message" -ForegroundColor Green
}

function Write-Warning-Message {
    param([string]$Message)
    Write-Host "⚠️  $Message" -ForegroundColor Yellow
}

function Write-Error-Message {
    param([string]$Message)
    Write-Host "❌ $Message" -ForegroundColor Red
}

function Test-AzCliInstalled {
    try {
        $null = az --version
        return $true
    } catch {
        return $false
    }
}

function Test-AzLoggedIn {
    try {
        $account = az account show 2>$null | ConvertFrom-Json
        return $account -ne $null
    } catch {
        return $false
    }
}

# ============================================================================
# Validation
# ============================================================================

Write-Step "Validating Prerequisites"

if (-not (Test-AzCliInstalled)) {
    Write-Error-Message "Azure CLI is not installed. Please install from https://aka.ms/azure-cli"
    exit 1
}
Write-Success "Azure CLI installed"

if (-not (Test-AzLoggedIn)) {
    Write-Error-Message "Not logged in to Azure. Please run 'az login'"
    exit 1
}
Write-Success "Logged in to Azure"

# Verify resource group exists
$rgExists = az group exists --name $ResourceGroup
if ($rgExists -eq 'false') {
    Write-Error-Message "Resource group '$ResourceGroup' does not exist"
    exit 1
}
Write-Success "Resource group '$ResourceGroup' exists"

# Verify Key Vault exists
$kvExists = az keyvault show --name $KeyVaultName --resource-group $ResourceGroup 2>$null
if (-not $kvExists) {
    Write-Error-Message "Key Vault '$KeyVaultName' does not exist in resource group '$ResourceGroup'"
    exit 1
}
Write-Success "Key Vault '$KeyVaultName' exists"

# Verify App Service exists
$appExists = az webapp show --name $AppServiceName --resource-group $ResourceGroup 2>$null
if (-not $appExists) {
    Write-Error-Message "App Service '$AppServiceName' does not exist in resource group '$ResourceGroup'"
    exit 1
}
Write-Success "App Service '$AppServiceName' exists"

Write-Host "`nProvisioning Configuration:" -ForegroundColor White
Write-Host "  Environment:   $Environment" -ForegroundColor White
Write-Host "  Redis Tier:    $($Config.Sku) $($Config.VmSize)" -ForegroundColor White
Write-Host "  Instance Name: $InstanceName" -ForegroundColor White
Write-Host "  Location:      $Location" -ForegroundColor White

# ============================================================================
# Provision Redis Cache
# ============================================================================

Write-Step "Provisioning Azure Redis Cache"

Write-Host "Creating Redis cache '$RedisName'..."
Write-Host "This may take 15-20 minutes..." -ForegroundColor Yellow

$redisExists = az redis show --name $RedisName --resource-group $ResourceGroup 2>$null
if ($redisExists) {
    Write-Warning-Message "Redis cache '$RedisName' already exists. Skipping creation."
} else {
    try {
        az redis create `
            --name $RedisName `
            --resource-group $ResourceGroup `
            --location $Location `
            --sku $Config.Sku `
            --vm-size $Config.VmSize `
            --enable-non-ssl-port false `
            --minimum-tls-version 1.2 `
            --output none

        Write-Success "Redis cache '$RedisName' created successfully"
    } catch {
        Write-Error-Message "Failed to create Redis cache: $_"
        exit 1
    }
}

# ============================================================================
# Retrieve Connection String
# ============================================================================

Write-Step "Retrieving Redis Connection String"

Write-Host "Fetching primary connection string..."
$redisKeys = az redis list-keys --name $RedisName --resource-group $ResourceGroup | ConvertFrom-Json
$primaryKey = $redisKeys.primaryKey
$connectionString = "${RedisName}.redis.cache.windows.net:6380,password=${primaryKey},ssl=True,abortConnect=False"

Write-Success "Connection string retrieved"

# ============================================================================
# Store in Key Vault
# ============================================================================

Write-Step "Storing Connection String in Key Vault"

$secretName = "Redis-ConnectionString-$Environment"

try {
    az keyvault secret set `
        --vault-name $KeyVaultName `
        --name $secretName `
        --value $connectionString `
        --output none

    Write-Success "Connection string stored in Key Vault as '$secretName'"
} catch {
    Write-Error-Message "Failed to store connection string in Key Vault: $_"
    exit 1
}

# ============================================================================
# Configure App Service
# ============================================================================

Write-Step "Configuring App Service"

Write-Host "Setting application settings on '$AppServiceName'..."

try {
    az webapp config appsettings set `
        --name $AppServiceName `
        --resource-group $ResourceGroup `
        --settings `
            "Redis__Enabled=true" `
            "Redis__ConnectionString=@Microsoft.KeyVault(SecretUri=https://${KeyVaultName}.vault.azure.net/secrets/${secretName})" `
            "Redis__InstanceName=${InstanceName}" `
            "Redis__DefaultExpirationMinutes=60" `
            "Redis__AbsoluteExpirationMinutes=1440" `
        --output none

    Write-Success "App Service configured with Redis settings"
} catch {
    Write-Error-Message "Failed to configure App Service: $_"
    exit 1
}

# ============================================================================
# Configure Network Security (Optional but Recommended)
# ============================================================================

Write-Step "Configuring Network Security"

Write-Host "Retrieving App Service outbound IP addresses..."
$appService = az webapp show --name $AppServiceName --resource-group $ResourceGroup | ConvertFrom-Json
$outboundIps = $appService.outboundIpAddresses -split ','

Write-Host "Configuring Redis firewall rules for App Service IPs..."
foreach ($ip in $outboundIps) {
    $ruleName = "AppService-$($ip.Replace('.', '-'))"

    # Check if rule already exists
    $existingRule = az redis firewall-rules show `
        --name $RedisName `
        --resource-group $ResourceGroup `
        --rule-name $ruleName 2>$null

    if ($existingRule) {
        Write-Warning-Message "Firewall rule '$ruleName' already exists. Skipping."
    } else {
        try {
            az redis firewall-rules create `
                --name $RedisName `
                --resource-group $ResourceGroup `
                --rule-name $ruleName `
                --start-ip $ip `
                --end-ip $ip `
                --output none

            Write-Success "Added firewall rule for IP: $ip"
        } catch {
            Write-Warning-Message "Failed to add firewall rule for IP $ip : $_"
        }
    }
}

# ============================================================================
# Validation Tests
# ============================================================================

Write-Step "Running Validation Tests"

Write-Host "Testing Redis connectivity..."
try {
    $pingResult = az redis show --name $RedisName --resource-group $ResourceGroup | ConvertFrom-Json
    if ($pingResult.provisioningState -eq 'Succeeded') {
        Write-Success "Redis cache is online and provisioning succeeded"
    } else {
        Write-Warning-Message "Redis cache provisioning state: $($pingResult.provisioningState)"
    }
} catch {
    Write-Error-Message "Failed to verify Redis status: $_"
}

Write-Host "`nVerifying Key Vault secret..."
try {
    $secret = az keyvault secret show --vault-name $KeyVaultName --name $secretName | ConvertFrom-Json
    if ($secret) {
        Write-Success "Key Vault secret '$secretName' is accessible"
    }
} catch {
    Write-Error-Message "Failed to verify Key Vault secret: $_"
}

Write-Host "`nVerifying App Service configuration..."
try {
    $appSettings = az webapp config appsettings list --name $AppServiceName --resource-group $ResourceGroup | ConvertFrom-Json
    $redisEnabled = ($appSettings | Where-Object { $_.name -eq 'Redis__Enabled' }).value

    if ($redisEnabled -eq 'true') {
        Write-Success "App Service configured with Redis__Enabled=true"
    } else {
        Write-Warning-Message "Redis__Enabled is not set to 'true' in App Service"
    }
} catch {
    Write-Error-Message "Failed to verify App Service configuration: $_"
}

# ============================================================================
# Summary
# ============================================================================

Write-Step "Provisioning Complete"

Write-Host "Redis Cache Details:" -ForegroundColor White
Write-Host "  Name:          $RedisName" -ForegroundColor White
Write-Host "  Tier:          $($Config.Sku) $($Config.VmSize)" -ForegroundColor White
Write-Host "  Host:          ${RedisName}.redis.cache.windows.net" -ForegroundColor White
Write-Host "  Port:          6380 (SSL)" -ForegroundColor White
Write-Host "  Instance Name: $InstanceName" -ForegroundColor White

Write-Host "`nKey Vault Details:" -ForegroundColor White
Write-Host "  Vault:         $KeyVaultName" -ForegroundColor White
Write-Host "  Secret:        $secretName" -ForegroundColor White

Write-Host "`nApp Service Configuration:" -ForegroundColor White
Write-Host "  App Service:   $AppServiceName" -ForegroundColor White
Write-Host "  Redis Enabled: true" -ForegroundColor White

Write-Host "`n" -ForegroundColor White
Write-Success "Redis provisioning completed successfully!"

Write-Host "`nNext Steps:" -ForegroundColor Yellow
Write-Host "1. Restart the App Service to apply new settings:" -ForegroundColor White
Write-Host "   az webapp restart --name $AppServiceName --resource-group $ResourceGroup" -ForegroundColor Cyan
Write-Host "`n2. Verify startup logs for Redis initialization:" -ForegroundColor White
Write-Host "   az webapp log tail --name $AppServiceName --resource-group $ResourceGroup" -ForegroundColor Cyan
Write-Host "`n3. Test the health check endpoint:" -ForegroundColor White
Write-Host "   curl https://${AppServiceName}.azurewebsites.net/health" -ForegroundColor Cyan
Write-Host "`n4. Monitor Redis metrics in Azure Portal:" -ForegroundColor White
Write-Host "   https://portal.azure.com/#@/resource/subscriptions/.../resourceGroups/$ResourceGroup/providers/Microsoft.Cache/Redis/$RedisName" -ForegroundColor Cyan

Write-Host "`n" -ForegroundColor White
Write-Host "Expected log output:" -ForegroundColor Yellow
Write-Host "  info: Distributed cache: Redis enabled with instance name '$InstanceName'" -ForegroundColor Gray
Write-Host "  info: Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckPublisher[0]" -ForegroundColor Gray
Write-Host "        Health check 'redis' status: Healthy ('Redis cache is available and responsive')" -ForegroundColor Gray

Write-Host "`n" -ForegroundColor White
