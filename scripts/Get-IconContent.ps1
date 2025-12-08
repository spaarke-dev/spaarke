# Get content of deployed SVG icon web resources

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
$accessToken = (& az account get-access-token --resource "$orgUrl/" --query accessToken -o tsv 2>$null)
$headers = @{ "Authorization" = "Bearer $accessToken" }
$apiUrl = "$orgUrl/api/data/v9.2"

$iconNames = @(
    "sprk_ThemeMenu16.svg",
    "sprk_ThemeMenu32.svg",
    "sprk_ThemeAuto16.svg",
    "sprk_ThemeLight16.svg",
    "sprk_ThemeDark16.svg"
)

Write-Host "====================================="
Write-Host "Checking Icon Web Resource Content"
Write-Host "====================================="

foreach ($iconName in $iconNames) {
    Write-Host ""
    Write-Host "--- $iconName ---"

    # Get web resource
    $searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$iconName'&`$select=name,content,webresourcetype"
    $response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

    if ($response.value.Count -eq 0) {
        Write-Host "NOT FOUND" -ForegroundColor Red
        continue
    }

    $wr = $response.value[0]
    Write-Host "Type: $($wr.webresourcetype)"

    if ($wr.content) {
        try {
            $decoded = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($wr.content))
            Write-Host "Content (first 500 chars):"
            Write-Host $decoded.Substring(0, [Math]::Min(500, $decoded.Length))
        } catch {
            Write-Host "Error decoding content: $_" -ForegroundColor Red
        }
    } else {
        Write-Host "No content" -ForegroundColor Yellow
    }
}
