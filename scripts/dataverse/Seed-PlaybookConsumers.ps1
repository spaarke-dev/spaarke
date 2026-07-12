<#
.SYNOPSIS
    Seed / export / round-trip-verify the sprk_playbookconsumer Binding table —
    the SINGLE routing surface of the Spaarke AI platform (ADR-039).

.DESCRIPTION
    REGENERATED 2026-07-07 by spaarke-ai-architecture-redesign-r1 task 051 (FR-P4-02)
    from the LIVE spaarkedev1 Binding table. The previous version of this script was a
    stale chat-routing-redesign-r1 (2026-06-24) projection: 7 playbook-only rows on the
    abandoned R4 taxonomy, none of the FR-P0-03 extended columns. This version is a
    faithful projection of the post-redesign table and is DATA-DRIVEN:

        MIRROR FILE (single source for row data — never edit rows in this script):
            infra/dataverse/sprk_playbookconsumer-rows.json

    Three modes:

      Seed (default)  Mirror -> environment. Idempotent UPSERT of every mirror row via
                      the alternate key sprk_ConsumerTypeCodeEnvironment
                      (sprk_consumertype + sprk_consumercode + sprk_environment).
                      Lookups are resolved PER ENVIRONMENT at run time:
                        actionCode    -> sprk_analysisaction  by sprk_actioncode
                        playbookName  -> sprk_analysisplaybook by sprk_name
                      so the mirror carries no environment-specific GUIDs in its
                      semantic fields. A null actionCode/playbookName CLEARS the lookup
                      on the target row (DELETE $ref), guaranteeing convergence.

      -Export         Environment -> mirror. Reads the live table (statecode=0) and
                      REWRITES the mirror file as the normalized projection. Run after
                      any deliberate live catalog change, then commit the mirror.

      -DiffOnly       The ROUND-TRIP PROOF. Re-exports the live table in memory and
                      compares it field-by-field against the mirror file. Exit 0 =
                      zero semantic drift (env == seed); exit 1 = drift, printed
                      per row/field. CI- and reviewer-friendly.

    ROUND-TRIP CONTRACT (FR-P4-02 "seed round-trips"): running -Export then -DiffOnly
    is always clean; running Seed against an environment that already matches the
    mirror is a no-op (upsert converges); Seed into a CLEAN environment followed by
    -DiffOnly reproduces the mirror exactly.

    The Binding table is the ONE routing surface — the mirror is a projection OF the
    table, never a second source of truth. Regeneration direction is table -> mirror
    (-Export). Do NOT hand-author routing here that does not exist in a live table
    (spec MUST NOT: no routing config outside the Binding table).

.PARAMETER DataverseUrl
    Dataverse environment URL. Defaults to DATAVERSE_URL env var, then spaarkedev1.

.PARAMETER MirrorFile
    Path to the row mirror JSON. Default: infra/dataverse/sprk_playbookconsumer-rows.json

.PARAMETER Export
    Export live table -> mirror file (overwrites the mirror).

.PARAMETER DiffOnly
    Compare live table vs mirror; no writes anywhere. Exit 1 on drift.

.PARAMETER DryRun
    Seed mode only: print what would be written without calling Dataverse.

.PARAMETER SkipConfirm
    Seed mode only: skip the interactive confirmation prompt.

.EXAMPLE
    # Round-trip proof (read-only)
    .\Seed-PlaybookConsumers.ps1 -DiffOnly

.EXAMPLE
    # Refresh the mirror after deliberate live catalog edits
    .\Seed-PlaybookConsumers.ps1 -Export

.EXAMPLE
    # Seed a new environment from the mirror
    .\Seed-PlaybookConsumers.ps1 -DataverseUrl "https://myorg.crm.dynamics.com" -SkipConfirm

.NOTES
    Author:        spaarke-ai-architecture-redesign-r1 task 051 (FR-P4-02)
    Entity:        sprk_playbookconsumer   (entity set: sprk_playbookconsumers)
    Alternate key: sprk_ConsumerTypeCodeEnvironment
    Auth:          Azure CLI (az account get-access-token). Run 'az login' first.
    ADRs:          ADR-039 (closed catalogs / one routing surface), ADR-027 (sprk_ prefix)
    Convention:    sprk_environment is ALWAYS a literal value ('*' = all environments) —
                   never null. Null cannot be addressed by the alternate key (upsert
                   would duplicate the row). Live spaarkedev1 was normalized 2026-07-07
                   (two legacy rows null -> '*'; router semantics identical per Binding.cs).
#>

[CmdletBinding()]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [string]$MirrorFile,
    [switch]$Export,
    [switch]$DiffOnly,
    [switch]$DryRun,
    [switch]$SkipConfirm
)

