<#
.SYNOPSIS
  Seed the DEF-11 compose-revise-document Action + Binding to a Dataverse env (direct Web API).

.DESCRIPTION
  The shared seeders (Deploy-AnalysisAction.ps1 / Seed-PlaybookConsumers.ps1) are drifted from the
  live schema — see projects/spaarkeai-compose-r2/notes/catalog-deploy-tooling-debt-and-resume.md.
  This self-contained script upserts the Action (sprk_analysisaction, upsert by sprk_actioncode) and
  inserts the Binding (sprk_playbookconsumer, linked via sprk_Action@odata.bind — CAPITAL nav prop),
  reading values from the mirror-first sources under infra/dataverse/. Idempotent: skips insert when a
  row already exists. Matches the live sibling compose-draft-alternative field posture.

  disposition 100000006 = BindingDisposition.Compose (must already exist on the env optionset
  new_sprk_playbookconsumer_sprk_disposition — added to spaarkedev1 2026-07-10). NO version suffix (owner hygiene).

.EXAMPLE
  pwsh -File projects/spaarkeai-compose-r2/scripts/seed-compose-revise-document.ps1
#>
param(
  [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com'
)
$ErrorActionPreference = 'Stop'
$AZ  = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'
$api = "$DataverseUrl/api/data/v9.2"

# repo root = three levels up from this script (projects/spaarkeai-compose-r2/scripts/ -> repo root)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = (Resolve-Path (Join-Path $scriptDir '..\..\..')).Path

$token = & $AZ account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if ([string]::IsNullOrEmpty($token)) { Write-Error 'no token (run: az login)'; exit 1 }
$h = @{ Authorization = "Bearer $token"; 'OData-MaxVersion'='4.0'; 'OData-Version'='4.0'; Accept='application/json'; 'Content-Type'='application/json; charset=utf-8' }

function Post-Utf8($url, $bodyObj, $prefer) {
  $hh = $h.Clone(); if ($prefer) { $hh['Prefer'] = $prefer }
  $json = $bodyObj | ConvertTo-Json -Depth 60
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
  return Invoke-WebRequest -Uri $url -Headers $hh -Method Post -Body $bytes -UseBasicParsing
}

# ---------- ACTION ----------
$actionMirror = Get-Content "$repo/infra/dataverse/actions/compose-revise-document.action.json" -Raw | ConvertFrom-Json
$inputSchemaRaw = Get-Content "$repo/infra/dataverse/inputschemas/compose-revise-document.input.schema.json" -Raw
$outputSchemaJson = $actionMirror.outputSchema | ConvertTo-Json -Depth 60 -Compress

Write-Host '=== [1/3] ACTION upsert (sprk_analysisaction) ==='
$exist = Invoke-RestMethod -Uri "$api/sprk_analysisactions?`$filter=sprk_actioncode eq 'compose-revise-document'&`$select=sprk_analysisactionid" -Headers $h -Method Get
if ($exist.value.Count -gt 0) {
  $actionId = $exist.value[0].sprk_analysisactionid
  Write-Host "  already present: $actionId (skip insert)" -ForegroundColor Yellow
} else {
  $actionBody = [ordered]@{
    sprk_actioncode       = 'compose-revise-document'
    sprk_name             = $actionMirror.name
    sprk_description      = $actionMirror.description
    sprk_systemprompt     = $actionMirror.systemPrompt
    sprk_outputschemajson = $outputSchemaJson
    sprk_inputschema      = $inputSchemaRaw
    sprk_temperature      = $actionMirror.temperature
    sprk_allowsknowledge  = $true
    sprk_allowstools      = $true
    sprk_allowsskills     = $true
    sprk_availableadhoc   = $false
    sprk_allowsdelivery   = $false
  }
  $resp = Post-Utf8 "$api/sprk_analysisactions" $actionBody 'return=representation'
  $actionId = ($resp.Content | ConvertFrom-Json).sprk_analysisactionid
  Write-Host "  INSERTED: $actionId" -ForegroundColor Green
}

# ---------- BINDING ----------
Write-Host '=== [2/3] BINDING insert (sprk_playbookconsumer) ==='
$rows = (Get-Content "$repo/infra/dataverse/sprk_playbookconsumer-rows.json" -Raw | ConvertFrom-Json).rows
$brow = $rows | Where-Object { $_.actionCode -eq 'compose-revise-document' } | Select-Object -First 1
if (-not $brow) { Write-Error 'binding mirror row not found'; exit 1 }

$bExist = Invoke-RestMethod -Uri "$api/sprk_playbookconsumers?`$filter=sprk_consumertype eq 'compose-revise-document'&`$select=sprk_playbookconsumerid" -Headers $h -Method Get
if ($bExist.value.Count -gt 0) {
  Write-Host "  already present: $($bExist.value[0].sprk_playbookconsumerid) (skip insert)" -ForegroundColor Yellow
} else {
  $bindBody = [ordered]@{
    sprk_consumertype        = $brow.consumerType
    sprk_consumercode        = $brow.consumerCode
    sprk_environment         = $brow.environment
    sprk_name                = $brow.name
    sprk_priority            = $brow.priority
    sprk_enabled             = [bool]$brow.enabled
    sprk_disposition         = $brow.disposition
    sprk_risk                = $brow.risk
    sprk_capturemode         = $brow.captureMode
    sprk_surfaces            = $brow.surfaces
    sprk_tooldescription     = $brow.toolDescription
    'sprk_Action@odata.bind' = "/sprk_analysisactions($actionId)"
  }
  $resp = Post-Utf8 "$api/sprk_playbookconsumers" $bindBody 'return=representation'
  Write-Host "  INSERTED: $(($resp.Content | ConvertFrom-Json).sprk_playbookconsumerid)" -ForegroundColor Green
}

# ---------- VERIFY ----------
Write-Host '=== [3/3] VERIFY (binding -> action link) ==='
$v = Invoke-RestMethod -Uri "$api/sprk_playbookconsumers?`$filter=sprk_consumertype eq 'compose-revise-document'&`$select=sprk_name,sprk_disposition,sprk_surfaces,sprk_enabled,_sprk_action_value" -Headers $h -Method Get
$vr = $v.value[0]
Write-Host ("  name={0}  disposition={1}  surfaces={2}  enabled={3}" -f $vr.sprk_name, $vr.sprk_disposition, $vr.sprk_surfaces, $vr.sprk_enabled)
if ($vr._sprk_action_value -eq $actionId) { Write-Host "  LINK OK -> $actionId" -ForegroundColor Green } else { Write-Host "  LINK MISMATCH ($($vr._sprk_action_value) != $actionId)" -ForegroundColor Red }
Write-Host 'DONE — BFF catalog cache (5-min IMemoryCache TTL) serves the new capability on next read; no restart needed.'
