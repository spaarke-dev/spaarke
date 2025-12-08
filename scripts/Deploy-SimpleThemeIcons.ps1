# Deploy simplified SVG theme icons that match Dataverse's expected format

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
$accessToken = (& az account get-access-token --resource "$orgUrl/" --query accessToken -o tsv 2>$null)
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Host "Error: Failed to get access token" -ForegroundColor Red
    exit 1
}

$headers = @{
    "Authorization" = "Bearer $accessToken"
    "Content-Type" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}
$apiUrl = "$orgUrl/api/data/v9.2"

Write-Host "====================================="
Write-Host "Deploying Simple SVG Theme Icons"
Write-Host "====================================="

# Simple half-circle icons using only path elements (like entity icons use)
# Using the same 24x24 viewBox that entity icons use
$icons = @{
    # Main menu icon - half white, half dark
    "sprk_ThemeMenu16" = @'
<svg width="16" height="16" viewBox="0 0 16 16" xmlns="http://www.w3.org/2000/svg"><path d="M8 1a7 7 0 0 0 0 14V1z" fill="#FFFFFF" stroke="#666666" stroke-width="0.5"/><path d="M8 1a7 7 0 0 1 0 14V1z" fill="#444444"/></svg>
'@
    "sprk_ThemeMenu32" = @'
<svg width="32" height="32" viewBox="0 0 32 32" xmlns="http://www.w3.org/2000/svg"><path d="M16 2a14 14 0 0 0 0 28V2z" fill="#FFFFFF" stroke="#666666" stroke-width="1"/><path d="M16 2a14 14 0 0 1 0 28V2z" fill="#444444"/></svg>
'@
    # Auto icon - split circle
    "sprk_ThemeAuto16" = @'
<svg width="16" height="16" viewBox="0 0 16 16" xmlns="http://www.w3.org/2000/svg"><path d="M8 1a7 7 0 0 0 0 14V1z" fill="#FFFFFF" stroke="#888888" stroke-width="0.5"/><path d="M8 1a7 7 0 0 1 0 14V1z" fill="#555555"/></svg>
'@
    # Light icon - white circle
    "sprk_ThemeLight16" = @'
<svg width="16" height="16" viewBox="0 0 16 16" xmlns="http://www.w3.org/2000/svg"><circle cx="8" cy="8" r="6" fill="#FFFFFF" stroke="#888888" stroke-width="1"/></svg>
'@
    # Dark icon - dark circle
    "sprk_ThemeDark16" = @'
<svg width="16" height="16" viewBox="0 0 16 16" xmlns="http://www.w3.org/2000/svg"><circle cx="8" cy="8" r="6" fill="#444444" stroke="#666666" stroke-width="1"/></svg>
'@
}

$webResourceIds = @()

foreach ($name in $icons.Keys) {
    Write-Host ""
    Write-Host "Processing: $name"

    $svgContent = $icons[$name].Trim()
    $base64Content = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($svgContent))

    # Check if exists
    $filter = "`$filter=name eq '$name'"
    $select = "`$select=webresourceid,name"
    $searchUrl = "$apiUrl/webresourceset?$filter&$select"
    $response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

    if ($response.value.Count -gt 0) {
        $webResourceId = $response.value[0].webresourceid
        Write-Host "  Updating existing: $webResourceId"

        $updateUrl = "$apiUrl/webresourceset($webResourceId)"
        $updateBody = @{ content = $base64Content } | ConvertTo-Json
        Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBody | Out-Null
        Write-Host "  Updated" -ForegroundColor Green
        $webResourceIds += $webResourceId
    } else {
        Write-Host "  Creating new..."
        $createBody = @{
            name = $name
            displayname = $name
            webresourcetype = 11  # SVG
            content = $base64Content
        } | ConvertTo-Json
        $createUrl = "$apiUrl/webresourceset"

        try {
            Invoke-RestMethod -Uri $createUrl -Headers $headers -Method Post -Body $createBody | Out-Null
            Write-Host "  Created" -ForegroundColor Green

            # Get the ID
            $response2 = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get
            if ($response2.value.Count -gt 0) {
                $webResourceIds += $response2.value[0].webresourceid
            }
        } catch {
            Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "====================================="
Write-Host "Publishing..."
Write-Host "====================================="

if ($webResourceIds.Count -gt 0) {
    $webResourcesXml = ""
    foreach ($id in $webResourceIds) {
        $webResourcesXml += "<webresource>{$id}</webresource>"
    }

    $publishXml = "<importexportxml><webresources>$webResourcesXml</webresources></importexportxml>"
    $publishUrl = "$apiUrl/PublishXml"
    $publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json

    try {
        Invoke-RestMethod -Uri $publishUrl -Headers $headers -Method Post -Body $publishBody | Out-Null
        Write-Host "Published!" -ForegroundColor Green
    } catch {
        Write-Host "Error: $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Done! Clear browser cache and reload to see updates."
