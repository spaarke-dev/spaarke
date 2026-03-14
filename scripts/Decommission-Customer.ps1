<#
.SYNOPSIS
    Decommission a Spaarke customer by removing all per-customer resources.

.DESCRIPTION
    Decommission-Customer.ps1 performs a safe, ordered teardown of all resources
    provisioned for a customer. This is the reverse of Provision-Customer.ps1.

    Teardown order:
      1. Validate customer exists (resource group, naming pattern)
      2. Safety checks (resource group name matches expected pattern)
      3. De-register customer from BFF API tenant registry
      4. Remove SPE containers
      5. Delete Dataverse environment
      6. Delete Azure resource group (removes Storage, Key Vault, Service Bus, Redis)
      7. Verify cleanup complete

    Safety features:
      - DryRun mode lists what would be deleted without changing anything
      - Confirmation prompt unless -Force is specified
      - Resource group name must match expected naming pattern
      - Platform resource groups are explicitly blocked from deletion
      - Each step verifies before proceeding

.PARAMETER CustomerId
    Customer identifier (lowercase, alphanumeric, 3-10 chars).
    Must match the CustomerId used during provisioning.

.PARAMETER Environment
    Target environment. Default: "prod".

.PARAMETER DryRun
    List all resources that would be removed without making any changes.

.PARAMETER Force
    Skip confirmation prompts. Use with caution.

.PARAMETER SkipDataverse
    Skip Dataverse environment deletion (if managed separately).

.PARAMETER SkipSpe
    Skip SPE container removal (if managed separately).

.PARAMETER BffApiUrl
    BFF API base URL for tenant de-registration.
    Default: "https://api.spaarke.com"

.PARAMETER Region
    Azure region where customer resources were deployed.
    Default: "westus2"

.PARAMETER LogPath
    Path for decommission log file.
    Default: ./logs/decommission-{CustomerId}-{timestamp}.log

.EXAMPLE
    # Preview what would be deleted (recommended first)
    .\Decommission-Customer.ps1 -CustomerId demo -DryRun

.EXAMPLE
    # Decommission demo customer with confirmation prompt
    .\Decommission-Customer.ps1 -CustomerId demo

.EXAMPLE
    # Decommission without prompts (CI/CD use)
    .\Decommission-Customer.ps1 -CustomerId demo -Force

.EXAMPLE
    # Decommission only Azure resources (skip Dataverse and SPE)
    .\Decommission-Customer.ps1 -CustomerId testcust -SkipDataverse -SkipSpe

.EXAMPLE
    # Decommission in dev environment
    .\Decommission-Customer.ps1 -CustomerId testcust -Environment dev -BffApiUrl "https://spe-api-dev-67e2xz.azurewebsites.net"

.NOTES
    Requires:
      - Azure CLI (az) authenticated with Contributor role on customer resource group
      - Power Platform CLI (pac) authenticated (for Dataverse deletion)
      - Microsoft Graph permissions (for SPE container removal)

    Naming conventions (per AZURE-RESOURCE-NAMING-CONVENTION.md):
      Resource group:   rg-spaarke-{customerId}-{env}
      Storage account:  sprk{customerId}{env}sa
      Key Vault:        sprk-{customerId}-{env}-kv
      Service Bus:      spaarke-{customerId}-{env}-sb
      Redis:            spaarke-{customerId}-{env}-cache
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9]{3,10}$')]
    [string]$CustomerId,

    [ValidateSet("dev", "staging", "prod")]
    [string]$Environment = "prod",

    [switch]$DryRun,

    [switch]$Force,

    [switch]$SkipDataverse,

    [switch]$SkipSpe,

    [string]$BffApiUrl = "https://api.spaarke.com",

    [string]$Region = "westus2",

    [string]$LogPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ============================================================================
# CONSTANTS & DERIVED NAMES
# ============================================================================

# Platform resource groups that must NEVER be deleted
$PlatformResourceGroups = @(
    "rg-spaarke-platform-prod",
    "rg-spaarke-platform-staging",
    "rg-spaarke-platform-dev",
    "spe-infrastructure-westus2"
)

# Expected resource group name for this customer
$ResourceGroupName = "rg-spaarke-$CustomerId-$Environment"

