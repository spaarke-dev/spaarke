# Create icon web resources without .svg extension

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
Write-Host "Creating Icons Without Extension"
Write-Host "====================================="

$iconsPath = "c:\code_files\spaarke\src\client\assets\icons"

# New names without .svg extension
$icons = @(
    @{ oldName = "sprk_ThemeMenu16.svg"; newName = "sprk_ThemeMenu16"; file = "sprk_ThemeMenu16.svg" },
    @{ oldName = "sprk_ThemeMenu32.svg"; newName = "sprk_ThemeMenu32"; file = "sprk_ThemeMenu32.svg" },
    @{ oldName = "sprk_ThemeAuto16.svg"; newName = "sprk_ThemeAuto16"; file = "sprk_ThemeAuto16.svg" },
    @{ oldName = "sprk_ThemeLight16.svg"; newName = "sprk_ThemeLight16"; file = "sprk_ThemeLight16.svg" },
    @{ oldName = "sprk_ThemeDark16.svg"; newName = "sprk_ThemeDark16"; file = "sprk_ThemeDark16.svg" }
)

$webResourceIds = @()

foreach ($icon in $icons) {
    $filePath = Join-Path $iconsPath $icon.file
    Write-Host ""
    Write-Host "Processing: $($icon.newName)"

    if (-not (Test-Path $filePath)) {
        Write-Host "  File not found: $filePath" -ForegroundColor Red
        continue
    }

    # Read file
    $svgBytes = [System.IO.File]::ReadAllBytes($filePath)
    $svgContent = [Convert]::ToBase64String($svgBytes)
    Write-Host "  File read ($($svgBytes.Length) bytes)"

    # Check if new name already exists
    $searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$($icon.newName)'&`$select=webresourceid,name"
    $response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

    if ($response.value.Count -gt 0) {
        $webResourceId = $response.value[0].webresourceid
        Write-Host "  Found existing: $webResourceId - updating"

        # Update
        $updateUrl = "$apiUrl/webresourceset($webResourceId)"
        $updateBody = @{ content = $svgContent } | ConvertTo-Json
        Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updateBody | Out-Null
        Write-Host "  Updated" -ForegroundColor Green
        $webResourceIds += $webResourceId
    } else {
        Write-Host "  Creating new web resource..."
        # Create new web resource
        $createBody = @{
            name = $icon.newName
            displayname = $icon.newName
            webresourcetype = 11  # SVG
            content = $svgContent
        } | ConvertTo-Json
        $createUrl = "$apiUrl/webresourceset"

        try {
            $result = Invoke-RestMethod -Uri $createUrl -Headers $headers -Method Post -Body $createBody
            Write-Host "  Created successfully" -ForegroundColor Green

            # Get the ID from the OData-EntityId header or re-query
            $searchUrl2 = "$apiUrl/webresourceset?`$filter=name eq '$($icon.newName)'&`$select=webresourceid"
            $response2 = Invoke-RestMethod -Uri $searchUrl2 -Headers $headers -Method Get
            if ($response2.value.Count -gt 0) {
                $webResourceIds += $response2.value[0].webresourceid
            }
        } catch {
            Write-Host "  Error creating: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "====================================="
Write-Host "Publishing web resources..."
Write-Host "====================================="

if ($webResourceIds.Count -gt 0) {
    # Build publish XML
    $webResourcesXml = ""
    foreach ($id in $webResourceIds) {
        $webResourcesXml += "<webresource>{$id}</webresource>"
    }

    $publishXml = "<importexportxml><webresources>$webResourcesXml</webresources></importexportxml>"

    $publishUrl = "$apiUrl/PublishXml"
    $publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json

    try {
        Invoke-RestMethod -Uri $publishUrl -Headers $headers -Method Post -Body $publishBody | Out-Null
        Write-Host "Published successfully!" -ForegroundColor Green
    } catch {
        Write-Host "Error publishing: $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "New web resource names (use these in ribbon XML):"
foreach ($icon in $icons) {
    Write-Host "  `$webresource:$($icon.newName)"
}
