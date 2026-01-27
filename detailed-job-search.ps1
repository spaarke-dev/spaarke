param([string]$JobId = "d93a3078-b6fb-f011-8407-7c1e520aa4df")

Write-Host "=== Detailed Search for Job: $JobId ===" -ForegroundColor Cyan

# Download logs
$null = az webapp log download --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --log-file detail-logs.zip 2>&1

# Extract eventlog.xml
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path "detail-logs.zip").Path)
$entry = $zip.Entries | Where-Object { $_.FullName -eq "LogFiles/eventlog.xml" }
if ($entry) {
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "detail-eventlog.xml", $true)
}
$zip.Dispose()

if (Test-Path "detail-eventlog.xml") {
    [xml]$log = Get-Content "detail-eventlog.xml"

    # Find all events for this job
    $jobEvents = $log.Events.Event | Where-Object {
        $_.EventData.Data -match $JobId
    } | Sort-Object { [DateTime]::Parse($_.System.TimeCreated.SystemTime) }

    if ($jobEvents) {
        Write-Host "Found $($jobEvents.Count) events for this job" -ForegroundColor Green
        Write-Host ""

        foreach ($event in $jobEvents) {
            $time = [DateTime]::Parse($event.System.TimeCreated.SystemTime)
            $category = ($event.EventData.Data -split "`n" | Select-Object -First 1).Replace("Category: ", "")

            Write-Host "[$($time.ToString('HH:mm:ss'))] $category" -ForegroundColor Yellow
            Write-Host $event.EventData.Data -ForegroundColor Gray
            Write-Host ""
        }
    } else {
        Write-Host "No events found for job ID (this is very unusual)" -ForegroundColor Red
    }

    # Also search for "Upload finalization" or "queue" messages around the same time
    Write-Host "`n=== Recent Service Bus Queue Activity ===" -ForegroundColor Cyan
    $recentTime = (Get-Date).AddMinutes(-5)
    $queueEvents = $log.Events.Event | Where-Object {
        $timeStr = $_.System.TimeCreated.SystemTime
        $eventTime = [DateTime]::Parse($timeStr)
        $eventTime -gt $recentTime -and $_.EventData.Data -match "Upload finalization|queue|ServiceBus|office-upload"
    } | Sort-Object { [DateTime]::Parse($_.System.TimeCreated.SystemTime) } -Descending | Select-Object -First 5

    if ($queueEvents) {
        foreach ($event in $queueEvents) {
            $time = [DateTime]::Parse($event.System.TimeCreated.SystemTime)
            Write-Host "[$($time.ToString('HH:mm:ss'))]" -ForegroundColor Yellow
            Write-Host $event.EventData.Data -ForegroundColor Gray
            Write-Host ""
        }
    } else {
        Write-Host "No Service Bus queue activity in last 5 minutes" -ForegroundColor Red
    }

    Remove-Item "detail-eventlog.xml" -ErrorAction SilentlyContinue
    Remove-Item "detail-logs.zip" -ErrorAction SilentlyContinue
}
