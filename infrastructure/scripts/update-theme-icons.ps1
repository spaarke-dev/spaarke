# Update Theme Icons in Dataverse
# This script updates the theme SVG web resources with new icon files

param(
    [string]$DataverseUrl = $env:DATAVERSE_URL
)

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot ".." ".." "scripts" "lib" "Publish-SolutionComponents.ps1")

# Get access token for Dataverse
$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if (-not $token) {
    Write-Error "Failed to get access token. Run 'az login' first."
    exit 1
}

$baseUrl = "$DataverseUrl/api/data/v9.2"
$headers = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
    "Content-Type" = "application/json"
}

# Icon files to update
$iconFiles = @{
    "sprk_ThemeMenu16.svg" = "c:\code_files\spaarke\src\client\assets\icons\sprk_ThemeMenu16.svg"
    "sprk_ThemeMenu32.svg" = "c:\code_files\spaarke\src\client\assets\icons\sprk_ThemeMenu32.svg"
    "sprk_ThemeAuto16.svg" = "c:\code_files\spaarke\src\client\assets\icons\sprk_ThemeAuto16.svg"
    "sprk_ThemeLight16.svg" = "c:\code_files\spaarke\src\client\assets\icons\sprk_ThemeLight16.svg"
    "sprk_ThemeDark16.svg" = "c:\code_files\spaarke\src\client\assets\icons\sprk_ThemeDark16.svg"
}

# Get existing web resources
Write-Host "Fetching existing web resources..."
$response = Invoke-RestMethod -Uri "$baseUrl/webresourceset?`$filter=startswith(name,'sprk_Theme')&`$select=name,webresourceid" -Headers $headers -Method Get

Write-Host "Found $($response.value.Count) theme web resources:"
foreach ($wr in $response.value) {
    Write-Host "  $($wr.name) - $($wr.webresourceid)"
}

# Update each icon
$updatedIds = @()
foreach ($iconName in $iconFiles.Keys) {
    $filePath = $iconFiles[$iconName]

    if (-not (Test-Path $filePath)) {
        Write-Warning "File not found: $filePath"
        continue
    }

    # Find the web resource
    $webResource = $response.value | Where-Object { $_.name -eq $iconName }

    if (-not $webResource) {
        Write-Warning "Web resource not found in Dataverse: $iconName"
        continue
    }

    Write-Host "Updating $iconName..."

    # Read file and convert to base64
    $content = Get-Content -Path $filePath -Raw
    $base64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($content))

    # Update the web resource
    $body = @{
        content = $base64
    } | ConvertTo-Json

    $updateUrl = "$baseUrl/webresourceset($($webResource.webresourceid))"

    try {
        Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $body
        Write-Host "  Updated successfully"
        $updatedIds += $webResource.webresourceid
    }
    catch {
        Write-Error "  Failed to update: $_"
    }
}

Write-Host ""
Write-Host "Publishing customizations..."

# Task 130 (D-83): publish only the theme web resources that were updated above, through the shared scoped-publish module.
if ($updatedIds.Count -eq 0) { Write-Warning "No web resource was updated; nothing to publish."; return }
try {
    Invoke-PublishXml -Context @{ Api = $baseUrl; Headers = $headers } -ParameterXml (New-PublishParameterXml -WebResources $updatedIds)
    Write-Host "Published successfully"
}
catch {
    Write-Error "Failed to publish: $_"
}
