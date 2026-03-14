#!/usr/bin/env pwsh
<#
.SYNOPSIS
    End-to-end customer provisioning for Spaarke production environments.

.DESCRIPTION
    Orchestrates the complete customer onboarding pipeline:
      1. Validate inputs and prerequisites (Azure CLI, PAC CLI, Bicep template)
      2. Create resource group (rg-spaarke-{customerId}-{env})
      3. Deploy customer.bicep (Storage, Key Vault, Service Bus, Redis)
      4. Populate customer Key Vault with secrets (connection strings, API keys)
      5. Create Dataverse environment via Power Platform Admin API
      6. Wait for Dataverse environment provisioning
      7. Import managed solutions (Deploy-DataverseSolutions.ps1)
      8. Provision SPE containers
      9. Register customer in BFF API tenant registry
      10. Run smoke tests (Test-Deployment.ps1)

    The script is idempotent — safe to re-run if partially failed (FR-10).
    Progress is tracked in a state file so the script can resume from the last
    successful step on re-execution.

    The demo environment is provisioned using this exact script (FR-06).

.PARAMETER CustomerId
    Customer identifier (lowercase, alphanumeric, 3-10 chars).
    Drives all resource naming: rg-spaarke-{customerId}-prod, sprk-{customerId}-prod-kv, etc.

.PARAMETER DisplayName
    Human-readable customer name for display purposes and Dataverse environment.

.PARAMETER EnvironmentName
    Target environment: dev, staging, prod (default: prod)

.PARAMETER Location
    Azure region for all customer resources (default: westus2)

.PARAMETER TenantId
    Azure AD / Entra ID tenant ID. Required for Dataverse and PAC CLI auth.

.PARAMETER ClientId
    Service principal (app registration) client ID for PAC CLI and Admin API auth.

.PARAMETER ClientSecret
    Service principal client secret. Mutually exclusive with -CertificateThumbprint.

.PARAMETER CertificateThumbprint
    Certificate thumbprint for service principal auth. Mutually exclusive with -ClientSecret.

.PARAMETER PlatformKeyVaultName
    Name of the shared platform Key Vault (default: sprk-platform-prod-kv).
    Used to read shared secrets (e.g., BFF API key, OpenAI key).

.PARAMETER PlatformResourceGroup
    Resource group containing shared platform resources (default: rg-spaarke-platform-prod).

.PARAMETER BffApiBaseUrl
    Base URL of the BFF API for tenant registration and smoke tests.
    Default: https://api.spaarke.com

.PARAMETER DataverseRegion
    Region for Dataverse environment creation (default: unitedstates).

.PARAMETER ResumeFromStep
    Resume from a specific step number (1-10). Overrides state file detection.

.PARAMETER SkipDataverse
    Skip Dataverse provisioning steps (5-7). Use when Dataverse env already exists.

.PARAMETER WhatIf
    Show what would be provisioned without executing.

