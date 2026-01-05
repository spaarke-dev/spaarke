# Test if entity sets exist and can be queried
param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

$token = az account get-access-token --resource $EnvironmentUrl --query 'accessToken' -o tsv

if (-not $token) {
    Write-Error "Failed to get access token"
    exit 1
}

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
}

$entitySets = @(
    'sprk_aitooltypes',
    'sprk_aiskilltypes',
    'sprk_aiknowledgetypes',
    'sprk_analysisactiontypes'
)

foreach ($entitySet in $entitySets) {
    $uri = "$EnvironmentUrl/api/data/v9.2/$entitySet"

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -ErrorAction Stop
        Write-Host "$entitySet : OK ($($result.value.Count) records)" -ForegroundColor Green

        # Show first few records
        $result.value | Select-Object -First 3 | ForEach-Object {
            Write-Host "  - $($_.sprk_name)" -ForegroundColor Gray
        }
    } catch {
        Write-Host "$entitySet : ERROR" -ForegroundColor Red
        Write-Host "  $($_.Exception.Message)" -ForegroundColor Gray
    }
}
