# Verify Skill records exist
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

Write-Host "=== Skill Verification ===" -ForegroundColor Cyan
Write-Host ""

$expected = @(
    "Contract Analysis",
    "Invoice Processing",
    "NDA Review",
    "Lease Review",
    "Employment Contract",
    "SLA Analysis",
    "Compliance Check",
    "Executive Summary",
    "Risk Assessment",
    "Clause Comparison"
)

$uri = "$EnvironmentUrl/api/data/v9.2/sprk_analysisskills?`$select=sprk_name,sprk_description&`$orderby=sprk_name"

try {
    $result = Invoke-RestMethod -Uri $uri -Headers $headers
    $actual = $result.value | ForEach-Object { $_.sprk_name }

    Write-Host "Found $($result.value.Count) Skill records:" -ForegroundColor Yellow
    Write-Host ""

    foreach ($name in $expected) {
        if ($actual -contains $name) {
            Write-Host "  [X] $name" -ForegroundColor Green
        } else {
            Write-Host "  [ ] $name (MISSING)" -ForegroundColor Red
        }
    }

    Write-Host ""
    $missingCount = ($expected | Where-Object { $_ -notin $actual }).Count
    if ($missingCount -eq 0) {
        Write-Host "All 10 required Skill records present!" -ForegroundColor Green
    } else {
        Write-Host "$missingCount records missing!" -ForegroundColor Red
    }
} catch {
    Write-Error "Error: $_"
}
