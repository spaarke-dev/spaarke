# Decode SharePoint permission IDs
$spAppId = "00000003-0000-0ff1-ce00-000000000000"

Write-Host "Querying SharePoint service principal..." -ForegroundColor Gray
$spJson = az ad sp show --id $spAppId
$sp = $spJson | ConvertFrom-Json

# Permission IDs from PCF app
$roleId = "19766c1b-905b-43af-8756-06526ab42875"
$scopeId = "4d114b1a-3649-4764-9dfb-be1e236ff371"

$role = $sp.appRoles | Where-Object {$_.id -eq $roleId}
$scope = $sp.oauth2PermissionScopes | Where-Object {$_.id -eq $scopeId}

Write-Host ""
Write-Host "PCF App SharePoint Permissions:" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Application Permission (Role):" -ForegroundColor Yellow
if ($role) {
    Write-Host "  - $($role.value) : $($role.displayName)" -ForegroundColor White
} else {
    Write-Host "  - Unknown role ID: $roleId" -ForegroundColor Red
}

Write-Host ""
Write-Host "Delegated Permission (Scope):" -ForegroundColor Yellow
if ($scope) {
    Write-Host "  - $($scope.value) : $($scope.adminConsentDisplayName)" -ForegroundColor White
} else {
    Write-Host "  - Unknown scope ID: $scopeId" -ForegroundColor Red
}
Write-Host ""

# Check if it has Container.Selected
$containerSelected = $sp.appRoles | Where-Object {$_.value -eq "Container.Selected"}
if ($containerSelected) {
    Write-Host "Container.Selected Permission Details:" -ForegroundColor Cyan
    Write-Host "  ID: $($containerSelected.id)" -ForegroundColor Gray
    Write-Host "  Display Name: $($containerSelected.displayName)" -ForegroundColor Gray

    # Check if PCF app has this permission granted
    if ($role -and $role.value -eq "Container.Selected") {
        Write-Host "  ✅ PCF app HAS Container.Selected!" -ForegroundColor Green
    } else {
        Write-Host "  ❌ PCF app does NOT have Container.Selected" -ForegroundColor Red
    }
}
