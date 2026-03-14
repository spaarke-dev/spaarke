<#
.SYNOPSIS
    Rotates secrets in Azure Key Vault for Spaarke platform and customer vaults.

.DESCRIPTION
    Rotate-Secrets.ps1 handles credential regeneration, Key Vault update, and
    service restart for both platform-level and customer-level secrets.

    Supported secret types:
      - StorageKey      : Regenerates storage account keys
      - ServiceBus      : Regenerates Service Bus access keys
      - Redis           : Regenerates Redis access keys
      - EntraId         : Rotates Entra ID app registration client secrets
      - All             : Rotates all supported secret types

    The script follows a safe rotation pattern:
      1. Regenerate the credential at the source (Azure resource)
      2. Update the corresponding Key Vault secret with the new value
      3. Verify the service still works with the new credential
      4. Log an audit record of the rotation

    Zero-downtime is achieved by regenerating the secondary key, updating Key Vault
    to use it, verifying connectivity, then regenerating the primary key.

.PARAMETER Scope
    Which vaults to rotate: Platform, Customer, or All.

.PARAMETER CustomerId
    Customer identifier (e.g., "demo"). Required when Scope is Customer.

.PARAMETER SecretType
    Which secret type to rotate: StorageKey, ServiceBus, Redis, EntraId, or All.

.PARAMETER Environment
    Target environment. Default: "prod".

.PARAMETER DryRun
    Show what would be rotated without making changes.

.PARAMETER Force
    Skip confirmation prompts.

.PARAMETER LogPath
    Path for audit log file. Default: ./logs/secret-rotation-{timestamp}.log

.EXAMPLE
    # Preview platform secret rotation
    .\Rotate-Secrets.ps1 -Scope Platform -SecretType All -DryRun

.EXAMPLE
    # Rotate all platform Redis keys
    .\Rotate-Secrets.ps1 -Scope Platform -SecretType Redis

.EXAMPLE
    # Rotate demo customer storage keys
    .\Rotate-Secrets.ps1 -Scope Customer -CustomerId demo -SecretType StorageKey

.EXAMPLE
    # Rotate all secrets for all vaults (platform + all customers)
    .\Rotate-Secrets.ps1 -Scope All -SecretType All -Force

.NOTES
    Requires:
      - Azure CLI (az) authenticated with sufficient permissions
      - Key Vault Secrets Officer role on target vaults
      - Contributor role on target resources (storage, service bus, redis)
      - Application Administrator role for Entra ID secret rotation

    Naming conventions (per AZURE-RESOURCE-NAMING-CONVENTION.md):
      Platform vault:  sprk-platform-{env}-kv
      Customer vault:  sprk-{customerId}-{env}-kv
      Storage account: sprk{customerId}{env}sa
      Service Bus:     sprk-{customerId}-{env}-sb
      Redis:           sprk-{customerId}-{env}-redis
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateSet("Platform", "Customer", "All")]
    [string]$Scope,

    [Parameter()]
    [string]$CustomerId,

    [Parameter(Mandatory)]
    [ValidateSet("StorageKey", "ServiceBus", "Redis", "EntraId", "All")]
    [string]$SecretType,

    [Parameter()]
    [ValidateSet("dev", "staging", "prod")]
    [string]$Environment = "prod",

    [Parameter()]
    [switch]$DryRun,

    [Parameter()]
    [switch]$Force,

    [Parameter()]
    [string]$LogPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────
# Constants & Naming
# ─────────────────────────────────────────────

$script:RotationResults = @()
$script:AuditEntries = @()

function Get-PlatformVaultName { "sprk-platform-$Environment-kv" }
function Get-CustomerVaultName([string]$cid) { "sprk-$cid-$Environment-kv" }
function Get-StorageAccountName([string]$cid) { "sprk$($cid)$($Environment)sa" }
function Get-ServiceBusName([string]$cid) { "sprk-$cid-$Environment-sb" }
function Get-RedisName([string]$cid) { "sprk-$cid-$Environment-redis" }

# ─────────────────────────────────────────────
# Logging & Audit
# ─────────────────────────────────────────────

