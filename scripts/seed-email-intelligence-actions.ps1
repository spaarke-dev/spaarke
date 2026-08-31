<#
.SYNOPSIS
  Seed the 3 email-intelligence sprk_analysisaction rows (TRIAGE-EMAIL, PROPOSE-FIELD-UPDATES,
  CREATE-TASK-FROM-EMAIL) into a Dataverse environment from their infra mirror .action.json files.

.DESCRIPTION
  UAT Fix #4 (email-communication-intelligence-r2). Root cause: these 3 Linear-AI-Consumer Actions
  (+ their sprk_playbookconsumer routing rows) were never seeded to spaarkedev1, so
  ActionResolver.ResolveAsync("email-triage"/"email-propose"/"email-create-task") throws and the
  triage/Job-B/Job-C enrichment steps degrade to null (NFR-04) — leaving the reconciliation grid's
  triage columns blank on every real capture. See notes/DEFECT-triage-not-populating-root-cause.md.

  Idempotent UPSERT by sprk_actioncode (GET-then-PATCH-or-POST). Handles BOTH action shapes:
    * classic-JPS (triage-email: root has $schema) -> sprk_systemprompt = the JPS root (minus $comment*
      keys + deploy-row scalars); sprk_outputschemajson DERIVED from output.fields[] ($choices fields
      become free strings — dynamic option sets/taxonomy, not static enums, per FR-16).
    * flat-prompt (propose-field-updates / create-task-from-email: no root $schema) ->
      sprk_systemprompt = j.systemPrompt; sprk_outputschemajson = j.outputSchema (ready-made draft-07).

  sprk_outputschemajson is MANDATORY even for JPS actions (ActionRunner.RunAsync:122-127 throws if empty).

  Seed the ROUTING rows AFTER this: scripts/dataverse/Seed-PlaybookConsumers.ps1 (default Seed mode)
  resolves actionCode -> sprk_analysisaction per-env, so the Action rows must exist first.

.PARAMETER DataverseUrl
  Default: https://spaarkedev1.crm.dynamics.com
.PARAMETER DryRun
  Print the payloads without writing.
#>
param(
  [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com',
  [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$api = "$DataverseUrl/api/data/v9.2"
$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
$headers = @{
  Authorization      = "Bearer $token"
  'Content-Type'     = 'application/json'
  'OData-MaxVersion' = '4.0'
  'OData-Version'    = '4.0'
  Prefer             = 'return=representation'
}
$actionsDir = 'infra/dataverse/actions'
# ExecutorType.AiAnalysis prompted actions: Fast tier = 100000000 (AiModelTier.Fast).
$ModelTierFast = 100000000

# Remove $comment* + deploy-row scalar keys from a PSCustomObject, in place.
function Remove-Keys($obj, [string[]]$explicit) {
  $toRemove = @()
  foreach ($p in $obj.PSObject.Properties) {
    if ($p.Name -like '$comment*' -or $explicit -contains $p.Name) { $toRemove += $p.Name }
  }
  foreach ($n in $toRemove) { $obj.PSObject.Properties.Remove($n) }
}

# Derive a draft-07 constrained-decoding schema from a classic-JPS output.fields[] section.
function ConvertTo-OutputSchema($outputFields) {
  $props = [ordered]@{}
  $required = @()
  foreach ($f in $outputFields) {
    $required += $f.name
    if ($f.type -eq 'array') {
      $items = [ordered]@{ type = $f.items.type }
      if ($f.items.maxLength) { $items['maxLength'] = $f.items.maxLength }
      $p = [ordered]@{ type = 'array'; description = $f.description; items = $items }
      if ($f.maxItems) { $p['maxItems'] = $f.maxItems }
    } else {
      # string (incl. $choices fields — kept as free strings; dynamic option sets/taxonomy per FR-16)
      $p = [ordered]@{ type = $f.type; description = $f.description }
      if ($f.maxLength) { $p['maxLength'] = $f.maxLength }
    }
    $props[$f.name] = $p
  }
  return [ordered]@{
    '$schema'            = 'http://json-schema.org/draft-07/schema#'
    type                 = 'object'
    additionalProperties = $false
    required             = $required
    properties           = $props
  }
}

$specs = @(
  @{ code = 'triage-email';           file = 'triage-email.action.json' }
  @{ code = 'propose-field-updates';  file = 'propose-field-updates.action.json' }
  @{ code = 'create-task-from-email'; file = 'create-task-from-email.action.json' }
)

Write-Host "--- Seeding email-intelligence sprk_analysisaction rows ($DataverseUrl) ---"
foreach ($s in $specs) {
  $raw = Get-Content -Raw -Path (Join-Path $actionsDir $s.file)
  $j = $raw | ConvertFrom-Json

  $isJps = [bool]($j.PSObject.Properties.Name -contains '$schema')
  if ($isJps) {
    # classic-JPS: sprk_systemprompt = JPS root (strip $comment* + deploy scalars); derive output schema.
    $jpsRoot = $raw | ConvertFrom-Json
    Remove-Keys $jpsRoot @('actionCode','name','description','actionType','modelTier','temperature')
    $systemPrompt   = $jpsRoot | ConvertTo-Json -Depth 40
    $outputSchemaObj = ConvertTo-OutputSchema $j.output.fields
    $outputSchema   = $outputSchemaObj | ConvertTo-Json -Depth 40
  } else {
    # flat-prompt: sprk_systemprompt = j.systemPrompt; sprk_outputschemajson = j.outputSchema (ready-made).
    $systemPrompt = [string]$j.systemPrompt
    $schemaObj = $raw | ConvertFrom-Json | Select-Object -ExpandProperty outputSchema
    Remove-Keys $schemaObj @()
    $outputSchema = $schemaObj | ConvertTo-Json -Depth 40
  }

  $body = [ordered]@{
    sprk_actioncode      = $j.actionCode
    sprk_name            = $j.name
    sprk_description     = $j.description
    sprk_systemprompt    = $systemPrompt
    sprk_temperature     = [decimal]$j.temperature
    sprk_modeltier       = $ModelTierFast
    sprk_allowsknowledge = $false
    sprk_outputschemajson = $outputSchema
  }

  if ($DryRun) {
    Write-Host "`n=== $($j.actionCode) (isJps=$isJps) ==="
    Write-Host "  name=$($j.name) temp=$($j.temperature) systempromptLen=$($systemPrompt.Length) outputSchemaLen=$($outputSchema.Length)"
    continue
  }

  # Upsert by actioncode.
  $enc = [uri]::EscapeDataString($j.actionCode)
  $existing = Invoke-RestMethod -Method Get -Headers $headers -Uri "$api/sprk_analysisactions?`$select=sprk_analysisactionid&`$filter=sprk_actioncode eq '$enc'&`$top=1"
  $bodyJson = $body | ConvertTo-Json -Depth 40
  if ($existing.value.Count -gt 0) {
    $id = $existing.value[0].sprk_analysisactionid
    Invoke-RestMethod -Method Patch -Headers $headers -Uri "$api/sprk_analysisactions($id)" -Body $bodyJson | Out-Null
    Write-Host "UPDATED: $($j.actionCode) -> $id"
  } else {
    $created = Invoke-RestMethod -Method Post -Headers $headers -Uri "$api/sprk_analysisactions" -Body $bodyJson
    Write-Host "CREATED: $($j.actionCode) -> $($created.sprk_analysisactionid)"
  }
}
Write-Host "`nNext: pwsh scripts/dataverse/Seed-PlaybookConsumers.ps1 -SkipConfirm   (creates the email-* routing rows)"
