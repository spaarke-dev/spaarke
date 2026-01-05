# Deploy-Skills.ps1
# Populate Skill records for AI Document Intelligence R4
#
# Usage:
#   .\Deploy-Skills.ps1
#   .\Deploy-Skills.ps1 -DryRun

param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [switch]$DryRun = $false,
    [switch]$Force = $false
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$JsonPath = Join-Path $ScriptDir "skills.json"

Write-Host "=== Deploy Skills ===" -ForegroundColor Cyan
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

# Build SkillType lookup
$skillTypeLookup = @{}
foreach ($prop in $data.skillTypes.PSObject.Properties) {
    $skillTypeLookup[$prop.Name] = $prop.Value
}

# Function to check if record exists
function Test-RecordExists {
    param([string]$Name)

    $filter = "`$filter=sprk_name eq '$Name'"
    $uri = "$EnvironmentUrl/api/data/v9.2/sprk_analysisskills?$filter&`$select=sprk_name"

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
        return $result.value.Count -gt 0
    } catch {
        return $false
    }
}

# Function to insert record
function New-SkillRecord {
    param([object]$Skill)

    $name = $Skill.sprk_name

    # Check if exists
    if (-not $Force -and (Test-RecordExists -Name $name)) {
        Write-Host "  SKIP: '$name' already exists" -ForegroundColor Yellow
        return @{ Status = "Skipped"; Name = $name }
    }

    if ($DryRun) {
        Write-Host "  WOULD INSERT: '$name' (Type: $($Skill.skillType))" -ForegroundColor Gray
        return @{ Status = "DryRun"; Name = $name }
    }

    # Build the record with SkillType lookup
    $skillTypeId = $skillTypeLookup[$Skill.skillType]
    if (-not $skillTypeId) {
        Write-Host "  ERROR: SkillType '$($Skill.skillType)' not found in lookup" -ForegroundColor Red
        return @{ Status = "Error"; Name = $name; Error = "SkillType not found" }
    }

    $record = @{
        "sprk_name" = $Skill.sprk_name
        "sprk_description" = $Skill.sprk_description
        "sprk_promptfragment" = $Skill.sprk_promptfragment
        "sprk_SkillTypeId@odata.bind" = "/sprk_aiskilltypes($skillTypeId)"
    }

    $uri = "$EnvironmentUrl/api/data/v9.2/sprk_analysisskills"
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

# Process skills
$summary = @{
    Inserted = 0
    Skipped = 0
    Errors = 0
    DryRun = 0
}

Write-Host "Processing $($data.skills.Count) Skill records..." -ForegroundColor Cyan
Write-Host ""

foreach ($skill in $data.skills) {
    $result = New-SkillRecord -Skill $skill
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
