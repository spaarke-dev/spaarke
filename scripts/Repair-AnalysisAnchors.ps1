#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Restores the ANCHOR (parent lookup) of every active sprk_analysis row that has none, where the anchor is derivable
    from data that still exists — and classifies every anchorless row by WHY it has none (unified-access-control-r2
    task 162 f1; owner decision round 15, 2026-10-03). Dry run by default; -Apply writes; -Verify checks.

.DESCRIPTION
    WHY. GET /api/ai/analysis/{id} (and the chat analysis host, task 164) decide with ONE analysis-read rule
    (AnalysisAuthorizationFilter.ResolveAnalysisReadTargetsAsync): Read on EVERY populated anchor; an analysis with NO
    anchor is PERSONAL — only the person who created it (Dataverse systemuserid) may read it. Owner round 15 item 3:
    class (i) rows — created in a record's context — whose anchor is derivable from existing data are backfilled, so
    they are decided by their parent again rather than by the personal branch.

    WHAT COUNTS AS DERIVABLE (authoritative sources only; a guess is never written):
      (1) THE POLYMORPHIC PAIR. sprk_regardingrecordid + sprk_regardingrecordtype (-> sprk_recordtype_ref
          .sprk_regardingfield) name the typed anchor column the pair was written with. Written when that column is an
          anchor column of sprk_analysis AND the target row exists.
      (2) THE ANALYSIS'S CHAT SESSION. sprk_aichatsummary rows whose sprk_analysis = the analysis carry the session's
          sprk_documentid. Written to sprk_documentid when ALL of the analysis's sessions name exactly ONE document AND
          that document exists.
      (3) The analysis's OUTPUT document (sprk_outputfileid) is itself an anchor column, so a row carrying it is not
          anchorless; sprk_analysisoutput rows carry no record reference. Nothing to derive there.
    A source naming a row that no longer exists (the usual case: see CLASSIFICATION) is reported, never written —
    Dataverse cannot point a lookup at a deleted row, and pointing it at a different row would grant that row's readers
    access to the analysis.

    CLASSIFICATION (informational, never written; -Classify adds the audit evidence, ~1 read per deleted document):
      - parent-deleted (proven)     the analysis's name or creation time matches exactly ONE deleted sprk_document in
                                    the audit log (Delete operations, with RetrieveAuditDetails for the deleted row's
                                    name). The relationship sprk_document_analysis_document has Delete = RemoveLink, so
                                    deleting a document CLEARS the analysis's sprk_documentid.
      - parent-deleted (ambiguous)  several deleted documents match.
      - unattributed                no audit evidence either way.
      - derivable                   a source above names an existing parent: the -Apply plan.
    Each row is also attributed to the WRITER that produced it, by its name pattern and creator (see Get-WriterOf).

    SAFETY MODEL
      - Dry run is the DEFAULT. Only -Apply writes; -Verify never writes. -Apply and -Verify are exclusive.
      - Every write is one PATCH of one lookup on one row (no transaction to leave half-applied); idempotent — a
        re-run plans 0 writes for a row whose anchor is already set (it is no longer anchorless).
      - -Verify exits 1 when any derivable anchor is still unwritten, naming each row; 0 otherwise.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code. Cannot be combined with -Apply.

.PARAMETER Classify
    Adds the audit-log evidence (deleted parents) to the per-row classification. Read-only.

.EXAMPLE
    .\Repair-AnalysisAnchors.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Classify
    Dry run with the full classification. Zero writes.

.EXAMPLE
    .\Repair-AnalysisAnchors.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply
    Writes every derivable anchor.

.EXAMPLE
    .\Repair-AnalysisAnchors.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify
    Exit 0 when no derivable anchor is left unwritten.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [switch]$Apply,

    [switch]$Verify,

    [switch]$Classify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')

if ($Apply -and $Verify) {
    throw '-Apply and -Verify cannot be combined.'
}

$Mode = if ($Apply) { 'APPLY' } elseif ($Verify) { 'VERIFY' } else { 'DRY RUN' }

