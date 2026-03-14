<#
.SYNOPSIS
    Verifies Entra ID app registrations by testing token acquisition.

.DESCRIPTION
    Tests both production app registrations created by Register-EntraAppRegistrations.ps1:
      1. spaarke-bff-api-prod — Acquires token using client credentials
      2. spaarke-dataverse-s2s-prod — Acquires token for Dataverse

    For each registration:
      - Retrieves client ID from Key Vault (or parameter)
      - Retrieves client secret from Key Vault (or parameter)
      - Acquires an app-only token using client credentials flow
      - Validates the token contains expected claims

    Prerequisites:
      - Azure CLI authenticated
      - Access to sprk-platform-prod-kv Key Vault
      - App registrations created by Register-EntraAppRegistrations.ps1

.PARAMETER TenantId
    Entra ID tenant ID. Default: a221a95e-6abc-4434-aecc-e48338a1b2f2

.PARAMETER KeyVaultName
    Key Vault name for retrieving secrets. Default: sprk-platform-prod-kv

.PARAMETER BffApiClientId
    Override BFF API client ID (skip Key Vault lookup). Optional.

.PARAMETER BffApiClientSecret
    Override BFF API client secret (skip Key Vault lookup). Optional.

.PARAMETER S2SClientId
    Override Dataverse S2S client ID (skip Key Vault lookup). Optional.

.PARAMETER S2SClientSecret
    Override Dataverse S2S client secret (skip Key Vault lookup). Optional.

.PARAMETER DataverseOrgUrl
    Dataverse organization URL for S2S token test. Default: (skipped if empty)

.EXAMPLE
    # Test using Key Vault secrets
    .\Test-EntraAppRegistrations.ps1

.EXAMPLE
    # Test with explicit credentials
    .\Test-EntraAppRegistrations.ps1 -BffApiClientId "abc123" -BffApiClientSecret "secret"

.NOTES
    Project: production-environment-setup-r1
    Task: 021 — Create Entra ID app registrations
#>

param(
    [string]$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    [string]$KeyVaultName = "sprk-platform-prod-kv",
    [string]$BffApiClientId = "",
    [string]$BffApiClientSecret = "",
    [string]$S2SClientId = "",
    [string]$S2SClientSecret = "",
    [string]$DataverseOrgUrl = ""
)

$ErrorActionPreference = "Stop"
$passCount = 0
$failCount = 0
$skipCount = 0

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

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Detail = ""
    )

    if ($Passed) {
        Write-Host "  [PASS] $TestName" -ForegroundColor Green
        $script:passCount++
    } else {
        Write-Host "  [FAIL] $TestName" -ForegroundColor Red
        $script:failCount++
    }

    if ($Detail) {
        Write-Host "         $Detail" -ForegroundColor Gray
    }
}

function Write-TestSkipped {
    param(
        [string]$TestName,
        [string]$Reason
    )
    Write-Host "  [SKIP] $TestName — $Reason" -ForegroundColor DarkYellow
    $script:skipCount++
}

function Get-SecretFromKeyVault {
    param(
        [string]$VaultName,
        [string]$SecretName
    )

    $result = az keyvault secret show --vault-name $VaultName --name $SecretName --query "value" -o tsv 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $result) {
        return $null
    }
    return $result.Trim()
}

function Get-ClientCredentialToken {
    param(
        [string]$TenantId,
        [string]$ClientId,
        [string]$ClientSecret,
        [string]$Scope
    )

    $tokenUrl = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token"

    $body = @{
        grant_type    = "client_credentials"
        client_id     = $ClientId
        client_secret = $ClientSecret
        scope         = $Scope
    }

    try {
        $response = Invoke-RestMethod -Uri $tokenUrl -Method Post -Body $body -ContentType "application/x-www-form-urlencoded"
        return $response
    } catch {
        return $null
    }
}

function Decode-JwtPayload {
    param([string]$Token)

    $parts = $Token.Split('.')
    if ($parts.Count -lt 2) { return $null }

    $payload = $parts[1]
    # Pad base64url to base64
    switch ($payload.Length % 4) {
        2 { $payload += "==" }
        3 { $payload += "=" }
    }
    $payload = $payload.Replace('-', '+').Replace('_', '/')

    $bytes = [Convert]::FromBase64String($payload)
    $json = [System.Text.Encoding]::UTF8.GetString($bytes)
    return $json | ConvertFrom-Json
}

# ─────────────────────────────────────────────────────────────────────────────
# Pre-flight
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "ENTRA ID APP REGISTRATION VERIFICATION"

Write-Host "  Tenant: $TenantId" -ForegroundColor White
Write-Host "  Key Vault: $KeyVaultName" -ForegroundColor White
Write-Host ""

