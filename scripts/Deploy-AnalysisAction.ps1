<#
.SYNOPSIS
    Upserts sprk_analysisaction records from a JSON definition file's `actions[]` array.

.DESCRIPTION
    Companion to `Deploy-Playbook.ps1`. The playbook deploy script resolves
    `actionCode` references against existing `sprk_analysisaction` rows; this
    script SEEDS those rows from the `actions[]` array in the same definition
    JSON file. Run this BEFORE `Deploy-Playbook.ps1` when a definition introduces
    a new action.

    Idempotent: upserts by `sprk_actioncode` — existing rows are PATCHed,
    new rows are POSTed.

    The action's foreign key to `sprk_analysisactiontype` is resolved by the
    `actionTypeName` field on each action in the JSON (e.g., "Summarize",
    "Analyze", "80 - Live Fact Resolver").

.PARAMETER DefinitionFile
    Path to the JSON definition file. The file MUST contain a top-level
    `actions: []` array.

.PARAMETER Environment
    Target Dataverse environment. Default: dev.

.PARAMETER DryRun
    Preview operations without writing.

.EXAMPLE
    .\Deploy-AnalysisAction.ps1 -DefinitionFile ./src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/summarize-document-for-chat.playbook.json

.NOTES
    Spaarke AI Platform Unification R5 — companion to Deploy-Playbook.ps1.
    Reuse mandate (R5 CLAUDE.md §3.1): this is a SIBLING focused script, not a
    refactor of the 973-line canonical Deploy-Playbook.ps1. Lower regression risk.

    Idempotency: uses GET-then-POST-or-PATCH pattern; safe to re-run.
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$DefinitionFile,

    [Parameter(Mandatory = $false)]
    [string]$Environment = "dev",

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Environment resolution (mirrors Deploy-Playbook.ps1 conventions)
# ---------------------------------------------------------------------------
$envMap = @{
    "dev"     = "https://spaarkedev1.crm.dynamics.com"
    "demo"    = "https://spaarke-demo.crm.dynamics.com"
}
if (-not $envMap.ContainsKey($Environment)) {
    Write-Error "Unknown environment '$Environment'. Known: $($envMap.Keys -join ', ')"
    exit 1
}
$DataverseUrl = $envMap[$Environment]
$ApiBase = "$DataverseUrl/api/data/v9.2"

Write-Host "=== Deploy-AnalysisAction (R5 task 010) ===" -ForegroundColor Cyan
Write-Host "Definition:  $DefinitionFile"
Write-Host "Environment: $Environment ($DataverseUrl)"
Write-Host "DryRun:      $DryRun"
Write-Host ""

# ---------------------------------------------------------------------------
# Load and validate definition
# ---------------------------------------------------------------------------
if (-not (Test-Path $DefinitionFile)) {
    Write-Error "Definition file not found: $DefinitionFile"
    exit 1
}
$definition = Get-Content $DefinitionFile -Raw | ConvertFrom-Json
$actions = $definition.actions
if (-not $actions -or $actions.Count -eq 0) {
    Write-Host "No actions[] array in definition; nothing to do." -ForegroundColor Yellow
    exit 0
}
Write-Host "Found $($actions.Count) action(s) to upsert." -ForegroundColor Green

# ---------------------------------------------------------------------------
# Authenticate
# ---------------------------------------------------------------------------
Write-Host "Authenticating via az..."
$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if (-not $token) {
    Write-Error "Failed to acquire Dataverse access token. Run 'az login' first."
    exit 1
}
$headers = @{
    "Authorization"      = "Bearer $token"
    "Accept"             = "application/json"
    "OData-MaxVersion"   = "4.0"
    "OData-Version"      = "4.0"
    "Content-Type"       = "application/json; charset=utf-8"
}

# ---------------------------------------------------------------------------
# Helper: resolve action-type lookup by name
# ---------------------------------------------------------------------------
function Resolve-ActionTypeId {
    param([string]$Name)
    $encoded = [Uri]::EscapeDataString($Name)
    $endpoint = "$ApiBase/sprk_analysisactiontypes?`$filter=sprk_name eq '$encoded'&`$select=sprk_analysisactiontypeid,sprk_name"
    $result = Invoke-RestMethod -Uri $endpoint -Headers $headers -Method Get
    if ($result.value.Count -eq 0) {
        Write-Error "Action type '$Name' not found in sprk_analysisactiontype. List available via the script's verbose log."
        return $null
    }
    return $result.value[0].sprk_analysisactiontypeid
}

