# Grant-ManagedIdentityPermissions.ps1
# Grants SharePoint Embedded permissions to Managed Identity

param(
    [string]$ManagedIdentityClientId = "c8cdf6fc-a414-4a5b-981c-006d0d84850f",
    [string]$ContainerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06"
)

Write-Host "=== Grant Managed Identity Permissions for SharePoint Embedded ===" -ForegroundColor Yellow
Write-Host ""

# Check if connected to Microsoft Graph
Write-Host "Checking Microsoft Graph connection..." -ForegroundColor Cyan
try {
    $context = Get-MgContext -ErrorAction Stop
    if (-not $context) {
        Write-Host "Not connected to Microsoft Graph. Connecting..." -ForegroundColor Yellow
        Connect-MgGraph -Scopes "Application.Read.All","AppRoleAssignment.ReadWrite.All" -NoWelcome
    } else {
        Write-Host "  ✓ Connected as: $($context.Account)" -ForegroundColor Green
    }
} catch {
    Write-Host "Not connected. Connecting to Microsoft Graph..." -ForegroundColor Yellow
    Connect-MgGraph -Scopes "Application.Read.All","AppRoleAssignment.ReadWrite.All" -NoWelcome
}

Write-Host ""

# Get the Managed Identity service principal
Write-Host "Finding Managed Identity..." -ForegroundColor Cyan
try {
    $managedIdentitySP = Get-MgServicePrincipal -Filter "appId eq '$ManagedIdentityClientId'" -ErrorAction Stop
    if (-not $managedIdentitySP) {
        Write-Host "  ✗ Managed Identity not found with Client ID: $ManagedIdentityClientId" -ForegroundColor Red
        exit 1
    }
    Write-Host "  ✓ Found: $($managedIdentitySP.DisplayName)" -ForegroundColor Green
    Write-Host "    Object ID: $($managedIdentitySP.Id)" -ForegroundColor Gray
} catch {
    Write-Host "  ✗ Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Get Microsoft Graph service principal
Write-Host "Finding Microsoft Graph service principal..." -ForegroundColor Cyan
try {
    $graphSP = Get-MgServicePrincipal -Filter "appId eq '00000003-0000-0000-c000-000000000000'" -ErrorAction Stop
    Write-Host "  ✓ Found Microsoft Graph" -ForegroundColor Green
} catch {
    Write-Host "  ✗ Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Find the FileStorageContainer.Selected permission
Write-Host "Finding required permissions..." -ForegroundColor Cyan
$requiredPermissions = @(
    "FileStorageContainer.Selected"
)

foreach ($permissionName in $requiredPermissions) {
    Write-Host "  Checking: $permissionName" -ForegroundColor Gray

    $appRole = $graphSP.AppRoles | Where-Object { $_.Value -eq $permissionName }

    if (-not $appRole) {
        Write-Host "    ✗ Permission not found: $permissionName" -ForegroundColor Red
        Write-Host "    This might be a preview permission. Trying Sites.Selected instead..." -ForegroundColor Yellow
        continue
    }

    Write-Host "    ✓ Found permission: $($appRole.DisplayName)" -ForegroundColor Green
    Write-Host "      ID: $($appRole.Id)" -ForegroundColor Gray

    # Check if permission is already granted
    Write-Host "    Checking if already granted..." -ForegroundColor Gray
    $existingAssignment = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $managedIdentitySP.Id |
        Where-Object { $_.AppRoleId -eq $appRole.Id -and $_.ResourceId -eq $graphSP.Id }

    if ($existingAssignment) {
        Write-Host "    ✓ Permission already granted" -ForegroundColor Green
    } else {
        Write-Host "    Granting permission..." -ForegroundColor Yellow
        try {
            $params = @{
                PrincipalId = $managedIdentitySP.Id
                ResourceId = $graphSP.Id
                AppRoleId = $appRole.Id
            }

            New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $managedIdentitySP.Id -BodyParameter $params -ErrorAction Stop
            Write-Host "    ✓ Permission granted successfully" -ForegroundColor Green
        } catch {
            Write-Host "    ✗ Error granting permission: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

Write-Host ""

# Alternative: Sites.Selected permission (more common)
Write-Host "Checking Sites.Selected permission (alternative)..." -ForegroundColor Cyan
$sitesPermission = $graphSP.AppRoles | Where-Object { $_.Value -eq "Sites.Selected" }

if ($sitesPermission) {
    Write-Host "  ✓ Found Sites.Selected permission" -ForegroundColor Green

    $existingAssignment = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $managedIdentitySP.Id |
        Where-Object { $_.AppRoleId -eq $sitesPermission.Id -and $_.ResourceId -eq $graphSP.Id }

    if ($existingAssignment) {
        Write-Host "  ✓ Sites.Selected already granted" -ForegroundColor Green
    } else {
        Write-Host "  Granting Sites.Selected permission..." -ForegroundColor Yellow
        try {
            $params = @{
                PrincipalId = $managedIdentitySP.Id
                ResourceId = $graphSP.Id
                AppRoleId = $sitesPermission.Id
            }

            New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $managedIdentitySP.Id -BodyParameter $params -ErrorAction Stop
            Write-Host "  ✓ Sites.Selected granted successfully" -ForegroundColor Green
        } catch {
            Write-Host "  ✗ Error: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Yellow
Write-Host ""
Write-Host "Current permissions for Managed Identity:" -ForegroundColor Cyan
$allAssignments = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $managedIdentitySP.Id
Write-Host "  Total permissions: $($allAssignments.Count)" -ForegroundColor Gray

foreach ($assignment in $allAssignments) {
    $resource = Get-MgServicePrincipal -ServicePrincipalId $assignment.ResourceId -ErrorAction SilentlyContinue
    if ($resource) {
        $role = $resource.AppRoles | Where-Object { $_.Id -eq $assignment.AppRoleId }
        if ($role) {
            Write-Host "  - $($role.Value) on $($resource.DisplayName)" -ForegroundColor Gray
        }
    }
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Wait 2-3 minutes for permissions to propagate" -ForegroundColor Gray
Write-Host "2. Re-run the API test: .\Test-SpeApis.ps1" -ForegroundColor Gray
Write-Host "3. If using Sites.Selected, you may need to grant specific container permissions" -ForegroundColor Gray
Write-Host ""