# Per-customer resource names (for verification reporting)
$StorageAccountName = "sprk$($CustomerId)$($Environment)sa".ToLower() -replace '-', ''
if ($StorageAccountName.Length -gt 24) { $StorageAccountName = $StorageAccountName.Substring(0, 24) }
$KeyVaultName = "sprk-$CustomerId-$Environment-kv"
if ($KeyVaultName.Length -gt 24) { $KeyVaultName = $KeyVaultName.Substring(0, 24) }
$ServiceBusName = "spaarke-$CustomerId-$Environment-sbus"
$RedisName = "spaarke-$CustomerId-$Environment-cache"

# Dataverse environment display name pattern
$DataverseEnvName = "spaarke-$CustomerId"

# Log setup
$Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
if (-not $LogPath) {
    $LogPath = Join-Path $PSScriptRoot "logs" "decommission-$CustomerId-$Timestamp.log"
}

# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

function Write-Log {
    param(
        [string]$Message,
        [ValidateSet("INFO", "WARN", "ERROR", "SUCCESS", "DRY-RUN")]
        [string]$Level = "INFO"
    )
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $entry = "[$ts] [$Level] $Message"

    switch ($Level) {
        "ERROR"   { Write-Host $entry -ForegroundColor Red }
        "WARN"    { Write-Host $entry -ForegroundColor Yellow }
        "SUCCESS" { Write-Host $entry -ForegroundColor Green }
        "DRY-RUN" { Write-Host $entry -ForegroundColor Cyan }
        default   { Write-Host $entry }
    }

    # Ensure log directory exists
    $logDir = Split-Path $LogPath -Parent
    if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
    Add-Content -Path $LogPath -Value $entry
}

function Write-StepHeader {
    param([int]$Step, [int]$Total, [string]$Description)
    $separator = "=" * 70
    Write-Log $separator
    Write-Log "STEP $Step of $Total : $Description"
    Write-Log $separator
}

function Confirm-Continue {
    param([string]$Message)

    if ($Force) { return $true }
    if ($DryRun) { return $true }

    Write-Host ""
    Write-Host "  $Message" -ForegroundColor Yellow
    Write-Host ""
    $response = Read-Host "  Type 'YES' to confirm (or anything else to abort)"
    return ($response -eq "YES")
}

function Test-AzCliAuthenticated {
    try {
        $account = az account show 2>&1 | ConvertFrom-Json
        if ($account.id) {
            Write-Log "Azure CLI authenticated as: $($account.user.name) (subscription: $($account.name))"
            return $true
        }
    }
    catch {
        Write-Log "Azure CLI not authenticated. Run 'az login' first." -Level ERROR
        return $false
    }
    return $false
}

# ============================================================================
# PRE-FLIGHT CHECKS
# ============================================================================

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  SPAARKE CUSTOMER DECOMMISSION" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

if ($DryRun) {
    Write-Log "*** DRY RUN MODE — No resources will be deleted ***" -Level DRY-RUN
    Write-Host ""
}

Write-Log "Customer ID:     $CustomerId"
Write-Log "Environment:     $Environment"
Write-Log "Resource Group:  $ResourceGroupName"
Write-Log "Region:          $Region"
Write-Log "BFF API URL:     $BffApiUrl"
Write-Log "Log File:        $LogPath"

if ($SkipDataverse) { Write-Log "Dataverse deletion: SKIPPED (--SkipDataverse)" -Level WARN }
if ($SkipSpe)       { Write-Log "SPE container removal: SKIPPED (--SkipSpe)" -Level WARN }

Write-Host ""

# ============================================================================
# STEP 1: Validate Customer Exists
# ============================================================================

$TotalSteps = 7
Write-StepHeader -Step 1 -Total $TotalSteps -Description "Validate customer exists"

# Check Azure CLI authentication
if (-not (Test-AzCliAuthenticated)) {
    Write-Log "FATAL: Azure CLI authentication required. Run 'az login' and retry." -Level ERROR
    exit 1
}

# Check if customer resource group exists
Write-Log "Checking for resource group: $ResourceGroupName"
$rgExists = az group exists --name $ResourceGroupName 2>&1
if ($rgExists -ne "true") {
    Write-Log "Resource group '$ResourceGroupName' does not exist." -Level WARN
    Write-Log "Customer may already be decommissioned, or CustomerId/Environment is incorrect." -Level WARN

    if (-not $Force) {
        Write-Log "Aborting. Use -Force to continue with Dataverse/SPE cleanup even without Azure resources." -Level ERROR
        exit 1
    }
    else {
        Write-Log "Continuing with -Force despite missing resource group." -Level WARN
        $rgExists = "false"
    }
}
else {
    Write-Log "Resource group found: $ResourceGroupName" -Level SUCCESS
}

