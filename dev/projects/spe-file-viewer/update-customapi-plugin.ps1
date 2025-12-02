# Update Custom API to link to Plugin Type
# Custom APIs don't use traditional plugin steps - they reference the plugin directly

$dataverseUrl = "https://spaarkedev1.api.crm.dynamics.com"

Write-Host "Connecting Custom API to Plugin..." -ForegroundColor Cyan

# Get access token
$token = az account get-access-token --resource $dataverseUrl --query accessToken -o tsv

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
    "Accept" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}

# Step 1: Find the Custom API ID
Write-Host "`n[Step 1] Finding Custom API record..." -ForegroundColor Yellow

try {
    $customApiQuery = "$dataverseUrl/api/data/v9.2/customapis?`$filter=uniquename eq 'sprk_GetFilePreviewUrl'&`$select=customapiid,name,_plugintypeid_value"
    $customApiResponse = Invoke-RestMethod -Uri $customApiQuery -Method Get -Headers $headers

    if ($customApiResponse.value.Count -eq 0) {
        Write-Host "❌ Error: Custom API 'sprk_GetFilePreviewUrl' not found" -ForegroundColor Red
        exit 1
    }

    $customApiId = $customApiResponse.value[0].customapiid
    $currentPluginTypeId = $customApiResponse.value[0]._plugintypeid_value

    Write-Host "✅ Custom API found" -ForegroundColor Green
    Write-Host "   ID: $customApiId" -ForegroundColor Cyan
    Write-Host "   Current Plugin Type: $currentPluginTypeId" -ForegroundColor Cyan
}
catch {
    Write-Host "❌ Error finding Custom API: $($_.ErrorDetails.Message)" -ForegroundColor Red
    exit 1
}

# Step 2: Find the Plugin Type ID
Write-Host "`n[Step 2] Finding Plugin Type..." -ForegroundColor Yellow

try {
    $pluginTypeQuery = "$dataverseUrl/api/data/v9.2/plugintypes?`$filter=typename eq 'Spaarke.Dataverse.CustomApiProxy.GetFilePreviewUrlPlugin'&`$select=plugintypeid,typename"
    $pluginTypeResponse = Invoke-RestMethod -Uri $pluginTypeQuery -Method Get -Headers $headers

    if ($pluginTypeResponse.value.Count -eq 0) {
        Write-Host "❌ Error: Plugin type 'GetFilePreviewUrlPlugin' not found" -ForegroundColor Red
        exit 1
    }

    $pluginTypeId = $pluginTypeResponse.value[0].plugintypeid

    Write-Host "✅ Plugin Type found" -ForegroundColor Green
    Write-Host "   ID: $pluginTypeId" -ForegroundColor Cyan
    Write-Host "   Type: $($pluginTypeResponse.value[0].typename)" -ForegroundColor Cyan
}
catch {
    Write-Host "❌ Error finding Plugin Type: $($_.ErrorDetails.Message)" -ForegroundColor Red
    exit 1
}

# Step 3: Update Custom API to link to Plugin
Write-Host "`n[Step 3] Linking Custom API to Plugin..." -ForegroundColor Yellow

if ($currentPluginTypeId -eq $pluginTypeId) {
    Write-Host "✅ Custom API already linked to correct plugin" -ForegroundColor Green
} else {
    $updateData = @{
        "plugintypeid@odata.bind" = "/plugintypes($pluginTypeId)"
    } | ConvertTo-Json

    try {
        $updateUrl = "$dataverseUrl/api/data/v9.2/customapis($customApiId)"
        Invoke-RestMethod -Uri $updateUrl -Method Patch -Headers $headers -Body $updateData | Out-Null

        Write-Host "✅ Custom API linked to plugin successfully!" -ForegroundColor Green
    }
    catch {
        Write-Host "❌ Error updating Custom API: $($_.ErrorDetails.Message)" -ForegroundColor Red
        exit 1
    }
}

Write-Host "`n============================================" -ForegroundColor Cyan
Write-Host "CUSTOM API CONFIGURATION COMPLETE" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "✅ Custom API is now connected to the plugin" -ForegroundColor Green
Write-Host "`nNext step: Publish customizations (Task 2.6)" -ForegroundColor Yellow
Write-Host ""