# ── The anchor columns of sprk_analysis. MUST equal AnalysisAuthorizationFilter.AnchorColumns (pinned by
#    AnalysisAnchorRepairScriptAgreementTests): a column the filter treats as an anchor is a column a backfill may fill.
$AnchorColumns = @('sprk_documentid', 'sprk_outputfileid', 'sprk_regardingdocument', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment', 'sprk_regardinginvoice', 'sprk_regardingbudget', 'sprk_regardingcommunication', 'sprk_regardingservicerequest')

# ── Auth: the operator's own az CLI context (the scripts/ convention). No secret in this script or the repo.
function Get-DvToken {
    $t = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
    if (-not $t) { throw "Failed to acquire a Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
    return $t
}
$script:Token = Get-DvToken

function Get-DvHeaders {
    param([switch]$ForWrite, [switch]$Annotations)
    $h = @{
        Authorization      = "Bearer $script:Token"
        Accept             = 'application/json'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        Prefer             = 'odata.maxpagesize=5000'
    }
    if ($Annotations) { $h['Prefer'] = 'odata.maxpagesize=5000,odata.include-annotations="*"' }
    if ($ForWrite) {
        $h['Content-Type'] = 'application/json'
        $h['If-Match'] = '*'
        $h['Prefer'] = 'return=minimal'
    }
    return $h
}

function Invoke-DvGet {
    param([Parameter(Mandatory)][string]$RelativePath, [switch]$Annotations)
    $uri = "$EnvironmentUrl/api/data/v9.2/$RelativePath"
    return Invoke-RestMethod -Uri $uri -Headers (Get-DvHeaders -Annotations:$Annotations) -Method Get
}

function Invoke-DvGetAll {
    param([Parameter(Mandatory)][string]$RelativePath, [switch]$Annotations)
    $all = [System.Collections.Generic.List[object]]::new()
    $uri = "$EnvironmentUrl/api/data/v9.2/$RelativePath"
    $headers = Get-DvHeaders -Annotations:$Annotations
    while ($uri) {
        $resp = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
        if ($null -ne $resp.value) { $all.AddRange(@($resp.value)) }
        $uri = $resp.'@odata.nextLink'
    }
    return , $all
}

function Test-DvRowExists {
    param([string]$EntitySet, [string]$PrimaryId, [string]$Id)
    try {
        $null = Invoke-DvGet "$EntitySet($Id)?`$select=$PrimaryId"
        return $true
    } catch {
        $status = $null
        try { $status = $_.Exception.Response.StatusCode.value__ } catch { $status = $null }
        if ($status -eq 404) { return $false }
        throw
    }
}

function ConvertTo-CleanGuid {
    param([AllowNull()][AllowEmptyString()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $clean = $Value.Trim().Trim('{', '}').ToLowerInvariant()
    $parsed = [guid]::Empty
    if ([guid]::TryParse($clean, [ref]$parsed) -and $parsed -ne [guid]::Empty) { return $parsed.ToString() }
    return $null
}

# ── The WRITER that produced a row, inferred from its name pattern and creator (task 162 f1 note §14).
function Get-WriterOf {
    param($Row, [string]$CreatedByName)
    $name = [string]$Row.sprk_name
    $isApp = ($CreatedByName -match '^(SDAP-BFF|# )')
    if ($name -like 'Document Profile - *') { return "AppOnlyAnalysisService document profile / AnalysisResultPersistence ($(if ($isApp) { 'BFF app identity' } else { 'pre-app-only BFF, as the user' }))" }
    if ($name -like 'Email Analysis - *') { return 'AppOnlyAnalysisService email analysis' }
    if ($name -like 'Analysis - *') { return "Playbook Library / Analysis Builder create ($(if ($isApp) { 'POST /api/ai/analysis/create' } else { 'earlier direct client create' }))" }
    if ($isApp) { return 'POST /api/ai/analysis/promote or /create (user-named, BFF app identity)' }
    return 'Create Analysis wizard (client Xrm.WebApi, user-named)'
}

Write-Host "Repair-AnalysisAnchors — $Mode — $EnvironmentUrl" -ForegroundColor Cyan

# ── Metadata: every anchor column must exist live, and each one's navigation property + target set comes from metadata
#    (never a pluralized guess).
$rels = (Invoke-DvGet "EntityDefinitions(LogicalName='sprk_analysis')/ManyToOneRelationships?`$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity").value
$anchorMeta = @{}
foreach ($col in $AnchorColumns) {
    $rel = $rels | Where-Object { $_.ReferencingAttribute -eq $col } | Select-Object -First 1
    if (-not $rel) { throw "Anchor column '$col' does not exist on sprk_analysis in $EnvironmentUrl — the filter and this script are out of step with the environment." }
    $target = Invoke-DvGet "EntityDefinitions(LogicalName='$($rel.ReferencedEntity)')?`$select=EntitySetName,PrimaryIdAttribute"
    $anchorMeta[$col] = @{ Nav = $rel.ReferencingEntityNavigationPropertyName; Entity = $rel.ReferencedEntity; Set = $target.EntitySetName; PrimaryId = $target.PrimaryIdAttribute }
}

$recordTypes = @{}
foreach ($rt in (Invoke-DvGetAll "sprk_recordtype_refs?`$select=sprk_recordtype_refid,sprk_recordlogicalname,sprk_regardingfield")) {
    $recordTypes[[string]$rt.sprk_recordtype_refid] = $rt
}

# ── Census: active analyses with no populated anchor.
$select = @('sprk_analysisid', 'sprk_name', 'createdon', '_createdby_value', 'sprk_regardingrecordid', '_sprk_regardingrecordtype_value') +
    ($AnchorColumns | ForEach-Object { "_$($_)_value" })
$rows = Invoke-DvGetAll "sprk_analysises?`$select=$($select -join ',')&`$filter=statecode eq 0" -Annotations
$anchorless = @($rows | Where-Object { $r = $_; -not ($AnchorColumns | Where-Object { $r."_$($_)_value" }) })
Write-Host ("Active analyses: {0}; with no anchor: {1}" -f $rows.Count, $anchorless.Count)

# ── Derivation.
$plan = [System.Collections.Generic.List[object]]::new()
$report = [System.Collections.Generic.List[object]]::new()
foreach ($row in $anchorless) {
    $id = [string]$row.sprk_analysisid
    $createdByName = [string]$row.'_createdby_value@OData.Community.Display.V1.FormattedValue'
    $writes = @{}
    $notes = [System.Collections.Generic.List[string]]::new()

    # (1) the polymorphic pair
    $pairId = ConvertTo-CleanGuid ([string]$row.sprk_regardingrecordid)
    $pairType = [string]$row._sprk_regardingrecordtype_value
    if ($pairId -and $pairType -and $recordTypes.ContainsKey($pairType)) {
        $col = [string]$recordTypes[$pairType].sprk_regardingfield
        if ($anchorMeta.ContainsKey($col)) {
            $m = $anchorMeta[$col]
            if (Test-DvRowExists $m.Set $m.PrimaryId $pairId) { $writes[$col] = $pairId } else { $notes.Add("pair names a $($m.Entity) that no longer exists") }
        } else { $notes.Add("pair type '$col' is not an anchor column") }
    }

    # (2) the analysis's chat sessions
    $sessions = Invoke-DvGetAll "sprk_aichatsummaries?`$select=sprk_documentid&`$filter=_sprk_analysis_value eq $id"
    $sessionDocs = @($sessions | ForEach-Object { ConvertTo-CleanGuid ([string]$_.sprk_documentid) } | Where-Object { $_ } | Sort-Object -Unique)
    if ($sessionDocs.Count -eq 1) {
        $m = $anchorMeta['sprk_documentid']
        if (Test-DvRowExists $m.Set $m.PrimaryId $sessionDocs[0]) { $writes['sprk_documentid'] = $sessionDocs[0] } else { $notes.Add('its chat session names a document that no longer exists') }
    } elseif ($sessionDocs.Count -gt 1) {
        $notes.Add("its chat sessions name $($sessionDocs.Count) different documents (ambiguous; not written)")
    }

    $entry = [pscustomobject]@{
        Id = $id; Name = [string]$row.sprk_name; CreatedOn = [string]$row.createdon; CreatedBy = $createdByName
        Writer = (Get-WriterOf $row $createdByName); Writes = $writes; Notes = ($notes -join '; '); Class = $null
    }
    if ($writes.Count -gt 0) { $entry.Class = 'derivable'; $plan.Add($entry) }
    $report.Add($entry)
}

# ── Classification evidence from the audit log (read-only).
if ($Classify) {
    $deletes = Invoke-DvGetAll "audits?`$select=auditid,createdon,_objectid_value&`$filter=objecttypecode eq 'sprk_document' and operation eq 3"
    $creates = @{}
    foreach ($c in (Invoke-DvGetAll "audits?`$select=createdon,_objectid_value&`$filter=objecttypecode eq 'sprk_document' and operation eq 1")) {
        $creates[[string]$c._objectid_value] = [datetime]$c.createdon
    }
    $deleted = @{}
    foreach ($d in $deletes) {
        $docId = [string]$d._objectid_value
        if (-not $docId -or $deleted.ContainsKey($docId)) { continue }
        $detail = Invoke-DvGet "audits($($d.auditid))/Microsoft.Dynamics.CRM.RetrieveAuditDetails"
        $deleted[$docId] = @{ DeletedOn = [datetime]$d.createdon; Name = [string]$detail.AuditDetail.OldValue.sprk_documentname; CreatedOn = $creates[$docId] }
    }
    Write-Host ("Audit: {0} deleted documents" -f $deleted.Count)
    foreach ($entry in $report) {
        if ($entry.Class -eq 'derivable') { continue }
        $at = [datetime]$entry.CreatedOn
        $cands = @()
        if ($entry.Name -match '^Analysis - Document \(([0-9A-Fa-f]{8})\.\.\.\)$') {
            $prefix = $Matches[1].ToLowerInvariant()
            $cands = @($deleted.Keys | Where-Object { $_.StartsWith($prefix) })
        } elseif ($entry.Name -match '^Analysis - (.+)$' -and $Matches[1] -notlike 'Document*') {
            $docName = $Matches[1].Trim()
            $cands = @($deleted.Keys | Where-Object {
                $d = $deleted[$_]
                $d.Name -and $d.Name.Trim() -ieq $docName -and $d.DeletedOn -ge $at -and (-not $d.CreatedOn -or $d.CreatedOn -le $at)
            })
        } else {
            $cands = @($deleted.Keys | Where-Object {
                $d = $deleted[$_]
                $d.CreatedOn -and ($at - $d.CreatedOn).TotalSeconds -ge 0 -and ($at - $d.CreatedOn).TotalSeconds -le 600 -and $d.DeletedOn -ge $at
            })
        }
        $entry.Class = switch ($cands.Count) { 0 { 'unattributed' } 1 { 'parent-deleted (proven)' } default { 'parent-deleted (ambiguous)' } }
    }
} else {
    foreach ($entry in $report) { if (-not $entry.Class) { $entry.Class = 'not derivable (run -Classify for the audit evidence)' } }
}

# ── Report.
Write-Host "`nAnchorless analyses by writer and class:" -ForegroundColor Cyan
$report | Group-Object Writer, Class | Sort-Object Count -Descending | ForEach-Object { "{0,5}  {1}" -f $_.Count, $_.Name } | Write-Host
Write-Host ("`nDerivable anchors (the -Apply plan): {0} row(s)" -f $plan.Count) -ForegroundColor Cyan
foreach ($p in $plan) {
    Write-Host ("  {0}  {1}  -> {2}" -f $p.Id.Substring(0, 8), $p.Name, (($p.Writes.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value.Substring(0, 8))" }) -join ', '))
}
$report | Where-Object { $_.Notes } | ForEach-Object { Write-Host ("  note {0}: {1}" -f $_.Id.Substring(0, 8), $_.Notes) }

if ($Verify) {
    if ($plan.Count -gt 0) {
        Write-Host "`nVERIFY FAILED: $($plan.Count) derivable anchor(s) are not written." -ForegroundColor Red
        exit 1
    }
    Write-Host "`nVERIFY PASSED: no derivable anchor is left unwritten. The remaining anchorless analyses are PERSONAL (creator-only) under the analysis-read rule." -ForegroundColor Green
    exit 0
}

if (-not $Apply) {
    Write-Host "`nDRY RUN — nothing written. Re-run with -Apply to write the plan." -ForegroundColor Yellow
    exit 0
}

# ── Apply: one PATCH per row, binding each derived anchor through its navigation property.
$failed = 0
foreach ($p in $plan) {
    $body = @{}
    foreach ($w in $p.Writes.GetEnumerator()) {
        $m = $anchorMeta[$w.Key]
        $body["$($m.Nav)@odata.bind"] = "/$($m.Set)($($w.Value))"
    }
    try {
        Invoke-RestMethod -Uri "$EnvironmentUrl/api/data/v9.2/sprk_analysises($($p.Id))" -Headers (Get-DvHeaders -ForWrite) -Method Patch -Body ($body | ConvertTo-Json -Compress) | Out-Null
        Write-Host ("  wrote {0}" -f $p.Id.Substring(0, 8)) -ForegroundColor Green
    } catch {
        $failed++
        Write-Host ("  FAILED {0}: {1}" -f $p.Id.Substring(0, 8), $_.Exception.Message) -ForegroundColor Red
    }
}
Write-Host ("`nAPPLY done: {0} written, {1} failed. Run -Verify next." -f ($plan.Count - $failed), $failed)
exit ([int]($failed -gt 0))
