# Verify type lookup data
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

Write-Host "=== Type Lookup Verification ===" -ForegroundColor Cyan
Write-Host ""

$entitySets = @{
    'sprk_analysisactiontypes' = @('01 - Extraction', '02 - Classification', '03 - Summarization', '04 - Analysis', '05 - Comparison')
    'sprk_aiskilltypes' = @('01 - Document Analysis', '02 - Contract Specific', '03 - Compliance', '04 - Risk Analysis', '05 - Financial')
    'sprk_aiknowledgetypes' = @('01 - Standards', '02 - Regulations', '03 - Best Practices', '04 - Templates', '05 - Taxonomy')
    'sprk_aitooltypes' = @('01 - Entity Extraction', '02 - Classification', '03 - Analysis', '04 - Calculation')
}

$allPassed = $true

foreach ($entitySet in $entitySets.Keys) {
    $expected = $entitySets[$entitySet]
    $uri = "$EnvironmentUrl/api/data/v9.2/$entitySet`?`$select=sprk_name&`$orderby=sprk_name"

    Write-Host "$entitySet" -ForegroundColor Yellow

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -ErrorAction Stop
        $actual = $result.value | ForEach-Object { $_.sprk_name }

        foreach ($name in $expected) {
            if ($actual -contains $name) {
                Write-Host "  [X] $name" -ForegroundColor Green
            } else {
                Write-Host "  [ ] $name (MISSING)" -ForegroundColor Red
                $allPassed = $false
            }
        }

        # Show any unexpected records
        $unexpected = $actual | Where-Object { $_ -notin $expected }
        if ($unexpected) {
            Write-Host "  Extra records:" -ForegroundColor Gray
            $unexpected | ForEach-Object {
                Write-Host "    - $_" -ForegroundColor Gray
            }
        }
    } catch {
        Write-Host "  ERROR: $($_.Exception.Message)" -ForegroundColor Red
        $allPassed = $false
    }

    Write-Host ""
}

if ($allPassed) {
    Write-Host "All required records present!" -ForegroundColor Green
} else {
    Write-Host "Some records are missing!" -ForegroundColor Red
}
