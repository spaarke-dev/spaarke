<#
.SYNOPSIS
    Queries tenant license SKUs and outputs IDs for demo provisioning.
.EXAMPLE
    .\Get-LicenseSkuIds.ps1
#>

Import-Module Microsoft.Graph.Identity.DirectoryManagement -ErrorAction Stop
Connect-MgGraph -Scopes "Organization.Read.All" -NoWelcome -ErrorAction Stop
Write-Host "Connected to Microsoft Graph" -ForegroundColor Green

$skus = Get-MgSubscribedSku -All
Write-Host "Found $($skus.Count) SKUs in tenant" -ForegroundColor Cyan

# Target SKU part numbers
$targets = @(
    @{ Name = "Power Apps Plan 2 Trial"; Parts = @("POWERAPPS_VIRAL","POWERAPPS_PER_USER","POWERAPPS_DEV") },
    @{ Name = "Microsoft Fabric Free"; Parts = @("POWER_BI_STANDARD","PBI_FABRIC_FREE") },
    @{ Name = "Power Automate Free"; Parts = @("FLOW_FREE","FLOW_P1_VIRAL") }
)

Write-Host ""
Write-Host "=== Required License SKUs ===" -ForegroundColor Yellow

foreach ($t in $targets) {
    $found = $false
    foreach ($part in $t.Parts) {
        $match = $skus | Where-Object { $_.SkuPartNumber -eq $part }
        if ($match) {
            $avail = $match.PrepaidUnits.Enabled - $match.ConsumedUnits
            Write-Host "  $($t.Name): $($match.SkuId) [$($match.SkuPartNumber)] - $avail available" -ForegroundColor Green
            $found = $true
            break
        }
    }
    if (-not $found) {
        Write-Host "  $($t.Name): NOT FOUND - check tenant licenses" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== All Tenant SKUs ===" -ForegroundColor Cyan
$skus | Sort-Object SkuPartNumber | ForEach-Object {
    $avail = $_.PrepaidUnits.Enabled - $_.ConsumedUnits
    Write-Host "  $($_.SkuPartNumber): $($_.SkuId) - $avail avail" -ForegroundColor Gray
}

Disconnect-MgGraph -ErrorAction SilentlyContinue | Out-Null