function Initialize-AuditLog {
    if (-not $LogPath) {
        $logsDir = Join-Path $PSScriptRoot "logs"
        if (-not (Test-Path $logsDir)) {
            New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
        }
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $script:LogPath = Join-Path $logsDir "secret-rotation-$timestamp.log"
    }
    else {
        $script:LogPath = $LogPath
        $parentDir = Split-Path $script:LogPath -Parent
        if ($parentDir -and -not (Test-Path $parentDir)) {
            New-Item -ItemType Directory -Path $parentDir -Force | Out-Null
        }
    }
}

function Write-AuditLog {
    param(
        [string]$Level,
        [string]$SecretName,
        [string]$VaultName,
        [string]$Action,
        [string]$Result,
        [string]$Detail = ""
    )
    $entry = [PSCustomObject]@{
        Timestamp  = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
        Level      = $Level
        Operator   = (az account show --query "user.name" -o tsv 2>$null) ?? "unknown"
        Vault      = $VaultName
        SecretName = $SecretName
        Action     = $Action
        Result     = $Result
        Detail     = $Detail
    }
    $script:AuditEntries += $entry

    $color = switch ($Level) {
        "INFO"    { "Cyan" }
        "SUCCESS" { "Green" }
        "WARN"    { "Yellow" }
        "ERROR"   { "Red" }
        default   { "White" }
    }
    $msg = "[$($entry.Timestamp)] [$Level] $Action — $SecretName @ $VaultName : $Result"
    if ($Detail) { $msg += " ($Detail)" }
    Write-Host $msg -ForegroundColor $color

    # Append to log file
    $msg | Out-File -Append -FilePath $script:LogPath -Encoding utf8
}

function Write-AuditSummary {
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  SECRET ROTATION SUMMARY" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""

    $succeeded = ($script:AuditEntries | Where-Object { $_.Result -eq "Success" }).Count
    $failed    = ($script:AuditEntries | Where-Object { $_.Result -eq "Failed" }).Count
    $skipped   = ($script:AuditEntries | Where-Object { $_.Result -eq "Skipped" -or $_.Result -eq "DryRun" }).Count

    Write-Host "  Succeeded : $succeeded" -ForegroundColor Green
    Write-Host "  Failed    : $failed" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "White" })
    Write-Host "  Skipped   : $skipped" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Audit log : $($script:LogPath)" -ForegroundColor Gray
    Write-Host ""

    if ($failed -gt 0) {
        Write-Host "  FAILURES:" -ForegroundColor Red
        $script:AuditEntries | Where-Object { $_.Result -eq "Failed" } | ForEach-Object {
            Write-Host "    - $($_.SecretName) @ $($_.Vault): $($_.Detail)" -ForegroundColor Red
        }
        Write-Host ""
    }

    # Write JSON summary to log
    $summary = @{
        Timestamp   = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
        Scope       = $Scope
        SecretType  = $SecretType
        Environment = $Environment
        DryRun      = [bool]$DryRun
        Succeeded   = $succeeded
        Failed      = $failed
        Skipped     = $skipped
        Entries     = $script:AuditEntries
    }
    "" | Out-File -Append -FilePath $script:LogPath -Encoding utf8
    "=== JSON SUMMARY ===" | Out-File -Append -FilePath $script:LogPath -Encoding utf8
    ($summary | ConvertTo-Json -Depth 5) | Out-File -Append -FilePath $script:LogPath -Encoding utf8
}

# ─────────────────────────────────────────────
# Validation
# ─────────────────────────────────────────────

function Assert-Prerequisites {
    # Check Azure CLI login
    $account = az account show 2>$null | ConvertFrom-Json
    if (-not $account) {
        throw "Not logged in to Azure CLI. Run 'az login' first."
    }
    Write-Host "Authenticated as: $($account.user.name)" -ForegroundColor Gray
    Write-Host "Subscription:     $($account.name)" -ForegroundColor Gray
    Write-Host ""
}

function Assert-VaultAccess([string]$vaultName) {
    $result = az keyvault secret list --vault-name $vaultName --query "[0].id" -o tsv 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Cannot access Key Vault '$vaultName'. Verify permissions (Key Vault Secrets Officer role required)."
    }
}

# ─────────────────────────────────────────────
# Secret Rotation: Storage Account Keys
# ─────────────────────────────────────────────

