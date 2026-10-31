<#
.SYNOPSIS
    Idempotently ensures the sprk_analysisaction row for one repo Action mirror exists and carries its row-level columns,
    then deploys the prompt and output schema through Deploy-ActionMirrors.ps1.

.DESCRIPTION
    ebilling-r1 task 042 (FR-46, D-52). scripts/Deploy-ActionMirrors.ps1 only PATCHES existing rows ("MISSING ROW ... NOT
    created") and owns only the contract columns (name, description, systemprompt, outputschemajson, inputschema, tags).
    A new Action therefore needs its row created first, with the columns that script deliberately leaves alone:
    sprk_actioncode, sprk_kind (prompted), sprk_modeltier, sprk_temperature. This script does exactly that, and nothing else:

      1. read infra/dataverse/actions/<ActionCode>.action.json (actionCode, name, description, modelTier, temperature);
      2. read the row by sprk_actioncode; CREATE it when absent (sprk_kind = 100000000 prompted, sprk_allowsknowledge = false),
         otherwise PATCH only the row-level columns that differ (modelTier, temperature);
      3. set sprk_inputschema from infra/dataverse/inputschemas/<ActionCode>.input.schema.json when that mirror exists;
      4. run Deploy-ActionMirrors.ps1 -Filter <ActionCode> for the prompt, output schema, name and description.

    Re-running against a converged environment changes nothing. Never deletes. Reusable by the other eBilling Actions
    (invoice-line-code, billing-rule-draft, invoice-firm-notice): pass their code.

.PARAMETER ActionCode
    sprk_actioncode of the mirror, e.g. invoice-classify.

.PARAMETER DataverseUrl
    Target environment. Default spaarkedev1.

.PARAMETER DryRun
    Print what would be created / patched and write nothing (also passed to Deploy-ActionMirrors.ps1).

.EXAMPLE
    .\Ensure-ActionRow.ps1 -ActionCode invoice-classify -DryRun
.EXAMPLE
    .\Ensure-ActionRow.ps1 -ActionCode invoice-classify
#>
param(
    [Parameter(Mandatory = $true)][string]$ActionCode,
    [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Split-Path -Parent (Split-Path -Parent $scriptDir)
$actionFile = Join-Path $repoRoot "infra\dataverse\actions\$ActionCode.action.json"
$inputFile  = Join-Path $repoRoot "infra\dataverse\inputschemas\$ActionCode.input.schema.json"
if (-not (Test-Path $actionFile)) { throw "Action mirror not found: $actionFile" }

# sprk_modeltier Choice (AiModelTier): Fast / Standard / Reasoning.
$tierValues = @{ 'Fast' = 100000000; 'Standard' = 100000001; 'Reasoning' = 100000002 }

$def = Get-Content $actionFile -Raw -Encoding UTF8 | ConvertFrom-Json
if ($def.actionCode -ne $ActionCode) { throw "Mirror actionCode '$($def.actionCode)' does not match -ActionCode '$ActionCode'." }
$tier = $null
if ($def.PSObject.Properties.Name -contains 'modelTier' -and $def.modelTier) {
    if (-not $tierValues.ContainsKey([string]$def.modelTier)) { throw "Unknown modelTier '$($def.modelTier)' (expected Fast, Standard or Reasoning)." }
    $tier = $tierValues[[string]$def.modelTier]
}
$temperature = if ($def.PSObject.Properties.Name -contains 'temperature') { [decimal]$def.temperature } else { $null }

$az = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'
$token = & $az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if (-not $token) { throw 'Failed to acquire a Dataverse access token (az account get-access-token). Run az login.' }
$api = "$DataverseUrl/api/data/v9.2"
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'Content-Type'     = 'application/json; charset=utf-8'
    'OData-Version'    = '4.0'
    'OData-MaxVersion' = '4.0'
}

Write-Host "Ensure-ActionRow: $ActionCode on $DataverseUrl$(if ($DryRun) { ' (DRY RUN)' })"

$escaped = $ActionCode.Replace("'", "''")
$existing = Invoke-RestMethod -Uri "$api/sprk_analysisactions?`$filter=sprk_actioncode eq '$escaped'&`$select=sprk_analysisactionid,sprk_modeltier,sprk_temperature,sprk_inputschema" -Headers $headers -Method Get
if ($existing.value.Count -gt 1) { throw "More than one sprk_analysisaction has sprk_actioncode '$ActionCode'; refusing to guess." }

$inputSchemaJson = $null
if (Test-Path $inputFile) {
    $inputDef = Get-Content $inputFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $inputSchemaJson = $inputDef.inputSchema | ConvertTo-Json -Depth 30 -Compress
}

if ($existing.value.Count -eq 0) {
    $body = [ordered]@{
        sprk_actioncode      = $ActionCode
        sprk_name            = [string]$def.name
        sprk_description     = [string]$def.description
        sprk_kind            = 100000000   # prompted
        sprk_allowsknowledge = $false
    }
    if ($null -ne $tier) { $body['sprk_modeltier'] = $tier }
    if ($null -ne $temperature) { $body['sprk_temperature'] = $temperature }
    if ($inputSchemaJson) { $body['sprk_inputschema'] = $inputSchemaJson }
    Write-Host "  CREATE row ($($body.Keys -join ', '))" -ForegroundColor Cyan
    if (-not $DryRun) {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 30))
        Invoke-RestMethod -Uri "$api/sprk_analysisactions" -Headers $headers -Method Post -Body $bytes | Out-Null
        Write-Host '  CREATED' -ForegroundColor Green
    }
} else {
    $row = $existing.value[0]
    $patch = [ordered]@{}
    if ($null -ne $tier -and $row.sprk_modeltier -ne $tier) { $patch['sprk_modeltier'] = $tier }
    if ($null -ne $temperature -and ($null -eq $row.sprk_temperature -or [decimal]$row.sprk_temperature -ne $temperature)) { $patch['sprk_temperature'] = $temperature }
    if ($inputSchemaJson) {
        $current = $null
        if ($row.sprk_inputschema) { try { $current = ($row.sprk_inputschema | ConvertFrom-Json) | ConvertTo-Json -Depth 30 -Compress } catch { $current = $null } }
        if ($current -ne $inputSchemaJson) { $patch['sprk_inputschema'] = $inputSchemaJson }
    }
    if ($patch.Count -eq 0) {
        Write-Host '  row-level columns UNCHANGED' -ForegroundColor DarkGray
    } else {
        Write-Host "  PATCH row ($($patch.Keys -join ', '))" -ForegroundColor Cyan
        if (-not $DryRun) {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes(($patch | ConvertTo-Json -Depth 30))
            Invoke-RestMethod -Uri "$api/sprk_analysisactions($($row.sprk_analysisactionid))" -Headers $headers -Method Patch -Body $bytes | Out-Null
            Write-Host '  PATCHED' -ForegroundColor Green
        }
    }
}

# Prompt, output schema, name and description: the existing deployer owns those columns. On a dry run against an
# environment where the row does not exist yet it reports MISSING ROW, which is expected.
$deployer = Join-Path $repoRoot 'scripts\Deploy-ActionMirrors.ps1'
if ($DryRun) { & $deployer -DataverseUrl $DataverseUrl -Filter $ActionCode -DryRun }
else { & $deployer -DataverseUrl $DataverseUrl -Filter $ActionCode }
