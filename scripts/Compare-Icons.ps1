# Compare working vs non-working SVG icons

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
$accessToken = (& az account get-access-token --resource "$orgUrl/" --query accessToken -o tsv 2>$null)
$headers = @{ "Authorization" = "Bearer $accessToken" }
$apiUrl = "$orgUrl/api/data/v9.2"

Write-Host "====================================="
Write-Host "Comparing Working vs Theme Icons"
Write-Host "====================================="

# Working entity icon
Write-Host ""
Write-Host "--- WORKING: sprk_SPRK_Event_AppFolder ---"
$searchUrl = "$apiUrl/webresourceset?`$filter=name eq 'sprk_SPRK_Event_AppFolder'&`$select=name,content,webresourcetype"
$response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get
if ($response.value.Count -gt 0) {
    $wr = $response.value[0]
    $decoded = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($wr.content))
    Write-Host "Type: $($wr.webresourcetype)"
    Write-Host "Content:"
    Write-Host $decoded
}

# Theme icon
Write-Host ""
Write-Host "--- THEME: sprk_ThemeMenu16.svg ---"
$searchUrl = "$apiUrl/webresourceset?`$filter=name eq 'sprk_ThemeMenu16.svg'&`$select=name,content,webresourcetype"
$response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get
if ($response.value.Count -gt 0) {
    $wr = $response.value[0]
    $decoded = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($wr.content))
    Write-Host "Type: $($wr.webresourcetype)"
    Write-Host "Content:"
    Write-Host $decoded
}
