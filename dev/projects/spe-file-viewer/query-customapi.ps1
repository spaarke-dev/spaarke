# Query Custom API to see all available properties
$dataverseUrl = "https://spaarkedev1.api.crm.dynamics.com"

Write-Host "Querying Custom API properties..." -ForegroundColor Cyan

$token = az account get-access-token --resource $dataverseUrl --query accessToken -o tsv

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
    "Accept" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}

try {
    $customApiQuery = "$dataverseUrl/api/data/v9.2/customapis?`$filter=uniquename eq 'sprk_GetFilePreviewUrl'"
    $response = Invoke-RestMethod -Uri $customApiQuery -Method Get -Headers $headers

    if ($response.value.Count -gt 0) {
        Write-Host "`nCustom API Record:" -ForegroundColor Green
        $response.value[0] | ConvertTo-Json -Depth 3 | Write-Host
    } else {
        Write-Host "Custom API not found" -ForegroundColor Red
    }
}
catch {
    Write-Host "Error: $($_.ErrorDetails.Message)" -ForegroundColor Red
}
