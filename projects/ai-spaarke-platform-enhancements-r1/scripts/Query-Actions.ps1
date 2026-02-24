param(
    [string]$Filter = "sprk_actioncode ne null",
    [string]$Select = "sprk_name,sprk_actioncode,sprk_analysisactionid"
)

$token = (az account get-access-token --resource https://spaarkedev1.crm.dynamics.com --query accessToken -o tsv)
if (-not $token) {
    Write-Error "Failed to get access token"
    exit 1
}

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept'        = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

$encodedFilter = [System.Uri]::EscapeDataString($Filter)
$url = "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_analysisactions?`$select=$Select&`$filter=$encodedFilter"

Write-Host "Querying: $url"
$result = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
Write-Host "Total records: $($result.value.Count)"
$result.value | ConvertTo-Json -Depth 3
