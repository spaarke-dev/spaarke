Write-Host "=== Checking Fresh Application Logs ===" -ForegroundColor Cyan

$appLogDir = "fresh-logs/LogFiles/Application"
if (Test-Path $appLogDir) {
    $files = Get-ChildItem -Path $appLogDir -Filter "*.txt" | Sort-Object LastWriteTime -Descending

    if ($files) {
        Write-Host "`nFound $($files.Count) application log file(s):" -ForegroundColor Green

        $latestFile = $files | Select-Object -First 1
        Write-Host "`nLatest file: $($latestFile.Name)" -ForegroundColor Yellow
        Write-Host "Last modified: $($latestFile.LastWriteTime)" -ForegroundColor Gray
        Write-Host "Size: $($latestFile.Length) bytes" -ForegroundColor Gray

        Write-Host "`n=== Log Content (searching for Worker/ServiceBus/Error) ===" -ForegroundColor Cyan
        $content = Get-Content $latestFile.FullName
        $relevantLines = $content | Where-Object { $_ -match "Worker|ServiceBus|Background|Distributed|Redis|Exception|Error|fail|warn" }

        if ($relevantLines) {
            $relevantLines | Select-Object -First 50
        } else {
            Write-Host "No Worker/ServiceBus/Error keywords found" -ForegroundColor Yellow
            Write-Host "`nShowing last 30 lines of log:" -ForegroundColor Gray
            $content | Select-Object -Last 30
        }
    } else {
        Write-Host "No .txt files in Application logs directory" -ForegroundColor Yellow
    }
} else {
    Write-Host "Application logs directory not found: $appLogDir" -ForegroundColor Red
}
