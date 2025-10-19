# Find Container Type Owning Application (Using Azure CLI)
# Purpose: Identify which Azure AD app owns the container type

param(
    [string]$ContainerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06",
    [string]$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
)

Write-Host "=== Finding Container Type Owner ===" -ForegroundColor Cyan
Write-Host "Container Type ID: $ContainerTypeId" -ForegroundColor Gray
Write-Host "Tenant ID: $TenantId" -ForegroundColor Gray
Write-Host ""

# Step 1: Check if logged in to Azure CLI
Write-Host "Step 1: Checking Azure CLI authentication..." -ForegroundColor Yellow

$accountInfo = az account show 2>&1 | ConvertFrom-Json

if ($LASTEXITCODE -ne 0) {
    Write-Host "Not logged in. Logging in to Azure..." -ForegroundColor Yellow
    az login --tenant $TenantId --allow-no-subscriptions

    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Failed to login" -ForegroundColor Red
        exit 1
    }
}

Write-Host "✅ Logged in as: $($accountInfo.user.name)" -ForegroundColor Green
Write-Host ""

# Step 2: Query SharePoint for container types
Write-Host "Step 2: Querying SharePoint for container types..." -ForegroundColor Yellow
Write-Host "Getting access token for SharePoint..." -ForegroundColor Gray

$spToken = az account get-access-token --resource "https://spaarke.sharepoint.com" --query accessToken -o tsv

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Failed to get SharePoint token" -ForegroundColor Red
    Write-Host "Your account may not have SharePoint admin permissions" -ForegroundColor Yellow
    exit 1
}

Write-Host "✅ Got SharePoint access token" -ForegroundColor Green
Write-Host ""

# Try to list container types via SharePoint REST API
Write-Host "Step 3: Listing container types..." -ForegroundColor Yellow

$listUrl = "https://spaarke-admin.sharepoint.com/_api/v2.1/storageContainerTypes"

try {
    $response = Invoke-RestMethod -Uri $listUrl `
        -Method Get `
        -Headers @{
            "Authorization" = "Bearer $spToken"
            "Accept" = "application/json"
        } `
        -ErrorAction Stop

    if ($response.value -and $response.value.Count -gt 0) {
        Write-Host "✅ Found $($response.value.Count) container type(s)" -ForegroundColor Green
        Write-Host ""

        # Find our container type
        $ourType = $response.value | Where-Object {$_.id -eq $ContainerTypeId}

        if ($ourType) {
            Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
            Write-Host "CONTAINER TYPE INFORMATION" -ForegroundColor Cyan
            Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
            Write-Host ""
            Write-Host "Container Type ID:   $($ourType.id)" -ForegroundColor White
            Write-Host "Display Name:        $($ourType.displayName)" -ForegroundColor White
            Write-Host "Owning App ID:       $($ourType.owningApplicationId)" -ForegroundColor Yellow
            Write-Host "Created:             $($ourType.createdDateTime)" -ForegroundColor Gray
            Write-Host ""

            $owningAppId = $ourType.owningApplicationId

            # Step 4: Get application details
            Write-Host "Step 4: Getting owning application details..." -ForegroundColor Yellow

            $appJson = az ad app show --id $owningAppId 2>&1

            if ($LASTEXITCODE -eq 0) {
                $app = $appJson | ConvertFrom-Json

                Write-Host "✅ Found application registration" -ForegroundColor Green
                Write-Host ""
                Write-Host "APPLICATION DETAILS" -ForegroundColor Cyan
                Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
                Write-Host "App Display Name:    $($app.displayName)" -ForegroundColor White
                Write-Host "App ID (Client ID):  $($app.appId)" -ForegroundColor White
                Write-Host "Object ID:           $($app.id)" -ForegroundColor Gray
                Write-Host ""

                # Check credentials
                Write-Host "CREDENTIALS" -ForegroundColor Cyan
                Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan

                $credsJson = az ad app credential list --id $owningAppId 2>&1

                if ($LASTEXITCODE -eq 0) {
                    $creds = $credsJson | ConvertFrom-Json

                    $secrets = $creds | Where-Object {$_.customKeyIdentifier -eq $null -or $_.type -eq "Password"}
                    $certs = $creds | Where-Object {$_.customKeyIdentifier -ne $null -or $_.type -eq "AsymmetricX509Cert"}

                    Write-Host "Client Secrets:      $($secrets.Count)" -ForegroundColor $(if ($secrets.Count -gt 0) {"Green"} else {"Yellow"})
                    Write-Host "Certificates:        $($certs.Count)" -ForegroundColor $(if ($certs.Count -gt 0) {"Green"} else {"Yellow"})

                    if ($secrets -and $secrets.Count -gt 0) {
                        Write-Host ""
                        Write-Host "Secret(s) found:" -ForegroundColor Green
                        foreach ($secret in $secrets) {
                            $endDate = [DateTime]::Parse($secret.endDateTime)
                            $expired = $endDate -lt (Get-Date)
                            $status = if ($expired) {"EXPIRED"} else {"Valid"}
                            $color = if ($expired) {"Red"} else {"Green"}
                            Write-Host "  - KeyId: $($secret.keyId) | Expires: $endDate | Status: $status" -ForegroundColor $color
                        }
                    }

                    if (-not $secrets -or $secrets.Count -eq 0) {
                        Write-Host ""
                        Write-Host "⚠️  No valid client secrets found" -ForegroundColor Yellow
                        Write-Host "Create one with:" -ForegroundColor Yellow
                        Write-Host "  az ad app credential reset --id $owningAppId --append" -ForegroundColor Gray
                    }
                }

                Write-Host ""
                Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
                Write-Host "NEXT STEPS" -ForegroundColor Cyan
                Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
                Write-Host ""
                Write-Host "1. Owning App ID: $owningAppId" -ForegroundColor White
                Write-Host ""
                Write-Host "2. Create/verify client secret:" -ForegroundColor White
                Write-Host "   az ad app credential reset --id $owningAppId --append" -ForegroundColor Gray
                Write-Host ""
                Write-Host "3. Use this app to register BFF API with container type" -ForegroundColor White
                Write-Host "   See: CONTAINER-TYPE-REGISTRATION-GUIDE.md" -ForegroundColor Gray
                Write-Host ""

            } else {
                Write-Host "⚠️  Could not get application details: $appJson" -ForegroundColor Yellow
                Write-Host "Owning App ID: $owningAppId" -ForegroundColor Yellow
            }

        } else {
            Write-Host "❌ Container type not found: $ContainerTypeId" -ForegroundColor Red
            Write-Host ""
            Write-Host "Available container types:" -ForegroundColor Yellow
            foreach ($ct in $response.value) {
                Write-Host "  - $($ct.id) | $($ct.displayName)" -ForegroundColor Gray
            }
        }

    } else {
        Write-Host "⚠️  No container types found or access denied" -ForegroundColor Yellow
        Write-Host "Response: $($response | ConvertTo-Json)" -ForegroundColor Gray
    }

} catch {
    Write-Host "❌ Error querying SharePoint: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "This might mean:" -ForegroundColor Yellow
    Write-Host "  1. Your account lacks SharePoint admin permissions" -ForegroundColor Gray
    Write-Host "  2. The container type was created via a different method" -ForegroundColor Gray
    Write-Host "  3. The API endpoint format is different" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Try alternative: Use Graph API to query the container directly" -ForegroundColor Yellow
}
