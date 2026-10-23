#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Deleting a communication, or a parent document, can no longer clear a document's link to it: sets Delete = Restrict
    on sprk_document_RelatedCommunication_sprk_communication and sprk_document_ParentDocument (unified-access-control-r2
    task 188 slice D; owner round 111 item 3, option (a)). Dry run by default; -Apply writes; -Verify checks. Idempotent.

.DESCRIPTION
    WHY. D7 (task 188) hides a document from external callers when the communication it comes from is internal-only or
    not Open. The communication is found through the document's sprk_relatedcommunication, else through its
    sprk_parentdocument chain. Both relationships were Delete = RemoveLink, so deleting the communication, or the parent
    .eml document, CLEARED the link and its documents became visible to partners. With Delete = Restrict, Dataverse refuses
    the delete while a linked document exists; the link is kept, so the narrowing keeps working (security fails closed).
    The BFF's document delete routes refuse such a delete first with 409 document_has_child_documents
    (DocumentCheckoutService, DataverseDocumentsEndpoints), before any file is deleted.

    The script provisions, in order:
      (a) RESTRICT  sprk_document_RelatedCommunication_sprk_communication (sprk_document.sprk_relatedcommunication ->
                    sprk_communication) and sprk_document_ParentDocument (sprk_document.sprk_parentdocument ->
                    sprk_document): Delete = Restrict. Every OTHER cascade value (Archive included) is left exactly as it
                    is and reported; -Verify fails if Assign, Share, Unshare, Reparent or Merge is anything but NoCascade.
      (b) XML       the same two relationships read Restrict in the solution source
                    (src/dataverse/solutions/SpaarkeMaster/Other/Relationships/sprk_Communication.xml and sprk_Document.xml),
                    so a solution import cannot put RemoveLink back. Never written by this script.
      (c) SOLUTION  both relationships are in -SolutionUniqueName (SpaarkeCore), decided through
                    scripts/common/DataverseSolutionMembership.ps1.
      (d) PUBLISH   sprk_document, sprk_communication (-Apply only).

    Effect on deletes. A communication with at least one document linked through sprk_relatedcommunication, and a
    document with at least one child document, can no longer be deleted (by any identity, through any surface) until those
    documents are deleted or unlinked first. Rows already orphaned (their communication deleted before this ran) are NOT
    repaired here: their link is already empty.

    ⚠️ MANUAL GATE (main session). -Apply is NOT run by the implementer lane. After -Apply, run -Verify (exit 0).

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER SolutionUniqueName
    The unmanaged solution that carries the relationships. Default SpaarkeCore.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap): Restrict live AND in the XML. Cannot be combined with
    -Apply.

.PARAMETER SelfTest
    Offline: runs the XML reader against built-in samples (must-fire and must-not-fire) and exits. No environment needed.

.EXAMPLE
    .\Set-DocumentLinkDeleteRestrict.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: the current and intended cascade of each relationship; zero writes.

.EXAMPLE
    .\Set-DocumentLinkDeleteRestrict.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply

.EXAMPLE
    .\Set-DocumentLinkDeleteRestrict.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify

.EXAMPLE
    .\Set-DocumentLinkDeleteRestrict.ps1 -SelfTest

.NOTES
    unified-access-control-r2 task 188 slice D, decided by owner round 111 item 3
    (notes/session27-owner-decisions-and-research.md). Pattern: scripts/Set-DocumentAnalysisCascadeSchema.ps1.
    Auth: the operator's own az CLI identity (System Administrator in the environment). No secrets.
#>

