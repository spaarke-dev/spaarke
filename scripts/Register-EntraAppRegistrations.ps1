<#
.SYNOPSIS
    Creates production Entra ID app registrations for the Spaarke BFF API and Dataverse S2S.

.DESCRIPTION
    Creates two app registrations in the shared Entra ID tenant (a221a95e-...):
      1. spaarke-bff-api-prod — BFF API production app (validates user tokens, OBO for Graph + Dataverse)
      2. spaarke-dataverse-s2s-prod — Dataverse server-to-server authentication (ServiceClient)

    For each registration:
      - Creates the app registration with correct API permissions
      - Generates a 24-month client secret
      - Stores the secret in the platform Key Vault (sprk-platform-prod-kv)
      - Configures redirect URIs, exposed API scopes, and known client applications

    The BFF API registration mirrors the dev registration (1e40baad) with production-specific
    redirect URIs and known client applications.

    Prerequisites:
      - Azure CLI authenticated with Entra ID admin permissions
      - Access to sprk-platform-prod-kv Key Vault
      - Tenant: a221a95e-6abc-4434-aecc-e48338a1b2f2

.PARAMETER TenantId
    Entra ID tenant ID. Default: a221a95e-6abc-4434-aecc-e48338a1b2f2

.PARAMETER KeyVaultName
    Key Vault name for storing secrets. Default: sprk-platform-prod-kv

.PARAMETER ProductionApiDomain
    Production API domain for redirect URIs. Default: api.spaarke.com

.PARAMETER DataverseOrgUrl
    Production Dataverse organization URL. Default: (empty, set when known)

.PARAMETER DryRun
    If specified, shows what would be created without making changes.

.PARAMETER SkipBffApi
    Skip BFF API app registration (if already created).

.PARAMETER SkipDataverseS2S
    Skip Dataverse S2S app registration (if already created).

.EXAMPLE
    # Preview what will be created
    .\Register-EntraAppRegistrations.ps1 -DryRun

.EXAMPLE
    # Create both registrations
    .\Register-EntraAppRegistrations.ps1

.EXAMPLE
    # Create only Dataverse S2S (BFF already done)
    .\Register-EntraAppRegistrations.ps1 -SkipBffApi

.NOTES
    Project: production-environment-setup-r1
    Task: 021 — Create Entra ID app registrations
    Naming: FR-11 compliant (spaarke- prefix)
    Secrets: FR-08 compliant (Key Vault only)
#>

param(
    [string]$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    [string]$KeyVaultName = "sprk-platform-prod-kv",
    [string]$ProductionApiDomain = "api.spaarke.com",
    [string]$DataverseOrgUrl = "",
    [switch]$DryRun,
    [switch]$SkipBffApi,
    [switch]$SkipDataverseS2S
)

$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────────────────────────
# Configuration
# ─────────────────────────────────────────────────────────────────────────────

$BffApiDisplayName = "spaarke-bff-api-prod"
$DataverseS2SDisplayName = "spaarke-dataverse-s2s-prod"
$SecretExpiryMonths = 24
$SecretExpiryDate = (Get-Date).AddMonths($SecretExpiryMonths).ToString("yyyy-MM-ddTHH:mm:ssZ")

# Microsoft Graph API well-known IDs
$GraphApiId = "00000003-0000-0000-c000-000000000000"
$GraphFilesReadWriteAll = "75359482-378d-4052-8f01-80520e7db3cd"   # Files.ReadWrite.All (delegated)
$GraphSitesReadWriteAll = "89fe6a52-be36-487e-b7d8-d061c450a026"   # Sites.ReadWrite.All (delegated)
$GraphUserRead          = "e1fe6dd8-ba31-4d61-89e7-88639da4683d"   # User.Read (delegated)
$GraphMailSend          = "e383f46e-2787-4529-855e-0e479a3ffac0"   # Mail.Send (delegated)

# Dynamics CRM API well-known ID
$DynamicsCrmApiId = "00000007-0000-0000-c000-000000000000"
$DynamicsCrmUserImpersonation = "78ce3f0f-a1ce-49c2-8cde-64b5c0896db4"  # user_impersonation (delegated)

# ─────────────────────────────────────────────────────────────────────────────
# Helper Functions
# ─────────────────────────────────────────────────────────────────────────────

function Write-Header {
    param([string]$Title)
    Write-Host ""
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host ""
}

function Write-Step {
    param([int]$Number, [string]$Description)
    Write-Host "  [$Number] $Description" -ForegroundColor Yellow
}

