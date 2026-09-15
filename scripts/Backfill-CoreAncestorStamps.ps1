#!/usr/bin/env pwsh
<#
.SYNOPSIS
    One-time (re-runnable) backfill of FR-26 core-ancestor stamps onto EXISTING child-class Dataverse
    records whose regarding target is itself a child-class record — so FR-27 inheritance ("a contact with
    Project access sees its To Dos/communications/...") works for records created before FR-26 shipped.

.DESCRIPTION
    Spaarke's unified-access-control model gives CORE records (sprk_project, sprk_matter,
    sprk_workassignment, sprk_servicerequest) direct grants, and lets CHILD records (sprk_invoice,
    sprk_communication, sprk_document, sprk_event, sprk_todo, sprk_analysis) inherit access via a
    DENORMALIZED "core-ancestor stamp" written onto the child row itself — one of the four columns
    sprk_regardingproject / sprk_regardingmatter / sprk_regardingworkassignment /
    sprk_regardingservicerequest. This keeps every access chain exactly ONE hop (ADR-034), because the
    evaluator's child-inheritance term is a synchronous set-membership test that can only read a lookup
    the child row already carries — it cannot walk a chain at read time.

    Tasks 050 (TS write path) and 052 (C# write path) made every NEW write stamp correctly. This script
    is the companion one-time backfill for EXISTING rows: for a child record whose DIRECT regarding
    target is ITSELF a child-class record (a "child-of-child" chain, e.g. todo -> communication -> matter)
    and whose derivable ancestor-stamp column is still null, it reads the target's OWN core-ancestor
    columns (exactly one hop — never recursive, mirroring CoreAncestorResolver.cs /
    PolymorphicResolverService.ts exactly) and writes the same stamp onto the child.

    WHY THIS SCRIPT DISCOVERS SCHEMA LIVE, NEVER HARD-CODES IT:
    This project's own design notes (phase3-derivation-rules.md F-050-1, phase3-server-writers.md F-052-1)
    asserted sprk_todo has NO sprk_regardingservicerequest column. A live metadata check performed while
    authoring this script (2026-09-04) found that column DOES exist on sprk_todo today. The runtime
    resolvers already tolerate this because they discover columns from live MetadataService output, never
    from a hand-maintained list — this script does the same (EntityDefinitions / ManyToOneRelationships),
    so it can never encode a stale assumption the way a paragraph of prose can.

    A SECOND, MORE MATERIAL LIVE-METADATA FINDING (new, not previously documented as of this writing):
    sprk_invoice and sprk_document carry NONE of the four sprk_regarding{core} ancestor-stamp columns at
    all — they use differently-named direct association fields instead (sprk_matter / sprk_project /
    sprk_workassignment on both, with no equivalent for service request). Under the CURRENT resolver code
    (both the TS and C# sides check for the four sprk_regarding*-prefixed names exactly), this means:
      - sprk_invoice and sprk_document can NEVER receive a stamp via this mechanism as a HOST, regardless
        of what they regard (this script will correctly report 0 candidates for both, every run).
      - Any child that regards an sprk_invoice or sprk_document AS ITS TARGET derives NO ancestor through
        it (the resolver's own "NoAncestor" status — not a bug, a schema gap). This script mirrors that
        exactly rather than inventing a workaround, because reading sprk_matter/sprk_project on those two
        entities as if they were ancestor stamps would make this backfill's answer diverge from what the
        live evaluator actually derives — "a backfill that computes ancestry differently from the live
        resolver is worse than none: it produces confidently wrong access data." See the runbook
        (projects/unified-access-control-r2/notes/phase3-backfill-runbook.md) for the full finding and the
        two remediation options (owner decision, out of this script's scope).

    SAFETY MODEL
      - Dry-run is the DEFAULT. No arguments beyond -EnvironmentUrl issues zero writes. -WhatIf forces a
        preview even when combined with -Apply.
      - Idempotent: re-running after a successful -Apply writes exactly 0 additional stamps for rows
        already correctly stamped (verified by comparing the derived value against the row's CURRENT
        value, not just by whether the column is populated).
      - A non-null existing stamp that DISAGREES with the derived value is a CONFLICT — reported, never
        overwritten.
      - Resumable BY CONSTRUCTION, not by a persisted checkpoint file: the candidate query itself excludes
        rows whose relevant ancestor column is already populated, so an interrupted -Apply run can simply
        be re-run — no tenant is ever left in an "unknown" state, because every write is a single
        independent PATCH (no multi-row transaction to leave half-applied).
      - Deep chains (a still-unstamped target) are reported as UNRESOLVABLE, never chased recursively
        (ADR-034's 1-hop cap). The documented remedy is re-running the script until it converges
        (0 rows written) — each pass can resolve one more hop as earlier targets get stamped.
      - An ESCALATION gate mirrors the task's own trigger: if total candidates exceed 50,000, or any
        entity's unresolvable share exceeds 20%, -Apply refuses to write (prints a CLAUDE.md §6-style
        banner) unless the operator passes -AcknowledgeEscalation after reviewing the dry-run summary.

    A NOTE ON "CANDIDATES" vs "WRITES" IN THE SUMMARY
    The candidate pre-filter is deliberately broad (rows where the direct target is child-class AND *any*
    of the applicable ancestor columns is still null) because which single column will actually be
    written is only known after reading the target. Under the current one-ancestor-per-chain model, a
    correctly-stamped row keeps 3 of its 4 ancestor columns permanently null (only one core type is ever
    the real ancestor) — so "CandidatesScanned" will NOT trend to zero after a successful backfill. The
    convergence signal to watch is "ToWrite: 0", not "CandidatesScanned: 0".

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Write mode. Without this switch the script ALWAYS dry-runs, regardless of any other parameter.

.PARAMETER WhatIf
    Forces a dry-run even when -Apply is also passed. Use to preview an -Apply run's plan one more time.

.PARAMETER Entities
    Restrict which child entities to scan/write (default: all six). Reads of OTHER child entities as
    chain TARGETS still happen regardless of this filter — this only scopes which entities receive writes,
    for a staged rollout (e.g. one entity at a time during a UAT window).

.PARAMETER PageSize
    Requested Dataverse page size (Prefer: odata.maxpagesize) for candidate queries. Default 2000. Paging
    beyond one page is always followed via @odata.nextLink regardless of this value.

.PARAMETER BatchSize
    Progress-reporting / chunking granularity for writes during -Apply. Default 100 (per task constraint).
    Each write is still an individually-isolated PATCH — one row's failure never aborts the batch.

.PARAMETER MaxWritesPerRun
    Optional cap on the number of ACTUAL WRITES issued in one -Apply invocation (0 = unlimited). Does NOT
    limit discovery/reporting — the summary and escalation check always reflect the true totals. Lets an
    operator bound the blast radius of a single run without hiding the real candidate count.

.PARAMETER AcknowledgeEscalation
    Required in addition to -Apply when the escalation gate fires (>50,000 total candidates, or any entity
    >20% unresolvable). Absent that, -Apply reports the plan and skips all writes (exit code 2).

.PARAMETER LogPath
    Override the per-run log file path. Defaults to scripts/logs/backfill-core-ancestor-stamps-<timestamp>.log.
    Every planned/actual change gets one line: entity, id, stamp column, value, action.

.EXAMPLE
    .\Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"
    Dry run across all six entities. Zero writes. Full summary + log.

.EXAMPLE
    .\Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Apply
    Writes every resolvable stamp (unless the escalation gate fires).

.EXAMPLE
    .\Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Apply -Entities sprk_todo
    Stage one entity at a time.

.EXAMPLE
    .\Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Apply -AcknowledgeEscalation
    Proceed with writes after reviewing a dry-run that tripped the escalation gate.

.EXAMPLE
    .\Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Apply
    Re-running this exact command a second time (after the first succeeded) reports "ToWrite: 0" for every
    already-stamped row — that is the idempotency contract, not a bug.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [switch]$Apply,

    [switch]$WhatIf,

    [ValidateSet('sprk_todo', 'sprk_communication', 'sprk_event', 'sprk_invoice', 'sprk_document', 'sprk_analysis')]
    [string[]]$Entities = @('sprk_todo', 'sprk_communication', 'sprk_event', 'sprk_invoice', 'sprk_document', 'sprk_analysis'),

    [int]$PageSize = 2000,

    [int]$BatchSize = 100,

    [int]$MaxWritesPerRun = 0,

    [switch]$AcknowledgeEscalation,

    [string]$LogPath
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$IsDryRun = (-not $Apply.IsPresent) -or $WhatIf.IsPresent

# ── Taxonomy (MUST mirror CoreAncestorResolver.cs / PolymorphicResolverService.ts — pinned by tests on
#    both sides; if either drifts from this, this script's answer stops matching the live evaluator) ─────
$CoreEntities = @('sprk_project', 'sprk_matter', 'sprk_workassignment', 'sprk_servicerequest')
$ChildEntities = @('sprk_invoice', 'sprk_communication', 'sprk_document', 'sprk_event', 'sprk_todo', 'sprk_analysis')
$AncestorLookupToEntity = [ordered]@{
    'sprk_regardingproject'        = 'sprk_project'
    'sprk_regardingmatter'         = 'sprk_matter'
    'sprk_regardingworkassignment' = 'sprk_workassignment'
    'sprk_regardingservicerequest' = 'sprk_servicerequest'
}

# Escalation thresholds — mirrors this task's own <escalation><trigger> verbatim.
$EscalationMaxCandidates = 50000
$EscalationUnresolvablePct = 0.20

# ── Logging (buffered StreamWriter — Out-File -Append per line does not scale to a multi-thousand-row
#    backfill; the log is diagnostic/audit only, NOT the resumability mechanism — resumability comes from
#    the idempotent candidate query itself, so a lost tail of unflushed lines on an abrupt kill does not
#    put the tenant in an unknown state) ────────────────────────────────────────────────────────────────
if (-not $LogPath) {
    $logDir = Join-Path $PSScriptRoot 'logs'
    if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
    $LogPath = Join-Path $logDir "backfill-core-ancestor-stamps-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
}
$script:LogWriter = [System.IO.StreamWriter]::new($LogPath, $false, [System.Text.Encoding]::UTF8)

function Write-BackfillLog {
    param([string]$Line)
    $script:LogWriter.WriteLine("$(Get-Date -Format 'HH:mm:ss') $Line")
}

# ── Auth — operator's own az CLI context, matching the established scripts/ convention (e.g.
#    Backfill-DocumentHasFile.ps1, Migrate-DataverseData.ps1). No secrets in this script or the repo. ─────
function Get-DvToken {
    param([string]$Resource)
    $t = az account get-access-token --resource $Resource --query accessToken -o tsv 2>$null
    if (-not $t) {
        throw "Failed to acquire a Dataverse access token for $Resource. Run 'az login' (or check 'az account show') and retry."
    }
    return $t
}

# ── GUID canonicalization (ADR-044: bare, lowercase, at every boundary where a GUID crosses a system) ────
function ConvertTo-CleanGuid {
    param([AllowEmptyString()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    return $Value.Trim().Trim('{', '}').ToLowerInvariant()
}

# ── Web API plumbing ────────────────────────────────────────────────────────────────────────────────────
$script:Token = $null

function Get-DvHeaders {
    param([switch]$ForWrite)
    $h = @{
        Authorization      = "Bearer $script:Token"
        Accept             = 'application/json'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
    }
    if ($ForWrite) {
        $h['Content-Type'] = 'application/json'
        $h['If-Match'] = '*'
        $h['Prefer'] = 'return=minimal'
    }
    return $h
}

function Get-DvErrorDetail {
    param($ErrorRecord)
    $status = $null
    try { $status = $ErrorRecord.Exception.Response.StatusCode.value__ } catch { $status = $null }
    $msg = $ErrorRecord.Exception.Message
    try {
        $parsed = $ErrorRecord.ErrorDetails.Message | ConvertFrom-Json
        if ($parsed.error.message) { $msg = $parsed.error.message }
    } catch {
        # Response body wasn't the expected {error:{message:...}} shape (e.g. a network-level failure with
        # no HTTP body at all) — fall back to the raw exception message already assigned above.
        $null = $_
    }
    return "[$status] $msg"
}

function Invoke-DvWithRetry {
    <# One retry on HTTP 429, honoring Retry-After (default 3s). Bounded — not an open-ended retry loop. #>
    param([Parameter(Mandatory)][scriptblock]$Action, [int]$MaxRetries = 1)
    $attempt = 0
    while ($true) {
        try {
            return & $Action
        } catch {
            $status = $null
            try { $status = $_.Exception.Response.StatusCode.value__ } catch { $status = $null }
            if ($status -eq 429 -and $attempt -lt $MaxRetries) {
                $retryAfter = 3
                try {
                    $ra = $_.Exception.Response.Headers['Retry-After']
                    if ($ra) { $retryAfter = [int]"$ra" }
                } catch {
                    # No parseable Retry-After header — fall back to the 3s default set above.
                    $null = $_
                }
                Start-Sleep -Seconds $retryAfter
                $attempt++
                continue
            }
            throw
        }
    }
}

function Invoke-DvGetPaged {
    <# GET a collection query, following @odata.nextLink until exhausted. Never truncates at one page —
       this is what satisfies "pages correctly beyond 5,000 rows". #>
    param([Parameter(Mandatory)][string]$RelativePath)

    $headers = Get-DvHeaders
    $headers['Prefer'] = "odata.maxpagesize=$PageSize"
    $uri = "$EnvironmentUrl/api/data/v9.2/$RelativePath"
    $all = [System.Collections.Generic.List[object]]::new()

    while ($uri) {
        try {
            $resp = Invoke-DvWithRetry -Action { Invoke-RestMethod -Uri $uri -Headers $headers -Method Get }.GetNewClosure()
        } catch {
            return @{ Success = $false; Records = $all; Error = (Get-DvErrorDetail $_) }
        }
        if ($null -ne $resp.value) { $all.AddRange(@($resp.value)) }
        $uri = $resp.'@odata.nextLink'
    }
    return @{ Success = $true; Records = $all; Error = $null }
}

function Invoke-DvGetSingle {
    param([Parameter(Mandatory)][string]$RelativePath)
    $headers = Get-DvHeaders
    $uri = "$EnvironmentUrl/api/data/v9.2/$RelativePath"
    try {
        $resp = Invoke-DvWithRetry -Action { Invoke-RestMethod -Uri $uri -Headers $headers -Method Get }.GetNewClosure()
        return @{ Success = $true; Record = $resp; Error = $null }
    } catch {
        return @{ Success = $false; Record = $null; Error = (Get-DvErrorDetail $_) }
    }
}

function Invoke-DvPatch {
    param([Parameter(Mandatory)][string]$RelativePath, [Parameter(Mandatory)][hashtable]$Body)
    $headers = Get-DvHeaders -ForWrite
    $uri = "$EnvironmentUrl/api/data/v9.2/$RelativePath"
    $json = $Body | ConvertTo-Json -Compress
    try {
        Invoke-DvWithRetry -Action { Invoke-RestMethod -Uri $uri -Headers $headers -Method Patch -Body $json }.GetNewClosure() | Out-Null
        return @{ Success = $true; Error = $null }
    } catch {
        return @{ Success = $false; Error = (Get-DvErrorDetail $_) }
    }
}

function Get-LookupValue {
    <# Reads a Web API lookup response field (_<attr>_value), cleaned per ADR-044. #>
    param([Parameter(Mandatory)]$Row, [Parameter(Mandatory)][string]$Attribute)
    $prop = "_${Attribute}_value"
    $p = $Row.PSObject.Properties[$prop]
    if ($null -eq $p -or $null -eq $p.Value) { return $null }
    return ConvertTo-CleanGuid $p.Value
}

# ── Metadata discovery — ALWAYS live, NEVER a hand-maintained list. This is the direct fix for the
#    F-050-1/F-052-1 doc-drift finding: a hard-coded column list is exactly how "sprk_todo has no
#    sprk_regardingservicerequest" survived in project notes after the schema had already changed. ───────
function Get-EntitySetInfo {
    param([Parameter(Mandatory)][string]$LogicalName)
    $r = Invoke-DvGetSingle "EntityDefinitions(LogicalName='$LogicalName')?`$select=EntitySetName,PrimaryIdAttribute"
    if (-not $r.Success) { throw "Metadata lookup failed for '$LogicalName': $($r.Error)" }
    return @{ EntitySetName = $r.Record.EntitySetName; PrimaryIdAttribute = $r.Record.PrimaryIdAttribute }
}

function Get-ChildEntitySchema {
    <# Discovers, from live ManyToOneRelationships (never a name-prefix guess):
         AncestorColumns — which of the 4 canonical sprk_regarding{core} columns actually exist on this
                            entity AND correctly target the expected core entity.
         ChildLookups    — every lookup on this entity whose target is itself one of the 6 CHILD entities
                            (the "child-of-child" candidates). #>
    param([Parameter(Mandatory)][string]$LogicalName)

    $r = Invoke-DvGetPaged "EntityDefinitions(LogicalName='$LogicalName')/ManyToOneRelationships?`$select=ReferencingAttribute,ReferencedEntity"
    if (-not $r.Success) { throw "ManyToOneRelationships lookup failed for '$LogicalName': $($r.Error)" }

    $ancestorCols = @()
    foreach ($lookupName in $AncestorLookupToEntity.Keys) {
        $expectedTarget = $AncestorLookupToEntity[$lookupName]
        $match = $r.Records | Where-Object {
            $_.ReferencingAttribute -eq $lookupName -and $_.ReferencedEntity -eq $expectedTarget
        }
        if ($match) { $ancestorCols += $lookupName }
    }

    $childLookups = @()
    foreach ($rel in $r.Records) {
        if ($ChildEntities -contains $rel.ReferencedEntity) {
            $childLookups += [pscustomobject]@{ Attribute = $rel.ReferencingAttribute; Target = $rel.ReferencedEntity }
        }
    }

    return @{ AncestorColumns = $ancestorCols; ChildLookups = $childLookups }
}

function Resolve-TargetAncestors {
    <# The ONE-HOP read (ADR-034): given a child-of-child TARGET row, read exactly its own ancestor-stamp
       columns and stop. Never recurses. Caches schema/meta for targets outside the requested -Entities
       scope (a todo can target a communication even when -Entities only asked to write todo). #>
    param([Parameter(Mandatory)][string]$TargetEntity, [Parameter(Mandatory)][string]$TargetId)

    if (-not $script:childSchema.ContainsKey($TargetEntity)) {
        $script:childSchema[$TargetEntity] = Get-ChildEntitySchema -LogicalName $TargetEntity
    }
    $targetSchema = $script:childSchema[$TargetEntity]

    if ($targetSchema.AncestorColumns.Count -eq 0) {
        # Structural gap (sprk_invoice / sprk_document today) — legitimate, not an error. Matches the
        # resolver's own "applicable.Count == 0 -> NoAncestor" branch.
        return @{ Status = 'NoAncestorSchemaGap'; Stamps = @{}; Detail = $null }
    }

    if (-not $script:entityMeta.ContainsKey($TargetEntity)) {
        $script:entityMeta[$TargetEntity] = Get-EntitySetInfo -LogicalName $TargetEntity
    }
    $meta = $script:entityMeta[$TargetEntity]

    $select = $targetSchema.AncestorColumns -join ','
    $path = "$($meta.EntitySetName)($TargetId)?`$select=$select"
    $r = Invoke-DvGetSingle $path
    if (-not $r.Success) {
        return @{ Status = 'Error'; Stamps = @{}; Detail = $r.Error }
    }

    $stamps = @{}
    foreach ($col in $targetSchema.AncestorColumns) {
        $v = Get-LookupValue -Row $r.Record -Attribute $col
        if ($v) { $stamps[$col] = $v }
    }

    if ($stamps.Count -eq 0) {
        # Target is child-class, HAS ancestor columns, but every one is currently null — its own chain is
        # unresolved (run-to-fixpoint by re-running is the documented remedy; ADR-034).
        return @{ Status = 'Unresolvable'; Stamps = @{}; Detail = $null }
    }
    return @{ Status = 'Derived'; Stamps = $stamps; Detail = $null }
}

# ════════════════════════════════════════════════════════════════════════════════════════════════════════
# Main
# ════════════════════════════════════════════════════════════════════════════════════════════════════════

$exitCode = 0

try {
    Write-Host ""
    Write-Host "=== FR-26 Core-Ancestor Stamp Backfill ===" -ForegroundColor Cyan
    Write-Host "  Environment : $EnvironmentUrl"
    Write-Host "  Mode        : $(if ($IsDryRun) { 'DRY-RUN (no writes)' } else { 'APPLY (will write)' })" -ForegroundColor $(if ($IsDryRun) { 'Yellow' } else { 'Green' })
    Write-Host "  Entities    : $($Entities -join ', ')"
    Write-Host "  Log         : $LogPath"
    Write-Host ""
    Write-BackfillLog "RUN EnvironmentUrl=$EnvironmentUrl Mode=$(if ($IsDryRun) { 'DRY-RUN' } else { 'APPLY' }) Entities=$($Entities -join ',')"

    Write-Host "Acquiring Dataverse access token..." -ForegroundColor Yellow
    $script:Token = Get-DvToken -Resource $EnvironmentUrl
    Write-Host "  Token acquired." -ForegroundColor Green

    # ── Phase A: live schema discovery ─────────────────────────────────────────────────────────────────
    Write-Host ""
    Write-Host "[Phase A] Discovering live schema (never hard-coded)..." -ForegroundColor Cyan

    $script:entityMeta = @{}
    $script:childSchema = @{}

    foreach ($e in ($CoreEntities + $ChildEntities)) {
        $script:entityMeta[$e] = Get-EntitySetInfo -LogicalName $e
    }
    foreach ($e in $Entities) {
        $script:childSchema[$e] = Get-ChildEntitySchema -LogicalName $e
        $ac = $script:childSchema[$e].AncestorColumns
        $cl = $script:childSchema[$e].ChildLookups
        $acDisplay = if ($ac.Count) { $ac -join ', ' } else { '(none — structural gap, see script header)' }
        Write-Host ("  {0,-20} ancestor-columns: {1,-70} child-lookups: {2}" -f $e, $acDisplay, $cl.Count) `
            -ForegroundColor $(if ($ac.Count -eq 0) { 'DarkYellow' } else { 'Gray' })
        Write-BackfillLog "SCHEMA $e ancestorColumns=$($ac -join '|') childLookups=$(($cl | ForEach-Object { "$($_.Attribute)->$($_.Target)" }) -join '|')"
    }

    # ── Phase B pass 1: candidate discovery + classification (read-only; ALWAYS runs, dry-run or apply) ─
    Write-Host ""
    Write-Host "[Phase B] Scanning candidates and classifying (read-only pass)..." -ForegroundColor Cyan

    $plan = [System.Collections.Generic.List[object]]::new()
    $report = [System.Collections.Generic.List[object]]::new()
    $script:targetCache = @{}
    $candidateCountByEntity = @{}
    $unresolvableCountByEntity = @{}
    $hardErrorCount = 0

    foreach ($childEntity in $Entities) {
        $schema = $script:childSchema[$childEntity]
        $meta = $script:entityMeta[$childEntity]
        $candidateCountByEntity[$childEntity] = 0
        $unresolvableCountByEntity[$childEntity] = 0

        if ($schema.AncestorColumns.Count -eq 0) {
            Write-Host "  $childEntity — SKIPPED: 0 ancestor-stamp columns in live schema (structural gap; not a defect in this run)." -ForegroundColor DarkYellow
            Write-BackfillLog "SKIP-ENTITY $childEntity reason=no-ancestor-columns-in-schema"
            continue
        }
        if ($schema.ChildLookups.Count -eq 0) {
            Write-Host "  $childEntity — SKIPPED: 0 lookups targeting a CHILD-class entity in live schema." -ForegroundColor DarkYellow
            Write-BackfillLog "SKIP-ENTITY $childEntity reason=no-child-class-lookups-in-schema"
            continue
        }

        $childLookupNames = $schema.ChildLookups.Attribute | Select-Object -Unique
        $selectFields = @($meta.PrimaryIdAttribute) + $schema.AncestorColumns + $childLookupNames | Select-Object -Unique
        $childOr = ($childLookupNames | ForEach-Object { "$_ ne null" }) -join ' or '
        $ancestorOr = ($schema.AncestorColumns | ForEach-Object { "$_ eq null" }) -join ' or '
        $filter = "($childOr) and ($ancestorOr)"
        $select = $selectFields -join ','
        $path = "$($meta.EntitySetName)?`$select=$select&`$filter=$filter"

        Write-Host "  $childEntity — querying candidates..." -ForegroundColor White -NoNewline
        $result = Invoke-DvGetPaged $path
        if (-not $result.Success) {
            Write-Host " ERROR" -ForegroundColor Red
            Write-BackfillLog "ERROR-ENTITY $childEntity query-failed: $($result.Error)"
            $report.Add([pscustomobject]@{ Entity = $childEntity; Id = $null; Outcome = 'EntityQueryError'; Detail = $result.Error })
            $hardErrorCount++
            continue
        }
        $candidateCountByEntity[$childEntity] = $result.Records.Count
        Write-Host " $($result.Records.Count) candidate row(s)" -ForegroundColor Green

        foreach ($row in $result.Records) {
            $rowId = $row.($meta.PrimaryIdAttribute)

            # FR-13 mutual exclusivity means normally exactly one child-class lookup is populated per row.
            # Pre-FR-26 data quality is not guaranteed, so this is defensive, not assumed.
            $populated = $schema.ChildLookups | Where-Object { (Get-LookupValue -Row $row -Attribute $_.Attribute) }
            if ($populated.Count -eq 0) { continue }
            if ($populated.Count -gt 1) {
                Write-BackfillLog "WARN $childEntity($rowId) has $($populated.Count) populated child-class regarding lookups (FR-13 expects <=1) — processing all"
            }

            foreach ($link in $populated) {
                $targetEntity = $link.Target
                $targetId = Get-LookupValue -Row $row -Attribute $link.Attribute
                $cacheKey = "$targetEntity|$targetId"

                if (-not $script:targetCache.ContainsKey($cacheKey)) {
                    $script:targetCache[$cacheKey] = Resolve-TargetAncestors -TargetEntity $targetEntity -TargetId $targetId
                }
                $targetResult = $script:targetCache[$cacheKey]

                if ($targetResult.Status -eq 'Error') {
                    $report.Add([pscustomobject]@{ Entity = $childEntity; Id = $rowId; Outcome = 'Error'; Detail = "target $targetEntity($targetId): $($targetResult.Detail)" })
                    Write-BackfillLog "ERROR $childEntity($rowId) via $($link.Attribute)->$targetEntity($targetId): $($targetResult.Detail)"
                    $hardErrorCount++
                    continue
                }
                if ($targetResult.Status -eq 'NoAncestorSchemaGap') {
                    $report.Add([pscustomobject]@{ Entity = $childEntity; Id = $rowId; Outcome = 'NoAncestor'; Detail = "target $targetEntity has no ancestor-stamp columns in schema" })
                    Write-BackfillLog "NO-ANCESTOR $childEntity($rowId) via $($link.Attribute)->$targetEntity($targetId): target entity has no ancestor columns"
                    continue
                }
                if ($targetResult.Status -eq 'Unresolvable') {
                    $report.Add([pscustomobject]@{ Entity = $childEntity; Id = $rowId; Outcome = 'Unresolvable'; Detail = "target $targetEntity($targetId) carries no ancestor stamp yet" })
                    Write-BackfillLog "UNRESOLVABLE $childEntity($rowId) via $($link.Attribute)->$targetEntity($targetId): target unstamped"
                    $unresolvableCountByEntity[$childEntity]++
                    continue
                }

                # Derived — one or more (lookupAttribute -> guid) stamps from the target.
                foreach ($stampCol in $targetResult.Stamps.Keys) {
                    $derivedGuid = $targetResult.Stamps[$stampCol]
                    $coreEntity = $AncestorLookupToEntity[$stampCol]

                    if ($schema.AncestorColumns -notcontains $stampCol) {
                        $report.Add([pscustomobject]@{ Entity = $childEntity; Id = $rowId; Outcome = 'Unstampable'; Detail = "derived $coreEntity ancestor via $stampCol but $childEntity has no such column" })
                        Write-BackfillLog "UNSTAMPABLE $childEntity($rowId) stamp=$stampCol value=$derivedGuid host-has-no-such-column (FR-26 gap, not fatal)"
                        continue
                    }

                    $currentValue = Get-LookupValue -Row $row -Attribute $stampCol
                    if (-not $currentValue) {
                        $plan.Add([pscustomobject]@{
                                Entity          = $childEntity; EntitySet = $meta.EntitySetName; Id = $rowId
                                LookupAttribute = $stampCol; CoreEntity = $coreEntity
                                CoreEntitySet   = $script:entityMeta[$coreEntity].EntitySetName; Value = $derivedGuid
                            })
                        $report.Add([pscustomobject]@{ Entity = $childEntity; Id = $rowId; Outcome = 'ToWrite'; Detail = "$stampCol = $derivedGuid" })
                        Write-BackfillLog "WOULD-WRITE $childEntity($rowId) $stampCol=$derivedGuid via $($link.Attribute)->$targetEntity($targetId)"
                    } elseif ($currentValue -eq $derivedGuid) {
                        $report.Add([pscustomobject]@{ Entity = $childEntity; Id = $rowId; Outcome = 'AlreadyCorrect'; Detail = "$stampCol = $derivedGuid" })
                        Write-BackfillLog "SKIP-ALREADY-CORRECT $childEntity($rowId) $stampCol=$derivedGuid"
                    } else {
                        $report.Add([pscustomobject]@{ Entity = $childEntity; Id = $rowId; Outcome = 'Conflict'; Detail = "$stampCol existing=$currentValue derived=$derivedGuid" })
                        Write-BackfillLog "CONFLICT $childEntity($rowId) $stampCol existing=$currentValue derived=$derivedGuid NOT-OVERWRITTEN"
                    }
                }
            }
        }
    }

    # ── Summary (always printed — dry-run and apply share this) ────────────────────────────────────────
    Write-Host ""
    Write-Host "[Summary] Classification results" -ForegroundColor Cyan
    Write-Host "  (CandidatesScanned will NOT trend to 0 after a successful backfill — see script header." -ForegroundColor DarkGray
    Write-Host "   The convergence signal is ToWrite: 0.)" -ForegroundColor DarkGray

    $summaryRows = foreach ($e in $Entities) {
        $entityReport = $report | Where-Object { $_.Entity -eq $e }
        [pscustomobject]@{
            Entity            = $e
            CandidatesScanned = $candidateCountByEntity[$e]
            ToWrite           = @($entityReport | Where-Object Outcome -eq 'ToWrite').Count
            AlreadyCorrect    = @($entityReport | Where-Object Outcome -eq 'AlreadyCorrect').Count
            Conflict          = @($entityReport | Where-Object Outcome -eq 'Conflict').Count
            Unresolvable      = @($entityReport | Where-Object Outcome -eq 'Unresolvable').Count
            NoAncestor        = @($entityReport | Where-Object Outcome -eq 'NoAncestor').Count
            Unstampable       = @($entityReport | Where-Object Outcome -eq 'Unstampable').Count
            Errors            = @($entityReport | Where-Object { $_.Outcome -in @('Error', 'EntityQueryError') }).Count
        }
    }
    $summaryRows | Format-Table -AutoSize | Out-String | Write-Host

    $grandToWrite = @($report | Where-Object Outcome -eq 'ToWrite').Count
    $grandConflict = @($report | Where-Object Outcome -eq 'Conflict').Count
    $grandUnresolvable = @($report | Where-Object Outcome -eq 'Unresolvable').Count
    Write-Host ("  TOTAL: {0} to write, {1} already correct, {2} conflicts, {3} unresolvable, {4} hard errors" -f `
            $grandToWrite, `
            (@($report | Where-Object Outcome -eq 'AlreadyCorrect').Count), `
            $grandConflict, `
            $grandUnresolvable, `
            $hardErrorCount) -ForegroundColor White

    # Bounded inline display of conflicts/unresolvable; full detail always in the log.
    foreach ($kind in @('Conflict', 'Unresolvable')) {
        $rows = @($report | Where-Object Outcome -eq $kind)
        if ($rows.Count -gt 0) {
            Write-Host ""
            Write-Host "  $kind rows (entity + id) — first $([Math]::Min(25, $rows.Count)) of $($rows.Count), full list in log:" -ForegroundColor $(if ($kind -eq 'Conflict') { 'Red' } else { 'Yellow' })
            $rows | Select-Object -First 25 | ForEach-Object { Write-Host "    $($_.Entity)($($_.Id)) — $($_.Detail)" -ForegroundColor Gray }
        }
    }

    # ── Escalation gate (matches the task's own <escalation><trigger> verbatim) ────────────────────────
    $grandTotalCandidates = 0
    foreach ($v in $candidateCountByEntity.Values) { $grandTotalCandidates += $v }

    $escalationReasons = @()
    if ($grandTotalCandidates -gt $EscalationMaxCandidates) {
        $escalationReasons += "Total candidate rows across all entities ($grandTotalCandidates) exceeds $EscalationMaxCandidates."
    }
    foreach ($e in $Entities) {
        $c = $candidateCountByEntity[$e]
        if ($c -gt 0) {
            $u = $unresolvableCountByEntity[$e]
            $pct = $u / $c
            if ($pct -gt $EscalationUnresolvablePct) {
                $escalationReasons += ("Entity '{0}': {1} of {2} candidates ({3:P1}) are unresolvable (target itself unstamped) — exceeds {4:P0}." -f $e, $u, $c, $pct, $EscalationUnresolvablePct)
            }
        }
    }
    $escalationTriggered = $escalationReasons.Count -gt 0

    if ($escalationTriggered) {
        Write-Host ""
        Write-Host "############################################################" -ForegroundColor Red
        Write-Host "ESCALATION — the data may need a staged fixpoint plan (CLAUDE.md Section 6)" -ForegroundColor Red
        Write-Host "############################################################" -ForegroundColor Red
        foreach ($r in $escalationReasons) { Write-Host "  - $r" -ForegroundColor Red }
        Write-Host "  Recommendation: review the unresolvable/conflict detail in the log, consider -Entities" -ForegroundColor Red
        Write-Host "  to stage a narrower rollout, and re-run to fixpoint rather than -Apply-ing everything at once." -ForegroundColor Red
        Write-BackfillLog "ESCALATION-TRIGGERED $($escalationReasons -join ' | ')"
    }

    # ── Mode-specific epilogue ──────────────────────────────────────────────────────────────────────────
    if ($IsDryRun) {
        Write-Host ""
        Write-Host "DRY-RUN complete. 0 writes issued (verify: log has zero WRITTEN/WRITE-FAILED lines)." -ForegroundColor Cyan
        Write-Host "Re-run with -Apply to write $grandToWrite stamp(s)." -ForegroundColor Cyan
        $exitCode = if ($hardErrorCount -gt 0) { 1 } else { 0 }
    } else {
        if ($escalationTriggered -and -not $AcknowledgeEscalation) {
            Write-Host ""
            Write-Host "WRITES SKIPPED — escalation threshold met. Re-run with -Apply -AcknowledgeEscalation after review." -ForegroundColor Red
            Write-BackfillLog "WRITES-SKIPPED escalation-not-acknowledged"
            $exitCode = 2
        } else {
            Write-Host ""
            Write-Host "[Apply] Writing $($plan.Count) stamp(s) in batches of $BatchSize..." -ForegroundColor Cyan
            $written = 0; $failed = 0; $deferred = 0

            for ($i = 0; $i -lt $plan.Count; $i++) {
                if ($MaxWritesPerRun -gt 0 -and $written -ge $MaxWritesPerRun) {
                    $deferred = $plan.Count - $i
                    Write-Host "  MaxWritesPerRun ($MaxWritesPerRun) reached — $deferred stamp(s) deferred to a future run." -ForegroundColor DarkYellow
                    Write-BackfillLog "CAP-REACHED MaxWritesPerRun=$MaxWritesPerRun deferred=$deferred"
                    break
                }

                $item = $plan[$i]
                $body = @{ "$($item.LookupAttribute)@odata.bind" = "/$($item.CoreEntitySet)($($item.Value))" }
                $result = Invoke-DvPatch -RelativePath "$($item.EntitySet)($($item.Id))" -Body $body

                if ($result.Success) {
                    $written++
                    Write-BackfillLog "WRITTEN $($item.Entity)($($item.Id)) $($item.LookupAttribute)=$($item.Value)"
                } else {
                    $failed++
                    Write-BackfillLog "WRITE-FAILED $($item.Entity)($($item.Id)) $($item.LookupAttribute)=$($item.Value): $($result.Error)"
                    Write-Host "  WRITE-FAILED $($item.Entity)($($item.Id)): $($result.Error)" -ForegroundColor Red
                }

                if ((($written + $failed) % $BatchSize) -eq 0 -or ($i -eq $plan.Count - 1)) {
                    Write-Host "  Progress: $($written + $failed)/$($plan.Count) ($written written, $failed failed)" -ForegroundColor Gray
                }
            }

            Write-Host ""
            $tail = if ($deferred -gt 0) { ", $deferred deferred (MaxWritesPerRun)" } else { '' }
            Write-Host "Apply complete: $written written, $failed failed$tail." -ForegroundColor $(if ($failed -gt 0) { 'Red' } else { 'Green' })
            Write-BackfillLog "APPLY-COMPLETE written=$written failed=$failed deferred=$deferred"
            $exitCode = if ($failed -gt 0) { 1 } else { 0 }
        }
    }

    Write-Host ""
    Write-Host "Log: $LogPath" -ForegroundColor Gray
} finally {
    if ($script:LogWriter) {
        $script:LogWriter.Flush()
        $script:LogWriter.Close()
    }
}

exit $exitCode