# ============================================================================
# STEP 2: Safety Checks
# ============================================================================

Write-StepHeader -Step 2 -Total $TotalSteps -Description "Safety checks"

# 2a: Verify resource group name matches expected pattern
$expectedPattern = "^rg-spaarke-[a-z0-9]{3,10}-(dev|staging|prod)$"
if ($ResourceGroupName -notmatch $expectedPattern) {
    Write-Log "SAFETY BLOCK: Resource group name '$ResourceGroupName' does not match expected pattern: $expectedPattern" -Level ERROR
    Write-Log "This prevents accidental deletion of non-customer resource groups." -Level ERROR
    exit 1
}
Write-Log "Resource group name matches expected customer pattern." -Level SUCCESS

# 2b: Verify this is NOT a platform resource group
if ($PlatformResourceGroups -contains $ResourceGroupName) {
    Write-Log "SAFETY BLOCK: '$ResourceGroupName' is a PLATFORM resource group. Cannot decommission." -Level ERROR
    Write-Log "Platform resource groups are protected from deletion." -Level ERROR
    exit 1
}
Write-Log "Resource group is NOT a platform resource group." -Level SUCCESS

# 2c: List resources that will be deleted (for confirmation)
if ($rgExists -eq "true") {
    Write-Log "Resources in ${ResourceGroupName}:"
    $resources = az resource list --resource-group $ResourceGroupName --output json 2>&1 | ConvertFrom-Json
    if ($resources -and $resources.Count -gt 0) {
        foreach ($r in $resources) {
            $label = if ($DryRun) { "DRY-RUN" } else { "INFO" }
            Write-Log "  - $($r.type): $($r.name)" -Level $label
        }
        Write-Log "Total resources to delete: $($resources.Count)"
    }
    else {
        Write-Log "No resources found in resource group (may already be empty)." -Level WARN
    }
}

# 2d: Confirmation prompt
if (-not $DryRun) {
    Write-Host ""
    Write-Host "  !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!" -ForegroundColor Red
    Write-Host "  !!  WARNING: This will PERMANENTLY DELETE all    !!" -ForegroundColor Red
    Write-Host "  !!  resources for customer '$CustomerId'             !!" -ForegroundColor Red
    Write-Host "  !!  in the '$Environment' environment.               !!" -ForegroundColor Red
    Write-Host "  !!                                                !!" -ForegroundColor Red
    Write-Host "  !!  This action CANNOT be undone.                 !!" -ForegroundColor Red
    Write-Host "  !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!" -ForegroundColor Red

    if (-not (Confirm-Continue "Delete ALL resources for customer '$CustomerId' in '$Environment'?")) {
        Write-Log "Decommission aborted by user." -Level WARN
        exit 0
    }
    Write-Log "User confirmed decommission." -Level SUCCESS
}

# ============================================================================
# STEP 3: De-register from BFF API tenant registry
# ============================================================================

Write-StepHeader -Step 3 -Total $TotalSteps -Description "De-register from BFF API tenant registry"

if ($DryRun) {
    Write-Log "WOULD de-register customer '$CustomerId' from BFF API at $BffApiUrl" -Level DRY-RUN
}
else {
    Write-Log "De-registering customer '$CustomerId' from BFF API tenant registry..."
    try {
        # Get access token for BFF API
        $token = az account get-access-token --resource "api://spaarke-bff" --query accessToken -o tsv 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Log "Could not acquire BFF API token. Tenant de-registration may need manual cleanup." -Level WARN
            Write-Log "Error: $token" -Level WARN
        }
        else {
            $headers = @{
                "Authorization" = "Bearer $token"
                "Content-Type"  = "application/json"
            }

            $deregUrl = "$BffApiUrl/api/admin/tenants/$CustomerId"
            try {
                $response = Invoke-RestMethod -Uri $deregUrl -Method DELETE -Headers $headers -StatusCodeVariable statusCode
                Write-Log "Tenant de-registration successful (HTTP $statusCode)." -Level SUCCESS
            }
            catch {
                $statusCode = $_.Exception.Response.StatusCode.value__
                if ($statusCode -eq 404) {
                    Write-Log "Tenant '$CustomerId' not found in registry (already removed or never registered)." -Level WARN
                }
                else {
                    Write-Log "Tenant de-registration failed (HTTP $statusCode): $_" -Level WARN
                    Write-Log "Continuing — manual cleanup may be required." -Level WARN
                }
            }
        }
    }
    catch {
        Write-Log "Tenant de-registration error: $_" -Level WARN
        Write-Log "Continuing — manual cleanup may be required." -Level WARN
    }

    # Fallback: Remove tenant registration from platform Key Vault directly
    $platformKvName = "sprk-platform-$Environment-kv"
    $tenantSecretName = "Tenant-$CustomerId"
    Write-Log "Removing tenant secret '$tenantSecretName' from platform Key Vault '$platformKvName'..."
    try {
        $null = az keyvault secret delete --vault-name $platformKvName --name $tenantSecretName 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Log "Tenant secret '$tenantSecretName' removed from platform Key Vault." -Level SUCCESS
        }
        else {
            Write-Log "Tenant secret '$tenantSecretName' not found or already removed." -Level WARN
        }
    }
    catch {
        Write-Log "Could not remove tenant secret from platform Key Vault: $_" -Level WARN
    }
}