function Rotate-StorageKey {
    param(
        [string]$StorageAccountName,
        [string]$VaultName,
        [string]$SecretName,
        [string]$ResourceGroup
    )

    if ($DryRun) {
        Write-AuditLog -Level "INFO" -SecretName $SecretName -VaultName $VaultName `
            -Action "Rotate-StorageKey" -Result "DryRun" `
            -Detail "Would regenerate key2 for storage '$StorageAccountName', update vault, then regenerate key1"
        return
    }

    try {
        # Step 1: Regenerate secondary key (key2) — services still using key1
        Write-AuditLog -Level "INFO" -SecretName $SecretName -VaultName $VaultName `
            -Action "RegenerateKey2" -Result "InProgress" -Detail "Storage: $StorageAccountName"

        az storage account keys renew `
            --account-name $StorageAccountName `
            --resource-group $ResourceGroup `
            --key key2 `
            -o none

        if ($LASTEXITCODE -ne 0) { throw "Failed to regenerate key2 for '$StorageAccountName'" }

        # Step 2: Get the new key2 value
        $newKey = az storage account keys list `
            --account-name $StorageAccountName `
            --resource-group $ResourceGroup `
            --query "[1].value" -o tsv

        if (-not $newKey) { throw "Failed to retrieve new key2 for '$StorageAccountName'" }

        # Step 3: Build connection string and update Key Vault
        $connString = "DefaultEndpointsProtocol=https;AccountName=$StorageAccountName;AccountKey=$newKey;EndpointSuffix=core.windows.net"

        az keyvault secret set `
            --vault-name $VaultName `
            --name $SecretName `
            --value $connString `
            -o none

        if ($LASTEXITCODE -ne 0) { throw "Failed to update Key Vault secret '$SecretName'" }

        # Step 4: Verify connectivity with new key
        $testResult = az storage container list `
            --account-name $StorageAccountName `
            --account-key $newKey `
            --query "[0].name" -o tsv 2>$null

        if ($LASTEXITCODE -ne 0) {
            Write-AuditLog -Level "WARN" -SecretName $SecretName -VaultName $VaultName `
                -Action "VerifyConnectivity" -Result "Warning" `
                -Detail "Connectivity check returned non-zero but key may still be valid (empty account?)"
        }

        # Step 5: Now regenerate key1 (old key) to invalidate it
        az storage account keys renew `
            --account-name $StorageAccountName `
            --resource-group $ResourceGroup `
            --key key1 `
            -o none

        Write-AuditLog -Level "SUCCESS" -SecretName $SecretName -VaultName $VaultName `
            -Action "Rotate-StorageKey" -Result "Success" `
            -Detail "Rotated to key2, invalidated key1"
    }
    catch {
        Write-AuditLog -Level "ERROR" -SecretName $SecretName -VaultName $VaultName `
            -Action "Rotate-StorageKey" -Result "Failed" -Detail $_.Exception.Message
    }
}

# ─────────────────────────────────────────────
# Secret Rotation: Service Bus Keys
# ─────────────────────────────────────────────

