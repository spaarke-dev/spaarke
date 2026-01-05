# Deploy-Tools.ps1
# Populate Tool records for AI Document Intelligence R4
#
# Usage:
#   .\Deploy-Tools.ps1
#   .\Deploy-Tools.ps1 -DryRun

param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [switch]$DryRun = $false,
    [switch]$Force = $false
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$JsonPath = Join-Path $ScriptDir "tools.json"

Write-Host "=== Deploy Tools ===" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl"
Write-Host "JSON Source: $JsonPath"
if ($DryRun) {
    Write-Host "Mode: DRY RUN" -ForegroundColor Yellow
} else {
    Write-Host "Mode: LIVE"
}
Write-Host ""

# Load JSON
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

# Build ToolType lookup
$toolTypeLookup = @{}
foreach ($prop in $data.toolTypes.PSObject.Properties) {
    $toolTypeLookup[$prop.Name] = $prop.Value
}

# Function to check if record exists
function Test-RecordExists {
    param([string]$Name)

    $filter = "`$filter=sprk_name eq '$Name'"
    $uri = "$EnvironmentUrl/api/data/v9.2/sprk_analysistools?$filter&`$select=sprk_name"

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
        return $result.value.Count -gt 0
    } catch {
        return $false
    }
}

# Function to insert record
function New-ToolRecord {
    param([object]$Tool)

    $name = $Tool.sprk_name

    # Check if exists
    if (-not $Force -and (Test-RecordExists -Name $name)) {
        Write-Host "  SKIP: '$name' already exists" -ForegroundColor Yellow
        return @{ Status = "Skipped"; Name = $name }
    }

    if ($DryRun) {
        Write-Host "  WOULD INSERT: '$name' (Handler: $($Tool.sprk_handlerclass))" -ForegroundColor Gray
        return @{ Status = "DryRun"; Name = $name }
    }

    # Build the record with ToolType lookup
    $toolTypeId = $toolTypeLookup[$Tool.toolType]
    if (-not $toolTypeId) {
        Write-Host "  ERROR: ToolType '$($Tool.toolType)' not found in lookup" -ForegroundColor Red
        return @{ Status = "Error"; Name = $name; Error = "ToolType not found" }
    }

    # Convert configuration to JSON string
    $configJson = $Tool.sprk_configuration | ConvertTo-Json -Depth 10 -Compress

    $record = @{
        "sprk_name" = $Tool.sprk_name
        "sprk_description" = $Tool.sprk_description
        "sprk_handlerclass" = $Tool.sprk_handlerclass
        "sprk_configuration" = $configJson
        "sprk_ToolTypeId@odata.bind" = "/sprk_aitooltypes($toolTypeId)"
    }

    $uri = "$EnvironmentUrl/api/data/v9.2/sprk_analysistools"
    $body = $record | ConvertTo-Json -Depth 10

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -Method Post -Body $body
        Write-Host "  INSERTED: '$name'" -ForegroundColor Green
        return @{ Status = "Inserted"; Name = $name }
    } catch {
        $errorMsg = $_.Exception.Message
        Write-Host "  ERROR: '$name' - $errorMsg" -ForegroundColor Red
        return @{ Status = "Error"; Name = $name; Error = $errorMsg }
    }
}

# Process tools
$summary = @{
    Inserted = 0
    Skipped = 0
    Errors = 0
    DryRun = 0
}

Write-Host "Processing $($data.tools.Count) Tool records..." -ForegroundColor Cyan
Write-Host ""

foreach ($tool in $data.tools) {
    $result = New-ToolRecord -Tool $tool
    $summary[$result.Status]++
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
