# Register Plugin Step for Custom API
# This script registers the GetFilePreviewUrlPlugin step for sprk_GetFilePreviewUrl message

$dataverseUrl = "https://spaarkedev1.api.crm.dynamics.com"

Write-Host "Getting access token..." -ForegroundColor Cyan
$token = az account get-access-token --resource $dataverseUrl --query accessToken -o tsv

if ([string]::IsNullOrEmpty($token)) {
    Write-Host "Error: Failed to get access token" -ForegroundColor Red
    exit 1
}

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
    "Accept" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}

# Step 1: Find the Plugin Type ID
Write-Host "`nStep 1: Finding Plugin Type..." -ForegroundColor Yellow

$pluginTypeUrl = "$dataverseUrl/api/data/v9.2/plugintypes?`$filter=typename eq 'Spaarke.Dataverse.CustomApiProxy.GetFilePreviewUrlPlugin'&`$select=plugintypeid,typename"
$pluginTypeResponse = Invoke-RestMethod -Uri $pluginTypeUrl -Method Get -Headers $headers

if ($pluginTypeResponse.value.Count -eq 0) {
    Write-Host "Error: Plugin type not found. Make sure the assembly is registered." -ForegroundColor Red
    exit 1
}

$pluginTypeId = $pluginTypeResponse.value[0].plugintypeid
Write-Host "  Plugin Type ID: $pluginTypeId" -ForegroundColor Green

# Step 2: Find the SDK Message ID for Custom API
Write-Host "`nStep 2: Finding SDK Message for Custom API..." -ForegroundColor Yellow

$messageUrl = "$dataverseUrl/api/data/v9.2/sdkmessages?`$filter=name eq 'sprk_GetFilePreviewUrl'&`$select=sdkmessageid,name"
$messageResponse = Invoke-RestMethod -Uri $messageUrl -Method Get -Headers $headers

if ($messageResponse.value.Count -eq 0) {
    Write-Host "  Warning: SDK Message not found with name 'sprk_GetFilePreviewUrl'" -ForegroundColor Yellow
    Write-Host "  This is expected for Custom APIs. The message is auto-created." -ForegroundColor Gray
    Write-Host "  Searching for Custom API instead..." -ForegroundColor Yellow

    # Query Custom API to verify it exists
    $customApiUrl = "$dataverseUrl/api/data/v9.2/customapis?`$filter=uniquename eq 'sprk_GetFilePreviewUrl'&`$select=customapiid,uniquename"
    $customApiResponse = Invoke-RestMethod -Uri $customApiUrl -Method Get -Headers $headers

    if ($customApiResponse.value.Count -eq 0) {
        Write-Host "  Error: Custom API 'sprk_GetFilePreviewUrl' not found!" -ForegroundColor Red
        exit 1
    }

    $customApiId = $customApiResponse.value[0].customapiid
    Write-Host "  Custom API ID: $customApiId" -ForegroundColor Green

    # For Custom APIs, we need to create the step differently
    # The message is the Custom API unique name itself
    $messageName = "sprk_GetFilePreviewUrl"

} else {
    $messageId = $messageResponse.value[0].sdkmessageid
    $messageName = $messageResponse.value[0].name
    Write-Host "  SDK Message ID: $messageId" -ForegroundColor Green
    Write-Host "  Message Name: $messageName" -ForegroundColor Green
}

# Step 3: Find SDK Message Filter for sprk_document entity
Write-Host "`nStep 3: Finding/Creating SDK Message Filter..." -ForegroundColor Yellow

$filterUrl = "$dataverseUrl/api/data/v9.2/sdkmessagefilters?`$filter=primaryobjecttypecode eq 'sprk_document'&`$select=sdkmessagefilterid"
$filterResponse = Invoke-RestMethod -Uri $filterUrl -Method Get -Headers $headers

if ($filterResponse.value.Count -gt 0) {
    $messageFilterId = $filterResponse.value[0].sdkmessagefilterid
    Write-Host "  Message Filter ID: $messageFilterId" -ForegroundColor Green
} else {
    Write-Host "  No message filter found for sprk_document" -ForegroundColor Yellow
    Write-Host "  Custom APIs don't always need explicit filters" -ForegroundColor Gray
    $messageFilterId = $null
}

# Step 4: Register the Plugin Step
Write-Host "`nStep 4: Registering Plugin Step..." -ForegroundColor Yellow

$stepData = @{
    "name" = "sprk_GetFilePreviewUrl: sprk_document"
    "plugintypeid@odata.bind" = "/plugintypes($pluginTypeId)"
    "mode" = 0  # Synchronous
    "rank" = 1
    "stage" = 40  # PostOperation
    "supporteddeployment" = 0  # Server only
    "description" = "Executes GetFilePreviewUrlPlugin when sprk_GetFilePreviewUrl Custom API is called on sprk_document"
}

# Add message filter if we found one
if ($messageFilterId) {
    $stepData["sdkmessagefilterid@odata.bind"] = "/sdkmessagefilters($messageFilterId)"
}

# For Custom APIs, we need to set the message name
$stepData["eventhandler_sdkmessageprocessingstep"] = @{
    "customapi" = "sprk_GetFilePreviewUrl"
}

$stepJson = $stepData | ConvertTo-Json -Depth 5

try {
    $stepUrl = "$dataverseUrl/api/data/v9.2/sdkmessageprocessingsteps"
    $stepResponse = Invoke-RestMethod -Uri $stepUrl -Method Post -Headers $headers -Body $stepJson

    Write-Host "`n✅ Plugin Step registered successfully!" -ForegroundColor Green
    Write-Host "  You can now test the Custom API" -ForegroundColor Cyan
}
catch {
    if ($_.ErrorDetails.Message -match "duplicate") {
        Write-Host "`n⚠️  Plugin step already exists (skipping)" -ForegroundColor Yellow
    } else {
        Write-Host "`n❌ Error registering step:" -ForegroundColor Red
        Write-Host $_.ErrorDetails.Message -ForegroundColor Red

        Write-Host "`nNote: For Custom APIs, the step registration might need to be done differently." -ForegroundColor Yellow
        Write-Host "The Custom API itself should work even without a traditional 'step'." -ForegroundColor Yellow
    }
}

Write-Host "`nDone!" -ForegroundColor Green