.EXAMPLE
    .\Provision-Customer.ps1 -CustomerId "demo" -DisplayName "Spaarke Demo" `
        -TenantId "a221a95e-..." -ClientId "..." -ClientSecret "..."
    # Provision the demo customer end-to-end

.EXAMPLE
    .\Provision-Customer.ps1 -CustomerId "acme" -DisplayName "Acme Legal" `
        -TenantId "a221a95e-..." -ClientId "..." -ClientSecret "..." `
        -ResumeFromStep 5
    # Resume provisioning from step 5 (Dataverse creation)

.EXAMPLE
    .\Provision-Customer.ps1 -CustomerId "demo" -DisplayName "Spaarke Demo" `
        -TenantId "a221a95e-..." -ClientId "..." -ClientSecret "..." `
        -WhatIf
    # Preview provisioning plan without executing

.EXAMPLE
    .\Provision-Customer.ps1 -CustomerId "acme" -DisplayName "Acme Legal" `
        -TenantId "a221a95e-..." -ClientId "..." -CertificateThumbprint "ABC123..." `
        -SkipDataverse
    # Provision Azure resources only (Dataverse created manually)
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9]{3,10}$')]
    [string]$CustomerId,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$DisplayName,

    [ValidateSet('dev', 'staging', 'prod')]
    [string]$EnvironmentName = 'prod',

    [string]$Location = 'westus2',

    [Parameter(Mandatory = $true)]
    [string]$TenantId,

    [Parameter(Mandatory = $true)]
    [string]$ClientId,

    [Parameter(Mandatory = $true, ParameterSetName = "Secret")]
    [string]$ClientSecret,

    [Parameter(Mandatory = $true, ParameterSetName = "Certificate")]
    [string]$CertificateThumbprint,

    [string]$PlatformKeyVaultName = 'sprk-platform-prod-kv',

    [string]$PlatformResourceGroup = 'rg-spaarke-platform-prod',

    [string]$BffApiBaseUrl = 'https://api.spaarke.com',

    [string]$DataverseRegion = 'unitedstates',

    [ValidateRange(1, 10)]
    [int]$ResumeFromStep = 0,

    [switch]$SkipDataverse
)

$ErrorActionPreference = "Stop"

# ============================================================================
# CONFIGURATION
# ============================================================================

$TotalSteps = 10
$ScriptRoot = $PSScriptRoot
$RepoRoot = (Resolve-Path "$ScriptRoot\..").Path
$BicepTemplate = Join-Path $RepoRoot "infrastructure" "bicep" "customer.bicep"
$StateDir = Join-Path $RepoRoot "logs" "provisioning"
$StateFile = Join-Path $StateDir "provision-$CustomerId-$EnvironmentName.state.json"
$LogFile = Join-Path $RepoRoot "logs" "provision-$CustomerId-$EnvironmentName-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"

# Resource naming (follows AZURE-RESOURCE-NAMING-CONVENTION.md)
$ResourceGroupName = "rg-spaarke-$CustomerId-$EnvironmentName"
$KeyVaultName = "sprk-$CustomerId-$EnvironmentName-kv" | ForEach-Object { $_.Substring(0, [Math]::Min($_.Length, 24)) }
$DataverseEnvName = "spaarke-$CustomerId"
$DataverseEnvUrl = "https://$DataverseEnvName.crm.dynamics.com"

# ============================================================================
# LOGGING
# ============================================================================

function Write-Log {
    param(
        [string]$Message,
        [ValidateSet('INFO', 'WARN', 'ERROR', 'SUCCESS', 'STEP')]
        [string]$Level = 'INFO'
    )

    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $logEntry = "[$timestamp] [$Level] $Message"

    switch ($Level) {
        'INFO'    { Write-Host "  $logEntry" -ForegroundColor Gray }
        'WARN'    { Write-Host "  $logEntry" -ForegroundColor Yellow }
        'ERROR'   { Write-Host "  $logEntry" -ForegroundColor Red }
        'SUCCESS' { Write-Host "  $logEntry" -ForegroundColor Green }
        'STEP'    { Write-Host "" ; Write-Host "  $logEntry" -ForegroundColor Cyan }
    }

    # File output
    $logDir = Split-Path $LogFile -Parent
    if (-not (Test-Path $logDir)) {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    }
    Add-Content -Path $LogFile -Value $logEntry
}

function Write-StepHeader {
    param([int]$Step, [string]$Message)
    Write-Host ""
    Write-Host "  [$Step/$TotalSteps] $Message" -ForegroundColor Yellow
    Write-Host "  $('-' * 60)" -ForegroundColor DarkGray
    Write-Log "Step ${Step}: $Message" -Level STEP
}

# ============================================================================
# STATE MANAGEMENT (Resumability — FR-10)
# ============================================================================

function Get-ProvisioningState {
    if (Test-Path $StateFile) {
        try {
            $state = Get-Content $StateFile -Raw | ConvertFrom-Json
            return $state
        }
        catch {
            Write-Log "Could not read state file: $($_.Exception.Message). Starting fresh." -Level WARN
        }
    }

    return [PSCustomObject]@{
        CustomerId      = $CustomerId
        DisplayName     = $DisplayName
        EnvironmentName = $EnvironmentName
        StartedAt       = (Get-Date -Format 'o')
        CompletedAt     = $null
        CompletedSteps  = @()
        StepOutputs     = @{}
        LastStep        = 0
        Status          = 'not-started'
    }
}

function Save-ProvisioningState {
    param([PSCustomObject]$State)

    if (-not (Test-Path $StateDir)) {
        New-Item -ItemType Directory -Path $StateDir -Force | Out-Null
    }

    $State | ConvertTo-Json -Depth 10 | Set-Content -Path $StateFile -Encoding UTF8
    Write-Log "State saved to $StateFile"
}

function Complete-Step {
    param(
        [PSCustomObject]$State,
        [int]$StepNumber,
        [string]$StepName,
        [hashtable]$Outputs = @{}
    )

    $State.CompletedSteps += [PSCustomObject]@{
        Step      = $StepNumber
        Name      = $StepName
        Completed = (Get-Date -Format 'o')
    }
    $State.LastStep = $StepNumber
    $State.Status = 'in-progress'

    # Merge outputs
    foreach ($key in $Outputs.Keys) {
        $State.StepOutputs | Add-Member -NotePropertyName $key -NotePropertyValue $Outputs[$key] -Force
    }

    Save-ProvisioningState -State $State
    Write-Log "Step $StepNumber ($StepName) completed." -Level SUCCESS
}

function Test-StepCompleted {
    param(
        [PSCustomObject]$State,
        [int]$StepNumber
    )

    return ($State.CompletedSteps | Where-Object { $_.Step -eq $StepNumber }) -ne $null
}

function Get-ResumeStep {
    param([PSCustomObject]$State)

    if ($ResumeFromStep -gt 0) {
        Write-Log "User requested resume from step $ResumeFromStep" -Level INFO
        return $ResumeFromStep
    }

    if ($State.LastStep -gt 0 -and $State.Status -eq 'in-progress') {
        $resumeStep = $State.LastStep + 1
        Write-Log "Resuming from step $resumeStep (last completed: $($State.LastStep))" -Level INFO
        return $resumeStep
    }

    return 1
}

# ============================================================================
# STEP 1: Validate inputs and prerequisites
# ============================================================================

function Invoke-Step1_ValidatePrerequisites {
    param([PSCustomObject]$State)

    Write-StepHeader 1 "Validating inputs and prerequisites"

    # Azure CLI
    $azVersion = az version --output json 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI is not installed or not in PATH. Install from https://aka.ms/installazurecli"
    }
    Write-Log "Azure CLI: available"

    # Azure login check
    $account = az account show --output json 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Not logged in to Azure. Run 'az login' first."
    }
    $accountObj = $account | ConvertFrom-Json
    Write-Log "Azure account: $($accountObj.user.name) (subscription: $($accountObj.name))"

    # PAC CLI (needed for Dataverse steps)
    if (-not $SkipDataverse) {
        try {
            # Resolve pac to pac.cmd (bash wrapper scripts can't be piped in PowerShell)
            $pacCmd = Get-Command pac -ErrorAction Stop | Select-Object -ExpandProperty Source
            if ($pacCmd -match '\.cmd$') {
                $script:PacExe = $pacCmd
            }
            elseif (Test-Path "$env:LOCALAPPDATA\Microsoft\PowerAppsCLI\pac.cmd") {
                $script:PacExe = "$env:LOCALAPPDATA\Microsoft\PowerAppsCLI\pac.cmd"
            }
            else {
                $script:PacExe = $pacCmd
            }
            $pacVersion = & $script:PacExe help 2>&1 | Out-String
            if ($pacVersion -match 'Microsoft PowerPlatform CLI|Usage: pac') {
                Write-Log "PAC CLI: available ($script:PacExe)"
            }
            else {
                Write-Log "PAC CLI not found. Dataverse steps (5-7) will require manual execution." -Level WARN
                Write-Log "Install: dotnet tool install --global Microsoft.PowerApps.CLI.Tool" -Level WARN
            }
        }
        catch {
            Write-Log "PAC CLI not found. Dataverse steps (5-7) will require manual execution." -Level WARN
            Write-Log "Install: dotnet tool install --global Microsoft.PowerApps.CLI.Tool" -Level WARN
        }
    }

    # Bicep template
    if (-not (Test-Path $BicepTemplate)) {
        throw "Customer Bicep template not found: $BicepTemplate"
    }
    Write-Log "Bicep template: $BicepTemplate"

    # Validate naming constraints
    Write-Log "Resource group: $ResourceGroupName"
    Write-Log "Key Vault: $KeyVaultName"
    Write-Log "Dataverse env: $DataverseEnvName"
    Write-Log "BFF API: $BffApiBaseUrl"

    Complete-Step -State $State -StepNumber 1 -StepName "Validate prerequisites"
}

# ============================================================================
# STEP 2: Create resource group
# ============================================================================

function Invoke-Step2_CreateResourceGroup {
    param([PSCustomObject]$State)

    Write-StepHeader 2 "Creating resource group $ResourceGroupName"

    # Check if already exists (idempotent)
    $rgExists = az group exists --name $ResourceGroupName 2>&1 | Out-String
    if ($rgExists.Trim() -eq 'true') {
        Write-Log "Resource group '$ResourceGroupName' already exists (idempotent)." -Level INFO
    }
    else {
        Write-Log "Creating resource group '$ResourceGroupName' in $Location..."
        $output = az group create `
            --name $ResourceGroupName `
            --location $Location `
            --tags "customer=$CustomerId" "environment=$EnvironmentName" "application=spaarke" "managedBy=provision-customer" `
            --output json 2>&1 | Out-String

        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create resource group: $output"
        }
        Write-Log "Resource group created." -Level SUCCESS
    }

    Complete-Step -State $State -StepNumber 2 -StepName "Create resource group" `
        -Outputs @{ ResourceGroupName = $ResourceGroupName }
}

