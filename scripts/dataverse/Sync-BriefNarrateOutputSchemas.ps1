<#
.SYNOPSIS
    Idempotent sync of sprk_outputschemajson on BRIEF-NARRATE-TLDR + BRIEF-NARRATE-CHANNEL
    sprk_analysisaction rows.

.DESCRIPTION
    The BRIEF-NARRATE-* JPS action files declare their output contract via output.fields[]
    inside the canonical source JSON at:
      projects/spaarke-daily-update-service/notes/playbooks/actions/brief-narrate-tldr.action.json
      projects/spaarke-daily-update-service/notes/playbooks/actions/brief-narrate-channel.action.json

    Those rows were originally deployed via Dataverse MCP create_record (R4 task 006),
    which populated sprk_systemprompt but did NOT populate sprk_outputschemajson — the
    AiCompletion executor reads sprk_outputschemajson at runtime to constrain LLM
    structured-completion output. Without it, /narrate dispatch through the playbook
    engine still routes correctly but the LLM has no enforced output schema.

    This script derives a JSON Schema draft-07 from each source action's output.fields[]
    and PATCHes the deployed row. Idempotent — re-running with no changes is a no-op
    other than the round-trip PATCH (which Dataverse OData treats as a write).

    Action IDs are pinned from the R4 deploy notes:
      BRIEF-NARRATE-TLDR    ce299eb4-fc70-f111-ab0e-7ced8ddc4cc6
      BRIEF-NARRATE-CHANNEL dc3533c0-fc70-f111-ab0e-7ced8ddc4cc6

.PARAMETER DataverseUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL or spaarkedev1.

.PARAMETER DryRun
    Print what would be PATCHed without applying.

.EXAMPLE
    ./Sync-BriefNarrateOutputSchemas.ps1
    Sync schemas on spaarkedev1.

.EXAMPLE
    ./Sync-BriefNarrateOutputSchemas.ps1 -DryRun
    Print derived schemas without writing.
#>
param(
    [string]$DataverseUrl = ($env:DATAVERSE_URL ?? 'https://spaarkedev1.crm.dynamics.com'),
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$ApiBase = "$DataverseUrl/api/data/v9.2"

# Canonical action IDs (resolved 2026-06-25, hardcoded so script doesn't need a lookup roundtrip).
$Actions = @(
    @{
        Code     = 'BRIEF-NARRATE-TLDR'
        Id       = 'ce299eb4-fc70-f111-ab0e-7ced8ddc4cc6'
        Source   = 'projects/spaarke-daily-update-service/notes/playbooks/actions/brief-narrate-tldr.action.json'
        Required = @('summary', 'keyTakeaways', 'topAction', 'categoryCount', 'priorityItemCount')
    },
    @{
        Code     = 'BRIEF-NARRATE-CHANNEL'
        Id       = 'dc3533c0-fc70-f111-ab0e-7ced8ddc4cc6'
        Source   = 'projects/spaarke-daily-update-service/notes/playbooks/actions/brief-narrate-channel.action.json'
        Required = @('channel', 'narrative', 'itemCount', 'bulletCount')
    }
)

# Map JPS output.fields[].type -> JSON Schema type
function Convert-JpsTypeToSchemaType {
    param([string]$JpsType)
    switch ($JpsType) {
        'string'  { return 'string' }
        'number'  { return 'integer' }
        'boolean' { return 'boolean' }
        'array'   { return 'array' }
        default   { throw "Unknown JPS field type '$JpsType' — extend Convert-JpsTypeToSchemaType." }
    }
}

# Build a JSON Schema draft-07 object from a JPS action's output.fields[] array
function Build-SchemaFromJpsAction {
    param(
        [hashtable]$ActionJson,
        [string[]]$Required
    )
    $properties = [ordered]@{}
    foreach ($field in $ActionJson.output.fields) {
        $schemaType = Convert-JpsTypeToSchemaType -JpsType $field.type
        $prop = [ordered]@{
            type        = $schemaType
            description = $field.description
        }
        if ($field.maxLength -and $schemaType -eq 'string') {
            $prop['maxLength'] = [int]$field.maxLength
        }
        if ($field.maxLength -and $schemaType -eq 'array') {
            $prop['maxItems'] = [int]$field.maxLength
            $prop['items'] = @{ type = 'string' }
        }
        $properties[$field.name] = $prop
    }
    return [ordered]@{
        '$schema'             = 'http://json-schema.org/draft-07/schema#'
        type                  = 'object'
        additionalProperties  = $false
        required              = $Required
        properties            = $properties
    }
}

function Get-Headers {
    $AZ = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'
    $token = & $AZ account get-access-token --resource $DataverseUrl --query accessToken -o tsv
    if (-not $token) { throw "Failed to acquire access token. Run 'az login' first." }
    return @{
        'Authorization'    = "Bearer $token"
        'Content-Type'     = 'application/json'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        'Accept'           = 'application/json'
        'If-Match'         = '*'
    }
}

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$headers = if (-not $DryRun) { Get-Headers } else { $null }

Write-Host ""
Write-Host "Sync-BriefNarrateOutputSchemas — $DataverseUrl" -ForegroundColor Cyan
Write-Host "RepoRoot: $RepoRoot" -ForegroundColor DarkGray
if ($DryRun) { Write-Host "(DryRun — no writes)" -ForegroundColor Yellow }
Write-Host ""

foreach ($a in $Actions) {
    $sourcePath = Join-Path $RepoRoot $a.Source
    if (-not (Test-Path $sourcePath)) {
        Write-Host "  SKIP $($a.Code) — source missing: $sourcePath" -ForegroundColor Yellow
        continue
    }
    $actionJson = Get-Content $sourcePath -Raw | ConvertFrom-Json -AsHashtable
    $schema = Build-SchemaFromJpsAction -ActionJson $actionJson -Required $a.Required
    $schemaJson = $schema | ConvertTo-Json -Depth 25 -Compress

    Write-Host "  $($a.Code) ($($a.Id)) — $($schemaJson.Length) chars"
    if ($DryRun) {
        Write-Host "    [DryRun] Would PATCH sprk_outputschemajson" -ForegroundColor Yellow
        Write-Host "    $schemaJson" -ForegroundColor DarkGray
        continue
    }

    try {
        $body = @{ sprk_outputschemajson = $schemaJson } | ConvertTo-Json -Compress
        Invoke-RestMethod -Uri "$ApiBase/sprk_analysisactions($($a.Id))" -Headers $headers -Method Patch -Body $body | Out-Null
        Write-Host "    PATCHED" -ForegroundColor Green
    } catch {
        Write-Host "    FAIL: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
