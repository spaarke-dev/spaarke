Write-Host "=== Checking App Start Time ===" -ForegroundColor Cyan

# Download logs
$null = az webapp log download --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --log-file ts-logs.zip 2>&1

# Extract eventlog.xml
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path "ts-logs.zip").Path)
$entry = $zip.Entries | Where-Object { $_.FullName -eq "LogFiles/eventlog.xml" }
if ($entry) {
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "ts-eventlog.xml", $true)
}
$zip.Dispose()

if (Test-Path "ts-eventlog.xml") {
    [xml]$log = Get-Content "ts-eventlog.xml"

    # Find app start events
    $startEvents = $log.Events.Event | Where-Object {
        $_.System.Provider.Name -eq "IIS AspNetCore Module V2" -and $_.EventData.Data -like "*started successfully*"
    } | Sort-Object { [DateTime]::Parse($_.System.TimeCreated.SystemTime) } -Descending | Select-Object -First 3

    Write-Host "Recent app starts:" -ForegroundColor Yellow
    foreach ($e in $startEvents) {
        $time = [DateTime]::Parse($e.System.TimeCreated.SystemTime)
        $localTime = $time.ToLocalTime()
        Write-Host "  $($localTime.ToString('yyyy-MM-dd HH:mm:ss')) (UTC: $($time.ToString('HH:mm:ss')))" -ForegroundColor White
    }

    # Also check for recent compilation/build info
    $distCacheEvents = $log.Events.Event | Where-Object {
        $_.EventData.Data -match "Distributed cache.*Redis|JobStatusService initialized"
    } | Sort-Object { [DateTime]::Parse($_.System.TimeCreated.SystemTime) } -Descending | Select-Object -First 1

    if ($distCacheEvents) {
        Write-Host "`nMost recent app initialization:" -ForegroundColor Cyan
        $time = [DateTime]::Parse($distCacheEvents.System.TimeCreated.SystemTime)
        $localTime = $time.ToLocalTime()
        Write-Host "  $($localTime.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor White
        Write-Host "  $(($distCacheEvents.EventData.Data -split "`n" | Select-Object -First 200 -Join "`n"))" -ForegroundColor Gray
    }

    Remove-Item "ts-eventlog.xml" -ErrorAction SilentlyContinue
    Remove-Item "ts-logs.zip" -ErrorAction SilentlyContinue
}
