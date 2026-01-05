# Verify Tool records exist
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

Write-Host "=== Tool Verification ===" -ForegroundColor Cyan
Write-Host ""

$expected = @(
    "Entity Extractor",
    "Clause Analyzer",
    "Document Classifier",
    "Summary Generator",
    "Risk Detector",
    "Clause Comparator",
    "Date Extractor",
    "Financial Calculator"
)

$uri = "$EnvironmentUrl/api/data/v9.2/sprk_analysistools?`$select=sprk_name,sprk_handlerclass&`$orderby=sprk_name"

try {
    $result = Invoke-RestMethod -Uri $uri -Headers $headers
    $actual = $result.value | ForEach-Object { $_.sprk_name }

    Write-Host "Found $($result.value.Count) Tool records:" -ForegroundColor Yellow
    Write-Host ""

    foreach ($name in $expected) {
        if ($actual -contains $name) {
            $tool = $result.value | Where-Object { $_.sprk_name -eq $name }
            Write-Host "  [X] $name ($($tool.sprk_handlerclass))" -ForegroundColor Green
        } else {
            Write-Host "  [ ] $name (MISSING)" -ForegroundColor Red
        }
    }

    Write-Host ""
    $missingCount = ($expected | Where-Object { $_ -notin $actual }).Count
    if ($missingCount -eq 0) {
        Write-Host "All 8 required Tool records present!" -ForegroundColor Green
    } else {
        Write-Host "$missingCount records missing!" -ForegroundColor Red
    }
} catch {
    Write-Error "Error: $_"
}
