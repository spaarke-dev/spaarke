param(
    [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com'
)
$ErrorActionPreference = 'Stop'
$env:Path = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin;' + $env:Path

Write-Host '========================================='
Write-Host 'Remove Obsolete SprkChat Web Resources'
Write-Host "Target: $DataverseUrl"
Write-Host '========================================='

$accessToken = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if ([string]::IsNullOrEmpty($accessToken)) { Write-Error 'Failed to get token'; exit 1 }
Write-Host 'Token acquired' -ForegroundColor Green

$apiUrl  = "$DataverseUrl/api/data/v9.2"
$headers = @{
    'Authorization'    = "Bearer $accessToken"
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
}

$obsolete = @(
    'sprk_SprkChatPane',
    'sprk_/scripts/openSprkChatPane.js',
    'sprk_SidePaneManager'
)

foreach ($wrName in $obsolete) {
    Write-Host ''
    Write-Host "Searching for: $wrName" -ForegroundColor Cyan
    $searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$wrName'"
    try {
        $searchResponse = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get
        if ($searchResponse.value.Count -eq 0) {
            Write-Host "  Not found (may already be deleted)" -ForegroundColor Yellow
        } else {
            $wrId = $searchResponse.value[0].webresourceid
            Write-Host "  Found: $wrId -- deleting..."
            Invoke-RestMethod -Uri "$apiUrl/webresourceset($wrId)" -Headers $headers -Method Delete | Out-Null
            Write-Host "  Deleted!" -ForegroundColor Green
        }
    } catch {
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ''
Write-Host '========================================='
Write-Host 'Cleanup Complete!' -ForegroundColor Green
Write-Host '========================================='
