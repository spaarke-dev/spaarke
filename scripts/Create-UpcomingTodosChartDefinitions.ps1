<#
.SYNOPSIS
    Creates / updates the 4 'Upcoming To Dos' sprk_chartdefinition records for smart-todo-r4 (G).

.DESCRIPTION
    Reads the 4 JSON chart-def payloads from infrastructure/dataverse/charts/upcoming-todos-*.json
    and upserts one sprk_chartdefinition record per file via Dataverse Web API.

    Idempotency: keyed on sprk_name. Re-running the script updates the existing record's
    contract fields (sprk_entitylogicalname / sprk_contextfieldname / sprk_drillthroughtarget /
    sprk_visualtype / sprk_fetchxmlquery) instead of creating a duplicate.

    Auth: Azure CLI (az account get-access-token --resource <EnvironmentUrl>).
          Run 'az login' first, or set the DATAVERSE_URL environment variable.

    No hardcoded environment values (per spec NFR-03). Targets any Dataverse env via -EnvironmentUrl.

.PARAMETER EnvironmentUrl
    Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com).
    Defaults to the DATAVERSE_URL environment variable.

.PARAMETER ChartDefsRoot
    Directory holding the upcoming-todos-*.json files. Defaults to the
    repo-relative path infrastructure/dataverse/charts/ resolved from this script.

.PARAMETER DryRun
    Parse + report what WOULD happen; make no Web API writes.

.EXAMPLE
    pwsh -File scripts/Create-UpcomingTodosChartDefinitions.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.EXAMPLE
    $env:DATAVERSE_URL = "https://spaarkedev1.crm.dynamics.com"
    pwsh -File scripts/Create-UpcomingTodosChartDefinitions.ps1 -DryRun

.NOTES
    Project: smart-todo-r4
    Task:    080-G-create-chart-definitions
    Spec:    FR-31 through FR-36
    Spike:   projects/smart-todo-r4/notes/drill-through-spike.md (R4-003)
    Pattern: Mirrors scripts/create-test-chartdefinitions.ps1 (existing helper).
#>