# Verify Azure CLI is authenticated
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "  [ERROR] Azure CLI not authenticated. Run 'az login' first." -ForegroundColor Red
    exit 1
}
Write-Host "  Azure CLI: $($account.user.name)" -ForegroundColor Gray
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# Test 1: BFF API App Registration
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "TEST 1: BFF API App Registration (spaarke-bff-api-prod)"

# Retrieve credentials
if (-not $BffApiClientId) {
    Write-Host "  Retrieving BFF-API-ClientId from Key Vault..." -ForegroundColor Gray
    $BffApiClientId = Get-SecretFromKeyVault -VaultName $KeyVaultName -SecretName "BFF-API-ClientId"
}
if (-not $BffApiClientSecret) {
    Write-Host "  Retrieving BFF-API-ClientSecret from Key Vault..." -ForegroundColor Gray
    $BffApiClientSecret = Get-SecretFromKeyVault -VaultName $KeyVaultName -SecretName "BFF-API-ClientSecret"
}

if (-not $BffApiClientId -or -not $BffApiClientSecret) {
    Write-TestSkipped "BFF API token acquisition" "Credentials not available (Key Vault not accessible or secrets not set)"
} else {
    # Test 1a: App registration exists in Entra ID
    $appInfo = az ad app show --id $BffApiClientId --output json 2>$null | ConvertFrom-Json
    Write-TestResult "App registration exists in Entra ID" ($null -ne $appInfo) `
        $(if ($appInfo) { "DisplayName: $($appInfo.displayName)" } else { "App not found for ID: $BffApiClientId" })

    if ($appInfo) {
        # Test 1b: Display name follows naming convention
        Write-TestResult "Display name follows spaarke- naming" `
            ($appInfo.displayName -match "^spaarke-") `
            "Name: $($appInfo.displayName)"

        # Test 1c: Application ID URI is set
        $hasIdUri = $appInfo.identifierUris -and $appInfo.identifierUris.Count -gt 0
        Write-TestResult "Application ID URI configured" $hasIdUri `
            $(if ($hasIdUri) { "URI: $($appInfo.identifierUris[0])" } else { "No identifier URIs set" })

        # Test 1d: Exposed API scope exists
        $hasScope = $false
        if ($appInfo.api -and $appInfo.api.oauth2PermissionScopes) {
            $hasScope = ($appInfo.api.oauth2PermissionScopes | Where-Object { $_.value -eq "user_impersonation" }).Count -gt 0
        }
        Write-TestResult "Exposed API scope (user_impersonation)" $hasScope

        # Test 1e: Required API permissions configured
        $hasGraphPerms = $false
        $hasDynamicsPerms = $false
        if ($appInfo.requiredResourceAccess) {
            $hasGraphPerms = ($appInfo.requiredResourceAccess | Where-Object { $_.resourceAppId -eq "00000003-0000-0000-c000-000000000000" }).Count -gt 0
            $hasDynamicsPerms = ($appInfo.requiredResourceAccess | Where-Object { $_.resourceAppId -eq "00000007-0000-0000-c000-000000000000" }).Count -gt 0
        }
        Write-TestResult "Microsoft Graph permissions configured" $hasGraphPerms
        Write-TestResult "Dynamics CRM permissions configured" $hasDynamicsPerms
    }

    # Test 1f: Token acquisition (client credentials for Graph)
    Write-Host ""
    Write-Host "  Testing token acquisition..." -ForegroundColor Gray
    $graphToken = Get-ClientCredentialToken `
        -TenantId $TenantId `
        -ClientId $BffApiClientId `
        -ClientSecret $BffApiClientSecret `
        -Scope "https://graph.microsoft.com/.default"

    Write-TestResult "Token acquisition (Graph scope)" ($null -ne $graphToken) `
        $(if ($graphToken) { "Token type: $($graphToken.token_type), expires_in: $($graphToken.expires_in)s" } else { "Token acquisition failed" })

    if ($graphToken -and $graphToken.access_token) {
        $claims = Decode-JwtPayload -Token $graphToken.access_token
        if ($claims) {
            Write-TestResult "Token has correct audience" ($claims.aud -eq "https://graph.microsoft.com") `
                "aud: $($claims.aud)"
            Write-TestResult "Token has correct tenant" ($claims.tid -eq $TenantId) `
                "tid: $($claims.tid)"
        }
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Test 2: Dataverse S2S App Registration
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "TEST 2: Dataverse S2S App Registration (spaarke-dataverse-s2s-prod)"

# Retrieve credentials
if (-not $S2SClientId) {
    Write-Host "  Retrieving Dataverse-S2S-ClientId from Key Vault..." -ForegroundColor Gray
    $S2SClientId = Get-SecretFromKeyVault -VaultName $KeyVaultName -SecretName "Dataverse-S2S-ClientId"
}
if (-not $S2SClientSecret) {
    Write-Host "  Retrieving Dataverse-S2S-ClientSecret from Key Vault..." -ForegroundColor Gray
    $S2SClientSecret = Get-SecretFromKeyVault -VaultName $KeyVaultName -SecretName "Dataverse-S2S-ClientSecret"
}

if (-not $S2SClientId -or -not $S2SClientSecret) {
    Write-TestSkipped "Dataverse S2S token acquisition" "Credentials not available (Key Vault not accessible or secrets not set)"
} else {
    # Test 2a: App registration exists
    $s2sAppInfo = az ad app show --id $S2SClientId --output json 2>$null | ConvertFrom-Json
    Write-TestResult "App registration exists in Entra ID" ($null -ne $s2sAppInfo) `
        $(if ($s2sAppInfo) { "DisplayName: $($s2sAppInfo.displayName)" } else { "App not found for ID: $S2SClientId" })

    if ($s2sAppInfo) {
        # Test 2b: Display name follows naming convention
        Write-TestResult "Display name follows spaarke- naming" `
            ($s2sAppInfo.displayName -match "^spaarke-") `
            "Name: $($s2sAppInfo.displayName)"

        # Test 2c: Dynamics CRM permissions configured
        $hasDynamics = $false
        if ($s2sAppInfo.requiredResourceAccess) {
            $hasDynamics = ($s2sAppInfo.requiredResourceAccess | Where-Object { $_.resourceAppId -eq "00000007-0000-0000-c000-000000000000" }).Count -gt 0
        }
        Write-TestResult "Dynamics CRM permissions configured" $hasDynamics
    }

    # Test 2d: Token acquisition for Dataverse
    if ($DataverseOrgUrl) {
        Write-Host ""
        Write-Host "  Testing Dataverse token acquisition..." -ForegroundColor Gray
        $dvToken = Get-ClientCredentialToken `
            -TenantId $TenantId `
            -ClientId $S2SClientId `
            -ClientSecret $S2SClientSecret `
            -Scope "$DataverseOrgUrl/.default"

        Write-TestResult "Token acquisition (Dataverse scope)" ($null -ne $dvToken) `
            $(if ($dvToken) { "Token type: $($dvToken.token_type), expires_in: $($dvToken.expires_in)s" } else { "Token acquisition failed — admin consent may be needed" })
    } else {
        Write-TestSkipped "Dataverse token acquisition" "DataverseOrgUrl not provided (not yet provisioned)"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Test 3: Key Vault Secret Verification
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "TEST 3: Key Vault Secret Verification ($KeyVaultName)"

$expectedSecrets = @(
    "TenantId",
    "BFF-API-ClientId",
    "BFF-API-ClientSecret",
    "BFF-API-Audience",
    "Dataverse-S2S-ClientId",
    "Dataverse-S2S-ClientSecret"
)

foreach ($secretName in $expectedSecrets) {
    $secretValue = Get-SecretFromKeyVault -VaultName $KeyVaultName -SecretName $secretName
    if ($null -eq $secretValue) {
        Write-TestResult "Key Vault secret: $secretName" $false "Secret not found or not accessible"
    } elseif ($secretValue.Length -lt 5) {
        Write-TestResult "Key Vault secret: $secretName" $false "Secret value suspiciously short ($($secretValue.Length) chars)"
    } else {
        Write-TestResult "Key Vault secret: $secretName" $true "Value present ($($secretValue.Length) chars)"
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────────────────────────────────────

Write-Header "VERIFICATION SUMMARY"

$total = $passCount + $failCount + $skipCount
Write-Host "  Total tests:  $total" -ForegroundColor White
Write-Host "  Passed:       $passCount" -ForegroundColor Green
Write-Host "  Failed:       $failCount" -ForegroundColor $(if ($failCount -gt 0) { "Red" } else { "Green" })
Write-Host "  Skipped:      $skipCount" -ForegroundColor $(if ($skipCount -gt 0) { "DarkYellow" } else { "Gray" })
Write-Host ""

if ($failCount -gt 0) {
    Write-Host "  RESULT: VERIFICATION FAILED" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Common fixes:" -ForegroundColor Yellow
    Write-Host "    - Token acquisition failed? Grant admin consent first." -ForegroundColor Gray
    Write-Host "    - Key Vault secrets missing? Run Register-EntraAppRegistrations.ps1" -ForegroundColor Gray
    Write-Host "    - App not found? Check app was created in correct tenant." -ForegroundColor Gray
    exit 1
} elseif ($skipCount -gt 0) {
    Write-Host "  RESULT: PARTIAL VERIFICATION (some tests skipped)" -ForegroundColor DarkYellow
    exit 0
} else {
    Write-Host "  RESULT: ALL TESTS PASSED" -ForegroundColor Green
    exit 0
}
