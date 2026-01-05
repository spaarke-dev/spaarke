# Deploy-Knowledge.ps1
# Populate Knowledge records for AI Document Intelligence R4
#
# Usage:
#   .\Deploy-Knowledge.ps1
#   .\Deploy-Knowledge.ps1 -DryRun

param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [switch]$DryRun = $false,
    [switch]$Force = $false
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$JsonPath = Join-Path $ScriptDir "knowledge.json"

Write-Host "=== Deploy Knowledge ===" -ForegroundColor Cyan
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

# Build KnowledgeType lookup
$knowledgeTypeLookup = @{}
foreach ($prop in $data.knowledgeTypes.PSObject.Properties) {
    $knowledgeTypeLookup[$prop.Name] = $prop.Value
}

# Function to check if record exists
function Test-RecordExists {
    param([string]$Name)

    $filter = "`$filter=sprk_name eq '$Name'"
    $uri = "$EnvironmentUrl/api/data/v9.2/sprk_analysisknowledges?$filter&`$select=sprk_name"

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
        return $result.value.Count -gt 0
    } catch {
        return $false
    }
}

# Function to insert record
function New-KnowledgeRecord {
    param([object]$Knowledge)

    $name = $Knowledge.sprk_name

    # Check if exists
    if (-not $Force -and (Test-RecordExists -Name $name)) {
        Write-Host "  SKIP: '$name' already exists" -ForegroundColor Yellow
        return @{ Status = "Skipped"; Name = $name }
    }

    $type = if ($Knowledge.isInline) { "Inline" } else { "RAG" }
    if ($DryRun) {
        Write-Host "  WOULD INSERT: '$name' (Type: $type)" -ForegroundColor Gray
        return @{ Status = "DryRun"; Name = $name }
    }

    # Build the record with KnowledgeType lookup
    $knowledgeTypeId = $knowledgeTypeLookup[$Knowledge.knowledgeType]
    if (-not $knowledgeTypeId) {
        Write-Host "  ERROR: KnowledgeType '$($Knowledge.knowledgeType)' not found in lookup" -ForegroundColor Red
        return @{ Status = "Error"; Name = $name; Error = "KnowledgeType not found" }
    }

    $record = @{
        "sprk_name" = $Knowledge.sprk_name
        "sprk_description" = $Knowledge.sprk_description
        "sprk_KnowledgeTypeId@odata.bind" = "/sprk_aiknowledgetypes($knowledgeTypeId)"
    }

    # Add content for inline types, or note RAG deployment reference in description
    if ($Knowledge.isInline -and $Knowledge.sprk_content) {
        $record["sprk_content"] = $Knowledge.sprk_content
    } elseif (-not $Knowledge.isInline -and $Knowledge.ragDeployment) {
        # For RAG-based knowledge, store deployment reference in content for now
        # (actual KnowledgeSource linking would be done separately if needed)
        $record["sprk_content"] = "RAG Deployment: $($Knowledge.ragDeployment)"
    }

    $uri = "$EnvironmentUrl/api/data/v9.2/sprk_analysisknowledges"
    $body = $record | ConvertTo-Json -Depth 10

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -Method Post -Body $body
        Write-Host "  INSERTED: '$name' ($type)" -ForegroundColor Green
        return @{ Status = "Inserted"; Name = $name }
    } catch {
        $errorMsg = $_.Exception.Message
        Write-Host "  ERROR: '$name' - $errorMsg" -ForegroundColor Red
        return @{ Status = "Error"; Name = $name; Error = $errorMsg }
    }
}

# Process knowledge records
$summary = @{
    Inserted = 0
    Skipped = 0
    Errors = 0
    DryRun = 0
}

Write-Host "Processing $($data.knowledge.Count) Knowledge records..." -ForegroundColor Cyan
Write-Host ""

foreach ($knowledge in $data.knowledge) {
    $result = New-KnowledgeRecord -Knowledge $knowledge
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
