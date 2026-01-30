$logFiles = Get-ChildItem -Path app-logs-extracted/LogFiles/Application -Filter "*.txt" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 2

foreach ($file in $logFiles) {
    Write-Host "`n=== $($file.Name) (Last modified: $($file.LastWriteTime)) ===" -ForegroundColor Cyan
    Get-Content $file.FullName -Tail 100 | Where-Object { $_ -match "Worker|ServiceBus|BackgroundService|Distributed|Redis|Exception|Error" }
}
