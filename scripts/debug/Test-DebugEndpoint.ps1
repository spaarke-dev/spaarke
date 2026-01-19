# Test the debug endpoint with the document from console

$documentId = "176e540e-27f4-f011-8406-7c1e520aa4df"
$apiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net"

Write-Host "Testing Debug Endpoint" -ForegroundColor Cyan
Write-Host "DocumentId: $documentId"
Write-Host ""

$uri = "$apiUrl/api/ai/visualization/debug/$documentId"
Write-Host "URL: $uri"
Write-Host ""

try {
    $response = Invoke-RestMethod -Uri $uri -Method GET
    Write-Host "Response:" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 5
} catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    Write-Host "Status: $statusCode" -ForegroundColor Red

    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $errorBody = $reader.ReadToEnd()
        Write-Host "Error: $errorBody" -ForegroundColor Yellow
    } else {
        Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}