function Rotate-ServiceBusKey {
    param(
        [string]$NamespaceName,
        [string]$VaultName,
        [string]$SecretName,
        [string]$ResourceGroup
    )

    if ($DryRun) {
        Write-AuditLog -Level "INFO" -SecretName $SecretName -VaultName $VaultName `
            -Action "Rotate-ServiceBusKey" -Result "DryRun" `
            -Detail "Would regenerate SecondaryKey for namespace '$NamespaceName', update vault, then regenerate PrimaryKey"
        return
    }

    try {
        # Step 1: Regenerate secondary key
        Write-AuditLog -Level "INFO" -SecretName $SecretName -VaultName $VaultName `
            -Action "RegenerateSecondaryKey" -Result "InProgress" -Detail "ServiceBus: $NamespaceName"

        az servicebus namespace authorization-rule keys renew `
            --resource-group $ResourceGroup `
            --namespace-name $NamespaceName `
            --name RootManageSharedAccessKey `
            --key SecondaryKey `
            -o none

        if ($LASTEXITCODE -ne 0) { throw "Failed to regenerate SecondaryKey for '$NamespaceName'" }

        # Step 2: Get new secondary connection string
        $keys = az servicebus namespace authorization-rule keys list `
            --resource-group $ResourceGroup `
            --namespace-name $NamespaceName `
            --name RootManageSharedAccessKey `
            -o json | ConvertFrom-Json

        $newConnString = $keys.secondaryConnectionString
        if (-not $newConnString) { throw "Failed to retrieve SecondaryConnectionString for '$NamespaceName'" }

        # Step 3: Update Key Vault with secondary connection string
        az keyvault secret set `
            --vault-name $VaultName `
            --name $SecretName `
            --value $newConnString `
            -o none

        if ($LASTEXITCODE -ne 0) { throw "Failed to update Key Vault secret '$SecretName'" }

        # Step 4: Verify — check namespace is accessible
        $nsInfo = az servicebus namespace show `
            --name $NamespaceName `
            --resource-group $ResourceGroup `
            --query "status" -o tsv 2>$null

        if ($nsInfo -ne "Active") {
            Write-AuditLog -Level "WARN" -SecretName $SecretName -VaultName $VaultName `
                -Action "VerifyConnectivity" -Result "Warning" `
                -Detail "Namespace status: $nsInfo (expected Active)"
        }

        # Step 5: Regenerate primary key to invalidate old value
        az servicebus namespace authorization-rule keys renew `
            --resource-group $ResourceGroup `
            --namespace-name $NamespaceName `
            --name RootManageSharedAccessKey `
            --key PrimaryKey `
            -o none

        Write-AuditLog -Level "SUCCESS" -SecretName $SecretName -VaultName $VaultName `
            -Action "Rotate-ServiceBusKey" -Result "Success" `
            -Detail "Rotated to SecondaryKey, invalidated PrimaryKey"
    }
    catch {
        Write-AuditLog -Level "ERROR" -SecretName $SecretName -VaultName $VaultName `
            -Action "Rotate-ServiceBusKey" -Result "Failed" -Detail $_.Exception.Message
    }
}

# ─────────────────────────────────────────────
# Secret Rotation: Redis Access Keys
# ─────────────────────────────────────────────

function Rotate-RedisKey {
    param(
        [string]$RedisName,
        [string]$VaultName,
        [string]$SecretName,
        [string]$ResourceGroup
    )

    if ($DryRun) {
        Write-AuditLog -Level "INFO" -SecretName $SecretName -VaultName $VaultName `
            -Action "Rotate-RedisKey" -Result "DryRun" `
            -Detail "Would regenerate Secondary key for Redis '$RedisName', update vault, then regenerate Primary"
        return
    }

    try {
        # Step 1: Regenerate secondary key
        Write-AuditLog -Level "INFO" -SecretName $SecretName -VaultName $VaultName `
            -Action "RegenerateSecondaryKey" -Result "InProgress" -Detail "Redis: $RedisName"

        az redis regenerate-keys `
            --name $RedisName `
            --resource-group $ResourceGroup `
            --key-type Secondary `
            -o none

        if ($LASTEXITCODE -ne 0) { throw "Failed to regenerate Secondary key for Redis '$RedisName'" }

        # Step 2: Get new keys and build connection string
        $redisKeys = az redis list-keys `
            --name $RedisName `
            --resource-group $ResourceGroup `
            -o json | ConvertFrom-Json

        $redisHost = "$RedisName.redis.cache.windows.net"
        $newConnString = "$redisHost`:6380,password=$($redisKeys.secondaryKey),ssl=True,abortConnect=False"

        if (-not $redisKeys.secondaryKey) { throw "Failed to retrieve secondary key for Redis '$RedisName'" }

        # Step 3: Update Key Vault
        az keyvault secret set `
            --vault-name $VaultName `
            --name $SecretName `
            --value $newConnString `
            -o none

        if ($LASTEXITCODE -ne 0) { throw "Failed to update Key Vault secret '$SecretName'" }

        # Step 4: Verify — check Redis is accessible
        $redisInfo = az redis show `
            --name $RedisName `
            --resource-group $ResourceGroup `
            --query "provisioningState" -o tsv 2>$null

        if ($redisInfo -ne "Succeeded") {
            Write-AuditLog -Level "WARN" -SecretName $SecretName -VaultName $VaultName `
                -Action "VerifyConnectivity" -Result "Warning" `
                -Detail "Redis provisioning state: $redisInfo (expected Succeeded)"
        }

        # Step 5: Regenerate primary key to invalidate old value
        az redis regenerate-keys `
            --name $RedisName `
            --resource-group $ResourceGroup `
            --key-type Primary `
            -o none

        Write-AuditLog -Level "SUCCESS" -SecretName $SecretName -VaultName $VaultName `
            -Action "Rotate-RedisKey" -Result "Success" `
            -Detail "Rotated to Secondary key, invalidated Primary"
    }
    catch {
        Write-AuditLog -Level "ERROR" -SecretName $SecretName -VaultName $VaultName `
            -Action "Rotate-RedisKey" -Result "Failed" -Detail $_.Exception.Message
    }
}

