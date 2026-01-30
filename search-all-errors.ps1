Write-Host "=== Searching for ALL Errors Around 14:29:20-14:29:30 ===" -ForegroundColor Cyan

# Download logs
$null = az webapp log download --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --log-file errors-logs.zip 2>&1

# Extract eventlog.xml
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path "errors-logs.zip").Path)
$entry = $zip.Entries | Where-Object { $_.FullName -eq "LogFiles/eventlog.xml" }
if ($entry) {
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "errors-eventlog.xml", $true)
}
$zip.Dispose()

if (Test-Path "errors-eventlog.xml") {
    [xml]$log = Get-Content "errors-eventlog.xml"

    # Find all events in timeframe
    $startTime = [DateTime]::Parse("2026-01-27T19:29:20Z")
    $endTime = [DateTime]::Parse("2026-01-27T19:29:35Z")

    $events = $log.Events.Event | Where-Object {
        $timeStr = $_.System.TimeCreated.SystemTime
        $eventTime = [DateTime]::Parse($timeStr)
        $eventTime -ge $startTime -and $eventTime -le $endTime
    } | Sort-Object { [DateTime]::Parse($_.System.TimeCreated.SystemTime) }

    Write-Host "Found $($events.Count) events in timeframe" -ForegroundColor Green
    Write-Host ""

    foreach ($event in $events) {
        $time = [DateTime]::Parse($event.System.TimeCreated.SystemTime).ToLocalTime()
        $category = ($event.EventData.Data -split "`n" | Where-Object { $_ -match "Category:" } | Select-Object -First 1) -replace "Category: ", ""
        $level = $event.System.Level

        $color = switch ($level) {
            1 { "Red" }      # Critical
            2 { "Red" }      # Error
            3 { "Yellow" }   # Warning
            4 { "White" }    # Information
            default { "Gray" }
        }

        Write-Host "[$($time.ToString('HH:mm:ss.fff'))] Level=$level - $category" -ForegroundColor $color

        # Show data (first 800 chars)
        $data = $event.EventData.Data
        if ($data.Length -gt 800) {
            $data = $data.Substring(0, 800) + "`n..."
        }
        Write-Host $data -ForegroundColor Gray
        Write-Host ""
    }

    Remove-Item "errors-eventlog.xml" -ErrorAction SilentlyContinue
    Remove-Item "errors-logs.zip" -ErrorAction SilentlyContinue
}