[CmdletBinding(DefaultParameterSetName = 'Live')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Live')][string]$EnvironmentUrl,
    [Parameter(ParameterSetName = 'Live')][string]$SolutionUniqueName = 'SpaarkeCore',
    [Parameter(ParameterSetName = 'Live')][switch]$Apply,
    [Parameter(ParameterSetName = 'Live')][switch]$Verify,
    [Parameter(Mandatory, ParameterSetName = 'SelfTest')][switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

# ── Constants ───────────────────────────────────────────────────────────────────────────────────────────────
$Relationships = @(
    [pscustomobject]@{ SchemaName = 'sprk_document_RelatedCommunication_sprk_communication'; Referenced = 'sprk_communication'; Referencing = 'sprk_document'; Attribute = 'sprk_relatedcommunication'; XmlFile = 'sprk_Communication.xml' }
    [pscustomobject]@{ SchemaName = 'sprk_document_ParentDocument'; Referenced = 'sprk_document'; Referencing = 'sprk_document'; Attribute = 'sprk_parentdocument'; XmlFile = 'sprk_Document.xml' }
)
$TargetDelete = 'Restrict'
# Cascades this script must never introduce (ownership/sharing propagation is not part of round 111 item 3).
$MustStayNoCascade = @('Assign', 'Share', 'Unshare', 'Reparent', 'Merge')
$XmlDir = Join-Path $PSScriptRoot '../src/dataverse/solutions/SpaarkeMaster/Other/Relationships'

# The CascadeDelete of one relationship in a solution relationships file; $null when the relationship is not in it.
function Get-XmlCascadeDelete([string]$XmlText, [string]$RelationshipName) {
    [xml]$doc = $XmlText
    $node = @($doc.EntityRelationships.EntityRelationship | Where-Object { $_.Name -eq $RelationshipName }) | Select-Object -First 1
    if (-not $node) { return $null }
    return [string]$node.CascadeDelete
}

if ($SelfTest) {
    $sample = {
        param([string]$Name, [string]$Delete)
        "<?xml version=`"1.0`" encoding=`"utf-8`"?><EntityRelationships xmlns:xsi=`"http://www.w3.org/2001/XMLSchema-instance`">" +
        "<EntityRelationship Name=`"other_rel`"><CascadeDelete>RemoveLink</CascadeDelete></EntityRelationship>" +
        "<EntityRelationship Name=`"$Name`"><CascadeAssign>NoCascade</CascadeAssign><CascadeDelete>$Delete</CascadeDelete></EntityRelationship>" +
        "</EntityRelationships>"
    }
    $cases = @(
        @{ Name = 'must-not-fire: Restrict reads Restrict'; Got = (Get-XmlCascadeDelete (& $sample 'sprk_document_ParentDocument' 'Restrict') 'sprk_document_ParentDocument'); Want = 'Restrict' }
        @{ Name = 'must-fire: RemoveLink reads RemoveLink'; Got = (Get-XmlCascadeDelete (& $sample 'sprk_document_ParentDocument' 'RemoveLink') 'sprk_document_ParentDocument'); Want = 'RemoveLink' }
        @{ Name = 'must-fire: another relationship''s value is not read'; Got = (Get-XmlCascadeDelete (& $sample 'x' 'Restrict') 'other_rel'); Want = 'RemoveLink' }
        @{ Name = 'must-fire: a missing relationship reads $null'; Got = ($null -eq (Get-XmlCascadeDelete (& $sample 'x' 'Restrict') 'sprk_document_ParentDocument')); Want = $true }
    )
    $failed = 0
    foreach ($c in $cases) {
        $ok = $c.Got -eq $c.Want
        if (-not $ok) { $failed++ }
        Write-Host ("  {0,-5} {1} (got '{2}', want '{3}')" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $c.Name, $c.Got, $c.Want) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
    }
    if ($failed -gt 0) { Write-Host "`nSELFTEST FAIL ($failed of $($cases.Count))" -ForegroundColor Red; exit 1 }
    Write-Host "`nSELFTEST PASS ($($cases.Count) checks)" -ForegroundColor Green
    exit 0
}

$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$Api = "$EnvironmentUrl/api/data/v9.2"

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

# ── (a) RESTRICT ────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(a) Delete cascade (live)"
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
    Report 'INFO' ("{0} now: Delete={1}, Archive={2}, Assign={3}, Share={4}, Unshare={5}, Reparent={6}, Merge={7}; managed={8}, customizable={9}" -f `
        $r.SchemaName, $cfg.Delete, $cfg.Archive, $cfg.Assign, $cfg.Share, $cfg.Unshare, $cfg.Reparent, $cfg.Merge, $rel.IsManaged, $rel.IsCustomizable.Value)

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

# ── (b) XML ─────────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Delete cascade (solution source)"
foreach ($r in $Relationships) {
    $path = Join-Path $XmlDir $r.XmlFile
    if (-not (Test-Path $path)) { Report 'FAIL' "$($r.XmlFile) not found at $path"; continue }
    $xmlDelete = Get-XmlCascadeDelete (Get-Content -Raw -Path $path) $r.SchemaName
    if ($null -eq $xmlDelete) { Report 'FAIL' "$($r.SchemaName) not found in $($r.XmlFile)"; continue }
    if ($xmlDelete -eq $TargetDelete) { Report 'OK' "$($r.XmlFile) $($r.SchemaName) CascadeDelete = $TargetDelete"; continue }
    # The XML is source, changed by a commit, never by this script: a gap here fails every mode's summary.
    Report 'FAIL' "$($r.XmlFile) $($r.SchemaName) CascadeDelete = $TargetDelete (is $xmlDelete; a solution import would put it back)"
}

# ── (c) SOLUTION ────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(c) Solution components"
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

# ── (d) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
    $tables = $Relationships | ForEach-Object { $_.Referenced; $_.Referencing } | Select-Object -Unique
    $entities = ($tables | ForEach-Object { "<entity>$_</entity>" }) -join ''
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities>$entities</entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $($tables -join ', ')." -ForegroundColor Green
}

if ($Verify) {
    if ($gaps.Count -eq 0) {
        Write-Host "`nVERIFY PASS: deleting a communication or a parent document is refused while a document links to it; the link is kept (live and in the XML)." -ForegroundColor Green
        exit 0
    }
    Write-Host "`nVERIFY FAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
if ($gaps.Count -gt 0) {
    Write-Host "`n$($gaps.Count) gap(s) this mode cannot close:" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
if ($IsDryRun) { Write-Host "`nDRY RUN complete — nothing was written. Re-run with -Apply (main-session manual gate)." -ForegroundColor Cyan }
