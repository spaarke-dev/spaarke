$settings = az webapp config appsettings list --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --output json | ConvertFrom-Json

Write-Host "=== Redis Configuration ===" -ForegroundColor Cyan
$redisSettings = $settings | Where-Object { $_.name -match 'Redis' }
if ($redisSettings) {
    $redisSettings | Format-Table name, value -AutoSize
} else {
    Write-Host "No Redis configuration found" -ForegroundColor Yellow
}

Write-Host "`n=== ServiceBus Configuration ===" -ForegroundColor Cyan
$sbSettings = $settings | Where-Object { $_.name -match 'ServiceBus' }
if ($sbSettings) {
    $sbSettings | Format-Table name, value -AutoSize
} else {
    Write-Host "No ServiceBus configuration found" -ForegroundColor Yellow
}
