#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Deleting a document deletes its AI analyses and their outputs: sets Delete = Cascade on
    sprk_document_analysis_document and sprk_analysis_analysisoutput (unified-access-control-r2 task 162, finding
    F-162-f1-1; decision round 34 item 1). Dry run by default; -Apply writes; -Verify checks. Idempotent.

.DESCRIPTION
    WHY. Both relationships were Delete = RemoveLink, so deleting a document left its sprk_analysis rows (and their
    sprk_analysisoutput rows) behind, still holding the deleted document's content in sprk_workingdocument and the
    outputs. Access was already correct (round 15: an anchorless analysis is personal, or the uniform 404) — this is the
    data lifecycle: the owner decided (round 34 item 1) that a document's analyses and outputs go with it.

    The script provisions, in order:
      (a) CASCADE   sprk_document_analysis_document (sprk_analysis.sprk_documentid -> sprk_document) and
                    sprk_analysis_analysisoutput (sprk_analysisoutput.sprk_analysisid -> sprk_analysis): Delete = Cascade.
                    Every OTHER cascade value is left exactly as it is (read back and reported); -Verify fails if Assign,
                    Share, Unshare, Reparent or Merge is anything but NoCascade (no ownership or sharing cascade is
                    introduced here — task 149's sharee mirror owns that, and round 11 item 2 rejects table-wide
                    Share/Reparent cascades).
      (b) SOLUTION  both relationships are in -SolutionUniqueName (SpaarkeCore), decided through
                    scripts/common/DataverseSolutionMembership.ps1 (a relationship of a table added with
                    rootcomponentbehavior 0 counts as included).
      (c) PUBLISH   sprk_document, sprk_analysis, sprk_analysisoutput (-Apply only).

    Scope of the effect. Only an analysis whose sprk_documentid names the deleted document is deleted, with its outputs.
    An analysis with NO document (a personal, standalone analysis — round 15 item 4) has nothing to cascade from and is
    untouched; so is an analysis anchored only to a matter/project/regarding record.

    ⚠️ MANUAL GATE (main session). -Apply is NOT run by the integration lane. After -Apply and -Verify (exit 0), probe
    with a NON-ADMIN test user (the round-11 child-BU user uac.child.user@demo.spaarke.com): create a TEST document the
    user may delete, an analysis on it (team-owned, as task 146 creates it) with one output; delete the document as that
    user; expect the delete to SUCCEED and the analysis and output to be gone; and an unrelated anchorless analysis to be
    unchanged. A cascade the deleting user's rights block would make documents undeletable — that is what the probe
    rules out. The probe steps are in projects/unified-access-control-r2/notes/task-162-ai-analysis-route-authorization.md
    §14.14.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER SolutionUniqueName
    The unmanaged solution that carries the relationships. Default SpaarkeCore.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-DocumentAnalysisCascadeSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: the current and intended cascade of each relationship; zero writes.

.EXAMPLE
    .\Set-DocumentAnalysisCascadeSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply

.EXAMPLE
    .\Set-DocumentAnalysisCascadeSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify

.NOTES
    unified-access-control-r2 task 162 f1 §14.8 (F-162-f1-1), decided by round 34 item 1
    (notes/session27-owner-decisions-and-research.md). Live metadata (spaarkedev1, read-only, 2026-10-04): both
    relationships unmanaged and customizable, Delete = RemoveLink, Archive = RemoveLink, everything else NoCascade.
    Auth: the operator's own az CLI identity (System Administrator in the environment). No secrets.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (live metadata, spaarkedev1 2026-10-04) ──────────────────────────────────────────────────────
$Relationships = @(
    [pscustomobject]@{ SchemaName = 'sprk_document_analysis_document'; Referenced = 'sprk_document'; Referencing = 'sprk_analysis'; Attribute = 'sprk_documentid' }
    [pscustomobject]@{ SchemaName = 'sprk_analysis_analysisoutput'; Referenced = 'sprk_analysis'; Referencing = 'sprk_analysisoutput'; Attribute = 'sprk_analysisid' }
)
$TargetDelete = 'Cascade'
# Cascades this script must never introduce (ownership/sharing propagation is not part of round 34 item 1).
$MustStayNoCascade = @('Assign', 'Share', 'Unshare', 'Reparent', 'Merge')

# ── Auth + helpers ──────────────────────────────────────────────────────────────────────────────────────────
$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
}
function Invoke-DvGet([string]$Path) { Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Get }
function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 30 -Compress }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json
}
function Try-DvGet([string]$Path) { try { Invoke-DvGet $Path } catch { $null } }
function Read-Relationship([string]$SchemaName) {
    Try-DvGet "RelationshipDefinitions(SchemaName='$SchemaName')/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
}

$IsDryRun = -not $Apply.IsPresent
. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
$gaps = [System.Collections.Generic.List[string]]::new()
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))
Write-Host "Solution    : $SolutionUniqueName"

