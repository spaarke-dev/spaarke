# Find working ribbon icons and check their web resource format

$orgUrl = "https://spaarkedev1.crm.dynamics.com"
$accessToken = (& az account get-access-token --resource "$orgUrl/" --query accessToken -o tsv 2>$null)
$headers = @{ "Authorization" = "Bearer $accessToken" }
$apiUrl = "$orgUrl/api/data/v9.2"

Write-Host "====================================="
Write-Host "Finding Working Ribbon Icons"
Write-Host "====================================="

# Get all sprk_ image web resources
$searchUrl = "$apiUrl/webresourceset?`$filter=startswith(name,'sprk_') and (webresourcetype eq 5 or webresourcetype eq 6 or webresourcetype eq 7 or webresourcetype eq 10 or webresourcetype eq 11)&`$select=name,webresourcetype,displayname&`$orderby=name"
$response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

$typeNames = @{
    5 = "PNG"; 6 = "JPG"; 7 = "GIF"; 10 = "ICO"; 11 = "SVG"
}

Write-Host ""
Write-Host "All sprk_ image web resources:"
Write-Host ""

foreach ($wr in $response.value) {
    $typeName = $typeNames[$wr.webresourcetype]
    Write-Host "  $($wr.name) [$typeName]"
}

Write-Host ""
Write-Host "====================================="
Write-Host "Checking Entity Icons (known to work)"
Write-Host "====================================="

# The entity icons like sprk_SPRK_Event_AppFolder work - let's see their format
$entityIcons = @(
    "sprk_SPRK_Event_AppFolder",
    "sprk_SPRK_Matter_FolderOpen",
    "sprk_SPRK_Project_Wrench"
)

foreach ($iconName in $entityIcons) {
    Write-Host ""
    Write-Host "--- $iconName ---"
    $searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$iconName'&`$select=name,webresourcetype,content"
    $response = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

    if ($response.value.Count -gt 0) {
        $wr = $response.value[0]
        $typeName = $typeNames[$wr.webresourcetype]
        Write-Host "Type: $typeName"

        if ($wr.content) {
            $decoded = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($wr.content))
            Write-Host "Content (first 300 chars):"
            Write-Host $decoded.Substring(0, [Math]::Min(300, $decoded.Length))
        }
    } else {
        Write-Host "Not found"
    }
}