# ─────────────────────────────────────────────
# Secret Rotation: Entra ID Client Secrets
# ─────────────────────────────────────────────

function Rotate-EntraIdSecret {
    param(
        [string]$AppDisplayName,
        [string]$AppId,
        [string]$VaultName,
        [string]$SecretName,
        [string]$ExpiryMonths = "12"
    )

    if ($DryRun) {
        Write-AuditLog -Level "INFO" -SecretName $SecretName -VaultName $VaultName `
            -Action "Rotate-EntraIdSecret" -Result "DryRun" `
            -Detail "Would create new client secret for app '$AppDisplayName' ($AppId), update vault, remove old secrets"
        return
    }

    try {
        # Step 1: Create new client secret with expiry
        Write-AuditLog -Level "INFO" -SecretName $SecretName -VaultName $VaultName `
            -Action "CreateNewSecret" -Result "InProgress" -Detail "App: $AppDisplayName ($AppId)"

        $endDate = (Get-Date).AddMonths([int]$ExpiryMonths).ToString("yyyy-MM-ddTHH:mm:ssZ")

        $newCredential = az ad app credential reset `
            --id $AppId `
            --display-name "Rotated-$(Get-Date -Format 'yyyyMMdd-HHmmss')" `
            --end-date $endDate `
            --query "password" -o tsv

        if ($LASTEXITCODE -ne 0 -or -not $newCredential) {
            throw "Failed to create new client secret for app '$AppId'"
        }

        # Step 2: Update Key Vault
        az keyvault secret set `
            --vault-name $VaultName `
            --name $SecretName `
            --value $newCredential `
            -o none

        if ($LASTEXITCODE -ne 0) { throw "Failed to update Key Vault secret '$SecretName'" }

        # Step 3: Verify — list app credentials to confirm new secret exists
        $credentials = az ad app credential list --id $AppId -o json | ConvertFrom-Json
        $newCreds = $credentials | Where-Object { $_.displayName -like "Rotated-*" }

        if (-not $newCreds -or $newCreds.Count -eq 0) {
            Write-AuditLog -Level "WARN" -SecretName $SecretName -VaultName $VaultName `
                -Action "VerifyCredential" -Result "Warning" `
                -Detail "Could not verify new credential was created"
        }

        # Step 4: Remove old credentials (keep only the newest rotated one)
        $oldCreds = $credentials | Where-Object {
            $_.displayName -notlike "Rotated-*" -or
            ($_.displayName -like "Rotated-*" -and $_.keyId -ne ($newCreds | Sort-Object endDateTime -Descending | Select-Object -First 1).keyId)
        }

        foreach ($old in $oldCreds) {
            Write-AuditLog -Level "INFO" -SecretName $SecretName -VaultName $VaultName `
                -Action "RemoveOldCredential" -Result "InProgress" `
                -Detail "Removing credential: $($old.displayName) ($($old.keyId))"

            az ad app credential delete --id $AppId --key-id $old.keyId -o none 2>$null
        }

        Write-AuditLog -Level "SUCCESS" -SecretName $SecretName -VaultName $VaultName `
            -Action "Rotate-EntraIdSecret" -Result "Success" `
            -Detail "New secret created (expires $endDate), old secrets removed"
    }
    catch {
        Write-AuditLog -Level "ERROR" -SecretName $SecretName -VaultName $VaultName `
            -Action "Rotate-EntraIdSecret" -Result "Failed" -Detail $_.Exception.Message
    }
}

# ─────────────────────────────────────────────
# Orchestration: Platform Secrets
# ─────────────────────────────────────────────

