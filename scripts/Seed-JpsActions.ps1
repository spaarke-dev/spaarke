<#
.SYNOPSIS
    Seeds Dataverse Action records with JPS (JSON Prompt Schema) definitions.

.DESCRIPTION
    Updates the sprk_systemprompt field on existing sprk_analysisactions records
    in Dataverse with JPS JSON content from conversion files.

    This script is idempotent — it can safely be run multiple times. Each run
    queries the Action record by sprk_name, optionally backs up the existing
    sprk_systemprompt value, then overwrites it with the JPS JSON content.

    JPS files are sourced from two project directories:
      - projects/ai-json-prompt-schema-system/notes/jps-conversions/
      - projects/jps-server-rollout/notes/jps-conversions/

    Authentication uses Azure CLI (az account get-access-token) by default,
    or you can pass a bearer token directly via -Token.

.PARAMETER Environment
    Target Dataverse environment. Accepts 'dev', 'test', 'prod', or a full URL.
    Default: dev

.PARAMETER Token
    Optional bearer token. If not provided, the script obtains one via Azure CLI.

.PARAMETER BackupPath
    Optional directory path to save existing sprk_systemprompt values before
    overwriting. Each backup is saved as {ActionName}.backup.json.

.PARAMETER JpsRoot
    Root directory of the repository. Defaults to two levels above the script.
    Used to resolve JPS file paths.

.EXAMPLE
    # Dry run against dev (preview changes without writing)
    .\Seed-JpsActions.ps1 -WhatIf

.EXAMPLE
    # Seed dev environment
    .\Seed-JpsActions.ps1

.EXAMPLE
    # Seed with backup of existing prompts
    .\Seed-JpsActions.ps1 -BackupPath ./backups

.EXAMPLE
    # Seed test environment with explicit token
    .\Seed-JpsActions.ps1 -Environment test -Token "eyJ0eXAi..."

.EXAMPLE
    # Seed a custom environment URL
    .\Seed-JpsActions.ps1 -Environment "https://myorg.crm.dynamics.com"

.NOTES
    Entity:       sprk_analysisactions
    Key field:    sprk_name (text) — used to look up the record
    Target field: sprk_systemprompt (text) — receives the JPS JSON content
    API version:  v9.2

    Prerequisites:
      - Azure CLI installed and authenticated (az login) — unless using -Token
      - Target Action records must already exist in Dataverse
      - PowerShell Core 7+ recommended
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateScript({
        $_ -in @('dev', 'test', 'prod') -or $_ -match '^https://.+\.crm\d?\.dynamics\.com$'
    })]
    [string]$Environment = 'dev',

    [string]$Token,

    [string]$BackupPath,

    [string]$JpsRoot
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Environment URL resolution
# ---------------------------------------------------------------------------
$EnvironmentUrls = @{
    'dev'  = 'https://spaarkedev1.crm.dynamics.com'
    'test' = 'https://spaarketest1.crm.dynamics.com'
    'prod' = 'https://spaarkeprod1.crm.dynamics.com'
}

if ($EnvironmentUrls.ContainsKey($Environment)) {
    $EnvironmentUrl = $EnvironmentUrls[$Environment]
} else {
    $EnvironmentUrl = $Environment.TrimEnd('/')
}

# ---------------------------------------------------------------------------
# Resolve repository root and JPS file paths
# ---------------------------------------------------------------------------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $JpsRoot) {
    $JpsRoot = (Resolve-Path (Join-Path $ScriptDir '..')).Path
}

