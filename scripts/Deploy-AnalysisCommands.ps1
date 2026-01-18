# Deploy sprk_analysis_commands.js Web Resource to Dataverse

Write-Host "====================================="
Write-Host "Analysis Commands Deployment"
Write-Host "====================================="

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
$webResourceName = "sprk_analysis_commands.js"
$jsFilePath = "C:\code_files\spaarke-wt-ai-rag-pipeline\src\client\webresources\js\sprk_analysis_commands.js"

Write-Host "[1/4] Environment: $orgUrl"

# Get token
Write-Host "[2/4] Getting access token..."
$accessToken = (& az account get-access-token --resource "$orgUrl/" --query accessToken -o tsv 2>$null)
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Host "Error: Failed to get access token" -ForegroundColor Red
    exit 1
}
Write-Host "      Token acquired"

$apiUrl = "$orgUrl/api/data/v9.2"
$headers = @{
    "Authorization" = "Bearer $accessToken"
    "Content-Type" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}

# Read file
Write-Host "[3/4] Reading JS file..."
$jsBytes = [System.IO.File]::ReadAllBytes($jsFilePath)
$jsContent = [Convert]::ToBase64String($jsBytes)
Write-Host "      File read ($($jsBytes.Length) bytes)"

# Deploy
Write-Host "[4/4] Deploying..."
$searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$webResourceName'"
$searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

if ($searchResponse.value.Count -gt 0) {
    $webResourceId = $searchResponse.value[0].webresourceid
    Write-Host "      Found: $webResourceId"

    $updateUrl = "$apiUrl/webresourceset($webResourceId)"
    $updateBody = @{ content = $jsContent } | ConvertTo-Json
    Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBody | Out-Null
    Write-Host "      Updated"

    $publishUrl = "$apiUrl/PublishXml"
    $publishXml = "<importexportxml><webresources><webresource>{$webResourceId}</webresource></webresources></importexportxml>"
    $publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json
    Invoke-RestMethod -Uri $publishUrl -Headers $headers -Method Post -Body $publishBody | Out-Null
    Write-Host "      Published"
} else {
    Write-Host "      Web resource not found: $webResourceName" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "====================================="
Write-Host "Deployment Complete"
Write-Host "====================================="
