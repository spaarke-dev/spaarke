<#
.SYNOPSIS
    Deploys the authoritative per-file Action mirrors (infra/dataverse/actions/*.action.json)
    to the `sprk_analysisaction` table, upserting by `sprk_actioncode`.

.DESCRIPTION
    This is the deployer that `scripts/seed-data/manifest.yaml` records as MISSING:

        - id: actions-r7
          authoritativeSource: infra/dataverse/actions/
          authoritativeSourcePattern: "infra/dataverse/actions/*.action.json"
          deployer: null          <-- this script fills that hole

    WHY THE OLDER SCRIPT CANNOT DO THIS (spaarkeai-compose-r8, 2026-08-26)
    `scripts/Deploy-AnalysisAction.ps1` targets a schema shape that no longer exists. It:
      (a) reads a `{ actions: [...] }` wrapper, while the authoritative mirrors are one bare
          object per file;
      (b) hard-requires `actionTypeName` and SKIPS any action without it — all 17 mirrors omit
          it, correctly; and
      (c) writes `sprk_ActionTypeId@odata.bind`, a lookup that DOES NOT EXIST on the entity.

    That is not a bug in the mirrors — the ActionType lookup was RETIRED ON PURPOSE by R7 task
    028 / FR-07: "read path simplified. ActionTypeId expand removed — Action is no longer the
    dispatch axis (orchestrator reads node.sprk_executortype directly)"
    (`Services/Ai/AnalysisActionService.cs:235`, `:343`). Verified 2026-08-26 against the live
    dev environment: `sprk_analysisaction` exposes 65 attributes and NONE is an action-type
    lookup; the only non-system lookups are `sprk_analysisid` and `sprk_modeldeploymentid`. The
    sole surviving mention of `sprk_ActionTypeId` anywhere in the BFF is a stale COMMENT at
    `Services/Ai/Insights/Routing/InsightsActionRouter.cs:290`.

    So this script deliberately does NOT bind an action type. Re-introducing that column would
    restore a dispatch axis the architecture removed — a regression against FR-07, not a fix.

    CONSEQUENCE OF THE GAP (why this matters, not just why it is tidy)
    Because no deployer existed, `infra/dataverse/actions/` has never reached Dataverse. The BFF
    reads `sprk_outputschemajson` + `sprk_systemprompt` AT RUNTIME to build the model request, so
    an un-deployed mirror means the model is asked for the OLD contract no matter what ships in
    the BFF. compose-r8 task 052 added `target_para_id` to four compose actions; without this
    script the model was still being asked for `target_text`, and every AI edit arrived with no
    anchor. That is what reached UAT on 2026-08-26.

.PARAMETER Filter
    Wildcard over the action-code, e.g. 'compose-*'. Default '*' (all mirrors).

.PARAMETER DryRun
    Print a per-field before/after comparison and write NOTHING.

.EXAMPLE
    .\Deploy-ActionMirrors.ps1 -Filter 'compose-*' -DryRun
.EXAMPLE
    .\Deploy-ActionMirrors.ps1 -Filter 'compose-*'
#>
param(
    [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com',
    [string]$Filter = '*',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$AZ = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Split-Path -Parent $scriptDir
$actionDir = Join-Path $repoRoot 'infra\dataverse\actions'

Write-Host '========================================='
Write-Host 'Action Mirror Deployment (sprk_analysisaction)'
Write-Host "Target : $DataverseUrl"
Write-Host "Filter : $Filter"
Write-Host ("Mode   : {0}" -f $(if ($DryRun) { 'DRY RUN — nothing will be written' } else { 'LIVE' }))
Write-Host '========================================='

if (-not (Test-Path $actionDir)) { throw "Action mirror directory not found: $actionDir" }

$token = & $AZ account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if (-not $token) { throw 'Failed to acquire a Dataverse access token (az account get-access-token).' }
$api = "$DataverseUrl/api/data/v9.2"
$headers = @{
    Authorization    = "Bearer $token"
    Accept           = 'application/json'
    'Content-Type'   = 'application/json; charset=utf-8'
    'OData-Version'  = '4.0'
    'OData-MaxVersion' = '4.0'
}

# The columns this deployer owns. Anything not listed is left untouched on the row — a mirror is
# the source of truth for the model CONTRACT, not for operational fields an admin may have tuned.
$fieldMap = @{
    'sprk_name'             = 'name'
    'sprk_description'      = 'description'
    'sprk_systemprompt'     = 'systemPrompt'
    'sprk_outputschemajson' = 'outputSchema'   # object -> compact JSON
    'sprk_inputschema'      = 'inputSchema'    # object -> compact JSON
    'sprk_tags'             = 'tags'
}
$jsonFields = @('sprk_outputschemajson', 'sprk_inputschema')

$files = Get-ChildItem $actionDir -Filter '*.action.json' | Sort-Object Name
$changed = 0; $skipped = 0; $missing = 0; $examined = 0

foreach ($file in $files) {
    $def = Get-Content $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    $code = $def.actionCode
    if (-not $code) { Write-Host "  SKIP $($file.Name) — no actionCode" -ForegroundColor Yellow; $skipped++; continue }
    if ($code -notlike $Filter) { continue }
    $examined++

    $escaped = $code.Replace("'", "''")
    $existing = Invoke-RestMethod -Uri "$api/sprk_analysisactions?`$filter=sprk_actioncode eq '$escaped'" -Headers $headers -Method Get
    if ($existing.value.Count -eq 0) {
        Write-Host ""
        Write-Host "  MISSING ROW  $code — no sprk_analysisaction with this code. NOT created." -ForegroundColor Red
        Write-Host "               Creating a row needs more than the contract fields this script owns;" -ForegroundColor DarkGray
        Write-Host "               surface it rather than inventing defaults." -ForegroundColor DarkGray
        $missing++; continue
    }
    $row = $existing.value[0]
    $rowId = $row.sprk_analysisactionid

    $body = @{}
    $diffs = @()
    foreach ($col in $fieldMap.Keys) {
        $srcKey = $fieldMap[$col]
        if (-not ($def.PSObject.Properties.Name -contains $srcKey)) { continue }
        $srcVal = $def.$srcKey
        if ($null -eq $srcVal) { continue }
        $newVal = if ($jsonFields -contains $col) { $srcVal | ConvertTo-Json -Depth 30 -Compress } else { [string]$srcVal }
        $oldVal = [string]$row.$col
        if ($oldVal -ne $newVal) {
            $body[$col] = $newVal
            $diffs += [PSCustomObject]@{ Column = $col; OldLen = $oldVal.Length; NewLen = $newVal.Length }
        }
    }

    if ($body.Count -eq 0) {
        Write-Host "  UNCHANGED    $code" -ForegroundColor DarkGray
        continue
    }

    Write-Host ""
    Write-Host "  $code" -ForegroundColor Cyan
    foreach ($d in $diffs) {
        Write-Host ("      {0,-24} {1,6} -> {2,6} chars" -f $d.Column, $d.OldLen, $d.NewLen)
    }
    # The single fact this whole exercise turns on — call it out per action.
    if ($body.ContainsKey('sprk_outputschemajson')) {
        $hadAnchor = ([string]$row.sprk_outputschemajson) -match 'target_para_id'
        $hasAnchor = $body['sprk_outputschemajson'] -match 'target_para_id'
        if ($hadAnchor -ne $hasAnchor) {
            Write-Host ("      target_para_id in outputSchema: {0} -> {1}" -f $hadAnchor, $hasAnchor) -ForegroundColor Yellow
        }
    }

    if ($DryRun) {
        Write-Host "      [DryRun] would PATCH sprk_analysisactions($rowId)" -ForegroundColor Yellow
    } else {
        $json = $body | ConvertTo-Json -Depth 30
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
        Invoke-RestMethod -Uri "$api/sprk_analysisactions($rowId)" -Headers $headers -Method Patch -Body $bytes | Out-Null
        Write-Host "      PATCHED" -ForegroundColor Green
    }
    $changed++
}

Write-Host ''
Write-Host '========================================='
Write-Host ("Examined : {0}" -f $examined)
Write-Host ("Changed  : {0}{1}" -f $changed, $(if ($DryRun) { ' (dry run — nothing written)' } else { '' }))
Write-Host ("Missing  : {0}" -f $missing)
Write-Host ("Skipped  : {0}" -f $skipped)
Write-Host '========================================='
if ($missing -gt 0) { Write-Host 'One or more mirrors have no matching row — investigate before assuming a clean deploy.' -ForegroundColor Red }