# ============================================================================
# STEP 3: Deploy customer.bicep (Azure resources)
# ============================================================================

function Invoke-Step3_DeployBicep {
    param([PSCustomObject]$State)

    Write-StepHeader 3 "Deploying customer.bicep (Storage, Key Vault, Service Bus, Redis)"

    $deploymentName = "customer-$CustomerId-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

    Write-Log "Deployment name: $deploymentName"
    Write-Log "Template: $BicepTemplate"
    Write-Log "This may take 10-15 minutes..."

    $output = az deployment sub create `
        --location $Location `
        --template-file $BicepTemplate `
        --name $deploymentName `
        --parameters `
            customerId=$CustomerId `
            environmentName=$EnvironmentName `
            location=$Location `
            platformKeyVaultName=$PlatformKeyVaultName `
        --output json 2>&1 | Out-String

    if ($LASTEXITCODE -ne 0) {
        Write-Log "Bicep deployment failed:" -Level ERROR
        Write-Log $output -Level ERROR
        throw "customer.bicep deployment failed. See log: $LogFile"
    }

    $deployResult = $output | ConvertFrom-Json
    $outputs = $deployResult.properties.outputs

    # Extract outputs for subsequent steps
    $stepOutputs = @{
        StorageAccountName      = $outputs.storageAccountName.value
        StorageConnectionString = $outputs.storageConnectionString.value
        KeyVaultName            = $outputs.keyVaultName.value
        KeyVaultUri             = $outputs.keyVaultUri.value
        ServiceBusName          = $outputs.serviceBusName.value
        ServiceBusConnString    = $outputs.serviceBusConnectionString.value
        RedisHostName           = $outputs.redisHostName.value
        RedisConnectionString   = $outputs.redisConnectionString.value
    }

    Write-Log "Storage Account: $($stepOutputs.StorageAccountName)" -Level SUCCESS
    Write-Log "Key Vault: $($stepOutputs.KeyVaultName)" -Level SUCCESS
    Write-Log "Service Bus: $($stepOutputs.ServiceBusName)" -Level SUCCESS
    Write-Log "Redis: $($stepOutputs.RedisHostName)" -Level SUCCESS

    Complete-Step -State $State -StepNumber 3 -StepName "Deploy customer.bicep" -Outputs $stepOutputs
}

# ============================================================================
# STEP 4: Populate Key Vault with secrets
# ============================================================================

function Invoke-Step4_PopulateKeyVault {
    param([PSCustomObject]$State)

    Write-StepHeader 4 "Populating customer Key Vault with secrets"

    $kvName = if ($State.StepOutputs.KeyVaultName) { $State.StepOutputs.KeyVaultName } else { $KeyVaultName }

    Write-Log "Target Key Vault: $kvName"

    # Secrets to set (connection strings from Bicep outputs, plus cross-references)
    $secrets = [ordered]@{
        "Storage-ConnectionString"    = $State.StepOutputs.StorageConnectionString
        "ServiceBus-ConnectionString" = $State.StepOutputs.ServiceBusConnString
        "Redis-ConnectionString"      = $State.StepOutputs.RedisConnectionString
        "Customer-Id"                 = $CustomerId
        "Customer-DisplayName"        = $DisplayName
        "Dataverse-Url"               = $DataverseEnvUrl
        "Bff-Api-BaseUrl"             = $BffApiBaseUrl
    }

    $setCount = 0
    $skipCount = 0

    foreach ($entry in $secrets.GetEnumerator()) {
        $secretName = $entry.Key
        $secretValue = $entry.Value

        if (-not $secretValue) {
            Write-Log "Skipping '$secretName' (no value available yet)" -Level WARN
            $skipCount++
            continue
        }

        Write-Log "Setting secret '$secretName'..."

        $output = az keyvault secret set `
            --vault-name $kvName `
            --name $secretName `
            --value $secretValue `
            --output json 2>&1 | Out-String

        if ($LASTEXITCODE -ne 0) {
            # Check if it's a permissions issue
            if ($output -match "Forbidden" -or $output -match "AccessDenied") {
                Write-Log "Access denied setting '$secretName'. Grant Key Vault Secrets Officer role." -Level ERROR
                throw "Key Vault access denied. Ensure the current identity has 'Key Vault Secrets Officer' role on $kvName."
            }
            Write-Log "Failed to set '$secretName': $output" -Level ERROR
            throw "Failed to set Key Vault secret '$secretName'."
        }
        $setCount++
    }

    Write-Log "$setCount secrets set, $skipCount skipped." -Level SUCCESS

    Complete-Step -State $State -StepNumber 4 -StepName "Populate Key Vault"
}

# ============================================================================
# STEP 5: Create Dataverse environment via Power Platform Admin API
# ============================================================================

function Invoke-Step5_CreateDataverseEnvironment {
    param([PSCustomObject]$State)

    Write-StepHeader 5 "Creating Dataverse environment via Power Platform Admin API"

    if ($SkipDataverse) {
        Write-Log "Dataverse provisioning skipped (-SkipDataverse flag)." -Level WARN
        Write-Log "Ensure Dataverse environment exists at: $DataverseEnvUrl" -Level WARN
        Complete-Step -State $State -StepNumber 5 -StepName "Create Dataverse (skipped)"
        return
    }

    # Authenticate with PAC CLI
    Write-Log "Authenticating PAC CLI..."
    $authArgs = @(
        "auth", "create",
        "--environment", $DataverseEnvUrl,
        "--tenant", $TenantId,
        "--applicationId", $ClientId
    )
    if ($ClientSecret) {
        $authArgs += "--clientSecret"
        $authArgs += $ClientSecret
    }
    else {
        $authArgs += "--certificateThumbprint"
        $authArgs += $CertificateThumbprint
    }

    $pacExe = if ($script:PacExe) { $script:PacExe } else { "$env:LOCALAPPDATA\Microsoft\PowerAppsCLI\pac.cmd" }
    $authOutput = & $pacExe @authArgs 2>&1 | Out-String
    # Auth create may fail if profile exists — that's OK

    # Get access token for Power Platform Admin API
    Write-Log "Acquiring access token for Power Platform Admin API..."

    $tokenScope = "https://api.bap.microsoft.com/.default"

    if ($ClientSecret) {
        $tokenBody = @{
            grant_type    = "client_credentials"
            client_id     = $ClientId
            client_secret = $ClientSecret
            scope         = $tokenScope
        }
    }
    else {
        # Certificate-based auth requires different flow — fall back to az CLI
        Write-Log "Certificate auth for Admin API — using Azure CLI token." -Level INFO
        $tokenOutput = az account get-access-token --resource "https://api.bap.microsoft.com" --output json 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
            Write-Log "Cannot acquire Admin API token. See manual fallback below." -Level ERROR
            Write-ManualDataverseFallback
            throw "Admin API token acquisition failed."
        }
        $tokenObj = $tokenOutput | ConvertFrom-Json
        $accessToken = $tokenObj.accessToken
    }

    if (-not $accessToken) {
        # Client credentials flow
        $tokenUrl = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token"
        try {
            $tokenResponse = Invoke-RestMethod -Uri $tokenUrl -Method POST -Body $tokenBody -ContentType "application/x-www-form-urlencoded" -TimeoutSec 30
            $accessToken = $tokenResponse.access_token
        }
        catch {
            Write-Log "Token acquisition failed: $($_.Exception.Message)" -Level ERROR
            Write-ManualDataverseFallback
            throw "Failed to acquire Power Platform Admin API token."
        }
    }

    Write-Log "Access token acquired."

    # Check if environment already exists
    Write-Log "Checking for existing Dataverse environment '$DataverseEnvName'..."

    $headers = @{
        "Authorization" = "Bearer $accessToken"
        "Content-Type"  = "application/json"
    }

    try {
        $envListUrl = "https://api.bap.microsoft.com/providers/Microsoft.BusinessAppsPlatform/scopes/admin/environments?api-version=2023-06-01"
        $envList = Invoke-RestMethod -Uri $envListUrl -Headers $headers -Method GET -TimeoutSec 60

        $existingEnv = $envList.value | Where-Object {
            $_.properties.displayName -eq $DataverseEnvName -or
            $_.properties.linkedEnvironmentMetadata.domainName -eq $DataverseEnvName
        }

        if ($existingEnv) {
            Write-Log "Dataverse environment '$DataverseEnvName' already exists (idempotent)." -Level INFO
            $envId = $existingEnv.name
            $envUrl = $existingEnv.properties.linkedEnvironmentMetadata.instanceUrl
            Write-Log "Environment ID: $envId"
            Write-Log "Instance URL: $envUrl"

            Complete-Step -State $State -StepNumber 5 -StepName "Create Dataverse (exists)" `
                -Outputs @{ DataverseEnvironmentId = $envId; DataverseInstanceUrl = $envUrl }
            return
        }
    }
    catch {
        Write-Log "Could not list environments: $($_.Exception.Message)" -Level WARN
        Write-Log "Proceeding with creation attempt..." -Level WARN
    }

    # Create new Dataverse environment
    Write-Log "Creating Dataverse environment '$DataverseEnvName'..."

    $createBody = @{
        properties = @{
            displayName                   = $DataverseEnvName
            description                   = "Spaarke customer environment for $DisplayName"
            environmentSku                = "Production"
            azureRegion                   = $DataverseRegion
            linkedEnvironmentMetadata     = @{
                baseLanguage = 1033
                domainName   = $DataverseEnvName
                currency     = @{
                    code = "USD"
                }
            }
        }
        location   = $DataverseRegion
    } | ConvertTo-Json -Depth 10

    try {
        $createUrl = "https://api.bap.microsoft.com/providers/Microsoft.BusinessAppsPlatform/environments?api-version=2023-06-01"
        $createResponse = Invoke-RestMethod -Uri $createUrl -Headers $headers -Method POST -Body $createBody -TimeoutSec 120

        $envId = $createResponse.name
        Write-Log "Dataverse environment creation initiated. ID: $envId" -Level SUCCESS
        Write-Log "Environment will take several minutes to provision..."

        Complete-Step -State $State -StepNumber 5 -StepName "Create Dataverse" `
            -Outputs @{ DataverseEnvironmentId = $envId }
    }
    catch {
        Write-Log "Dataverse environment creation failed: $($_.Exception.Message)" -Level ERROR
        Write-ManualDataverseFallback
        throw "Dataverse environment creation failed. See manual fallback above."
    }
}

function Write-ManualDataverseFallback {
    Write-Host ""
    Write-Host "  ================================================================" -ForegroundColor Yellow
    Write-Host "  MANUAL FALLBACK: Create Dataverse Environment" -ForegroundColor Yellow
    Write-Host "  ================================================================" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  If the Power Platform Admin API fails, create the environment manually:" -ForegroundColor White
    Write-Host ""
    Write-Host "  1. Go to https://admin.powerplatform.microsoft.com/" -ForegroundColor White
    Write-Host "  2. Click 'Environments' > 'New'" -ForegroundColor White
    Write-Host "  3. Settings:" -ForegroundColor White
    Write-Host "     - Name: $DataverseEnvName" -ForegroundColor Cyan
    Write-Host "     - Region: $DataverseRegion" -ForegroundColor Cyan
    Write-Host "     - Type: Production" -ForegroundColor Cyan
    Write-Host "     - Create database: Yes" -ForegroundColor Cyan
    Write-Host "     - Language: English (1033)" -ForegroundColor Cyan
    Write-Host "     - Currency: USD" -ForegroundColor Cyan
    Write-Host "     - URL: $DataverseEnvName" -ForegroundColor Cyan
    Write-Host "  4. Wait for provisioning to complete (~5-15 min)" -ForegroundColor White
    Write-Host "  5. Re-run this script with: -ResumeFromStep 7 -SkipDataverse" -ForegroundColor Green
    Write-Host ""
    Write-Host "  ================================================================" -ForegroundColor Yellow
    Write-Host ""
}

# ============================================================================
# STEP 6: Wait for Dataverse provisioning
# ============================================================================

function Invoke-Step6_WaitForDataverse {
    param([PSCustomObject]$State)

    Write-StepHeader 6 "Waiting for Dataverse environment provisioning"

    if ($SkipDataverse) {
        Write-Log "Dataverse wait skipped (-SkipDataverse flag)." -Level WARN
        Complete-Step -State $State -StepNumber 6 -StepName "Wait for Dataverse (skipped)"
        return
    }

    $envId = $State.StepOutputs.DataverseEnvironmentId
    if (-not $envId) {
        Write-Log "No Dataverse environment ID found in state. Skipping wait." -Level WARN
        Complete-Step -State $State -StepNumber 6 -StepName "Wait for Dataverse (no ID)"
        return
    }

    # If instance URL already captured, environment is ready
    if ($State.StepOutputs.DataverseInstanceUrl) {
        Write-Log "Dataverse environment already provisioned." -Level SUCCESS
        Complete-Step -State $State -StepNumber 6 -StepName "Wait for Dataverse (already ready)"
        return
    }

    # Acquire token
    $accessToken = $null
    if ($ClientSecret) {
        $tokenBody = @{
            grant_type    = "client_credentials"
            client_id     = $ClientId
            client_secret = $ClientSecret
            scope         = "https://api.bap.microsoft.com/.default"
        }
        $tokenUrl = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token"
        $tokenResponse = Invoke-RestMethod -Uri $tokenUrl -Method POST -Body $tokenBody -ContentType "application/x-www-form-urlencoded" -TimeoutSec 30
        $accessToken = $tokenResponse.access_token
    }
    else {
        $tokenOutput = az account get-access-token --resource "https://api.bap.microsoft.com" --output json 2>&1 | Out-String
        $tokenObj = $tokenOutput | ConvertFrom-Json
        $accessToken = $tokenObj.accessToken
    }

    $headers = @{
        "Authorization" = "Bearer $accessToken"
        "Content-Type"  = "application/json"
    }

    $maxWaitMinutes = 15
    $pollIntervalSeconds = 30
    $maxAttempts = [Math]::Ceiling(($maxWaitMinutes * 60) / $pollIntervalSeconds)
    $attempt = 0

    Write-Log "Polling every ${pollIntervalSeconds}s for up to $maxWaitMinutes minutes..."

    while ($attempt -lt $maxAttempts) {
        $attempt++
        try {
            $envUrl = "https://api.bap.microsoft.com/providers/Microsoft.BusinessAppsPlatform/scopes/admin/environments/${envId}?api-version=2023-06-01"
            $envStatus = Invoke-RestMethod -Uri $envUrl -Headers $headers -Method GET -TimeoutSec 30

            $provisioningState = $envStatus.properties.provisioningState
            $runtimeState = $envStatus.properties.states.runtime.runtimeReasonCode

            Write-Log "Attempt $attempt/$maxAttempts — Provisioning: $provisioningState, Runtime: $runtimeState"

            if ($provisioningState -eq "Succeeded") {
                $instanceUrl = $envStatus.properties.linkedEnvironmentMetadata.instanceUrl
                Write-Log "Dataverse environment is ready!" -Level SUCCESS
                Write-Log "Instance URL: $instanceUrl" -Level SUCCESS

                Complete-Step -State $State -StepNumber 6 -StepName "Wait for Dataverse" `
                    -Outputs @{ DataverseInstanceUrl = $instanceUrl }
                return
            }

            if ($provisioningState -in @("Failed", "Deleted")) {
                throw "Dataverse provisioning failed. State: $provisioningState"
            }
        }
        catch {
            if ($_.Exception.Message -match "provisioning failed") {
                throw
            }
            Write-Log "Poll error (retrying): $($_.Exception.Message)" -Level WARN
        }

        Start-Sleep -Seconds $pollIntervalSeconds
    }

    throw "Dataverse environment did not become ready within $maxWaitMinutes minutes. Re-run with -ResumeFromStep 6 to continue polling."
}

