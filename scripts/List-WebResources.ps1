# List all sprk_ web resources

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
$accessToken = (& az account get-access-token --resource "$orgUrl/" --query accessToken -o tsv 2>$null)
$headers = @{ "Authorization" = "Bearer $accessToken" }
$apiUrl = "$orgUrl/api/data/v9.2"

$searchUrl = "$apiUrl/webresourceset?`$filter=startswith(name,'sprk_')&`$select=name,displayname&`$orderby=name"
$response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get
$response.value | ForEach-Object { Write-Host $_.name }