param(
    [string]$EnvironmentUrl = $env:DATAVERSE_URL,
    [string]$ChartDefsRoot,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# 1. Resolve inputs
# ---------------------------------------------------------------------------

if (-not $EnvironmentUrl) {
    Write-Error "EnvironmentUrl is required. Set DATAVERSE_URL env var or pass -EnvironmentUrl."
    exit 1
}

if (-not $ChartDefsRoot) {
    # Resolve relative to the script location -> repo/infrastructure/dataverse/charts
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $ChartDefsRoot = Join-Path (Split-Path -Parent $scriptDir) "infrastructure/dataverse/charts"
}

if (-not (Test-Path $ChartDefsRoot)) {
    Write-Error "Chart-def directory not found: $ChartDefsRoot"
    exit 1
}

$jsonFiles = Get-ChildItem -Path $ChartDefsRoot -Filter "upcoming-todos-*.json" -File | Sort-Object Name

if ($jsonFiles.Count -eq 0) {
    Write-Error "No upcoming-todos-*.json files found in $ChartDefsRoot"
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Create Upcoming To Dos chart definitions" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
Write-Host "Source dir : $ChartDefsRoot" -ForegroundColor Yellow
Write-Host "Files found: $($jsonFiles.Count)" -ForegroundColor Yellow
if ($DryRun) {
    Write-Host "Mode       : DRY-RUN (no writes)" -ForegroundColor Magenta
}
Write-Host ""

# ---------------------------------------------------------------------------
# 2. Authenticate
# ---------------------------------------------------------------------------

Write-Host "Getting access token via Azure CLI..." -ForegroundColor Cyan
$token = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv 2>&1
if ($LASTEXITCODE -ne 0 -or -not $token) {
    Write-Error "Failed to get token. Run 'az login' first. Error: $token"
    exit 1
}
$token = $token.Trim()
Write-Host "  Token acquired" -ForegroundColor Green
Write-Host ""

$headers = @{
    "Authorization"    = "Bearer $token"
    "Accept"           = "application/json"
    "Content-Type"     = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
}
$apiBase = "$EnvironmentUrl/api/data/v9.2"

# ---------------------------------------------------------------------------
# 3. Helpers
# ---------------------------------------------------------------------------

# Read existing chart def records by sprk_name (single query, then in-memory match).
function Get-ExistingChartDefByName {
    param([string]$Name)

    $escaped = $Name.Replace("'", "''")
    $uri = "$apiBase/sprk_chartdefinitions?`$select=sprk_chartdefinitionid,sprk_name&`$filter=sprk_name eq '$escaped'"
    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $headers -Method GET
        if ($result.value -and $result.value.Count -gt 0) {
            return $result.value[0]
        }
        return $null
    }
    catch {
        Write-Host "    WARN: Lookup by name failed for '$Name' : $_" -ForegroundColor Yellow
        return $null
    }
}

# Strip metadata wrapper -> only fields safe to send to Web API.
function Get-RecordPayload {
    param([Parameter(Mandatory = $true)]$Json)

    if (-not $Json.record) {
        throw "JSON file is missing the 'record' property."
    }
    # Convert PSCustomObject -> Hashtable for clean ConvertTo-Json (keeps ordering simple)
    $payload = [ordered]@{}
    foreach ($prop in $Json.record.PSObject.Properties) {
        $payload[$prop.Name] = $prop.Value
    }
    return $payload
}

# Verify what was written by fetching the record back and reporting field-by-field match.
function Test-DeployedRecord {
    param(
        [string]$ChartDefId,
        [hashtable]$Expected
    )

    $selectCols = ($Expected.Keys + 'sprk_chartdefinitionid') -join ','
    $uri = "$apiBase/sprk_chartdefinitions($ChartDefId)?`$select=$selectCols"
    try {
        $actual = Invoke-RestMethod -Uri $uri -Headers $headers -Method GET
    }
    catch {
        Write-Host "    VERIFY FAIL: could not fetch record back: $_" -ForegroundColor Red
        return $false
    }

    $allMatch = $true
    foreach ($key in $Expected.Keys) {
        $expectedVal = $Expected[$key]
        $actualVal = $actual.$key
        if ($expectedVal -is [string] -and $actualVal -is [string]) {
            # Allow whitespace variance for multi-line fetchxml
            if ($expectedVal.Trim() -ne $actualVal.Trim()) {
                Write-Host "    MISMATCH on $key" -ForegroundColor Yellow
                $allMatch = $false
            }
        }
        elseif ($expectedVal -ne $actualVal) {
            Write-Host "    MISMATCH on $key : expected=$expectedVal actual=$actualVal" -ForegroundColor Yellow
            $allMatch = $false
        }
    }
    return $allMatch
}

# ---------------------------------------------------------------------------
# 4. Process each JSON file
# ---------------------------------------------------------------------------

$summary = @()

foreach ($jsonFile in $jsonFiles) {
    Write-Host "----- $($jsonFile.Name) -----" -ForegroundColor Cyan

    # Parse
    try {
        $raw = Get-Content -Path $jsonFile.FullName -Raw
        $doc = $raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        Write-Host "  PARSE FAIL: $_" -ForegroundColor Red
        $summary += [pscustomobject]@{ File = $jsonFile.Name; Result = "PARSE_FAIL"; Id = $null }
        continue
    }

    $payload = Get-RecordPayload -Json $doc
    $name = $payload['sprk_name']
    if (-not $name) {
        Write-Host "  RECORD MISSING sprk_name; skipping." -ForegroundColor Red
        $summary += [pscustomobject]@{ File = $jsonFile.Name; Result = "MISSING_NAME"; Id = $null }
        continue
    }

    Write-Host "  Name: $name" -ForegroundColor Gray
    Write-Host "  Context field: $($payload['sprk_contextfieldname'])" -ForegroundColor Gray
    Write-Host "  Drill target : $($payload['sprk_drillthroughtarget'])" -ForegroundColor Gray
    Write-Host "  Visual type  : $($payload['sprk_visualtype'])" -ForegroundColor Gray

    # Look up existing record
    $existing = Get-ExistingChartDefByName -Name $name

    if ($DryRun) {
        if ($existing) {
            Write-Host "  DRY-RUN: would PATCH existing record id=$($existing.sprk_chartdefinitionid)" -ForegroundColor Magenta
            $summary += [pscustomobject]@{ File = $jsonFile.Name; Result = "DRY_RUN_UPDATE"; Id = $existing.sprk_chartdefinitionid }
        }
        else {
            Write-Host "  DRY-RUN: would POST new record" -ForegroundColor Magenta
            $summary += [pscustomobject]@{ File = $jsonFile.Name; Result = "DRY_RUN_CREATE"; Id = $null }
        }
        continue
    }

    $body = $payload | ConvertTo-Json -Depth 10

    try {
        if ($existing) {
            # UPDATE (PATCH)
            $id = $existing.sprk_chartdefinitionid
            $patchUri = "$apiBase/sprk_chartdefinitions($id)"
            Write-Host "  Patching existing record id=$id ..." -ForegroundColor Gray
            Invoke-RestMethod -Uri $patchUri -Method PATCH -Headers $headers -Body $body | Out-Null
            Write-Host "  UPDATED" -ForegroundColor Green
        }
        else {
            # CREATE (POST) — capture id from OData-EntityId response header
            $createUri = "$apiBase/sprk_chartdefinitions"
            Write-Host "  Creating new record ..." -ForegroundColor Gray
            $response = Invoke-WebRequest -Uri $createUri -Method POST -Headers $headers -Body $body -UseBasicParsing
            $entityIdHeader = $response.Headers["OData-EntityId"]
            if ($entityIdHeader -is [array]) { $entityIdHeader = $entityIdHeader[0] }
            $id = $null
            if ($entityIdHeader -and $entityIdHeader -match "\(([0-9a-fA-F-]{36})\)") {
                $id = $Matches[1]
            }
            Write-Host "  CREATED id=$id" -ForegroundColor Green
        }

        # Post-write verification
        if ($id) {
            $verified = Test-DeployedRecord -ChartDefId $id -Expected $payload
            if ($verified) {
                Write-Host "  VERIFIED" -ForegroundColor Green
                $summary += [pscustomobject]@{ File = $jsonFile.Name; Result = "OK"; Id = $id }
            }
            else {
                Write-Host "  VERIFIED WITH MISMATCHES (review above)" -ForegroundColor Yellow
                $summary += [pscustomobject]@{ File = $jsonFile.Name; Result = "PARTIAL"; Id = $id }
            }
        }
        else {
            Write-Host "  WARN: could not parse new record id; manual verification required." -ForegroundColor Yellow
            $summary += [pscustomobject]@{ File = $jsonFile.Name; Result = "UNVERIFIED"; Id = $null }
        }
    }
    catch {
        $errMsg = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            try {
                $errJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
                if ($errJson.error.message) { $errMsg = $errJson.error.message }
            } catch { }
        }
        Write-Host "  FAILED: $errMsg" -ForegroundColor Red
        $summary += [pscustomobject]@{ File = $jsonFile.Name; Result = "ERROR"; Id = $null }
    }

    Write-Host ""
}

# ---------------------------------------------------------------------------
# 5. Summary
# ---------------------------------------------------------------------------

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
$summary | Format-Table -AutoSize | Out-String | Write-Host

$failed = ($summary | Where-Object { $_.Result -in @('ERROR', 'PARSE_FAIL', 'MISSING_NAME') }).Count
if ($failed -gt 0) {
    Write-Host "$failed record(s) FAILED. Review messages above." -ForegroundColor Red
    exit 1
}

Write-Host "Done. To verify all records:" -ForegroundColor Yellow
Write-Host "  pwsh -File scripts/query-chartdefinitions.ps1 -EnvironmentUrl `"$EnvironmentUrl`"" -ForegroundColor Gray
Write-Host ""
