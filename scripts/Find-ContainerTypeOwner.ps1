# Find Container Type Owning Application
# Purpose: Identify which Azure AD app owns the container type so we can register BFF API

param(
    [string]$ContainerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06"
)

Write-Host "=== Finding Container Type Owner ===" -ForegroundColor Cyan
Write-Host "Container Type ID: $ContainerTypeId" -ForegroundColor Gray
Write-Host ""

# Check if PnP PowerShell is installed
$pnpModule = Get-Module -ListAvailable -Name PnP.PowerShell

if (-not $pnpModule) {
    Write-Host "Installing PnP.PowerShell module..." -ForegroundColor Yellow
    Install-Module -Name PnP.PowerShell -Scope CurrentUser -Force -AllowClobber
}

# Import module
Import-Module PnP.PowerShell -ErrorAction Stop

Write-Host "Step 1: Connect to SharePoint Admin Center" -ForegroundColor Yellow
Write-Host "You'll be prompted to sign in with your user account (e.g., ralph.schroeder@spaarke.com)" -ForegroundColor Gray
Write-Host ""

# Construct admin center URL
# For spaarke.sharepoint.com, admin center is spaarke-admin.sharepoint.com
$adminUrl = "https://spaarke-admin.sharepoint.com"

Write-Host "Connecting to: $adminUrl" -ForegroundColor Gray

