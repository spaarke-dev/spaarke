# Query SkillType records for foreign key reference
param(
    [string]$EnvironmentUrl = $env:DATAVERSE_URL
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

$uri = "$EnvironmentUrl/api/data/v9.2/sprk_aiskilltypes?`$select=sprk_aiskilltypeid,sprk_name&`$orderby=sprk_name"

try {
    $result = Invoke-RestMethod -Uri $uri -Headers $headers
    Write-Host "SkillType Records:" -ForegroundColor Cyan

    $result.value | ForEach-Object {
        Write-Host "$($_.sprk_name): $($_.sprk_aiskilltypeid)"
    }

    Write-Host ""
    Write-Host "JSON format:" -ForegroundColor Yellow
    $lookup = @{}
    $result.value | ForEach-Object {
        $lookup[$_.sprk_name] = $_.sprk_aiskilltypeid
    }
    $lookup | ConvertTo-Json
} catch {
    Write-Error "Error: $_"
}