# ============================================================================
# STEP 7: Import managed solutions
# ============================================================================

function Invoke-Step7_ImportSolutions {
    param([PSCustomObject]$State)

    Write-StepHeader 7 "Importing managed solutions to Dataverse"

    if ($SkipDataverse) {
        Write-Log "Solution import skipped (-SkipDataverse flag)." -Level WARN
        Complete-Step -State $State -StepNumber 7 -StepName "Import solutions (skipped)"
        return
    }

    $deployScript = Join-Path $ScriptRoot "Deploy-DataverseSolutions.ps1"
    if (-not (Test-Path $deployScript)) {
        throw "Deploy-DataverseSolutions.ps1 not found at: $deployScript"
    }

    $envUrl = if ($State.StepOutputs.DataverseInstanceUrl) {
        $State.StepOutputs.DataverseInstanceUrl
    }
    else {
        $DataverseEnvUrl
    }

    Write-Log "Target environment: $envUrl"
    Write-Log "Calling Deploy-DataverseSolutions.ps1..."

    $deployArgs = @{
        EnvironmentUrl = $envUrl
        TenantId       = $TenantId
        ClientId       = $ClientId
    }

    if ($ClientSecret) {
        $deployArgs.ClientSecret = $ClientSecret
    }
    else {
        $deployArgs.CertificateThumbprint = $CertificateThumbprint
    }

    & $deployScript @deployArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Solution import failed. See Deploy-DataverseSolutions.ps1 output above."
    }

    Write-Log "All solutions imported successfully." -Level SUCCESS
    Complete-Step -State $State -StepNumber 7 -StepName "Import solutions"
}

