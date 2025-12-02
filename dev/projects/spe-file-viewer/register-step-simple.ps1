# Simple Plugin Step Registration
$dataverseUrl = "https://spaarkedev1.api.crm.dynamics.com"

Write-Host "Registering Plugin Step..." -ForegroundColor Cyan
$token = az account get-access-token --resource $dataverseUrl --query accessToken -o tsv

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
    "Accept" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}

# These IDs were found by the previous script
$pluginTypeId = "d128fc10-99c6-4c07-b80b-e3d1447744d1"
$messageId = "421b5a23-a02e-4ac1-afb2-63c6a4f0d635"
$messageFilterId = "e14f5012-00c7-f011-8543-000d3a1a9353"

$stepData = @{
    "name" = "sprk_GetFilePreviewUrl: sprk_document"
    "plugintypeid@odata.bind" = "/plugintypes($pluginTypeId)"
    "sdkmessageid@odata.bind" = "/sdkmessages($messageId)"
    "sdkmessagefilterid@odata.bind" = "/sdkmessagefilters($messageFilterId)"
    "mode" = 0  # Synchronous
    "rank" = 1
    "stage" = 40  # PostOperation
    "supporteddeployment" = 0  # Server
} | ConvertTo-Json

try {
    $stepUrl = "$dataverseUrl/api/data/v9.2/sdkmessageprocessingsteps"
    Invoke-RestMethod -Uri $stepUrl -Method Post -Headers $headers -Body $stepData | Out-Null
    Write-Host "✅ Plugin Step registered successfully!" -ForegroundColor Green
}
catch {
    if ($_.ErrorDetails.Message -match "duplicate") {
        Write-Host "⚠️  Plugin step already exists" -ForegroundColor Yellow
    } else {
        Write-Host "❌ Error: $($_.ErrorDetails.Message)" -ForegroundColor Red
    }
}
