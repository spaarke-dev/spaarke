param([string]$JobId = "d93a3078-b6fb-f011-8407-7c1e520aa4df")

Write-Host "=== Searching for UploadFinalizationWorker Activity ===" -ForegroundColor Cyan
Write-Host "Job ID: $JobId" -ForegroundColor Gray
Write-Host ""

# Download logs
$null = az webapp log download --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --log-file upload-logs.zip 2>&1

# Extract eventlog.xml
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path "upload-logs.zip").Path)
$entry = $zip.Entries | Where-Object { $_.FullName -eq "LogFiles/eventlog.xml" }
if ($entry) {
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "upload-eventlog.xml", $true)
}
$zip.Dispose()

if (Test-Path "upload-eventlog.xml") {
    [xml]$log = Get-Content "upload-eventlog.xml"

    # Find UploadFinalizationWorker events
    $uploadEvents = $log.Events.Event | Where-Object {
        $_.EventData.Data -match "UploadFinalizationWorker" -and $_.EventData.Data -match $JobId
    } | Sort-Object { [DateTime]::Parse($_.System.TimeCreated.SystemTime) }

    if ($uploadEvents) {
        Write-Host "Found $($uploadEvents.Count) UploadFinalizationWorker events:" -ForegroundColor Green
        Write-Host ""

        foreach ($event in $uploadEvents) {
            $time = [DateTime]::Parse($event.System.TimeCreated.SystemTime)
            Write-Host "[$($time.ToString('HH:mm:ss'))]" -ForegroundColor Yellow
            Write-Host $event.EventData.Data -ForegroundColor Gray
            Write-Host ""
        }
    } else {
        Write-Host "No UploadFinalizationWorker events found for this job" -ForegroundColor Red
        Write-Host ""
        Write-Host "Checking for ANY UploadFinalizationWorker activity in last 5 minutes..." -ForegroundColor Cyan

        $recentTime = (Get-Date).AddMinutes(-5)
        $recentUpload = $log.Events.Event | Where-Object {
            $timeStr = $_.System.TimeCreated.SystemTime
            $eventTime = [DateTime]::Parse($timeStr)
            $eventTime -gt $recentTime -and $_.EventData.Data -match "UploadFinalizationWorker"
        } | Sort-Object { [DateTime]::Parse($_.System.TimeCreated.SystemTime) } -Descending | Select-Object -First 5

        if ($recentUpload) {
            Write-Host "Found recent UploadFinalizationWorker activity:" -ForegroundColor Yellow
            foreach ($event in $recentUpload) {
                $time = [DateTime]::Parse($event.System.TimeCreated.SystemTime)
                Write-Host "[$($time.ToString('HH:mm:ss'))]" -ForegroundColor Yellow
                Write-Host ($event.EventData.Data -split "`n" | Select-Object -First 100 -Join "`n") -ForegroundColor Gray
                Write-Host ""
            }
        } else {
            Write-Host "No UploadFinalizationWorker activity at all in last 5 minutes" -ForegroundColor Red
        }
    }

    # Also check for EmailArtifact creation
    Write-Host "`n=== Checking for EmailArtifact/Document creation ===" -ForegroundColor Cyan
    $artifactEvents = $log.Events.Event | Where-Object {
        $_.EventData.Data -match "EmailArtifact|Document.*created" -and $_.EventData.Data -match $JobId
    } | Sort-Object { [DateTime]::Parse($_.System.TimeCreated.SystemTime) }

    if ($artifactEvents) {
        Write-Host "Found artifact creation events:" -ForegroundColor Green
        foreach ($event in $artifactEvents) {
            $time = [DateTime]::Parse($event.System.TimeCreated.SystemTime)
            Write-Host "[$($time.ToString('HH:mm:ss'))]" -ForegroundColor Yellow
            Write-Host $event.EventData.Data -ForegroundColor Gray
            Write-Host ""
        }
    } else {
        Write-Host "No artifact creation events found" -ForegroundColor Yellow
    }

    Remove-Item "upload-eventlog.xml" -ErrorAction SilentlyContinue
    Remove-Item "upload-logs.zip" -ErrorAction SilentlyContinue
}