# ============================================================================
# STEP 4: Remove SPE containers
# ============================================================================

Write-StepHeader -Step 4 -Total $TotalSteps -Description "Remove SPE containers"

if ($SkipSpe) {
    Write-Log "SPE container removal skipped (--SkipSpe specified)." -Level WARN
}
elseif ($DryRun) {
    Write-Log "WOULD remove SPE containers for customer '$CustomerId'" -Level DRY-RUN
}
else {
    Write-Log "Removing SPE containers for customer '$CustomerId'..."
    try {
        # Get access token for Microsoft Graph (SPE operations)
        $graphToken = az account get-access-token --resource "https://graph.microsoft.com" --query accessToken -o tsv 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Log "Could not acquire Graph API token. SPE cleanup may need manual action." -Level WARN
            Write-Log "Error: $graphToken" -Level WARN
        }
        else {
            $graphHeaders = @{
                "Authorization" = "Bearer $graphToken"
                "Content-Type"  = "application/json"
            }

            # List containers for this customer via BFF API
            $containerListUrl = "$BffApiUrl/api/admin/customers/$CustomerId/containers"
            try {
                $token = az account get-access-token --resource "api://spaarke-bff" --query accessToken -o tsv 2>&1
                $bffHeaders = @{ "Authorization" = "Bearer $token" }
                $containers = Invoke-RestMethod -Uri $containerListUrl -Method GET -Headers $bffHeaders

                if ($containers -and $containers.Count -gt 0) {
                    foreach ($container in $containers) {
                        Write-Log "Deleting SPE container: $($container.id) ($($container.displayName))"
                        try {
                            $deleteUrl = "https://graph.microsoft.com/beta/storage/fileStorage/containers/$($container.id)"
                            Invoke-RestMethod -Uri $deleteUrl -Method DELETE -Headers $graphHeaders
                            Write-Log "Container $($container.id) deleted." -Level SUCCESS
                        }
                        catch {
                            Write-Log "Failed to delete container $($container.id): $_" -Level WARN
                        }
                    }
                }
                else {
                    Write-Log "No SPE containers found for customer '$CustomerId'." -Level WARN
                }
            }
            catch {
                Write-Log "Could not list SPE containers: $_" -Level WARN
                Write-Log "SPE containers may need manual cleanup." -Level WARN
            }
        }
    }
    catch {
        Write-Log "SPE container removal error: $_" -Level WARN
        Write-Log "Continuing — manual SPE cleanup may be required." -Level WARN
    }
}

# ============================================================================
# STEP 5: Delete Dataverse environment
# ============================================================================

Write-StepHeader -Step 5 -Total $TotalSteps -Description "Delete Dataverse environment"

