Write-Host "Checking dead-letter messages in office-upload-finalization queue..." -ForegroundColor Cyan

# Get Service Bus connection details
$namespace = "spaarke-servicebus-dev"
$resourceGroup = "spe-infrastructure-westus2"
$queueName = "office-upload-finalization/$deadletterqueue"

# Get latest dead-letter messages from eventlog
$logPath = "latest-logs/LogFiles/eventlog.prev.xml"
if (Test-Path $logPath) {
    Write-Host "`nSearching event log for dead-letter errors..." -ForegroundColor Yellow

    $content = Get-Content $logPath -Raw

    # Extract dead-letter related errors
    $pattern = 'dead-lettered after \d+ attempts.*?(?=</Data>)'
    $matches = [regex]::Matches($content, $pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)

    Write-Host "`nFound $($matches.Count) dead-letter entries. Showing last 5:" -ForegroundColor Yellow

    $matches | Select-Object -Last 5 | ForEach-Object {
        Write-Host "`n---" -ForegroundColor Gray
        Write-Host $_.Value -ForegroundColor White
    }
} else {
    Write-Host "Event log not found at $logPath" -ForegroundColor Red
}

# Also check for UploadFinalizationWorker exceptions
Write-Host "`n`nSearching for UploadFinalizationWorker exceptions..." -ForegroundColor Yellow
$exPattern = 'UploadFinalizationWorker.*?Exception:.*?(?=</Data>)'
$exMatches = [regex]::Matches($content, $exPattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)

Write-Host "Found $($exMatches.Count) exception entries. Showing last 3:" -ForegroundColor Yellow

$exMatches | Select-Object -Last 3 | ForEach-Object {
    Write-Host "`n---" -ForegroundColor Gray
    $text = $_.Value -replace 'UploadFinalizationWorker\s*', '' -replace '\s+', ' '
    Write-Host $text.Substring(0, [Math]::Min(500, $text.Length)) -ForegroundColor White
}