try {
    # Connect with interactive login (user auth)
    Connect-PnPOnline -Url $adminUrl -Interactive -ErrorAction Stop

    Write-Host "✅ Connected successfully" -ForegroundColor Green
    Write-Host ""

    Write-Host "Step 2: List all container types" -ForegroundColor Yellow

    # Get all container types
    $containerTypes = Get-PnPContainerType -ErrorAction Stop

    if (-not $containerTypes) {
        Write-Host "❌ No container types found in tenant" -ForegroundColor Red
        Write-Host "This might mean:" -ForegroundColor Yellow
        Write-Host "  1. Container types haven't been created yet" -ForegroundColor Gray
        Write-Host "  2. Your account lacks permissions to view them" -ForegroundColor Gray
        Write-Host "  3. Container type was created in a different tenant" -ForegroundColor Gray
        exit 1
    }

    Write-Host "Found $($containerTypes.Count) container type(s)" -ForegroundColor Green
    Write-Host ""

    Write-Host "Step 3: Find our container type" -ForegroundColor Yellow

    # Find the specific container type
    $ourType = $containerTypes | Where-Object {$_.ContainerTypeId -eq $ContainerTypeId}

    if ($ourType) {
        Write-Host "✅ Found container type!" -ForegroundColor Green
        Write-Host ""
        Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host "CONTAINER TYPE INFORMATION" -ForegroundColor Cyan
        Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Container Type ID:   $($ourType.ContainerTypeId)" -ForegroundColor White
        Write-Host "Display Name:        $($ourType.DisplayName)" -ForegroundColor White
        Write-Host "Owning App ID:       $($ourType.OwningApplicationId)" -ForegroundColor Yellow
        Write-Host "Created:             $($ourType.CreatedDateTime)" -ForegroundColor Gray
        Write-Host ""
        Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host ""

        # Save to variable for next steps
        $owningAppId = $ourType.OwningApplicationId

        Write-Host "Step 4: Look up owning application details" -ForegroundColor Yellow

        try {
            # Connect to Azure AD to get app details
            Write-Host "Connecting to Azure AD..." -ForegroundColor Gray
            Connect-AzAccount -ErrorAction Stop | Out-Null

            # Get app registration details
            $app = Get-AzADApplication -ApplicationId $owningAppId -ErrorAction SilentlyContinue

            if ($app) {
                Write-Host "✅ Found application registration" -ForegroundColor Green
                Write-Host ""
                Write-Host "APPLICATION DETAILS" -ForegroundColor Cyan
                Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
                Write-Host "App Display Name:    $($app.DisplayName)" -ForegroundColor White
                Write-Host "App ID (Client ID):  $($app.AppId)" -ForegroundColor White
                Write-Host "Object ID:           $($app.Id)" -ForegroundColor Gray
                Write-Host ""

                # Check for credentials
                Write-Host "CREDENTIALS" -ForegroundColor Cyan
                Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan

                $creds = Get-AzADAppCredential -ApplicationId $owningAppId

                if ($creds) {
                    $secrets = $creds | Where-Object {$_.Type -eq "Password"}
                    $certs = $creds | Where-Object {$_.Type -eq "AsymmetricX509Cert"}

                    Write-Host "Client Secrets:      $($secrets.Count)" -ForegroundColor $(if ($secrets.Count -gt 0) {"Green"} else {"Yellow"})
                    Write-Host "Certificates:        $($certs.Count)" -ForegroundColor $(if ($certs.Count -gt 0) {"Green"} else {"Yellow"})

                    if ($secrets) {
                        Write-Host ""
                        Write-Host "Secret(s) found (use one of these):" -ForegroundColor Green
                        foreach ($secret in $secrets) {
                            $expired = $secret.EndDateTime -lt (Get-Date)
                            $status = if ($expired) {"EXPIRED"} else {"Valid"}
                            $color = if ($expired) {"Red"} else {"Green"}
                            Write-Host "  - KeyId: $($secret.KeyId) | Expires: $($secret.EndDateTime) | Status: $status" -ForegroundColor $color
                        }
                    }

                    if ($certs) {
                        Write-Host ""
                        Write-Host "Certificate(s) found:" -ForegroundColor Green
                        foreach ($cert in $certs) {
                            $expired = $cert.EndDateTime -lt (Get-Date)
                            $status = if ($expired) {"EXPIRED"} else {"Valid"}
                            $color = if ($expired) {"Red"} else {"Green"}
                            Write-Host "  - KeyId: $($cert.KeyId) | Expires: $($cert.EndDateTime) | Status: $status" -ForegroundColor $color
                        }
                    }

                    if (-not $secrets -and -not $certs) {
                        Write-Host "⚠️  No valid credentials found" -ForegroundColor Yellow
                        Write-Host "You'll need to create a client secret or certificate" -ForegroundColor Yellow
                    }
                } else {
                    Write-Host "⚠️  Could not retrieve credentials" -ForegroundColor Yellow
                }

                Write-Host ""

                # Check for Container.Selected permission
                Write-Host "SHAREPOINT PERMISSIONS" -ForegroundColor Cyan
                Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan

                $spResource = Get-AzADServicePrincipal -ApplicationId "00000003-0000-0ff1-ce00-000000000000" -ErrorAction SilentlyContinue

                if ($spResource) {
                    $containerPermission = $spResource.AppRole | Where-Object {$_.Value -eq "Container.Selected"}

                    if ($containerPermission) {
                        Write-Host "✅ Container.Selected permission exists in SharePoint" -ForegroundColor Green
                        Write-Host "   Permission ID: $($containerPermission.Id)" -ForegroundColor Gray

                        # Check if app has this permission
                        $appSp = Get-AzADServicePrincipal -ApplicationId $owningAppId -ErrorAction SilentlyContinue
                        if ($appSp) {
                            $assignments = Get-AzADServicePrincipalAppRoleAssignment -ServicePrincipalId $appSp.Id -ErrorAction SilentlyContinue
                            $hasPermission = $assignments | Where-Object {$_.AppRoleId -eq $containerPermission.Id}

                            if ($hasPermission) {
                                Write-Host "✅ App has Container.Selected permission" -ForegroundColor Green
                            } else {
                                Write-Host "⚠️  App does NOT have Container.Selected permission" -ForegroundColor Yellow
                                Write-Host "   You'll need to grant this permission before registration" -ForegroundColor Yellow
                            }
                        }
                    } else {
                        Write-Host "⚠️  Container.Selected permission not found in SharePoint resource" -ForegroundColor Yellow
                    }
                } else {
                    Write-Host "⚠️  Could not query SharePoint resource permissions" -ForegroundColor Yellow
                }

            } else {
                Write-Host "⚠️  Could not find application registration for ID: $owningAppId" -ForegroundColor Yellow
                Write-Host "The app may be in a different tenant" -ForegroundColor Gray
            }

        } catch {
            Write-Host "⚠️  Could not connect to Azure AD: $($_.Exception.Message)" -ForegroundColor Yellow
            Write-Host "You can still use the Owning App ID above to proceed" -ForegroundColor Gray
        }

        Write-Host ""
        Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host "NEXT STEPS" -ForegroundColor Cyan
        Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "1. Note the Owning App ID: $owningAppId" -ForegroundColor White
        Write-Host ""
        Write-Host "2. If no valid client secret exists, create one:" -ForegroundColor White
        Write-Host "   az ad app credential reset --id $owningAppId --append" -ForegroundColor Gray
        Write-Host ""
        Write-Host "3. Use this app to call the registration API:" -ForegroundColor White
        Write-Host "   See: CONTAINER-TYPE-REGISTRATION-GUIDE.md" -ForegroundColor Gray
        Write-Host ""

    } else {
        Write-Host "❌ Container type not found: $ContainerTypeId" -ForegroundColor Red
        Write-Host ""
        Write-Host "Available container types in this tenant:" -ForegroundColor Yellow
        foreach ($ct in $containerTypes) {
            Write-Host "  - $($ct.ContainerTypeId) | $($ct.DisplayName)" -ForegroundColor Gray
        }
    }

} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "  1. Make sure you're a SharePoint Admin or Global Admin" -ForegroundColor Gray
    Write-Host "  2. Verify the tenant URL is correct (spaarke-admin.sharepoint.com)" -ForegroundColor Gray
    Write-Host "  3. Check if MFA is required for your account" -ForegroundColor Gray
}
