Write-Host "=== Searching for /office/save Endpoint Calls ===" -ForegroundColor Cyan

# Download logs
$null = az webapp log download --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --log-file save-logs.zip 2>&1

# Extract eventlog.xml
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path "save-logs.zip").Path)
$entry = $zip.Entries | Where-Object { $_.FullName -eq "LogFiles/eventlog.xml" }
if ($entry) {
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "save-eventlog.xml", $true)
}
$zip.Dispose()

if (Test-Path "save-eventlog.xml") {
    [xml]$log = Get-Content "save-eventlog.xml"

    # Search for Save endpoint calls in last 10 minutes
    $recentTime = (Get-Date).AddMinutes(-10)
    $saveEvents = $log.Events.Event | Where-Object {
        $timeStr = $_.System.TimeCreated.SystemTime
        $eventTime = [DateTime]::Parse($timeStr)
        $eventTime -gt $recentTime -and
        ($_.EventData.Data -match "/office/save|Save requested|SaveAsync|OfficeService.*Save")
    } | Sort-Object { [DateTime]::Parse($_.System.TimeCreated.SystemTime) } -Descending

    if ($saveEvents) {
        Write-Host "Found $($saveEvents.Count) Save-related events in last 10 minutes:" -ForegroundColor Green
        Write-Host ""

        foreach ($event in $saveEvents) {
            $time = [DateTime]::Parse($event.System.TimeCreated.SystemTime).ToLocalTime()
            Write-Host "[$($time.ToString('HH:mm:ss'))]" -ForegroundColor Yellow
            Write-Host $event.EventData.Data -ForegroundColor Gray
            Write-Host ""
        }
    } else {
        Write-Host "No Save endpoint calls found in last 10 minutes!" -ForegroundColor Red
        Write-Host ""
        Write-Host "This means:" -ForegroundColor Yellow
        Write-Host "  1. The add-in is NOT calling the /office/save endpoint, OR" -ForegroundColor Gray
        Write-Host "  2. The endpoint is rejecting the request before logging, OR" -ForegroundColor Gray
        Write-Host "  3. The add-in is calling a different endpoint" -ForegroundColor Gray
    }

    # Also search for ANY office endpoint calls
    Write-Host "`n=== All /office/* Endpoint Activity ===" -ForegroundColor Cyan
    $officeEvents = $log.Events.Event | Where-Object {
        $timeStr = $_.System.TimeCreated.SystemTime
        $eventTime = [DateTime]::Parse($timeStr)
        $eventTime -gt $recentTime -and
        $_.EventData.Data -match "RequestPath.*:/office/"
    } | Sort-Object { [DateTime]::Parse($_.System.TimeCreated.SystemTime) } -Descending | Select-Object -First 10

    if ($officeEvents) {
        Write-Host "Found $($officeEvents.Count) office endpoint calls:" -ForegroundColor Green
        foreach ($event in $officeEvents) {
            $time = [DateTime]::Parse($event.System.TimeCreated.SystemTime).ToLocalTime()
            $path = if ($event.EventData.Data -match "RequestPath: (.*)") { $matches[1] } else { "unknown" }
            Write-Host "  [$($time.ToString('HH:mm:ss'))] $path" -ForegroundColor White
        }
    }

    Remove-Item "save-eventlog.xml" -ErrorAction SilentlyContinue
    Remove-Item "save-logs.zip" -ErrorAction SilentlyContinue
}
