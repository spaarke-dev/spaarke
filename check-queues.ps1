Write-Host "Waiting 30 seconds for workers to process messages..." -ForegroundColor Yellow
Start-Sleep -Seconds 30

Write-Host "`n=== Checking Service Bus Queue Status ===" -ForegroundColor Cyan

$queues = @("office-upload-finalization", "office-profile", "office-indexing")

foreach ($queue in $queues) {
    Write-Host "`nQueue: $queue" -ForegroundColor White
    $stats = az servicebus queue show `
        --resource-group SharePointEmbedded `
        --namespace-name spaarke-servicebus-dev `
        --name $queue `
        --query "{Active:messageCount, DeadLetter:deadLetterMessageCount}" `
        --output json | ConvertFrom-Json

    Write-Host "  Active messages: $($stats.Active)" -ForegroundColor $(if ($stats.Active -eq 0) { "Green" } else { "Yellow" })
    Write-Host "  Dead letter: $($stats.DeadLetter)" -ForegroundColor $(if ($stats.DeadLetter -eq 0) { "Green" } else { "Red" })
}