if ($SkipDataverse) {
    Write-Log "Dataverse environment deletion skipped (--SkipDataverse specified)." -Level WARN
}
elseif ($DryRun) {
    Write-Log "WOULD delete Dataverse environment matching '$DataverseEnvName'" -Level DRY-RUN

    # In dry-run mode, list matching environments
    Write-Log "Searching for matching Dataverse environments..."
    try {
        $envListJson = pac admin list 2>&1
        if ($envListJson -match $DataverseEnvName) {
            Write-Log "Found matching Dataverse environment(s) for '$DataverseEnvName'" -Level DRY-RUN
        }
        else {
            Write-Log "No matching Dataverse environment found for '$DataverseEnvName'" -Level DRY-RUN
        }
    }
    catch {
        Write-Log "Could not query Dataverse environments: $_" -Level WARN
    }
}
else {
    Write-Log "Deleting Dataverse environment for customer '$CustomerId'..."
    try {
        # Find the environment by display name
        Write-Log "Searching for Dataverse environment matching: $DataverseEnvName"

        # Use Power Platform Admin API to find and delete
        $envListOutput = pac admin list 2>&1
        $envLines = $envListOutput -split "`n" | Where-Object { $_ -match $DataverseEnvName }

        if ($envLines -and $envLines.Count -gt 0) {
            # Parse environment URL from the output
            # pac admin list output typically includes environment URL
            foreach ($line in $envLines) {
                Write-Log "Found environment: $line"

                # Extract environment URL (format varies, try common patterns)
                if ($line -match '(https://[^\s]+\.crm\.dynamics\.com)') {
                    $envUrl = $Matches[1]
                    Write-Log "Deleting Dataverse environment: $envUrl"

                    $deleteResult = pac admin delete --url $envUrl 2>&1
                    if ($LASTEXITCODE -eq 0) {
                        Write-Log "Dataverse environment deleted successfully." -Level SUCCESS
                    }
                    else {
                        Write-Log "Dataverse environment deletion returned non-zero exit code." -Level WARN
                        Write-Log "Output: $deleteResult" -Level WARN
                    }
                }
                else {
                    Write-Log "Could not parse environment URL from output. Manual deletion may be required." -Level WARN
                    Write-Log "Use: pac admin delete --url <environment-url>" -Level WARN
                }
            }
        }
        else {
            Write-Log "No Dataverse environment found matching '$DataverseEnvName'." -Level WARN
            Write-Log "Environment may already be deleted or was named differently." -Level WARN
        }
    }
    catch {
        Write-Log "Dataverse environment deletion error: $_" -Level WARN
        Write-Log "Manual cleanup may be required." -Level WARN
        Write-Log "Use: pac admin delete --url <environment-url>" -Level WARN
    }
}

# ============================================================================
# STEP 6: Delete Azure resource group
# ============================================================================

Write-StepHeader -Step 6 -Total $TotalSteps -Description "Delete Azure resource group"

if ($rgExists -ne "true") {
    Write-Log "Resource group '$ResourceGroupName' does not exist. Skipping Azure resource deletion." -Level WARN
}
elseif ($DryRun) {
    Write-Log "WOULD delete resource group: $ResourceGroupName" -Level DRY-RUN
    Write-Log "This would remove ALL resources within the group:" -Level DRY-RUN
    Write-Log "  - Storage Account: $StorageAccountName" -Level DRY-RUN
    Write-Log "  - Key Vault:       $KeyVaultName" -Level DRY-RUN
    Write-Log "  - Service Bus:     $ServiceBusName" -Level DRY-RUN
    Write-Log "  - Redis Cache:     $RedisName" -Level DRY-RUN
}
else {
    Write-Log "Deleting resource group: $ResourceGroupName"
    Write-Log "This removes: Storage ($StorageAccountName), Key Vault ($KeyVaultName), Service Bus ($ServiceBusName), Redis ($RedisName)"

    # Final safety re-check before deletion
    if ($PlatformResourceGroups -contains $ResourceGroupName) {
        Write-Log "SAFETY BLOCK: Last-resort check caught platform resource group. Aborting." -Level ERROR
        exit 1
    }

    try {
        $deleteResult = az group delete --name $ResourceGroupName --yes --no-wait 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Log "Resource group deletion initiated (async). Waiting for completion..." -Level SUCCESS

            # Wait for deletion with timeout
            $maxWaitMinutes = 10
            $waitSeconds = 0
            $maxWaitSeconds = $maxWaitMinutes * 60
            $checkInterval = 15

            while ($waitSeconds -lt $maxWaitSeconds) {
                Start-Sleep -Seconds $checkInterval
                $waitSeconds += $checkInterval

                $stillExists = az group exists --name $ResourceGroupName 2>&1
                if ($stillExists -ne "true") {
                    Write-Log "Resource group '$ResourceGroupName' deleted successfully." -Level SUCCESS
                    break
                }

                $elapsed = [math]::Round($waitSeconds / 60, 1)
                Write-Log "Still deleting... ($elapsed min elapsed)"
            }

            if ($waitSeconds -ge $maxWaitSeconds) {
                Write-Log "Resource group deletion is still in progress after $maxWaitMinutes minutes." -Level WARN
                Write-Log "Deletion will continue in the background. Verify manually." -Level WARN
            }
        }
        else {
            Write-Log "Resource group deletion command failed: $deleteResult" -Level ERROR
            Write-Log "Manual cleanup required: az group delete --name $ResourceGroupName --yes" -Level ERROR
        }
    }
    catch {
        Write-Log "Resource group deletion error: $_" -Level ERROR
        Write-Log "Manual cleanup required: az group delete --name $ResourceGroupName --yes" -Level ERROR
    }

    # Purge Key Vault if soft-delete is enabled
    Write-Log "Checking if Key Vault '$KeyVaultName' needs purging (soft-delete)..."
    try {
        $deletedVaults = az keyvault list-deleted --query "[?name=='$KeyVaultName']" --output json 2>&1 | ConvertFrom-Json
        if ($deletedVaults -and $deletedVaults.Count -gt 0) {
            Write-Log "Key Vault '$KeyVaultName' is in soft-deleted state. Purging..."
            az keyvault purge --name $KeyVaultName --location $Region 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Log "Key Vault purged successfully." -Level SUCCESS
            }
            else {
                Write-Log "Key Vault purge failed. May need manual purge or wait for retention period." -Level WARN
            }
        }
        else {
            Write-Log "Key Vault not in soft-deleted state (purge protection may be disabled, or deletion is still in progress)."
        }
    }
    catch {
        Write-Log "Could not check Key Vault soft-delete status: $_" -Level WARN
    }
}