function Write-Success {
    param([string]$Message)
    Write-Host "  [OK] $Message" -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host "  [--] $Message" -ForegroundColor Gray
}

function Write-Warn {
    param([string]$Message)
    Write-Host "  [!!] $Message" -ForegroundColor DarkYellow
}

function Store-SecretInKeyVault {
    param(
        [string]$VaultName,
        [string]$SecretName,
        [string]$SecretValue,
        [string]$Description
    )

    if ($DryRun) {
        Write-Info "DRY RUN: Would store secret '$SecretName' in Key Vault '$VaultName'"
        return
    }

    Write-Info "Storing secret '$SecretName' in Key Vault '$VaultName'..."
    az keyvault secret set `
        --vault-name $VaultName `
        --name $SecretName `
        --value $SecretValue `
        --description $Description `
        --output none 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to store secret '$SecretName' in Key Vault '$VaultName'"
    }

    Write-Success "Secret '$SecretName' stored in Key Vault"
}

# ─────────────────────────────────────────────────────────────────────────────
# Pre-flight Checks
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "ENTRA ID APP REGISTRATION — PRODUCTION"

if ($DryRun) {
    Write-Host "  *** DRY RUN MODE — No changes will be made ***" -ForegroundColor Magenta
    Write-Host ""
}

Write-Step 0 "Pre-flight checks"

# Verify Azure CLI is authenticated
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "  [ERROR] Azure CLI not authenticated. Run 'az login' first." -ForegroundColor Red
    exit 1
}
Write-Success "Azure CLI authenticated as: $($account.user.name)"

# Verify correct tenant
$currentTenant = $account.tenantId
if ($currentTenant -ne $TenantId) {
    Write-Warn "Current tenant ($currentTenant) differs from target ($TenantId)"
    Write-Info "Switching tenant..."
    if (-not $DryRun) {
        az account set --subscription $TenantId 2>$null
    }
}
Write-Success "Target tenant: $TenantId"

# Verify Key Vault access
if (-not $DryRun) {
    $kvCheck = az keyvault show --name $KeyVaultName --output json 2>$null | ConvertFrom-Json
    if (-not $kvCheck) {
        Write-Warn "Key Vault '$KeyVaultName' not accessible. Secrets will need manual storage."
    } else {
        Write-Success "Key Vault '$KeyVaultName' accessible"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 1: Create BFF API Production App Registration
# ─────────────────────────────────────────────────────────────────────────────

$BffApiAppId = $null
$BffApiObjectId = $null

if (-not $SkipBffApi) {
    Write-Header "STEP 1: BFF API Production App Registration"

    # Check if app already exists
    $existingBffApp = az ad app list --display-name $BffApiDisplayName --output json 2>$null | ConvertFrom-Json
    if ($existingBffApp -and $existingBffApp.Count -gt 0) {
        Write-Warn "App registration '$BffApiDisplayName' already exists (AppId: $($existingBffApp[0].appId))"
        Write-Info "Skipping creation. Use Azure Portal to modify if needed."
        $BffApiAppId = $existingBffApp[0].appId
        $BffApiObjectId = $existingBffApp[0].id
    } else {
        Write-Step 1 "Creating app registration: $BffApiDisplayName"

        if ($DryRun) {
            Write-Info "DRY RUN: Would create app '$BffApiDisplayName' with:"
            Write-Info "  - Platform: Web"
            Write-Info "  - Redirect URIs: https://$ProductionApiDomain/.auth/login/aad/callback"
            Write-Info "  - Graph permissions: Files.ReadWrite.All, Sites.ReadWrite.All, User.Read, Mail.Send (delegated)"
            Write-Info "  - Dynamics CRM permissions: user_impersonation (delegated)"
            Write-Info "  - Exposed API scope: user_impersonation"
            $BffApiAppId = "00000000-0000-0000-0000-000000000000"
        } else {
            # Create the app registration with required resource access
            $requiredResourceAccess = @(
                @{
                    resourceAppId = $GraphApiId
                    resourceAccess = @(
                        @{ id = $GraphFilesReadWriteAll; type = "Scope" }
                        @{ id = $GraphSitesReadWriteAll; type = "Scope" }
                        @{ id = $GraphUserRead; type = "Scope" }
                        @{ id = $GraphMailSend; type = "Scope" }
                    )
                },
                @{
                    resourceAppId = $DynamicsCrmApiId
                    resourceAccess = @(
                        @{ id = $DynamicsCrmUserImpersonation; type = "Scope" }
                    )
                }
            ) | ConvertTo-Json -Depth 4 -Compress

            $appManifest = @{
                displayName = $BffApiDisplayName
                signInAudience = "AzureADMyOrg"
                web = @{
                    redirectUris = @(
                        "https://$ProductionApiDomain/.auth/login/aad/callback"
                    )
                    implicitGrantSettings = @{
                        enableAccessTokenIssuance = $false
                        enableIdTokenIssuance = $true
                    }
                }
                requiredResourceAccess = @(
                    @{
                        resourceAppId = $GraphApiId
                        resourceAccess = @(
                            @{ id = $GraphFilesReadWriteAll; type = "Scope" }
                            @{ id = $GraphSitesReadWriteAll; type = "Scope" }
                            @{ id = $GraphUserRead; type = "Scope" }
                            @{ id = $GraphMailSend; type = "Scope" }
                        )
                    },
                    @{
                        resourceAppId = $DynamicsCrmApiId
                        resourceAccess = @(
                            @{ id = $DynamicsCrmUserImpersonation; type = "Scope" }
                        )
                    }
                )
            } | ConvertTo-Json -Depth 5

            # Write manifest to temp file (Azure CLI has limits on inline JSON)
            $manifestPath = [System.IO.Path]::GetTempFileName()
            $appManifest | Out-File -FilePath $manifestPath -Encoding utf8

            $createdApp = az ad app create --display-name $BffApiDisplayName `
                --sign-in-audience AzureADMyOrg `
                --web-redirect-uris "https://$ProductionApiDomain/.auth/login/aad/callback" `
                --enable-id-token-issuance true `
                --output json 2>&1 | ConvertFrom-Json

            if ($LASTEXITCODE -ne 0 -or -not $createdApp) {
                throw "Failed to create app registration '$BffApiDisplayName'"
            }

            $BffApiAppId = $createdApp.appId
            $BffApiObjectId = $createdApp.id
            Write-Success "Created app: $BffApiDisplayName (AppId: $BffApiAppId)"

            # Add required resource access (Graph + Dynamics CRM)
            Write-Step 2 "Adding API permissions..."

            # Add Graph permissions
            az ad app permission add --id $BffApiAppId `
                --api $GraphApiId `
                --api-permissions "$($GraphFilesReadWriteAll)=Scope $($GraphSitesReadWriteAll)=Scope $($GraphUserRead)=Scope $($GraphMailSend)=Scope" `
                --output none 2>&1

            # Add Dynamics CRM permission
            az ad app permission add --id $BffApiAppId `
                --api $DynamicsCrmApiId `
                --api-permissions "$($DynamicsCrmUserImpersonation)=Scope" `
                --output none 2>&1

            Write-Success "API permissions added"

            # Clean up temp file
            Remove-Item $manifestPath -ErrorAction SilentlyContinue
        }
    }

    # Step 2: Configure Application ID URI and exposed scope
    Write-Step 3 "Configuring Application ID URI and exposed scope"

    if ($BffApiAppId -and -not $DryRun) {
        $appIdUri = "api://$BffApiAppId"

        # Set Application ID URI
        az ad app update --id $BffApiAppId `
            --identifier-uris $appIdUri `
            --output none 2>&1

        Write-Success "Application ID URI set: $appIdUri"

        # Add exposed API scope: user_impersonation
        # This requires the Microsoft Graph API to update the app manifest
        $scopeId = [guid]::NewGuid().ToString()
        $apiDefinition = @{
            oauth2PermissionScopes = @(
                @{
                    adminConsentDescription = "Allow the application to access $BffApiDisplayName on behalf of the signed-in user."
                    adminConsentDisplayName = "Access $BffApiDisplayName"
                    id = $scopeId
                    isEnabled = $true
                    type = "User"
                    userConsentDescription = "Allow the application to access $BffApiDisplayName on your behalf."
                    userConsentDisplayName = "Access $BffApiDisplayName"
                    value = "user_impersonation"
                }
            )
        } | ConvertTo-Json -Depth 4

        $apiPath = [System.IO.Path]::GetTempFileName()
        $apiDefinition | Out-File -FilePath $apiPath -Encoding utf8

        az rest --method PATCH `
            --uri "https://graph.microsoft.com/v1.0/applications/$BffApiObjectId" `
            --headers "Content-Type=application/json" `
            --body "@$apiPath" `
            --output none 2>&1

        Remove-Item $apiPath -ErrorAction SilentlyContinue
        Write-Success "Exposed API scope: $appIdUri/user_impersonation"
    } elseif ($DryRun) {
        Write-Info "DRY RUN: Would set Application ID URI: api://<app-id>"
        Write-Info "DRY RUN: Would expose scope: api://<app-id>/user_impersonation"
    }

    # Step 3: Generate client secret
    Write-Step 4 "Generating client secret (valid $SecretExpiryMonths months)"

    if (-not $DryRun -and $BffApiAppId) {
        $secretResult = az ad app credential reset `
            --id $BffApiAppId `
            --append `
            --display-name "Production-$(Get-Date -Format 'yyyyMMdd')" `
            --end-date $SecretExpiryDate `
            --output json 2>&1 | ConvertFrom-Json

        if ($LASTEXITCODE -ne 0 -or -not $secretResult) {
            throw "Failed to create client secret for '$BffApiDisplayName'"
        }

        $bffApiSecret = $secretResult.password
        Write-Success "Client secret created (prefix: $($bffApiSecret.Substring(0, 5))...)"
        Write-Warn "IMPORTANT: This secret is shown only once. Storing in Key Vault now."

        # Store in Key Vault
        Store-SecretInKeyVault -VaultName $KeyVaultName `
            -SecretName "BFF-API-ClientSecret" `
            -SecretValue $bffApiSecret `
            -Description "BFF API production client secret ($BffApiDisplayName)"

        Store-SecretInKeyVault -VaultName $KeyVaultName `
            -SecretName "BFF-API-ClientId" `
            -SecretValue $BffApiAppId `
            -Description "BFF API production client ID ($BffApiDisplayName)"

        Store-SecretInKeyVault -VaultName $KeyVaultName `
            -SecretName "BFF-API-Audience" `
            -SecretValue "api://$BffApiAppId" `
            -Description "BFF API production audience URI ($BffApiDisplayName)"

        Store-SecretInKeyVault -VaultName $KeyVaultName `
            -SecretName "TenantId" `
            -SecretValue $TenantId `
            -Description "Entra ID tenant ID"
    } else {
        Write-Info "DRY RUN: Would generate 24-month client secret"
        Write-Info "DRY RUN: Would store secrets in Key Vault:"
        Write-Info "  - BFF-API-ClientSecret"
        Write-Info "  - BFF-API-ClientId"
        Write-Info "  - BFF-API-Audience"
        Write-Info "  - TenantId"
    }

    # Step 4: Create service principal
    Write-Step 5 "Creating service principal"

    if (-not $DryRun -and $BffApiAppId) {
        $sp = az ad sp create --id $BffApiAppId --output json 2>$null | ConvertFrom-Json
        if ($sp) {
            Write-Success "Service principal created (ObjectId: $($sp.id))"
        } else {
            Write-Info "Service principal may already exist"
        }
    } else {
        Write-Info "DRY RUN: Would create service principal for $BffApiDisplayName"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 2: Create Dataverse S2S Production App Registration
# ─────────────────────────────────────────────────────────────────────────────

$S2SAppId = $null
$S2SObjectId = $null

if (-not $SkipDataverseS2S) {
    Write-Header "STEP 2: Dataverse S2S Production App Registration"

    # Check if app already exists
    $existingS2SApp = az ad app list --display-name $DataverseS2SDisplayName --output json 2>$null | ConvertFrom-Json
    if ($existingS2SApp -and $existingS2SApp.Count -gt 0) {
        Write-Warn "App registration '$DataverseS2SDisplayName' already exists (AppId: $($existingS2SApp[0].appId))"
        Write-Info "Skipping creation. Use Azure Portal to modify if needed."
        $S2SAppId = $existingS2SApp[0].appId
        $S2SObjectId = $existingS2SApp[0].id
    } else {
        Write-Step 1 "Creating app registration: $DataverseS2SDisplayName"

        if ($DryRun) {
            Write-Info "DRY RUN: Would create app '$DataverseS2SDisplayName' with:"
            Write-Info "  - Dynamics CRM permissions: user_impersonation (delegated)"
            Write-Info "  - Purpose: Dataverse ServiceClient S2S auth (AuthType=ClientSecret)"
            $S2SAppId = "00000000-0000-0000-0000-000000000001"
        } else {
            $createdS2SApp = az ad app create --display-name $DataverseS2SDisplayName `
                --sign-in-audience AzureADMyOrg `
                --output json 2>&1 | ConvertFrom-Json

            if ($LASTEXITCODE -ne 0 -or -not $createdS2SApp) {
                throw "Failed to create app registration '$DataverseS2SDisplayName'"
            }

            $S2SAppId = $createdS2SApp.appId
            $S2SObjectId = $createdS2SApp.id
            Write-Success "Created app: $DataverseS2SDisplayName (AppId: $S2SAppId)"

            # Add Dynamics CRM permission
            Write-Step 2 "Adding Dynamics CRM API permissions..."
            az ad app permission add --id $S2SAppId `
                --api $DynamicsCrmApiId `
                --api-permissions "$($DynamicsCrmUserImpersonation)=Scope" `
                --output none 2>&1

            Write-Success "Dynamics CRM user_impersonation permission added"
        }
    }

    # Generate client secret
    Write-Step 3 "Generating client secret (valid $SecretExpiryMonths months)"

    if (-not $DryRun -and $S2SAppId) {
        $s2sSecretResult = az ad app credential reset `
            --id $S2SAppId `
            --append `
            --display-name "Production-$(Get-Date -Format 'yyyyMMdd')" `
            --end-date $SecretExpiryDate `
            --output json 2>&1 | ConvertFrom-Json

        if ($LASTEXITCODE -ne 0 -or -not $s2sSecretResult) {
            throw "Failed to create client secret for '$DataverseS2SDisplayName'"
        }

        $s2sSecret = $s2sSecretResult.password
        Write-Success "Client secret created (prefix: $($s2sSecret.Substring(0, 5))...)"

        # Store in Key Vault
        Store-SecretInKeyVault -VaultName $KeyVaultName `
            -SecretName "Dataverse-S2S-ClientSecret" `
            -SecretValue $s2sSecret `
            -Description "Dataverse S2S production client secret ($DataverseS2SDisplayName)"

        Store-SecretInKeyVault -VaultName $KeyVaultName `
            -SecretName "Dataverse-S2S-ClientId" `
            -SecretValue $S2SAppId `
            -Description "Dataverse S2S production client ID ($DataverseS2SDisplayName)"
    } else {
        Write-Info "DRY RUN: Would generate 24-month client secret"
        Write-Info "DRY RUN: Would store secrets in Key Vault:"
        Write-Info "  - Dataverse-S2S-ClientSecret"
        Write-Info "  - Dataverse-S2S-ClientId"
    }

    # Create service principal
    Write-Step 4 "Creating service principal"

    if (-not $DryRun -and $S2SAppId) {
        $s2sSp = az ad sp create --id $S2SAppId --output json 2>$null | ConvertFrom-Json
        if ($s2sSp) {
            Write-Success "Service principal created (ObjectId: $($s2sSp.id))"
        } else {
            Write-Info "Service principal may already exist"
        }
    } else {
        Write-Info "DRY RUN: Would create service principal for $DataverseS2SDisplayName"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 3: Configure Known Client Applications (BFF API)
# ─────────────────────────────────────────────────────────────────────────────

if ($BffApiAppId -and $BffApiObjectId -and -not $DryRun) {
    Write-Header "STEP 3: Configure Known Client Applications"

    Write-Info "Known client applications will need to be configured after"
    Write-Info "production PCF and Code Page client app registrations are created."
    Write-Info ""
    Write-Info "To add known clients later, run:"
    Write-Info "  az rest --method PATCH --uri 'https://graph.microsoft.com/v1.0/applications/$BffApiObjectId'"
    Write-Info "    --body '{""api"":{""knownClientApplications"":[""<pcf-client-id>"",""<codepage-client-id>""]}}'"
} elseif ($DryRun) {
    Write-Header "STEP 3: Configure Known Client Applications"
    Write-Info "DRY RUN: Would configure knownClientApplications on BFF API app"
    Write-Info "  (Requires PCF and Code Page client app IDs — set after those are created)"
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 4: Admin Consent
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "STEP 4: Admin Consent (Manual Step)"

Write-Info "Admin consent MUST be granted for both app registrations."
Write-Info "This requires a Global Administrator or Privileged Role Administrator."
Write-Info ""

if ($BffApiAppId) {
    Write-Info "BFF API ($BffApiDisplayName):"
    Write-Info "  Portal: https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/CallAnAPI/appId/$BffApiAppId"
    Write-Info "  CLI:    az ad app permission admin-consent --id $BffApiAppId"
    Write-Info ""
}

if ($S2SAppId) {
    Write-Info "Dataverse S2S ($DataverseS2SDisplayName):"
    Write-Info "  Portal: https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/CallAnAPI/appId/$S2SAppId"
    Write-Info "  CLI:    az ad app permission admin-consent --id $S2SAppId"
    Write-Info ""
}

# ─────────────────────────────────────────────────────────────────────────────
# Step 5: Dataverse Application User Registration
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "STEP 5: Dataverse Application User (Manual Step)"

Write-Info "After Dataverse environment is provisioned, register the app registrations"
Write-Info "as Application Users with appropriate security roles."
Write-Info ""

if ($BffApiAppId) {
    Write-Info "BFF API Application User:"
    Write-Info "  App ID: $BffApiAppId"
    Write-Info "  Security Role: System Administrator (or custom role)"
    Write-Info "  Command: pac admin assign-app-to-environment --environment <env-url> --app $BffApiAppId"
    Write-Info ""
}

if ($S2SAppId) {
    Write-Info "Dataverse S2S Application User:"
    Write-Info "  App ID: $S2SAppId"
    Write-Info "  Security Role: System Administrator (or custom role)"
    Write-Info "  Command: pac admin assign-app-to-environment --environment <env-url> --app $S2SAppId"
    Write-Info ""
}

# ─────────────────────────────────────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "REGISTRATION SUMMARY"

if ($DryRun) {
    Write-Host "  *** DRY RUN — No changes were made ***" -ForegroundColor Magenta
    Write-Host ""
}

Write-Host "  Tenant ID:       $TenantId" -ForegroundColor White
Write-Host "  Key Vault:       $KeyVaultName" -ForegroundColor White
Write-Host ""

if ($BffApiAppId) {
    Write-Host "  BFF API App Registration:" -ForegroundColor Green
    Write-Host "    Display Name:    $BffApiDisplayName" -ForegroundColor White
    Write-Host "    Application ID:  $BffApiAppId" -ForegroundColor White
    Write-Host "    App ID URI:      api://$BffApiAppId" -ForegroundColor White
    Write-Host "    Redirect URI:    https://$ProductionApiDomain/.auth/login/aad/callback" -ForegroundColor White
    Write-Host "    Permissions:     Graph (Files.RW.All, Sites.RW.All, User.Read, Mail.Send)" -ForegroundColor White
    Write-Host "                     Dynamics CRM (user_impersonation)" -ForegroundColor White
    Write-Host "    Exposed Scope:   api://$BffApiAppId/user_impersonation" -ForegroundColor White
    Write-Host "    KV Secrets:      BFF-API-ClientSecret, BFF-API-ClientId, BFF-API-Audience, TenantId" -ForegroundColor White
    Write-Host ""
}

if ($S2SAppId) {
    Write-Host "  Dataverse S2S App Registration:" -ForegroundColor Green
    Write-Host "    Display Name:    $DataverseS2SDisplayName" -ForegroundColor White
    Write-Host "    Application ID:  $S2SAppId" -ForegroundColor White
    Write-Host "    Permissions:     Dynamics CRM (user_impersonation)" -ForegroundColor White
    Write-Host "    KV Secrets:      Dataverse-S2S-ClientSecret, Dataverse-S2S-ClientId" -ForegroundColor White
    Write-Host ""
}

Write-Host "  Key Vault Secrets Stored:" -ForegroundColor Yellow
Write-Host "    sprk-platform-prod-kv:" -ForegroundColor White
Write-Host "      - TenantId" -ForegroundColor Gray
Write-Host "      - BFF-API-ClientId" -ForegroundColor Gray
Write-Host "      - BFF-API-ClientSecret" -ForegroundColor Gray
Write-Host "      - BFF-API-Audience" -ForegroundColor Gray
Write-Host "      - Dataverse-S2S-ClientId" -ForegroundColor Gray
Write-Host "      - Dataverse-S2S-ClientSecret" -ForegroundColor Gray
Write-Host ""

Write-Host "  NEXT STEPS:" -ForegroundColor Cyan
Write-Host "    1. Grant admin consent (see Step 4 above)" -ForegroundColor White
Write-Host "    2. Register Application Users in Dataverse (see Step 5 above)" -ForegroundColor White
Write-Host "    3. Configure knownClientApplications when PCF/CodePage clients are created" -ForegroundColor White
Write-Host "    4. Run Test-EntraAppRegistrations.ps1 to verify token acquisition" -ForegroundColor White
Write-Host ""
