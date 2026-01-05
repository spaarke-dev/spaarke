# Deploy-TypeLookups.ps1
# Populate type lookup tables for AI Document Intelligence R4
#
# Usage:
#   .\Deploy-TypeLookups.ps1
#   .\Deploy-TypeLookups.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"
#   .\Deploy-TypeLookups.ps1 -DryRun  # Preview without inserting

param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [switch]$DryRun = $false,
    [switch]$Force = $false  # Force insert even if records exist
)

$ErrorActionPreference = "Stop"

# Get the directory of this script
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$JsonPath = Join-Path $ScriptDir "type-lookups.json"

Write-Host "=== Deploy Type Lookups ===" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl"
Write-Host "JSON Source: $JsonPath"
if ($DryRun) {
    Write-Host "Mode: DRY RUN (no changes will be made)" -ForegroundColor Yellow
} else {
    Write-Host "Mode: LIVE"
}
Write-Host ""

# Load JSON data
if (-not (Test-Path $JsonPath)) {
    Write-Error "JSON file not found: $JsonPath"
    exit 1
}

$data = Get-Content $JsonPath -Raw | ConvertFrom-Json

# Get access token
Write-Host "Getting access token..." -ForegroundColor Gray
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
    'Prefer' = 'return=representation'
}

# Function to check if record exists
function Test-RecordExists {
    param(
        [string]$EntitySet,
        [string]$Name
    )

    $filter = "`$filter=sprk_name eq '$Name'"
    $uri = "$EnvironmentUrl/api/data/v9.2/$EntitySet`?$filter&`$select=sprk_name"

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
        return $result.value.Count -gt 0
    } catch {
        return $false
    }
}

# Function to insert record
function New-TypeRecord {
    param(
        [string]$EntitySet,
        [object]$Record
    )

    $name = $Record.sprk_name

    # Check if exists
    if (-not $Force) {
        if (Test-RecordExists -EntitySet $EntitySet -Name $name) {
            Write-Host "  SKIP: '$name' already exists" -ForegroundColor Yellow
            return @{ Status = "Skipped"; Name = $name }
        }
    }

    if ($DryRun) {
        Write-Host "  WOULD INSERT: '$name'" -ForegroundColor Gray
        return @{ Status = "DryRun"; Name = $name }
    }

    $uri = "$EnvironmentUrl/api/data/v9.2/$EntitySet"
    $body = $Record | ConvertTo-Json -Compress

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -Method Post -Body $body
        Write-Host "  INSERTED: '$name'" -ForegroundColor Green
        return @{ Status = "Inserted"; Name = $name }
    } catch {
        Write-Host "  ERROR: '$name' - $_" -ForegroundColor Red
        return @{ Status = "Error"; Name = $name; Error = $_.ToString() }
    }
}

# Process each entity type
$entitySets = @{
    'sprk_analysisactiontype' = 'sprk_analysisactiontypes'
    'sprk_aiskilltype' = 'sprk_aiskilltypes'
    'sprk_aiknowledgetype' = 'sprk_aiknowledgetypes'
    'sprk_aitooltype' = 'sprk_aitooltypes'
}

$summary = @{
    Inserted = 0
    Skipped = 0
    Errors = 0
    DryRun = 0
}

foreach ($entityKey in $entitySets.Keys) {
    $entitySet = $entitySets[$entityKey]
    $records = $data.$entityKey

    if (-not $records) {
        Write-Host "No data for $entityKey" -ForegroundColor Yellow
        continue
    }

    Write-Host ""
    Write-Host "Processing: $entityKey ($($records.Count) records)" -ForegroundColor Cyan

    foreach ($record in $records) {
        $result = New-TypeRecord -EntitySet $entitySet -Record $record
        $summary[$result.Status]++
    }
}

# Summary
Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "Inserted: $($summary.Inserted)" -ForegroundColor Green
Write-Host "Skipped:  $($summary.Skipped)" -ForegroundColor Yellow
Write-Host "Errors:   $($summary.Errors)" -ForegroundColor Red
if ($DryRun) {
    Write-Host "DryRun:   $($summary.DryRun)" -ForegroundColor Gray
}

if ($summary.Errors -gt 0) {
    exit 1
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
