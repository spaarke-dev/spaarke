# Test the visualization API with a known document and tenant

$documentId = "d914a8cc-ddf3-f011-8406-7ced8d1dc988"
$tenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
$apiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net"

Write-Host "Testing Visualization API" -ForegroundColor Cyan
Write-Host "DocumentId: $documentId"
Write-Host "TenantId: $tenantId"
Write-Host ""

$uri = "$apiUrl/api/ai/visualization/related/$documentId`?tenantId=$tenantId"
Write-Host "URL: $uri"
Write-Host ""

try {
    $response = Invoke-WebRequest -Uri $uri -Method GET
    Write-Host "Status: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "Response:"
    $response.Content | ConvertFrom-Json | ConvertTo-Json -Depth 10
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
