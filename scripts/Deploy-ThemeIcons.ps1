# Deploy Theme Icon Web Resources to Dataverse

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Theme Icons Web Resources Deployment" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
Write-Host "[1/3] Using Dataverse environment..."
Write-Host "      $orgUrl" -ForegroundColor Green
Write-Host ""

# Get access token
Write-Host "[2/3] Getting access token..."
$accessToken = (& az account get-access-token --resource "$orgUrl/" --query accessToken -o tsv 2>$null)
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Host "Error: Failed to get access token" -ForegroundColor Red
    exit 1
}
Write-Host "      Token acquired" -ForegroundColor Green
Write-Host ""

$apiUrl = "$orgUrl/api/data/v9.2"
$headers = @{
    "Authorization" = "Bearer $accessToken"
    "Content-Type" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
    "Accept" = "application/json"
}

# Icon files to deploy
$icons = @(
    @{ Name = "sprk_ThemeMenu16.svg"; DisplayName = "Theme Menu Icon 16x16" },
    @{ Name = "sprk_ThemeMenu32.svg"; DisplayName = "Theme Menu Icon 32x32" },
    @{ Name = "sprk_ThemeLight16.svg"; DisplayName = "Theme Light Icon 16x16" },
    @{ Name = "sprk_ThemeDark16.svg"; DisplayName = "Theme Dark Icon 16x16" },
    @{ Name = "sprk_ThemeAuto16.svg"; DisplayName = "Theme Auto Icon 16x16" }
)

$iconBasePath = "C:\code_files\spaarke\src\client\assets\icons"
$deployedIds = @()

Write-Host "[3/3] Deploying icons..."
foreach ($icon in $icons) {
    $iconPath = Join-Path $iconBasePath $icon.Name

    if (!(Test-Path $iconPath)) {
        Write-Host "      ! File not found: $($icon.Name)" -ForegroundColor Red
        continue
    }

    Write-Host "      Deploying $($icon.Name)..." -ForegroundColor Yellow

    # Read and encode file
    $iconBytes = [System.IO.File]::ReadAllBytes($iconPath)
    $iconContent = [Convert]::ToBase64String($iconBytes)

    # Check if exists
    $searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$($icon.Name)'"
    try {
        $searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get
    } catch {
        Write-Host "      ! API error: $($_.Exception.Message)" -ForegroundColor Red
        continue
    }

    if ($searchResponse.value.Count -gt 0) {
        $webResourceId = $searchResponse.value[0].webresourceid
        Write-Host "        Updating existing ($webResourceId)..." -ForegroundColor Gray

        $updateUrl = "$apiUrl/webresourceset($webResourceId)"
        $updateBody = @{ content = $iconContent } | ConvertTo-Json
        try {
            Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBody | Out-Null
            Write-Host "        Updated" -ForegroundColor Green
        } catch {
            Write-Host "        ! Update failed: $($_.Exception.Message)" -ForegroundColor Red
        }
        $deployedIds += $webResourceId
    } else {
        Write-Host "        Creating new..." -ForegroundColor Gray

        $createUrl = "$apiUrl/webresourceset"
        $createBody = @{
            name = $icon.Name
            displayname = $icon.DisplayName
            webresourcetype = 11  # 11 = SVG
            content = $iconContent
        } | ConvertTo-Json

        try {
            $createResponse = Invoke-RestMethod -Uri $createUrl -Headers $headers -Method Post -Body $createBody
            $webResourceId = $createResponse.webresourceid
            Write-Host "        Created ($webResourceId)" -ForegroundColor Green
            $deployedIds += $webResourceId
        } catch {
            Write-Host "        ! Create failed: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

Write-Host ""

# Publish
if ($deployedIds.Count -gt 0) {
    Write-Host "Publishing web resources..."
    $publishUrl = "$apiUrl/PublishXml"
    $resourceXml = ""
    foreach ($id in $deployedIds) {
        $resourceXml += "<webresource>{$id}</webresource>"
    }
    $publishXml = "<importexportxml><webresources>$resourceXml</webresources></importexportxml>"
    $publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json

    try {
        Invoke-RestMethod -Uri $publishUrl -Headers $headers -Method Post -Body $publishBody | Out-Null
        Write-Host "      Published" -ForegroundColor Green
    }
    catch {
        Write-Host "      Publish failed: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Theme Icons Deployment Complete" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