# ---------------------------------------------------------------------------
# Helper: lookup existing action by code
# ---------------------------------------------------------------------------
function Get-ActionByCode {
    param([string]$Code)
    $encoded = [Uri]::EscapeDataString($Code)
    $endpoint = "$ApiBase/sprk_analysisactions?`$filter=sprk_actioncode eq '$encoded'&`$select=sprk_analysisactionid,sprk_actioncode"
    $result = Invoke-RestMethod -Uri $endpoint -Headers $headers -Method Get
    if ($result.value.Count -gt 0) {
        return $result.value[0].sprk_analysisactionid
    }
    return $null
}

# ---------------------------------------------------------------------------
# Upsert each action
# ---------------------------------------------------------------------------
$created = 0
$updated = 0
foreach ($action in $actions) {
    Write-Host ""
    Write-Host "Processing action '$($action.actionCode)' ($($action.name))" -ForegroundColor Cyan

    if (-not $action.actionCode) {
        Write-Error "Action missing 'actionCode'. Skipping."
        continue
    }
    if (-not $action.actionTypeName) {
        Write-Error "Action '$($action.actionCode)' missing 'actionTypeName' (e.g., 'Summarize'). Skipping."
        continue
    }

    # Resolve action type lookup
    $actionTypeId = Resolve-ActionTypeId -Name $action.actionTypeName
    if (-not $actionTypeId) { continue }
    Write-Host "  action type: '$($action.actionTypeName)' = $actionTypeId"

    # Build the upsert body
    $body = @{
        "sprk_actioncode"                       = $action.actionCode
        "sprk_name"                             = $action.name
        "sprk_description"                      = $action.description
        "sprk_systemprompt"                     = $action.systemPrompt
        "sprk_ActionTypeId@odata.bind"          = "/sprk_analysisactiontypes($actionTypeId)"
    }
    if ($action.outputSchema) {
        $body["sprk_outputschemajson"] = ($action.outputSchema | ConvertTo-Json -Depth 20 -Compress)
    }
    if ($action.tags) {
        $body["sprk_tags"] = $action.tags
    }

    # Lookup existing
    $existingId = Get-ActionByCode -Code $action.actionCode

    if ($DryRun) {
        if ($existingId) {
            Write-Host "  [DryRun] Would PATCH sprk_analysisactions($existingId)" -ForegroundColor Yellow
        }
        else {
            Write-Host "  [DryRun] Would POST new sprk_analysisaction" -ForegroundColor Yellow
        }
        continue
    }

    $jsonBody = $body | ConvertTo-Json -Depth 20 -Compress
    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($jsonBody)

    if ($existingId) {
        # PATCH
        $uri = "$ApiBase/sprk_analysisactions($existingId)"
        Invoke-RestMethod -Uri $uri -Headers $headers -Method Patch -Body $bodyBytes | Out-Null
        Write-Host "  PATCHED sprk_analysisactions($existingId)" -ForegroundColor Green
        $updated++
    }
    else {
        # POST
        $uri = "$ApiBase/sprk_analysisactions"
        $response = Invoke-WebRequest -Uri $uri -Headers $headers -Method Post -Body $bodyBytes
        $entityIdHeader = $response.Headers['OData-EntityId']
        $newId = $null
        if ($entityIdHeader) {
            $headerValue = if ($entityIdHeader -is [array]) { $entityIdHeader[0] } else { $entityIdHeader }
            if ($headerValue -match '\(([0-9a-fA-F\-]{36})\)') {
                $newId = $Matches[1]
            }
        }
        Write-Host "  CREATED sprk_analysisactions($newId)" -ForegroundColor Green
        $created++
    }
}

Write-Host ""
Write-Host "=== Done. Created: $created  Updated: $updated ===" -ForegroundColor Cyan
