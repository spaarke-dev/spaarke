param([string]$JobId = "85605b87-b4fb-f011-8407-7c1e520aa4df")

Write-Host "=== Searching logs for Job ID: $JobId ===" -ForegroundColor Cyan

# Download logs
$null = az webapp log download --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --log-file search-logs.zip 2>&1

# Extract eventlog.xml
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path "search-logs.zip").Path)
$entry = $zip.Entries | Where-Object { $_.FullName -eq "LogFiles/eventlog.xml" }
if ($entry) {
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "search-eventlog.xml", $true)
}
$zip.Dispose()

if (Test-Path "search-eventlog.xml") {
    $content = Get-Content "search-eventlog.xml" -Raw

    # Search for job ID
    if ($content -match $JobId) {
        Write-Host "Found job ID in logs!" -ForegroundColor Green

        # Extract relevant sections
        $lines = $content -split "`n"
        $relevantLines = $lines | Where-Object { $_ -match $JobId }

        Write-Host "`nRelevant log entries:" -ForegroundColor Yellow
        foreach ($line in $relevantLines | Select-Object -First 10) {
            # Extract the data portion
            if ($line -match '<Data>(.*?)</Data>') {
                Write-Host $matches[1] -ForegroundColor Gray
                Write-Host ""
            }
        }
    } else {
        Write-Host "Job ID NOT found in event logs" -ForegroundColor Red
        Write-Host "This suggests the SaveEmail API call may have failed before logging" -ForegroundColor Yellow
    }

    # Search for recent "Upload finalization" or "Service Bus" messages
    Write-Host "`n=== Recent Service Bus Activity ===" -ForegroundColor Cyan
    if ($content -match "Upload finalization|ServiceBus|queue.*office") {
        $sbLines = $lines | Where-Object { $_ -match "Upload finalization|ServiceBus|queue.*office" } | Select-Object -Last 5
        foreach ($line in $sbLines) {
            if ($line -match '<Data>(.*?)</Data>') {
                Write-Host $matches[1] -ForegroundColor Gray
            }
        }
    } else {
        Write-Host "No Service Bus activity found in recent logs" -ForegroundColor Red
    }

    Remove-Item "search-eventlog.xml" -ErrorAction SilentlyContinue
    Remove-Item "search-logs.zip" -ErrorAction SilentlyContinue
} else {
    Write-Host "Could not extract event log" -ForegroundColor Red
}