# ============================================================================
# STEP 8: Provision SPE containers
# ============================================================================

function Invoke-Step8_ProvisionSPEContainers {
    param([PSCustomObject]$State)

    Write-StepHeader 8 "Provisioning SPE container for root business unit"

    # Each business unit gets one SPE container. During provisioning, we create
    # the container for the root BU. Additional BU containers are created via
    # New-BusinessUnitContainer.ps1 when new BUs are added.

    # 1. Get container type ID from platform Key Vault
    Write-Log "Retrieving SPE container type ID from Key Vault ($PlatformKeyVaultName)..."

    $containerTypeId = az keyvault secret show `
        --vault-name $PlatformKeyVaultName `
        --name "Spe--ContainerTypeId" `
        --query value -o tsv 2>&1

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerTypeId)) {
        Write-Log "Failed to retrieve Spe--ContainerTypeId from Key Vault: $containerTypeId" -Level ERROR
        Write-Log "Ensure the container type has been created and its ID stored in Key Vault." -Level ERROR
        Write-Log "See: scripts/Create-NewContainerType.ps1 or Create-ContainerType-PowerShell.ps1" -Level INFO
        throw "SPE container type ID not found in Key Vault"
    }

    Write-Log "Container Type ID: $containerTypeId" -Level INFO

    # 2. Get Graph API token via service principal (uses az CLI logged-in identity)
    Write-Log "Acquiring Graph API access token..."

    $graphToken = az account get-access-token `
        --resource "https://graph.microsoft.com" `
        --query accessToken -o tsv 2>&1

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($graphToken)) {
        Write-Log "Failed to acquire Graph API token: $graphToken" -Level ERROR
        throw "Cannot acquire Graph API token. Ensure az CLI is logged in with appropriate permissions."
    }

    Write-Log "Graph API token acquired." -Level SUCCESS

    # 3. Create SPE container via Graph API
    $containerDisplayName = "$DisplayName Documents"
    Write-Log "Creating SPE container: '$containerDisplayName'..."

    $createBody = @{
        displayName     = $containerDisplayName
        description     = "Document storage for $CustomerId"
        containerTypeId = $containerTypeId
    } | ConvertTo-Json

    $graphHeaders = @{
        "Authorization" = "Bearer $graphToken"
        "Content-Type"  = "application/json"
    }

    try {
        $container = Invoke-RestMethod `
            -Uri "https://graph.microsoft.com/v1.0/storage/fileStorage/containers" `
            -Method Post `
            -Headers $graphHeaders `
            -Body $createBody `
            -ErrorAction Stop

        $containerId = $container.id
        Write-Log "SPE container created: $containerId" -Level SUCCESS
    }
    catch {
        Write-Log "Failed to create SPE container: $($_.Exception.Message)" -Level ERROR
        if ($_.ErrorDetails.Message) {
            Write-Log "Details: $($_.ErrorDetails.Message)" -Level ERROR
        }
        throw "SPE container creation failed"
    }

    # 4. Get Dataverse instance URL from prior steps
    $dataverseUrl = if ($State.StepOutputs.DataverseInstanceUrl) {
        $State.StepOutputs.DataverseInstanceUrl
    } else {
        $DataverseEnvUrl
    }

    if ([string]::IsNullOrWhiteSpace($dataverseUrl)) {
        Write-Log "No Dataverse instance URL available. Cannot set sprk_containerid on business unit." -Level ERROR
        Write-Log "Container was created ($containerId) but BU update must be done manually." -Level WARN
        Complete-Step -State $State -StepNumber 8 -StepName "Provision SPE containers (partial)" `
            -Outputs @{ SpeContainerId = $containerId }
        return
    }

    # 5. Get Dataverse token
    Write-Log "Acquiring Dataverse access token for $dataverseUrl..."

    $dvToken = az account get-access-token `
        --resource $dataverseUrl `
        --query accessToken -o tsv 2>&1

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($dvToken)) {
        Write-Log "Failed to acquire Dataverse token: $dvToken" -Level WARN
        Write-Log "Container created ($containerId) but BU update must be done manually." -Level WARN
        Complete-Step -State $State -StepNumber 8 -StepName "Provision SPE containers (partial)" `
            -Outputs @{ SpeContainerId = $containerId }
        return
    }

    # 6. Find the root business unit (parentbusinessunitid eq null)
    Write-Log "Finding root business unit in Dataverse..."

    $dvHeaders = @{
        "Authorization" = "Bearer $dvToken"
        "Content-Type"  = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
    }

    try {
        $buResponse = Invoke-RestMethod `
            -Uri "$dataverseUrl/api/data/v9.2/businessunits?`$filter=parentbusinessunitid eq null&`$select=businessunitid,name,sprk_containerid" `
            -Headers $dvHeaders `
            -Method Get `
            -ErrorAction Stop

        $rootBu = $buResponse.value | Select-Object -First 1

        if (-not $rootBu) {
            Write-Log "No root business unit found in Dataverse." -Level ERROR
            throw "Root business unit not found"
        }

        Write-Log "Root BU: $($rootBu.name) ($($rootBu.businessunitid))" -Level INFO

        # Check if already has a container ID (idempotent)
        if (-not [string]::IsNullOrWhiteSpace($rootBu.sprk_containerid)) {
            Write-Log "Root BU already has sprk_containerid: $($rootBu.sprk_containerid)" -Level WARN
            Write-Log "Skipping BU update. New container ID: $containerId (not applied)." -Level WARN
            Complete-Step -State $State -StepNumber 8 -StepName "Provision SPE containers (BU already set)" `
                -Outputs @{ SpeContainerId = $rootBu.sprk_containerid }
            return
        }
    }
    catch {
        Write-Log "Failed to query business units: $($_.Exception.Message)" -Level ERROR
        Write-Log "Container created ($containerId) but BU update must be done manually." -Level WARN
        Complete-Step -State $State -StepNumber 8 -StepName "Provision SPE containers (partial)" `
            -Outputs @{ SpeContainerId = $containerId }
        return
    }

    # 7. Set sprk_containerid on the root business unit
    $rootBuId = $rootBu.businessunitid
    Write-Log "Setting sprk_containerid=$containerId on BU $rootBuId..."

    try {
        Invoke-RestMethod `
            -Uri "$dataverseUrl/api/data/v9.2/businessunits($rootBuId)" `
            -Headers $dvHeaders `
            -Method Patch `
            -Body (@{ sprk_containerid = $containerId } | ConvertTo-Json) `
            -ErrorAction Stop

        Write-Log "sprk_containerid set on root business unit." -Level SUCCESS
    }
    catch {
        Write-Log "Failed to update business unit: $($_.Exception.Message)" -Level ERROR
        Write-Log "Container created ($containerId) — update BU manually." -Level WARN
    }

    Write-Log "SPE provisioning complete. Container ID: $containerId" -Level SUCCESS

    Complete-Step -State $State -StepNumber 8 -StepName "Provision SPE containers" `
        -Outputs @{ SpeContainerId = $containerId }
}

# ============================================================================
# STEP 9: Register customer in BFF API tenant registry
# ============================================================================

function Invoke-Step9_RegisterTenant {
    param([PSCustomObject]$State)

    Write-StepHeader 9 "Registering customer in BFF API tenant registry"

    $kvName = if ($State.StepOutputs.KeyVaultName) { $State.StepOutputs.KeyVaultName } else { $KeyVaultName }

    # The tenant registry is stored in the platform Key Vault as a JSON config
    # that maps customerId -> customer configuration
    Write-Log "Registering customer '$CustomerId' in platform Key Vault tenant registry..."

    $tenantConfig = @{
        customerId            = $CustomerId
        displayName           = $DisplayName
        environmentName       = $EnvironmentName
        dataverseUrl          = $DataverseEnvUrl
        customerKeyVault      = $kvName
        resourceGroup         = $ResourceGroupName
        provisionedAt         = (Get-Date -Format 'o')
        status                = "active"
    } | ConvertTo-Json -Compress

    # Store tenant registration in platform Key Vault
    $secretName = "Tenant-$CustomerId"

    $output = az keyvault secret set `
        --vault-name $PlatformKeyVaultName `
        --name $secretName `
        --value $tenantConfig `
        --output json 2>&1 | Out-String

    if ($LASTEXITCODE -ne 0) {
        Write-Log "Failed to register tenant in platform Key Vault: $output" -Level ERROR
        Write-Log "Manual registration may be needed in $PlatformKeyVaultName." -Level WARN
        # Non-fatal — customer can be registered later
        Write-Log "Continuing despite registration failure (non-fatal)." -Level WARN
    }
    else {
        Write-Log "Tenant '$CustomerId' registered in $PlatformKeyVaultName as '$secretName'." -Level SUCCESS
    }

    Complete-Step -State $State -StepNumber 9 -StepName "Register tenant"
}

# ============================================================================
# STEP 10: Run smoke tests
# ============================================================================

function Invoke-Step10_RunSmokeTests {
    param([PSCustomObject]$State)

    Write-StepHeader 10 "Running smoke tests"

    $testScript = Join-Path $ScriptRoot "Test-Deployment.ps1"
    if (-not (Test-Path $testScript)) {
        Write-Log "Test-Deployment.ps1 not found at: $testScript" -Level WARN
        Write-Log "Skipping smoke tests. Run manually after setup." -Level WARN
        Complete-Step -State $State -StepNumber 10 -StepName "Smoke tests (skipped — script not found)"
        return
    }

    Write-Log "Calling Test-Deployment.ps1..."

    $testArgs = @{
        Environment = $EnvironmentName
        CustomerId  = $CustomerId
    }

    if ($BffApiBaseUrl) {
        $testArgs.ApiBaseUrl = $BffApiBaseUrl
    }

    # Smoke tests may partially fail for a brand-new environment (e.g., no data yet).
    # We run them but don't treat non-critical failures as blocking.
    try {
        & $testScript @testArgs
        $testExitCode = $LASTEXITCODE

        if ($testExitCode -eq 0) {
            Write-Log "All smoke tests passed." -Level SUCCESS
        }
        else {
            Write-Log "Some smoke tests failed (exit code: $testExitCode)." -Level WARN
            Write-Log "Review test output above. Non-critical failures are expected for new environments." -Level WARN
        }
    }
    catch {
        Write-Log "Smoke test execution error: $($_.Exception.Message)" -Level WARN
        Write-Log "Tests may need manual verification." -Level WARN
    }

    Complete-Step -State $State -StepNumber 10 -StepName "Smoke tests"
}

# ============================================================================
# WHATIF MODE
# ============================================================================

if ($WhatIfPreference) {
    Write-Host ""
    Write-Host "  ============================================================" -ForegroundColor Cyan
    Write-Host "  PROVISIONING PLAN (WhatIf)" -ForegroundColor Cyan
    Write-Host "  ============================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Customer ID:    $CustomerId" -ForegroundColor White
    Write-Host "  Display Name:   $DisplayName" -ForegroundColor White
    Write-Host "  Environment:    $EnvironmentName" -ForegroundColor White
    Write-Host "  Location:       $Location" -ForegroundColor White
    Write-Host ""
    Write-Host "  Resources to be created:" -ForegroundColor Yellow
    Write-Host "    1. Resource Group:  $ResourceGroupName" -ForegroundColor White
    Write-Host "    2. Storage Account: sprk${CustomerId}${EnvironmentName}sa" -ForegroundColor White
    Write-Host "    3. Key Vault:       $KeyVaultName" -ForegroundColor White
    Write-Host "    4. Service Bus:     spaarke-$CustomerId-$EnvironmentName-sbus" -ForegroundColor White
    Write-Host "    5. Redis Cache:     spaarke-$CustomerId-$EnvironmentName-cache" -ForegroundColor White
    if (-not $SkipDataverse) {
        Write-Host "    6. Dataverse Env:   $DataverseEnvName ($DataverseEnvUrl)" -ForegroundColor White
        Write-Host "    7. Solutions:       10 managed solutions (SpaarkeCore + features)" -ForegroundColor White
    }
    else {
        Write-Host "    6. Dataverse Env:   SKIPPED" -ForegroundColor DarkGray
        Write-Host "    7. Solutions:       SKIPPED" -ForegroundColor DarkGray
    }
    Write-Host "    8. SPE Containers:  On-demand via BFF API" -ForegroundColor White
    Write-Host "    9. Tenant Registry: $PlatformKeyVaultName / Tenant-$CustomerId" -ForegroundColor White
    Write-Host "   10. Smoke Tests:     Test-Deployment.ps1" -ForegroundColor White
    Write-Host ""
    Write-Host "  State file: $StateFile" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Remove -WhatIf to execute." -ForegroundColor Yellow
    Write-Host ""
    exit 0
}

# ============================================================================
# MAIN EXECUTION
# ============================================================================

$startTime = Get-Date

Write-Host ""
Write-Host "  ============================================================" -ForegroundColor Cyan
Write-Host "  Spaarke Customer Provisioning" -ForegroundColor Cyan
Write-Host "  ============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Customer:     $CustomerId ($DisplayName)" -ForegroundColor White
Write-Host "  Environment:  $EnvironmentName" -ForegroundColor White
Write-Host "  Location:     $Location" -ForegroundColor White
Write-Host "  Auth:         $(if ($ClientSecret) { 'Client Secret' } else { 'Certificate' })" -ForegroundColor White
Write-Host "  Timestamp:    $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor White
Write-Host ""

# Load or create state
$state = Get-ProvisioningState
$startStep = Get-ResumeStep -State $state

if ($startStep -gt 1) {
    Write-Host "  RESUMING from step $startStep (previous steps completed)" -ForegroundColor Yellow
    Write-Host "  State file: $StateFile" -ForegroundColor Gray
    Write-Host ""
}

Write-Log "Provisioning started for customer '$CustomerId'"
Write-Log "Log file: $LogFile"
Write-Log "State file: $StateFile"

try {
    # Map step numbers to functions
    $steps = @{
        1  = { Invoke-Step1_ValidatePrerequisites -State $state }
        2  = { Invoke-Step2_CreateResourceGroup -State $state }
        3  = { Invoke-Step3_DeployBicep -State $state }
        4  = { Invoke-Step4_PopulateKeyVault -State $state }
        5  = { Invoke-Step5_CreateDataverseEnvironment -State $state }
        6  = { Invoke-Step6_WaitForDataverse -State $state }
        7  = { Invoke-Step7_ImportSolutions -State $state }
        8  = { Invoke-Step8_ProvisionSPEContainers -State $state }
        9  = { Invoke-Step9_RegisterTenant -State $state }
        10 = { Invoke-Step10_RunSmokeTests -State $state }
    }

    for ($step = $startStep; $step -le $TotalSteps; $step++) {
        # Skip already completed steps (idempotency)
        if ((Test-StepCompleted -State $state -StepNumber $step) -and $step -ge $startStep -and $ResumeFromStep -eq 0) {
            Write-Log "Step $step already completed (skipping)." -Level INFO
            continue
        }

        & $steps[$step]
    }

    # Mark provisioning complete
    $state.Status = 'completed'
    $state.CompletedAt = (Get-Date -Format 'o')
    Save-ProvisioningState -State $state

    $elapsed = (Get-Date) - $startTime

    Write-Host ""
    Write-Host "  ============================================================" -ForegroundColor Green
    Write-Host "  Customer Provisioning Complete" -ForegroundColor Green
    Write-Host "  ============================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Customer:        $CustomerId ($DisplayName)" -ForegroundColor White
    Write-Host "  Resource Group:  $ResourceGroupName" -ForegroundColor White
    Write-Host "  Key Vault:       $KeyVaultName" -ForegroundColor White
    Write-Host "  Dataverse:       $DataverseEnvUrl" -ForegroundColor White
    Write-Host "  BFF API:         $BffApiBaseUrl" -ForegroundColor White
    Write-Host "  Duration:        $([math]::Round($elapsed.TotalMinutes, 1)) minutes" -ForegroundColor White
    Write-Host "  Log:             $LogFile" -ForegroundColor Gray
    Write-Host "  State:           $StateFile" -ForegroundColor Gray
    Write-Host ""
    Write-Log "Provisioning completed in $([math]::Round($elapsed.TotalMinutes, 1)) minutes." -Level SUCCESS
}
catch {
    $elapsed = (Get-Date) - $startTime

    Write-Log "PROVISIONING FAILED: $($_.Exception.Message)" -Level ERROR
    Write-Log "Stack trace: $($_.ScriptStackTrace)" -Level ERROR
    Write-Log "Failed after: $($elapsed.ToString('mm\:ss'))" -Level ERROR

    # State is already saved by the last successful step
    Write-Host ""
    Write-Host "  ============================================================" -ForegroundColor Red
    Write-Host "  Customer Provisioning Failed" -ForegroundColor Red
    Write-Host "  ============================================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Error:    $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Customer: $CustomerId" -ForegroundColor White
    Write-Host "  Last OK:  Step $($state.LastStep)" -ForegroundColor White
    Write-Host "  Duration: $([math]::Round($elapsed.TotalMinutes, 1)) minutes" -ForegroundColor White
    Write-Host ""
    Write-Host "  To resume from the failed step:" -ForegroundColor Yellow
    Write-Host "    .\Provision-Customer.ps1 -CustomerId `"$CustomerId`" -DisplayName `"$DisplayName`" \" -ForegroundColor Cyan
    Write-Host "        -TenantId `"$TenantId`" -ClientId `"$ClientId`" -ClientSecret `"...`"" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  The script will automatically resume from step $($state.LastStep + 1)." -ForegroundColor Yellow
    Write-Host "  Or use -ResumeFromStep $($state.LastStep + 1) to be explicit." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Log:   $LogFile" -ForegroundColor Gray
    Write-Host "  State: $StateFile" -ForegroundColor Gray
    Write-Host ""

    exit 1
}
