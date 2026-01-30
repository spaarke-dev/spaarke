param([string]$JobId = "d93a3078-b6fb-f011-8407-7c1e520aa4df")

Write-Host "=== Complete Job Activity Search ===" -ForegroundColor Cyan
Write-Host "Job ID: $JobId" -ForegroundColor Gray
Write-Host ""

# Download logs
$null = az webapp log download --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --log-file all-logs.zip 2>&1

# Extract eventlog.xml
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path "all-logs.zip").Path)
$entry = $zip.Entries | Where-Object { $_.FullName -eq "LogFiles/eventlog.xml" }
if ($entry) {
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "all-eventlog.xml", $true)
}
$zip.Dispose()

if (Test-Path "all-eventlog.xml") {
    $content = Get-Content "all-eventlog.xml" -Raw

    # Search for job ID mentions
    $lines = $content -split "`n"
    $jobLines = $lines | Select-String -Pattern $JobId

    Write-Host "Found $($jobLines.Count) lines mentioning job ID" -ForegroundColor Green
    Write-Host ""

    # Extract full events
    [xml]$log = Get-Content "all-eventlog.xml"
    $allJobEvents = $log.Events.Event | Where-Object {
        $_.EventData.Data -match $JobId
    } | Sort-Object { [DateTime]::Parse($_.System.TimeCreated.SystemTime) }

    Write-Host "=== Complete Event Timeline ===" -ForegroundColor Cyan
    $eventNum = 1
    foreach ($event in $allJobEvents) {
        $time = [DateTime]::Parse($event.System.TimeCreated.SystemTime).ToLocalTime()
        $category = ($event.EventData.Data -split "`n" | Where-Object { $_ -match "Category:" } | Select-Object -First 1) -replace "Category: ", ""

        Write-Host "[$eventNum] $($time.ToString('HH:mm:ss.fff')) - $category" -ForegroundColor Yellow

        # Show first 500 chars of data
        $data = $event.EventData.Data
        if ($data.Length -gt 1000) {
            $data = $data.Substring(0, 1000) + "..."
        }
        Write-Host $data -ForegroundColor Gray
        Write-Host ""
        $eventNum++
    }

    # Now search for messages queued around the same time
    Write-Host "`n=== Service Bus Queue Activity (14:29:25 - 14:29:40) ===" -ForegroundColor Cyan
    $startTime = [DateTime]::Parse("2026-01-27T14:29:25Z")
    $endTime = [DateTime]::Parse("2026-01-27T14:29:40Z")

    $queueEvents = $log.Events.Event | Where-Object {
        $timeStr = $_.System.TimeCreated.SystemTime
        $eventTime = [DateTime]::Parse($timeStr)
        $eventTime -ge $startTime -and $eventTime -le $endTime -and
        ($_.EventData.Data -match "queue|ServiceBus|SendMessage|office-upload")
    } | Sort-Object { [DateTime]::Parse($_.System.TimeCreated.SystemTime) }

    if ($queueEvents) {
        Write-Host "Found $($queueEvents.Count) queue-related events:" -ForegroundColor Green
        foreach ($event in $queueEvents) {
            $time = [DateTime]::Parse($event.System.TimeCreated.SystemTime).ToLocalTime()
            Write-Host "  [$($time.ToString('HH:mm:ss.fff'))]" -ForegroundColor Yellow
            $data = ($event.EventData.Data -split "`n" | Select-Object -First 10) -join "`n"
            Write-Host "  $data" -ForegroundColor Gray
            Write-Host ""
        }
    } else {
        Write-Host "No Service Bus queue activity in that timeframe" -ForegroundColor Red
    }

    Remove-Item "all-eventlog.xml" -ErrorAction SilentlyContinue
    Remove-Item "all-logs.zip" -ErrorAction SilentlyContinue
}
