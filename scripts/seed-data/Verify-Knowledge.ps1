# Verify Knowledge records exist
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

Write-Host "=== Knowledge Verification ===" -ForegroundColor Cyan
Write-Host ""

$expected = @(
    "Standard Contract Terms",
    "Regulatory Guidelines",
    "Best Practices",
    "Risk Categories",
    "Defined Terms",
    "NDA Standards",
    "Lease Standards",
    "Employment Standards",
    "SLA Benchmarks",
    "DD Checklist"
)

$uri = "$EnvironmentUrl/api/data/v9.2/sprk_analysisknowledges?`$select=sprk_name,sprk_description&`$orderby=sprk_name"

try {
    $result = Invoke-RestMethod -Uri $uri -Headers $headers
    $actual = $result.value | ForEach-Object { $_.sprk_name }

    Write-Host "Found $($result.value.Count) Knowledge records:" -ForegroundColor Yellow
    Write-Host ""

    foreach ($name in $expected) {
        if ($actual -contains $name) {
            $knowledge = $result.value | Where-Object { $_.sprk_name -eq $name }
            $descSnippet = if ($knowledge.sprk_description.Length -gt 50) {
                $knowledge.sprk_description.Substring(0, 50) + "..."
            } else {
                $knowledge.sprk_description
            }
            Write-Host "  [X] $name" -ForegroundColor Green
        } else {
            Write-Host "  [ ] $name (MISSING)" -ForegroundColor Red
        }
    }

    Write-Host ""
    $missingCount = ($expected | Where-Object { $_ -notin $actual }).Count
    if ($missingCount -eq 0) {
        Write-Host "All 10 required Knowledge records present!" -ForegroundColor Green
    } else {
        Write-Host "$missingCount records missing!" -ForegroundColor Red
    }
} catch {
    Write-Error "Error: $_"
}