$ErrorActionPreference = 'Stop'

if (-not $DataverseUrl) { $DataverseUrl = 'https://spaarkedev1.crm.dynamics.com' }
$DataverseUrl = $DataverseUrl.TrimEnd('/')
$ApiBase   = "$DataverseUrl/api/data/v9.2"
$EntitySet = 'sprk_playbookconsumers'

$RepoRoot = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '../..')).Path
if (-not $MirrorFile) { $MirrorFile = Join-Path $RepoRoot 'infra/dataverse/sprk_playbookconsumer-rows.json' }

# ---------------------------------------------------------------------------
# Auth + HTTP helpers
# ---------------------------------------------------------------------------
function Get-Headers {
    $token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv 2>$null
    if (-not $token) { throw "Failed to obtain access token. Run 'az login' first." }
    return @{
        'Authorization'    = "Bearer $token"
        'Accept'           = 'application/json'
        'Content-Type'     = 'application/json; charset=utf-8'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
    }
}

function Invoke-DvGet {
    param([string]$Path, [hashtable]$Headers)
    return Invoke-RestMethod -Uri "$ApiBase/$Path" -Headers $Headers -Method Get
}

# ---------------------------------------------------------------------------
# Normalized row shape — the ONE projection used by Export, Diff, and Seed.
# Field order is fixed so mirror diffs stay stable.
# ---------------------------------------------------------------------------
$SelectColumns = @(
    'sprk_name', 'sprk_consumertype', 'sprk_consumercode', 'sprk_environment',
    'sprk_priority', 'sprk_enabled', 'sprk_ucid', 'sprk_disposition', 'sprk_risk',
    'sprk_capturemode', 'sprk_surfaces', 'sprk_chiptransitions', 'sprk_oneventbindings',
    'sprk_matchconditions', 'sprk_tooldescription'
) -join ','

function Get-LiveRows {
    param([hashtable]$Headers)
    # Lookup columns come back as _<attr>_value; resolve to portable code/name via one
    # catalog read each (navigation-property names differ from attribute logical names,
    # so $expand is deliberately avoided).
    $actionCodeById = @{}
    (Invoke-DvGet -Path "sprk_analysisactions?`$select=sprk_analysisactionid,sprk_actioncode" -Headers $Headers).value |
        ForEach-Object { $actionCodeById[$_.sprk_analysisactionid] = $_.sprk_actioncode }
    $playbookNameById = @{}
    (Invoke-DvGet -Path "sprk_analysisplaybooks?`$select=sprk_analysisplaybookid,sprk_name" -Headers $Headers).value |
        ForEach-Object { $playbookNameById[$_.sprk_analysisplaybookid] = $_.sprk_name }

    $q = "$EntitySet`?`$select=$SelectColumns,_sprk_action_value,_sprk_playbook_value&`$filter=statecode eq 0&`$orderby=sprk_consumertype,sprk_consumercode"
    $resp = Invoke-DvGet -Path $q -Headers $Headers
    return @($resp.value | ForEach-Object {
        [ordered]@{
            consumerType    = $_.sprk_consumertype
            consumerCode    = $_.sprk_consumercode
            environment     = $_.sprk_environment
            name            = $_.sprk_name
            priority        = $_.sprk_priority
            enabled         = $_.sprk_enabled
            playbookName    = if ($_._sprk_playbook_value) { $playbookNameById[$_._sprk_playbook_value] } else { $null }
            actionCode      = if ($_._sprk_action_value)   { $actionCodeById[$_._sprk_action_value] }     else { $null }
            ucid            = $_.sprk_ucid
            disposition     = $_.sprk_disposition
            risk            = $_.sprk_risk
            captureMode     = $_.sprk_capturemode
            surfaces        = $_.sprk_surfaces
            chipTransitions = $_.sprk_chiptransitions
            onEventBindings = $_.sprk_oneventbindings
            matchConditions = $_.sprk_matchconditions
            toolDescription = $_.sprk_tooldescription
        }
    })
}

function Read-MirrorRows {
    if (-not (Test-Path $MirrorFile)) { throw "Mirror file not found: $MirrorFile" }
    $doc = Get-Content $MirrorFile -Raw -Encoding utf8 | ConvertFrom-Json
    return @($doc.rows)
}

$RowFieldOrder = @(
    'consumerType','consumerCode','environment','name','priority','enabled',
    'playbookName','actionCode','ucid','disposition','risk','captureMode',
    'surfaces','chipTransitions','onEventBindings','matchConditions','toolDescription'
)

function Get-RowKey { param($Row) return "$($Row.consumerType)|$($Row.consumerCode)|$($Row.environment)" }

