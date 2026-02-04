# Download logs quietly
$null = az webapp log download --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --log-file temp-logs.zip 2>&1

# Extract eventlog.xml only
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path "temp-logs.zip").Path)
$entry = $zip.Entries | Where-Object { $_.FullName -eq "LogFiles/eventlog.xml" }
if ($entry) {
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "temp-eventlog.xml", $true)
}
$zip.Dispose()

if (Test-Path "temp-eventlog.xml") {
    Write-Host "=== Recent Worker Activity ===" -ForegroundColor Cyan

    # Read the event log
    [xml]$log = Get-Content "temp-eventlog.xml"

    # Get recent events (last 10 minutes)
    $recentTime = (Get-Date).AddMinutes(-10)
    $events = $log.Events.Event | Where-Object {
        $timeStr = $_.System.TimeCreated.SystemTime
        $eventTime = [DateTime]::Parse($timeStr)
        $eventTime -gt $recentTime
    } | Sort-Object { [DateTime]::Parse($_.System.TimeCreated.SystemTime) } -Descending

    Write-Host "Found $($events.Count) events in last 10 minutes" -ForegroundColor Gray
    Write-Host ""

    # Check for worker-related messages
    $workerEvents = $events | Where-Object {
        $_.EventData.Data -match "Worker|ServiceBus|ProcessingJob|status"
    } | Select-Object -First 10

    if ($workerEvents) {
        foreach ($event in $workerEvents) {
            $time = [DateTime]::Parse($event.System.TimeCreated.SystemTime)
            Write-Host "[$($time.ToString('HH:mm:ss'))]" -ForegroundColor Yellow -NoNewline
            Write-Host " $($event.EventData.Data | Out-String | Select-Object -First 200)" -ForegroundColor Gray
            Write-Host ""
        }
    } else {
        Write-Host "No worker-related events found in last 10 minutes" -ForegroundColor Red
    }

    # Clean up
    Remove-Item "temp-eventlog.xml" -ErrorAction SilentlyContinue
    Remove-Item "temp-logs.zip" -ErrorAction SilentlyContinue
} else {
    Write-Host "Could not extract eventlog.xml" -ForegroundColor Red
}
