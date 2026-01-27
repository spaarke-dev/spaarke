param([string]$JobId = "d93a3078-b6fb-f011-8407-7c1e520aa4df")

Write-Host "=== Checking ProcessingJob Status ===" -ForegroundColor Cyan
Write-Host "Job ID: $JobId" -ForegroundColor Gray
Write-Host ""

try {
    $response = Invoke-RestMethod -Uri "https://spe-api-dev-67e2xz.azurewebsites.net/office/jobs/$JobId" -Method Get -ErrorAction Stop

    Write-Host "Job Status:" -ForegroundColor Yellow
    Write-Host "  Status: $($response.status)" -ForegroundColor White
    Write-Host "  Progress: $($response.progress)%" -ForegroundColor White
    Write-Host "  Current Phase: $($response.currentPhase)" -ForegroundColor White
    Write-Host "  Created: $($response.createdAt)" -ForegroundColor Gray
    if ($response.completedAt) {
        Write-Host "  Completed: $($response.completedAt)" -ForegroundColor Gray
    }
    if ($response.error) {
        Write-Host "  Error: $($response.error.message)" -ForegroundColor Red
    }

    Write-Host "`nFull Response:" -ForegroundColor Cyan
    $response | ConvertTo-Json -Depth 5
} catch {
    Write-Host "ERROR: Failed to get job status" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}
