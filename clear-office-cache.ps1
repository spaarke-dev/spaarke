# Clear Office Add-in Cache (Windows)
Write-Host "Clearing Office Add-in cache..." -ForegroundColor Cyan

# Close Outlook
Write-Host "Please close Outlook if it's running..." -ForegroundColor Yellow
Read-Host "Press Enter when Outlook is closed"

# Clear Local Storage
$localAppData = $env:LOCALAPPDATA
$cacheLocations = @(
    "$localAppData\Microsoft\Office\16.0\Wef",
    "$localAppData\Microsoft\Office\Wef"
)

foreach ($location in $cacheLocations) {
    if (Test-Path $location) {
        Write-Host "Clearing: $location" -ForegroundColor Green
        Remove-Item -Path "$location\*" -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Clear IE Cache (Office uses IE engine for some operations)
Write-Host "Clearing IE cache..." -ForegroundColor Green
RunDll32.exe InetCpl.cpl,ClearMyTracksByProcess 8

Write-Host "`nOffice cache cleared. Please restart Outlook and try sideloading again." -ForegroundColor Cyan