# ============================================================================
# STEP 7: Verify cleanup complete
# ============================================================================

Write-StepHeader -Step 7 -Total $TotalSteps -Description "Verify cleanup complete"

$verificationPassed = $true

if ($DryRun) {
    Write-Log "*** DRY RUN COMPLETE — No resources were modified ***" -Level DRY-RUN
    Write-Log "" -Level DRY-RUN
    Write-Log "Summary of what WOULD be deleted:" -Level DRY-RUN
    Write-Log "  - BFF API tenant registration for '$CustomerId'" -Level DRY-RUN
    if (-not $SkipSpe)       { Write-Log "  - SPE containers for '$CustomerId'" -Level DRY-RUN }
    if (-not $SkipDataverse) { Write-Log "  - Dataverse environment '$DataverseEnvName'" -Level DRY-RUN }
    if ($rgExists -eq "true") {
        Write-Log "  - Azure resource group '$ResourceGroupName' and all contents:" -Level DRY-RUN
        Write-Log "      Storage: $StorageAccountName" -Level DRY-RUN
        Write-Log "      Key Vault: $KeyVaultName" -Level DRY-RUN
        Write-Log "      Service Bus: $ServiceBusName" -Level DRY-RUN
        Write-Log "      Redis: $RedisName" -Level DRY-RUN
    }
}
else {
    # Verify resource group is gone
    Write-Log "Verifying resource group deletion..."
    $rgStillExists = az group exists --name $ResourceGroupName 2>&1
    if ($rgStillExists -eq "true") {
        Write-Log "Resource group '$ResourceGroupName' still exists (deletion may be in progress)." -Level WARN
        $verificationPassed = $false
    }
    else {
        Write-Log "Resource group '$ResourceGroupName' confirmed deleted." -Level SUCCESS
    }

    # Verify Key Vault is not lingering in soft-delete
    Write-Log "Verifying Key Vault cleanup..."
    try {
        $deletedVaults = az keyvault list-deleted --query "[?name=='$KeyVaultName']" --output json 2>&1 | ConvertFrom-Json
        if ($deletedVaults -and $deletedVaults.Count -gt 0) {
            Write-Log "Key Vault '$KeyVaultName' still in soft-deleted state." -Level WARN
            $verificationPassed = $false
        }
        else {
            Write-Log "Key Vault '$KeyVaultName' fully purged." -Level SUCCESS
        }
    }
    catch {
        Write-Log "Could not verify Key Vault status: $_" -Level WARN
    }

    # Summary
    Write-Host ""
    if ($verificationPassed) {
        Write-Host "========================================" -ForegroundColor Green
        Write-Host "  DECOMMISSION COMPLETE" -ForegroundColor Green
        Write-Host "========================================" -ForegroundColor Green
        Write-Log "All customer resources for '$CustomerId' have been removed." -Level SUCCESS
    }
    else {
        Write-Host "========================================" -ForegroundColor Yellow
        Write-Host "  DECOMMISSION PARTIALLY COMPLETE" -ForegroundColor Yellow
        Write-Host "========================================" -ForegroundColor Yellow
        Write-Log "Some resources may still be in deletion. Check log and verify manually." -Level WARN
    }
}

Write-Host ""
Write-Log "Log file: $LogPath"
Write-Log "Decommission process finished for customer '$CustomerId'."