# Map: Dataverse Action name -> relative JPS file path from repo root
$ActionMappings = @(
    @{ Name = 'Analyze Clauses';    File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/clause-analyzer.json' }
    @{ Name = 'Extract Dates';      File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/date-extractor.json' }
    @{ Name = 'Classify Document';  File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/document-classifier.json' }
    @{ Name = 'Extract Entities';   File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/entity-extractor.json' }
    @{ Name = 'Calculate Values';   File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/financial-calculator.json' }
    @{ Name = 'Detect Risks';       File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/risk-detector.json' }
    @{ Name = 'Document Profiler';  File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/document-profiler.json' }
    @{ Name = 'Compare Clauses';    File = 'projects/jps-server-rollout/notes/jps-conversions/clause-comparison.json' }
    @{ Name = 'Semantic Search';    File = 'projects/jps-server-rollout/notes/jps-conversions/semantic-search.json' }
    @{ Name = 'Summarize Content';  File = 'projects/jps-server-rollout/notes/jps-conversions/summary-handler.json' }

    # ACT-001 through ACT-008: Playbook-specific analysis actions
    @{ Name = 'Contract Review';              File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/ACT-001.json' }
    @{ Name = 'NDA Analysis';                 File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/ACT-002.json' }
    @{ Name = 'Lease Agreement Review';       File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/ACT-003.json' }
    @{ Name = 'Invoice Processing';           File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/ACT-004.json' }
    @{ Name = 'SLA Analysis';                 File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/ACT-005.json' }
    @{ Name = 'Employment Agreement Review';  File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/ACT-006.json' }
    @{ Name = 'Statement of Work Analysis';   File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/ACT-007.json' }
    @{ Name = 'General Legal Document Review'; File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/ACT-008.json' }

    # Additional actions
    @{ Name = 'SprkChat Document Assistant';  File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/sprkchat-document-assistant.json' }
    @{ Name = 'Review Agreement';             File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/review-agreement.json' }
    @{ Name = 'Prepare Response';             File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/prepare-response.json' }
    @{ Name = 'Extract Data';                 File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/extract-data.json' }
    @{ Name = 'Compare Documents';            File = 'projects/ai-json-prompt-schema-system/notes/jps-conversions/compare-documents.json' }
)

# ---------------------------------------------------------------------------
# Authentication
# ---------------------------------------------------------------------------
function Get-DataverseToken {
    param([string]$ResourceUrl)

    if ($Token) {
        return $Token
    }

    Write-Host 'Acquiring token via Azure CLI...' -ForegroundColor Gray
    $t = az account get-access-token --resource $ResourceUrl --query 'accessToken' -o tsv 2>$null

    if (-not $t) {
        throw "Failed to acquire access token. Run 'az login' first, or pass -Token explicitly."
    }

    return $t
}

# ---------------------------------------------------------------------------
# Dataverse helpers
# ---------------------------------------------------------------------------
$ApiBase = "$EnvironmentUrl/api/data/v9.2"
$EntitySet = 'sprk_analysisactions'

function Get-DataverseHeaders {
    param([string]$BearerToken)

    return @{
        'Authorization'  = "Bearer $BearerToken"
        'Accept'         = 'application/json'
        'Content-Type'   = 'application/json; charset=utf-8'
        'OData-MaxVersion' = '4.0'
        'OData-Version'  = '4.0'
    }
}

function Get-ActionByName {
    <#
    .SYNOPSIS
        Queries Dataverse for an Action record by sprk_name.
        Returns the record (with id and sprk_systemprompt) or $null.
    #>
    param(
        [string]$Name,
        [hashtable]$Headers
    )

    $encodedName = [Uri]::EscapeDataString($Name)
    $filter = "`$filter=sprk_name eq '$encodedName'"
    $select = "`$select=sprk_analysisactionid,sprk_name,sprk_systemprompt"
    $uri = "$ApiBase/$($EntitySet)?$filter&$select"

    try {
        $result = Invoke-RestMethod -Uri $uri -Headers $Headers -Method Get
        if ($result.value -and $result.value.Count -gt 0) {
            return $result.value[0]
        }
        return $null
    } catch {
        Write-Warning "  Query failed for '$Name': $($_.Exception.Message)"
        return $null
    }
}

function Backup-ExistingPrompt {
    <#
    .SYNOPSIS
        Saves the current sprk_systemprompt value to a backup file.
    #>
    param(
        [string]$ActionName,
        [string]$CurrentPrompt,
        [string]$OutputDir
    )

    if (-not $OutputDir) { return }
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }

    $safeName = $ActionName -replace '[^a-zA-Z0-9\-_ ]', '' -replace '\s+', '-'
    $backupFile = Join-Path $OutputDir "$safeName.backup.json"

    $backupData = @{
        actionName  = $ActionName
        backedUpAt  = (Get-Date -Format 'o')
        environment = $EnvironmentUrl
        content     = $CurrentPrompt
    } | ConvertTo-Json -Depth 4

    Set-Content -Path $backupFile -Value $backupData -Encoding utf8
    Write-Host "    Backup saved: $backupFile" -ForegroundColor Gray
}

function Update-ActionPrompt {
    <#
    .SYNOPSIS
        Updates the sprk_systemprompt field on an Action record via PATCH.
    #>
    param(
        [string]$RecordId,
        [string]$ActionName,
        [string]$JpsContent,
        [hashtable]$Headers
    )

    $uri = "$ApiBase/$EntitySet($RecordId)"
    $body = @{
        sprk_systemprompt = $JpsContent
    } | ConvertTo-Json -Depth 2

    try {
        Invoke-RestMethod -Uri $uri -Headers $Headers -Method Patch -Body ([System.Text.Encoding]::UTF8.GetBytes($body))
        return $true
    } catch {
        Write-Warning "  PATCH failed for '$ActionName': $($_.Exception.Message)"
        return $false
    }
}

# ===========================================================================
# Main execution
# ===========================================================================

Write-Host ''
Write-Host '=== Seed JPS Actions ===' -ForegroundColor Cyan
Write-Host "Environment : $EnvironmentUrl"
Write-Host "Repo root   : $JpsRoot"
Write-Host "Mappings    : $($ActionMappings.Count) actions"
if ($BackupPath) {
    Write-Host "Backup path : $BackupPath"
}
if ($WhatIfPreference) {
    Write-Host 'Mode        : DRY RUN (WhatIf)' -ForegroundColor Yellow
} else {
    Write-Host 'Mode        : LIVE' -ForegroundColor Green
}
Write-Host ''

# --- Validate JPS files exist before doing any network calls ---
$missingFiles = @()
foreach ($mapping in $ActionMappings) {
    $fullPath = Join-Path $JpsRoot $mapping.File
    if (-not (Test-Path $fullPath)) {
        $missingFiles += "$($mapping.Name) -> $($mapping.File)"
    }
}

if ($missingFiles.Count -gt 0) {
    Write-Host 'Missing JPS files:' -ForegroundColor Red
    $missingFiles | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    throw "Cannot proceed: $($missingFiles.Count) JPS file(s) not found."
}

Write-Host "All $($ActionMappings.Count) JPS files found." -ForegroundColor Green
Write-Host ''

# --- Acquire token (skip for WhatIf if no token provided) ---
$bearerToken = $null
$headers = $null

if (-not $WhatIfPreference -or $Token) {
    $bearerToken = Get-DataverseToken -ResourceUrl $EnvironmentUrl
    $headers = Get-DataverseHeaders -BearerToken $bearerToken
}

# --- Process each mapping ---
$summary = @{ Updated = 0; Skipped = 0; NotFound = 0; Errors = 0; DryRun = 0 }

foreach ($mapping in $ActionMappings) {
    $actionName = $mapping.Name
    $jpsFilePath = Join-Path $JpsRoot $mapping.File
    $jpsContent = Get-Content $jpsFilePath -Raw -Encoding utf8

    Write-Host "[$actionName]" -ForegroundColor White

    # WhatIf mode — report what would happen without querying Dataverse
    if ($PSCmdlet.ShouldProcess("Action '$actionName'", 'Update sprk_systemprompt with JPS content')) {

        # Look up the action record
        $record = Get-ActionByName -Name $actionName -Headers $headers

        if (-not $record) {
            Write-Host "  NOT FOUND: No Action record with sprk_name='$actionName'" -ForegroundColor Red
            $summary.NotFound++
            continue
        }

        $recordId = $record.sprk_analysisactionid

        # Optional backup
        if ($BackupPath -and $record.sprk_systemprompt) {
            Backup-ExistingPrompt -ActionName $actionName `
                                  -CurrentPrompt $record.sprk_systemprompt `
                                  -OutputDir $BackupPath
        }

        # Check if content is already identical (idempotent skip)
        if ($record.sprk_systemprompt -eq $jpsContent) {
            Write-Host "  UNCHANGED: Content already matches JPS file" -ForegroundColor Yellow
            $summary.Skipped++
            continue
        }

        # Perform the update
        $success = Update-ActionPrompt -RecordId $recordId `
                                       -ActionName $actionName `
                                       -JpsContent $jpsContent `
                                       -Headers $headers

        if ($success) {
            $jpsSize = [math]::Round($jpsContent.Length / 1024, 1)
            Write-Host "  UPDATED: sprk_systemprompt set (${jpsSize} KB)" -ForegroundColor Green
            $summary.Updated++
        } else {
            $summary.Errors++
        }
    } else {
        # ShouldProcess returned false — WhatIf / Confirm denied
        $jpsSize = [math]::Round($jpsContent.Length / 1024, 1)
        Write-Host "  WOULD UPDATE: sprk_systemprompt with $($mapping.File) (${jpsSize} KB)" -ForegroundColor Gray
        $summary.DryRun++
    }
}

# --- Summary ---
Write-Host ''
Write-Host '=== Summary ===' -ForegroundColor Cyan
Write-Host "Updated   : $($summary.Updated)" -ForegroundColor Green
Write-Host "Unchanged : $($summary.Skipped)" -ForegroundColor Yellow
Write-Host "Not Found : $($summary.NotFound)" -ForegroundColor Red
Write-Host "Errors    : $($summary.Errors)" -ForegroundColor Red
if ($summary.DryRun -gt 0) {
    Write-Host "Dry Run   : $($summary.DryRun)" -ForegroundColor Gray
}
Write-Host ''

if ($summary.Errors -gt 0 -or $summary.NotFound -gt 0) {
    $exitCode = $summary.Errors + $summary.NotFound
    Write-Host "Completed with $exitCode issue(s)." -ForegroundColor Yellow
    exit 1
}

Write-Host 'Done!' -ForegroundColor Green
