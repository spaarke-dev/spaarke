Write-Host "=== Checking Email Body Debug Logs ===" -ForegroundColor Cyan

$null = az webapp log download --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --log-file body-debug-logs.zip 2>&1

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path "body-debug-logs.zip").Path)
$entry = $zip.Entries | Where-Object { $_.FullName -eq "LogFiles/eventlog.xml" }
if ($entry) {
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "body-debug-eventlog.xml", $true)
}
$zip.Dispose()

if (Test-Path "body-debug-eventlog.xml") {
    [xml]$log = Get-Content "body-debug-eventlog.xml"

    $recentTime = (Get-Date).AddMinutes(-5)
    $bodyLogs = $log.Events.Event | Where-Object {
        $timeStr = $_.System.TimeCreated.SystemTime
        $eventTime = [DateTime]::Parse($timeStr)
        $eventTime -gt $recentTime -and
        $_.EventData.Data -match "EMAIL BODY DEBUG|Save requested for Email"
    } | Sort-Object { [DateTime]::Parse($_.System.TimeCreated.SystemTime) } -Descending | Select-Object -First 10

    if ($bodyLogs) {
        Write-Host "`nFound $($bodyLogs.Count) recent email save logs:" -ForegroundColor Green
        foreach ($e in $bodyLogs) {
            $time = [DateTime]::Parse($e.System.TimeCreated.SystemTime).ToLocalTime()
            Write-Host "`n[$($time.ToString('HH:mm:ss'))]" -ForegroundColor Yellow
            Write-Host $e.EventData.Data -ForegroundColor White
        }
    } else {
        Write-Host "`nNo recent email save logs found" -ForegroundColor Red
    }

    Remove-Item "body-debug-eventlog.xml" -ErrorAction SilentlyContinue
    Remove-Item "body-debug-logs.zip" -ErrorAction SilentlyContinue
}
