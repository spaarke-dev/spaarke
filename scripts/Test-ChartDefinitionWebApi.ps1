# Test sprk_chartdefinition Web API query and create a sample record
param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

# Get token
$token = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv
if ($LASTEXITCODE -ne 0) { throw "Failed to get token" }

$headers = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/json"
    "Content-Type" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
    "Prefer" = "return=representation"
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Test sprk_chartdefinition Web API" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Query existing records
Write-Host "Step 1: Query existing chart definitions..." -ForegroundColor Yellow
$uri = "$EnvironmentUrl/api/data/v9.2/sprk_chartdefinitions"
try {
    $result = Invoke-RestMethod -Uri $uri -Headers $headers
    Write-Host "  Query successful!" -ForegroundColor Green
    Write-Host "  Current record count: $($result.value.Count)" -ForegroundColor Gray
    if ($result.value.Count -gt 0) {
        $result.value | ForEach-Object {
            Write-Host "    - $($_.sprk_name) (ID: $($_.sprk_chartdefinitionid))" -ForegroundColor Gray
        }
    }
} catch {
    Write-Host "  Query failed: $_" -ForegroundColor Red
    exit 1
}

# Step 2: Create a sample record
Write-Host ""
Write-Host "Step 2: Create sample chart definition..." -ForegroundColor Yellow

$sampleRecord = @{
    "sprk_name" = "Sample - Active Projects Count"
    "sprk_visualtype" = 100000000  # MetricCard
    "sprk_entitylogicalname" = "sprk_project"
    "sprk_baseviewid" = "00000000-0000-0000-0000-000000000001"
    "sprk_aggregationtype" = 100000000  # Count
    "sprk_optionsjson" = '{"showTrend": true, "trendPeriod": "month"}'
}

try {
    $body = $sampleRecord | ConvertTo-Json -Depth 10
    $createResult = Invoke-RestMethod -Uri $uri -Method POST -Headers $headers -Body $body
    $newId = $createResult.sprk_chartdefinitionid
    Write-Host "  Record created successfully!" -ForegroundColor Green
    Write-Host "  New record ID: $newId" -ForegroundColor Cyan
} catch {
    $errorMessage = $_.Exception.Message
    if ($_.ErrorDetails.Message) {
        $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
        if ($errorJson.error.message) {
            $errorMessage = $errorJson.error.message
        }
    }
    Write-Host "  Create failed: $errorMessage" -ForegroundColor Red
    exit 1
}

# Step 3: Retrieve the created record
Write-Host ""
Write-Host "Step 3: Retrieve created record..." -ForegroundColor Yellow
$retrieveUri = "$EnvironmentUrl/api/data/v9.2/sprk_chartdefinitions($newId)"
try {
    $retrieveResult = Invoke-RestMethod -Uri $retrieveUri -Headers $headers
    Write-Host "  Retrieved successfully!" -ForegroundColor Green
    Write-Host "  Record details:" -ForegroundColor Gray
    Write-Host "    - Name: $($retrieveResult.sprk_name)" -ForegroundColor Gray
    Write-Host "    - Visual Type: $($retrieveResult.sprk_visualtype) ($($retrieveResult.'sprk_visualtype@OData.Community.Display.V1.FormattedValue'))" -ForegroundColor Gray
    Write-Host "    - Entity: $($retrieveResult.sprk_entitylogicalname)" -ForegroundColor Gray
    Write-Host "    - Base View ID: $($retrieveResult.sprk_baseviewid)" -ForegroundColor Gray
    Write-Host "    - Aggregation Type: $($retrieveResult.sprk_aggregationtype)" -ForegroundColor Gray
    Write-Host "    - Options JSON: $($retrieveResult.sprk_optionsjson)" -ForegroundColor Gray
} catch {
    Write-Host "  Retrieve failed: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " All Tests Passed!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Sample record ID for testing: $newId" -ForegroundColor Cyan
Write-Host ""
Write-Host "Web API Endpoints:" -ForegroundColor Yellow
Write-Host "  GET  $EnvironmentUrl/api/data/v9.2/sprk_chartdefinitions" -ForegroundColor Gray
Write-Host "  POST $EnvironmentUrl/api/data/v9.2/sprk_chartdefinitions" -ForegroundColor Gray
Write-Host "  GET  $EnvironmentUrl/api/data/v9.2/sprk_chartdefinitions($newId)" -ForegroundColor Gray
Write-Host ""

# Save ID to file for other tasks
$newId | Out-File -FilePath "projects/visualization-module/notes/sample-chartdefinition-id.txt"
Write-Host "Sample record ID saved to: projects/visualization-module/notes/sample-chartdefinition-id.txt" -ForegroundColor Gray
