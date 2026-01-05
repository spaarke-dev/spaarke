# Verify Action records exist
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

Write-Host "=== Action Verification ===" -ForegroundColor Cyan
Write-Host ""

$expected = @(
    "Extract Entities",
    "Analyze Clauses",
    "Classify Document",
    "Summarize Content",
    "Detect Risks",
    "Compare Clauses",
    "Extract Dates",
    "Calculate Values"
)

$uri = "$EnvironmentUrl/api/data/v9.2/sprk_analysisactions?`$select=sprk_name,sprk_sortorder&`$orderby=sprk_sortorder"

try {
    $result = Invoke-RestMethod -Uri $uri -Headers $headers
    $actual = $result.value | ForEach-Object { $_.sprk_name }

    Write-Host "Found $($result.value.Count) Action records:" -ForegroundColor Yellow
    Write-Host ""

    foreach ($name in $expected) {
        if ($actual -contains $name) {
            Write-Host "  [X] $name" -ForegroundColor Green
        } else {
            Write-Host "  [ ] $name (MISSING)" -ForegroundColor Red
        }
    }

    # Show extra records
    $unexpected = $actual | Where-Object { $_ -notin $expected }
    if ($unexpected) {
        Write-Host ""
        Write-Host "Extra records:" -ForegroundColor Gray
        $unexpected | ForEach-Object {
            Write-Host "  - $_" -ForegroundColor Gray
        }
    }

    Write-Host ""
    $missingCount = ($expected | Where-Object { $_ -notin $actual }).Count
    if ($missingCount -eq 0) {
        Write-Host "All 8 required Action records present!" -ForegroundColor Green
    } else {
        Write-Host "$missingCount records missing!" -ForegroundColor Red
    }
} catch {
    Write-Error "Error: $_"
}
