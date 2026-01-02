# Create test chart definition records for all visual types
param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [switch]$DryRun
)

$token = az account get-access-token --resource $EnvironmentUrl --query 'accessToken' -o tsv

if (-not $token) {
    Write-Error "Failed to get access token. Run 'az login' first."
    exit 1
}

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
    'Content-Type' = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

# Visual Type values from ChartDefinitionTypes.ts
$VisualTypes = @{
    MetricCard = 100000000
    BarChart = 100000001
    LineChart = 100000002
    AreaChart = 100000003
    DonutChart = 100000004
    StatusBar = 100000005
    Calendar = 100000006
    MiniTable = 100000007
}

# Aggregation Type values
$AggregationTypes = @{
    Count = 100000000
    Sum = 100000001
    Average = 100000002
    Min = 100000003
    Max = 100000004
}

# First, get existing records to avoid duplicates
$existingUri = "$EnvironmentUrl/api/data/v9.2/sprk_chartdefinitions?`$select=sprk_name,sprk_visualtype"
$existingResult = Invoke-RestMethod -Uri $existingUri -Headers $headers
$existingNames = $existingResult.value | ForEach-Object { $_.sprk_name }

Write-Host "Existing chart definitions: $($existingNames -join ', ')"
Write-Host ""

# Define test records - using account entity which should exist
$testRecords = @(
    @{
        sprk_name = "Test - Active Accounts Count"
        sprk_visualtype = $VisualTypes.MetricCard
        sprk_entitylogicalname = "account"
        sprk_aggregationtype = $AggregationTypes.Count
        sprk_optionsjson = '{"title":"Active Accounts","icon":"Building"}'
    },
    @{
        sprk_name = "Test - Accounts by Industry (Bar)"
        sprk_visualtype = $VisualTypes.BarChart
        sprk_entitylogicalname = "account"
        sprk_groupbyfield = "industrycode"
        sprk_aggregationtype = $AggregationTypes.Count
        sprk_optionsjson = '{"title":"Accounts by Industry","showDataLabels":true}'
    },
    @{
        sprk_name = "Test - Accounts Created Over Time (Line)"
        sprk_visualtype = $VisualTypes.LineChart
        sprk_entitylogicalname = "account"
        sprk_groupbyfield = "createdon"
        sprk_aggregationtype = $AggregationTypes.Count
        sprk_optionsjson = '{"title":"Accounts Created Over Time","dateGrouping":"month","showDataPoints":true}'
    },
    @{
        sprk_name = "Test - Revenue by Account Type (Area)"
        sprk_visualtype = $VisualTypes.AreaChart
        sprk_entitylogicalname = "account"
        sprk_groupbyfield = "accountcategorycode"
        sprk_aggregationfield = "revenue"
        sprk_aggregationtype = $AggregationTypes.Sum
        sprk_optionsjson = '{"title":"Revenue by Account Type","stacked":true}'
    },
    @{
        sprk_name = "Test - Accounts by Status (Donut)"
        sprk_visualtype = $VisualTypes.DonutChart
        sprk_entitylogicalname = "account"
        sprk_groupbyfield = "statecode"
        sprk_aggregationtype = $AggregationTypes.Count
        sprk_optionsjson = '{"title":"Account Status Distribution","showPercentages":true,"showLegend":true}'
    },
    @{
        sprk_name = "Test - Account Status Bar"
        sprk_visualtype = $VisualTypes.StatusBar
        sprk_entitylogicalname = "account"
        sprk_groupbyfield = "statecode"
        sprk_aggregationtype = $AggregationTypes.Count
        sprk_optionsjson = '{"title":"Account Status","showCounts":true,"showPercentages":true}'
    },
    @{
        sprk_name = "Test - Top 10 Accounts (Table)"
        sprk_visualtype = $VisualTypes.MiniTable
        sprk_entitylogicalname = "account"
        sprk_aggregationtype = $AggregationTypes.Count
        sprk_optionsjson = '{"title":"Top 10 Accounts","maxRows":10,"columns":["name","revenue","statecode"],"sortField":"revenue","sortDirection":"desc"}'
    }
)

$createUri = "$EnvironmentUrl/api/data/v9.2/sprk_chartdefinitions"

foreach ($record in $testRecords) {
    if ($existingNames -contains $record.sprk_name) {
        Write-Host "SKIP: '$($record.sprk_name)' already exists" -ForegroundColor Yellow
        continue
    }

    $visualTypeName = $VisualTypes.GetEnumerator() | Where-Object { $_.Value -eq $record.sprk_visualtype } | Select-Object -ExpandProperty Key

    if ($DryRun) {
        Write-Host "DRY-RUN: Would create '$($record.sprk_name)' ($visualTypeName)" -ForegroundColor Cyan
        continue
    }

    try {
        $body = $record | ConvertTo-Json -Depth 3
        $result = Invoke-RestMethod -Uri $createUri -Method Post -Headers $headers -Body $body
        Write-Host "CREATED: '$($record.sprk_name)' ($visualTypeName)" -ForegroundColor Green
    } catch {
        Write-Host "ERROR: Failed to create '$($record.sprk_name)'" -ForegroundColor Red
        Write-Host "  Details: $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Done. Query all records with:"
Write-Host "  pwsh -File scripts/query-chartdefinitions.ps1"
