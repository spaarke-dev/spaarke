$logFiles = Get-ChildItem -Path app-logs-extracted/LogFiles/Application -Filter "*.txt" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($logFiles) {
    foreach ($file in $logFiles) {
        Write-Host "=== $($file.Name) (Last modified: $($file.LastWriteTime)) ===" -ForegroundColor Cyan
        Write-Host "File size: $($file.Length) bytes" -ForegroundColor Gray
        $content = Get-Content $file.FullName -Tail 200
        Write-Host "Total lines (last 200): $($content.Count)" -ForegroundColor Gray
        Write-Host "`nContent:" -ForegroundColor Yellow
        $content | Select-Object -Last 50
    }
} else {
    Write-Host "No application log files found!" -ForegroundColor Red
    Write-Host "Checking if directory exists..."
    Test-Path "app-logs-extracted/LogFiles/Application"
}