# ---------------------------------------------------------------------------
# Mode: Export (environment -> mirror)
# ---------------------------------------------------------------------------
if ($Export) {
    Write-Host "`n=== Seed-PlaybookConsumers: EXPORT (env -> mirror) ===" -ForegroundColor Cyan
    Write-Host "Source: $DataverseUrl`nMirror: $MirrorFile`n"
    $headers = Get-Headers
    $rows = Get-LiveRows -Headers $headers
    $doc = [ordered]@{
        '$comment'  = 'Projection of the LIVE sprk_playbookconsumer Binding table (the single AI routing surface, ADR-039). Regenerate with Seed-PlaybookConsumers.ps1 -Export; verify with -DiffOnly; seed a new environment with the default mode. Lookups are portable (actionCode / playbookName), resolved per environment at seed time. Direction of truth: table -> mirror. Do NOT hand-author routing here.'
        '$exported' = (Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK')
        '$source'   = $DataverseUrl
        rows        = $rows
    }
    $json = $doc | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($MirrorFile, $json, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Exported $($rows.Count) rows -> $MirrorFile" -ForegroundColor Green
    exit 0
}

# ---------------------------------------------------------------------------
# Mode: DiffOnly (round-trip proof: env vs mirror, read-only)
# ---------------------------------------------------------------------------
if ($DiffOnly) {
    Write-Host "`n=== Seed-PlaybookConsumers: DIFF (round-trip proof) ===" -ForegroundColor Cyan
    Write-Host "Env:    $DataverseUrl`nMirror: $MirrorFile`n"
    $headers = Get-Headers
    $live   = Get-LiveRows -Headers $headers
    $mirror = Read-MirrorRows

    $liveByKey   = @{}; foreach ($r in $live)   { $liveByKey[(Get-RowKey $r)]   = $r }
    $mirrorByKey = @{}; foreach ($r in $mirror) { $mirrorByKey[(Get-RowKey $r)] = $r }

    $drift = 0
    foreach ($k in ($mirrorByKey.Keys | Sort-Object)) {
        if (-not $liveByKey.ContainsKey($k)) { Write-Host "  MISSING IN ENV   $k" -ForegroundColor Red; $drift++ }
    }
    foreach ($k in ($liveByKey.Keys | Sort-Object)) {
        if (-not $mirrorByKey.ContainsKey($k)) { Write-Host "  MISSING IN MIRROR $k" -ForegroundColor Red; $drift++ }
    }
    foreach ($k in ($mirrorByKey.Keys | Sort-Object)) {
        if (-not $liveByKey.ContainsKey($k)) { continue }
        $m = $mirrorByKey[$k]; $l = $liveByKey[$k]
        foreach ($f in $RowFieldOrder) {
            $mv = $m.$f; $lv = $l[$f]
            $mvs = if ($null -eq $mv) { '<null>' } else { [string]$mv }
            $lvs = if ($null -eq $lv) { '<null>' } else { [string]$lv }
            if ($mvs -cne $lvs) {
                Write-Host "  DRIFT  $k :: $f" -ForegroundColor Yellow
                Write-Host "         mirror: $($mvs.Substring(0, [Math]::Min(120, $mvs.Length)))"
                Write-Host "         env:    $($lvs.Substring(0, [Math]::Min(120, $lvs.Length)))"
                $drift++
            }
        }
    }

    Write-Host ""
    if ($drift -eq 0) {
        Write-Host "ROUND-TRIP CLEAN: $($mirror.Count) mirror rows == $($live.Count) live rows, zero semantic drift." -ForegroundColor Green
        exit 0
    }
    Write-Host "DRIFT DETECTED: $drift difference(s). Reconcile deliberately, then -Export + commit." -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------
# Mode: Seed (mirror -> environment)
# ---------------------------------------------------------------------------
Write-Host "`n=== Seed-PlaybookConsumers: SEED (mirror -> env) ===" -ForegroundColor Cyan
Write-Host "Target: $DataverseUrl`nMirror: $MirrorFile"
$rows = Read-MirrorRows
Write-Host "Rows:   $($rows.Count)"
if ($DryRun) { Write-Host "Mode:   DRY-RUN" -ForegroundColor Yellow } else { Write-Host "Mode:   LIVE" -ForegroundColor Green }
Write-Host ""

$rows | ForEach-Object {
    [PSCustomObject]@{
        ConsumerType = $_.consumerType; ConsumerCode = $_.consumerCode; Env = $_.environment
        Priority = $_.priority; Enabled = $_.enabled; Playbook = $_.playbookName; Action = $_.actionCode
        Ucid = $_.ucid; Disposition = $_.disposition
    }
} | Format-Table -AutoSize

if (-not ($DryRun -or $SkipConfirm)) {
    $response = Read-Host 'Proceed with seeding? [y/N]'
    if ($response -notmatch '^[yY]$') { Write-Host 'Aborted by operator.' -ForegroundColor Yellow; exit 0 }
}
if ($DryRun) { Write-Host 'DRY-RUN: no Dataverse calls made.' -ForegroundColor Yellow; exit 0 }

$headers = Get-Headers

# Resolve per-environment lookup GUID maps ONCE.
$actionMap = @{}
(Invoke-DvGet -Path "sprk_analysisactions?`$select=sprk_analysisactionid,sprk_actioncode&`$filter=statecode eq 0" -Headers $headers).value |
    Where-Object { $_.sprk_actioncode } | ForEach-Object { $actionMap[$_.sprk_actioncode] = $_.sprk_analysisactionid }
$playbookMap = @{}
(Invoke-DvGet -Path "sprk_analysisplaybooks?`$select=sprk_analysisplaybookid,sprk_name&`$filter=statecode eq 0" -Headers $headers).value |
    Where-Object { $_.sprk_name } | ForEach-Object { $playbookMap[$_.sprk_name] = $_.sprk_analysisplaybookid }

$summary = @{ Upserted = 0; Failed = 0 }

foreach ($r in $rows) {
    $ctEnc  = [Uri]::EscapeDataString([string]$r.consumerType)
    $ccEnc  = [Uri]::EscapeDataString([string]$r.consumerCode)
    $envEnc = [Uri]::EscapeDataString([string]$r.environment)
    $altKey = "sprk_consumertype='$ctEnc',sprk_consumercode='$ccEnc',sprk_environment='$envEnc'"
    $url    = "$ApiBase/$EntitySet($altKey)"

    $body = [ordered]@{
        sprk_name             = $r.name
        sprk_consumertype     = $r.consumerType
        sprk_consumercode     = $r.consumerCode
        sprk_environment      = $r.environment
        sprk_priority         = $r.priority
        sprk_enabled          = $r.enabled
        sprk_ucid             = $r.ucid
        sprk_disposition      = $r.disposition
        sprk_risk             = $r.risk
        sprk_capturemode      = $r.captureMode
        sprk_surfaces         = $r.surfaces
        sprk_chiptransitions  = $r.chipTransitions
        sprk_oneventbindings  = $r.onEventBindings
        sprk_matchconditions  = $r.matchConditions
        sprk_tooldescription  = $r.toolDescription
    }
    if ($r.actionCode) {
        if (-not $actionMap.ContainsKey($r.actionCode)) {
            Write-Host "  FAILED   $($r.consumerType)/$($r.consumerCode) -> actionCode '$($r.actionCode)' not found in target env (seed the Action row first)" -ForegroundColor Red
            $summary.Failed++; continue
        }
        $body['sprk_Action@odata.bind'] = "/sprk_analysisactions($($actionMap[$r.actionCode]))"
    }
    if ($r.playbookName) {
        if (-not $playbookMap.ContainsKey($r.playbookName)) {
            Write-Host "  FAILED   $($r.consumerType)/$($r.consumerCode) -> playbookName '$($r.playbookName)' not found in target env (deploy the playbook first)" -ForegroundColor Red
            $summary.Failed++; continue
        }
        $body['sprk_Playbook@odata.bind'] = "/sprk_analysisplaybooks($($playbookMap[$r.playbookName]))"
    }

    try {
        Invoke-WebRequest -Uri $url -Headers $headers -Method Patch -Body ($body | ConvertTo-Json -Depth 5) -UseBasicParsing | Out-Null
        # Null lookups in the mirror must CLEAR any existing value (convergence).
        foreach ($nav in @(@{ f = $r.actionCode; n = 'sprk_Action' }, @{ f = $r.playbookName; n = 'sprk_Playbook' })) {
            if (-not $nav.f) {
                try { Invoke-WebRequest -Uri "$url/$($nav.n)/`$ref" -Headers $headers -Method Delete -UseBasicParsing | Out-Null } catch { }
            }
        }
        $summary.Upserted++
        Write-Host "  UPSERTED $($r.consumerType.PadRight(24)) $($r.consumerCode)" -ForegroundColor Cyan
    }
    catch {
        $summary.Failed++
        Write-Host "  FAILED   $($r.consumerType)/$($r.consumerCode) -> $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "`nSummary: Upserted=$($summary.Upserted) Failed=$($summary.Failed)" -ForegroundColor $(if ($summary.Failed) { 'Red' } else { 'Green' })
Write-Host "Verify the round-trip: .\Seed-PlaybookConsumers.ps1 -DiffOnly`n"
if ($summary.Failed -gt 0) { exit 1 }
exit 0
