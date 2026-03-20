# List all sprk_ web resources

param(
    [string]$DataverseUrl = $env:DATAVERSE_URL
)

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$orgUrl = $DataverseUrl
$accessToken = (& az account get-access-token --resource "$orgUrl/" --query accessToken -o tsv 2>$null)
$headers = @{ "Authorization" = "Bearer $accessToken" }
$apiUrl = "$orgUrl/api/data/v9.2"

$searchUrl = "$apiUrl/webresourceset?`$filter=startswith(name,'sprk_')&`$select=name,displayname&`$orderby=name"
$response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get
$response.value | ForEach-Object { Write-Host $_.name }
