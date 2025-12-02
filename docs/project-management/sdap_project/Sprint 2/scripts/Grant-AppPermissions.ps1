# Grant-AppPermissions.ps1

# Connect to Microsoft Graph
Write-Host 'Connecting to Microsoft Graph...' -ForegroundColor Cyan
Connect-MgGraph -Scopes "Application.Read.All","AppRoleAssignment.ReadWrite.All" -NoWelcome

Write-Host 'Granting FileStorageContainer.Selected permission to Spaarke DSM-SPE Dev 2...' -ForegroundColor Cyan

# Get the service principals
$appSP = Get-MgServicePrincipal -Filter "appId eq '170c98e1-d486-4355-bcbe-170454e0207c'"
$graphSP = Get-MgServicePrincipal -Filter "appId eq '00000003-0000-0000-c000-000000000000'"

Write-Host "  App SP: $($appSP.DisplayName)" -ForegroundColor Gray
Write-Host "  Object ID: $($appSP.Id)" -ForegroundColor Gray

# Find FileStorageContainer.Selected permission
$permission = $graphSP.AppRoles | Where-Object { $_.Value -eq 'FileStorageContainer.Selected' }

if ($permission) {
    Write-Host '  Found FileStorageContainer.Selected' -ForegroundColor Green
    Write-Host "  Permission ID: $($permission.Id)" -ForegroundColor Gray

    # Check if already granted
    $existing = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $appSP.Id |
        Where-Object { $_.AppRoleId -eq $permission.Id -and $_.ResourceId -eq $graphSP.Id }

    if ($existing) {
        Write-Host '  ✓ Already granted!' -ForegroundColor Green
    } else {
        Write-Host '  Granting permission...' -ForegroundColor Yellow
        try {
            New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $appSP.Id -BodyParameter @{
                PrincipalId = $appSP.Id
                ResourceId = $graphSP.Id
                AppRoleId = $permission.Id
            } | Out-Null
            Write-Host '  ✓ Permission granted successfully!' -ForegroundColor Green
        } catch {
            Write-Host "  ✗ Error: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
} else {
    Write-Host '  FileStorageContainer.Selected not found' -ForegroundColor Yellow
    Write-Host '  Trying Sites.Selected instead...' -ForegroundColor Yellow

    $sitesPermission = $graphSP.AppRoles | Where-Object { $_.Value -eq 'Sites.Selected' }

    if ($sitesPermission) {
        Write-Host "  Found Sites.Selected (ID: $($sitesPermission.Id))" -ForegroundColor Green

        $existing = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $appSP.Id |
            Where-Object { $_.AppRoleId -eq $sitesPermission.Id -and $_.ResourceId -eq $graphSP.Id }

        if ($existing) {
            Write-Host '  ✓ Sites.Selected already granted!' -ForegroundColor Green
        } else {
            try {
                New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $appSP.Id -BodyParameter @{
                    PrincipalId = $appSP.Id
                    ResourceId = $graphSP.Id
                    AppRoleId = $sitesPermission.Id
                } | Out-Null
                Write-Host '  ✓ Sites.Selected granted!' -ForegroundColor Green
            } catch {
                Write-Host "  ✗ Error: $($_.Exception.Message)" -ForegroundColor Red
            }
        }
    }
}

Write-Host ""
Write-Host "Current Graph API permissions:" -ForegroundColor Cyan
$assignments = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $appSP.Id |
    Where-Object { $_.ResourceId -eq $graphSP.Id }

foreach ($assignment in $assignments) {
    $role = $graphSP.AppRoles | Where-Object { $_.Id -eq $assignment.AppRoleId }
    if ($role) {
        Write-Host "  - $($role.Value)" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "Wait 2-3 minutes for permissions to propagate, then retry the test." -ForegroundColor Yellow