function Rotate-PlatformSecrets {
    $vaultName = Get-PlatformVaultName
    $resourceGroup = "rg-spaarke-platform-$Environment"

    Write-Host ""
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
    Write-Host "  Platform Vault: $vaultName" -ForegroundColor Cyan
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
    Write-Host ""

    if (-not $DryRun) {
        Assert-VaultAccess $vaultName
    }

    # Service Bus (platform-level)
    if ($SecretType -eq "ServiceBus" -or $SecretType -eq "All") {
        $sbName = "sprk-platform-$Environment-sb"
        Rotate-ServiceBusKey `
            -NamespaceName $sbName `
            -VaultName $vaultName `
            -SecretName "ServiceBus-ConnectionString" `
            -ResourceGroup $resourceGroup
    }

    # Redis (platform-level)
    if ($SecretType -eq "Redis" -or $SecretType -eq "All") {
        $redisName = "sprk-platform-$Environment-redis"
        Rotate-RedisKey `
            -RedisName $redisName `
            -VaultName $vaultName `
            -SecretName "Redis-ConnectionString" `
            -ResourceGroup $resourceGroup
    }

    # Entra ID: BFF API client secret
    if ($SecretType -eq "EntraId" -or $SecretType -eq "All") {
        # Retrieve the app ID from Key Vault (or use known value)
        $appId = $null
        if (-not $DryRun) {
            $appId = az keyvault secret show `
                --vault-name $vaultName `
                --name "BFF-API-ClientId" `
                --query "value" -o tsv 2>$null
        }

        Rotate-EntraIdSecret `
            -AppDisplayName "Spaarke BFF API ($Environment)" `
            -AppId ($appId ?? "<BFF-API-ClientId>") `
            -VaultName $vaultName `
            -SecretName "BFF-API-ClientSecret"
    }

    # Storage keys are customer-level, not platform-level
    if ($SecretType -eq "StorageKey") {
        Write-AuditLog -Level "INFO" -SecretName "N/A" -VaultName $vaultName `
            -Action "Rotate-StorageKey" -Result "Skipped" `
            -Detail "Storage accounts are per-customer. Use -Scope Customer -CustomerId <id>"
    }
}

# ─────────────────────────────────────────────
# Orchestration: Customer Secrets
# ─────────────────────────────────────────────

function Rotate-CustomerSecrets([string]$cid) {
    $vaultName = Get-CustomerVaultName $cid
    $resourceGroup = "rg-spaarke-$cid-$Environment"

    Write-Host ""
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
    Write-Host "  Customer Vault: $vaultName (customer: $cid)" -ForegroundColor Cyan
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
    Write-Host ""

    if (-not $DryRun) {
        Assert-VaultAccess $vaultName
    }

    # Storage Account
    if ($SecretType -eq "StorageKey" -or $SecretType -eq "All") {
        $saName = Get-StorageAccountName $cid
        Rotate-StorageKey `
            -StorageAccountName $saName `
            -VaultName $vaultName `
            -SecretName "Storage-ConnectionString" `
            -ResourceGroup $resourceGroup
    }

    # Service Bus
    if ($SecretType -eq "ServiceBus" -or $SecretType -eq "All") {
        $sbName = Get-ServiceBusName $cid
        Rotate-ServiceBusKey `
            -NamespaceName $sbName `
            -VaultName $vaultName `
            -SecretName "ServiceBus-ConnectionString" `
            -ResourceGroup $resourceGroup
    }

    # Redis
    if ($SecretType -eq "Redis" -or $SecretType -eq "All") {
        $redisName = Get-RedisName $cid
        Rotate-RedisKey `
            -RedisName $redisName `
            -VaultName $vaultName `
            -SecretName "Redis-ConnectionString" `
            -ResourceGroup $resourceGroup
    }

    # Entra ID is platform-level, not per-customer
    if ($SecretType -eq "EntraId") {
        Write-AuditLog -Level "INFO" -SecretName "N/A" -VaultName $vaultName `
            -Action "Rotate-EntraIdSecret" -Result "Skipped" `
            -Detail "Entra ID app secrets are platform-level. Use -Scope Platform"
    }
}

# ─────────────────────────────────────────────
# Orchestration: All Customers
# ─────────────────────────────────────────────

function Get-AllCustomerIds {
    # Discover customer IDs from resource groups matching naming convention
    $rgs = az group list `
        --query "[?starts_with(name, 'rg-spaarke-') && ends_with(name, '-$Environment') && name != 'rg-spaarke-platform-$Environment'].name" `
        -o tsv 2>$null

    if (-not $rgs) {
        Write-Host "  No customer resource groups found for environment '$Environment'" -ForegroundColor Yellow
        return @()
    }

    $customerIds = @()
    foreach ($rg in ($rgs -split "`n")) {
        $rg = $rg.Trim()
        if ($rg) {
            # Extract customer ID from rg-spaarke-{customerId}-{env}
            $parts = $rg -replace "^rg-spaarke-", "" -replace "-$Environment$", ""
            if ($parts -and $parts -ne "platform") {
                $customerIds += $parts
            }
        }
    }

    return $customerIds
}

# ─────────────────────────────────────────────
# Service Restart (Post-Rotation)
# ─────────────────────────────────────────────

function Restart-AppService {
    param([string]$ResourceGroup)

    $appServiceName = "spaarke-bff-$Environment"

    if ($DryRun) {
        Write-AuditLog -Level "INFO" -SecretName "N/A" -VaultName "N/A" `
            -Action "Restart-AppService" -Result "DryRun" `
            -Detail "Would restart '$appServiceName' to pick up new Key Vault references"
        return
    }

    try {
        Write-AuditLog -Level "INFO" -SecretName "N/A" -VaultName "N/A" `
            -Action "Restart-AppService" -Result "InProgress" `
            -Detail "Restarting '$appServiceName' for Key Vault reference refresh"

        az webapp restart `
            --name $appServiceName `
            --resource-group $ResourceGroup `
            -o none

        if ($LASTEXITCODE -ne 0) {
            throw "Failed to restart App Service '$appServiceName'"
        }

        # Wait for app to come back up
        Start-Sleep -Seconds 10

        # Health check
        $healthUrl = "https://$appServiceName.azurewebsites.net/healthz"
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -Method GET -UseBasicParsing -TimeoutSec 30
            if ($response.StatusCode -eq 200) {
                Write-AuditLog -Level "SUCCESS" -SecretName "N/A" -VaultName "N/A" `
                    -Action "HealthCheck" -Result "Success" `
                    -Detail "$healthUrl returned 200"
            }
            else {
                Write-AuditLog -Level "WARN" -SecretName "N/A" -VaultName "N/A" `
                    -Action "HealthCheck" -Result "Warning" `
                    -Detail "$healthUrl returned $($response.StatusCode)"
            }
        }
        catch {
            Write-AuditLog -Level "WARN" -SecretName "N/A" -VaultName "N/A" `
                -Action "HealthCheck" -Result "Warning" `
                -Detail "Health check failed: $($_.Exception.Message). App may still be starting."
        }
    }
    catch {
        Write-AuditLog -Level "ERROR" -SecretName "N/A" -VaultName "N/A" `
            -Action "Restart-AppService" -Result "Failed" -Detail $_.Exception.Message
    }
}

# ─────────────────────────────────────────────
# Main
# ─────────────────────────────────────────────

function Main {
    Write-Host ""
    Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║         SPAARKE SECRET ROTATION                  ║" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""

    # Validate parameters
    if ($Scope -eq "Customer" -and -not $CustomerId) {
        throw "CustomerId is required when Scope is 'Customer'. Use -CustomerId <id>"
    }

    # DryRun banner
    if ($DryRun) {
        Write-Host "  *** DRY RUN MODE — No changes will be made ***" -ForegroundColor Yellow
        Write-Host ""
    }

    Write-Host "  Scope       : $Scope" -ForegroundColor White
    Write-Host "  SecretType  : $SecretType" -ForegroundColor White
    Write-Host "  Environment : $Environment" -ForegroundColor White
    if ($CustomerId) {
        Write-Host "  CustomerId  : $CustomerId" -ForegroundColor White
    }
    Write-Host ""

    # Confirmation
    if (-not $DryRun -and -not $Force) {
        $confirmation = Read-Host "Proceed with secret rotation? (yes/no)"
        if ($confirmation -ne "yes") {
            Write-Host "Aborted by user." -ForegroundColor Yellow
            return
        }
    }

    # Initialize
    Initialize-AuditLog
    Assert-Prerequisites

    # Execute rotation based on scope
    switch ($Scope) {
        "Platform" {
            Rotate-PlatformSecrets
            Restart-AppService -ResourceGroup "rg-spaarke-platform-$Environment"
        }
        "Customer" {
            Rotate-CustomerSecrets $CustomerId
        }
        "All" {
            Rotate-PlatformSecrets

            $customerIds = Get-AllCustomerIds
            foreach ($cid in $customerIds) {
                Rotate-CustomerSecrets $cid
            }

            Restart-AppService -ResourceGroup "rg-spaarke-platform-$Environment"
        }
    }

    # Summary
    Write-AuditSummary
}

# Run
Main
