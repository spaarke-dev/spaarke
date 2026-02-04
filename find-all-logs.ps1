Write-Host "=== Finding All Recent Log Files ===" -ForegroundColor Cyan

# Check for stdout/stderr logs (these contain actual application output)
$stdoutFiles = Get-ChildItem -Path "fresh-logs/LogFiles" -Filter "*stdout*.log" -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 3

if ($stdoutFiles) {
    Write-Host "`nFound stdout logs (application output):" -ForegroundColor Green
    foreach ($file in $stdoutFiles) {
        $relativePath = $file.FullName.Replace((Get-Location).Path, ".").Replace("\", "/")
        Write-Host "  $relativePath - Last modified: $($file.LastWriteTime)" -ForegroundColor Gray
    }

    $latestStdout = $stdoutFiles | Select-Object -First 1
    Write-Host "`n=== Latest stdout log: $($latestStdout.Name) ===" -ForegroundColor Yellow
    Write-Host "Size: $($latestStdout.Length) bytes" -ForegroundColor Gray

    $content = Get-Content $latestStdout.FullName -ErrorAction SilentlyContinue
    if ($content) {
        Write-Host "`nSearching for Worker/ServiceBus/Distributed keywords..." -ForegroundColor Cyan
        $relevantLines = $content | Where-Object { $_ -match "Worker|ServiceBus|Background|Distributed|Redis|Exception|Error" }

        if ($relevantLines) {
            Write-Host "Found $($relevantLines.Count) relevant lines:" -ForegroundColor Green
            $relevantLines | Select-Object -First 100
        } else {
            Write-Host "No worker-related keywords found." -ForegroundColor Yellow
            Write-Host "`nShowing last 50 lines:" -ForegroundColor Gray
            $content | Select-Object -Last 50
        }
    } else {
        Write-Host "Could not read file content" -ForegroundColor Red
    }
} else {
    Write-Host "No stdout log files found" -ForegroundColor Yellow

    # Check what files ARE there
    Write-Host "`nListing all recent logs in LogFiles directory:" -ForegroundColor Cyan
    Get-ChildItem -Path "fresh-logs/LogFiles" -Recurse -File |
        Where-Object { $_.LastWriteTime -gt (Get-Date).AddMinutes(-10) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 10 |
        ForEach-Object {
            $relativePath = $_.FullName.Replace((Get-Location).Path + "\", "")
            Write-Host "  $relativePath ($($_.Length) bytes) - $($_.LastWriteTime)" -ForegroundColor Gray
        }
}