# ── (a) CASCADE ─────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(a) Delete cascade"
$found = @{}
foreach ($r in $Relationships) {
    $rel = Read-Relationship $r.SchemaName
    if (-not $rel) { Report 'FAIL' "relationship $($r.SchemaName) not found (expected $($r.Referencing).$($r.Attribute) -> $($r.Referenced))"; continue }
    if ($rel.ReferencedEntity -ne $r.Referenced -or $rel.ReferencingEntity -ne $r.Referencing -or $rel.ReferencingAttribute -ne $r.Attribute) {
        Report 'FAIL' "$($r.SchemaName) is $($rel.ReferencingEntity).$($rel.ReferencingAttribute) -> $($rel.ReferencedEntity); expected $($r.Referencing).$($r.Attribute) -> $($r.Referenced)"
        continue
    }
    $found[$r.SchemaName] = $rel
    $cfg = $rel.CascadeConfiguration
    $widened = @($MustStayNoCascade | Where-Object { $cfg.$_ -ne 'NoCascade' } | ForEach-Object { "$_=$($cfg.$_)" })
    if ($widened.Count -gt 0) { Report 'FAIL' "$($r.SchemaName): expected NoCascade for $($MustStayNoCascade -join '/'), found $($widened -join '; ')" }
    Report 'INFO' ("{0} now: Delete={1}, Archive={2}, Assign={3}, Share={4}, Unshare={5}, Reparent={6}, Merge={7}" -f `
        $r.SchemaName, $cfg.Delete, $cfg.Archive, $cfg.Assign, $cfg.Share, $cfg.Unshare, $cfg.Reparent, $cfg.Merge)

    if ($cfg.Delete -eq $TargetDelete) { Report 'OK' "$($r.SchemaName) Delete = $TargetDelete"; continue }
    if ($Verify) { Report 'MISSING' "$($r.SchemaName) Delete = $TargetDelete (is $($cfg.Delete))"; continue }
    if ($IsDryRun) { Report 'WOULD' "set $($r.SchemaName) Delete $($cfg.Delete) -> $TargetDelete (nothing else changes)"; continue }

    # Update = PUT the full definition back (the Web API has no partial update for relationship metadata), with ONLY
    # Delete changed. MSCRM.MergeLabels keeps any label translations the PUT body does not carry.
    $body = [ordered]@{ '@odata.type' = 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata' }
    foreach ($p in $rel.PSObject.Properties) {
        if ($p.Name -like '@odata.*' -or $p.Name -like '*@odata.*') { continue }
        $body[$p.Name] = $p.Value
    }
    $body.CascadeConfiguration.Delete = $TargetDelete
    Invoke-DvWrite PUT "RelationshipDefinitions($($rel.MetadataId))" $body @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
    # Relationship metadata is read through a cache: an immediate read-back can still show the old value even
    # though the PUT took effect (seen on spaarkedev1, 2026-10-04). Poll for up to 2 minutes before failing.
    $after = $null
    for ($try = 1; $try -le 24; $try++) {
        $after = Read-Relationship $r.SchemaName
        if ($after.CascadeConfiguration.Delete -eq $TargetDelete) { break }
        Start-Sleep -Seconds 5
    }
    if ($after.CascadeConfiguration.Delete -ne $TargetDelete) { throw "$($r.SchemaName) still reads Delete = $($after.CascadeConfiguration.Delete) after the update." }
    $found[$r.SchemaName] = $after
    Report 'DONE' "$($r.SchemaName) Delete = $TargetDelete (read back)"
}

# ── (b) SOLUTION ────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Solution components"
$solution = @((Invoke-DvGet "solutions?`$select=solutionid,uniquename&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "solution '$SolutionUniqueName' not found" }
else {
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
    foreach ($r in $Relationships) {
        $rel = $found[$r.SchemaName]
        if (-not $rel) { continue }
        # A relationship is a subcomponent of its REFERENCING table (the table that carries the lookup).
        $tableId = (Invoke-DvGet "EntityDefinitions(LogicalName='$($r.Referencing)')?`$select=MetadataId").MetadataId
        $how = Test-DvInSolution -Membership $membership -ComponentId $rel.MetadataId -TableMetadataId $tableId
        $label = "relationship $($r.SchemaName)"
        if ($how -eq 'Direct') { Report 'OK' "$label in $SolutionUniqueName"; continue }
        if ($how -eq 'ViaTable') { Report 'OK' "$label in $SolutionUniqueName ($($r.Referencing) includes subcomponents)"; continue }
        if ($Verify) { Report 'MISSING' "$label in $SolutionUniqueName"; continue }
        if ($IsDryRun) { Report 'WOULD' "add $label to $SolutionUniqueName"; continue }
        Invoke-DvWrite POST 'AddSolutionComponent' @{ ComponentId = $rel.MetadataId; ComponentType = 10; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false } | Out-Null
        Report 'DONE' "added $label to $SolutionUniqueName"
    }
}

# ── (c) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
    $entities = ($Relationships | ForEach-Object { $_.Referenced; $_.Referencing } | Select-Object -Unique | ForEach-Object { "<entity>$_</entity>" }) -join ''
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities>$entities</entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $(($Relationships | ForEach-Object { $_.Referenced; $_.Referencing } | Select-Object -Unique) -join ', ')." -ForegroundColor Green
}

if ($Verify) {
    if ($gaps.Count -eq 0) {
        Write-Host "`nVERIFY PASS: deleting a document deletes its analyses and their outputs. Run the non-admin delete probe (task 162 note §14.14)." -ForegroundColor Green
        exit 0
    }
    Write-Host "`nVERIFY FAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
if ($IsDryRun) { Write-Host "`nDRY RUN complete — nothing was written. Re-run with -Apply (main-session manual gate)." -ForegroundColor Cyan }
