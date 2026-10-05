#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Creates sprk_document.sprk_relocationpending — the relocation ledger of unified-access-control-r2 task 166 f1-v1
    (owner round 37). Dry run by default; -Apply writes; -Verify checks. Idempotent.

.DESCRIPTION
    WHY. DocumentContainerRelocator (the legacy migration and every Make Secure move) copies a document's file into its
    derived container, re-points the row at the copy, and then still owes three things for the OLD item: re-keying the
    rows that hold the old item id for this document (a child attachment's sprk_parentgraphitemid, its communication
    attachment row), re-indexing (the new item indexed, the old item's chunks removed) and deleting the source once no row
    uses it. Any of them can fail after the re-point (a Graph delete fault, an index enqueue fault), and a source can be
    KEPT because a row of another record still uses it. Owner round 37 / finding F2: "a repeat call must recognise a row
    that is already re-pointed and settle it". The row itself must therefore say what is still owed — this column, written
    by the BFF in the SAME update as the re-point (so no crash can leave a re-pointed row that forgot its debt), and
    cleared when everything is settled.

    WHAT IT HOLDS. JSON: { "v": 1, "entries": [ { "sourceDrive", "sourceItem", "source": pending | keptForOtherRecords |
    removed | delegated, "rekeyPending", "indexed": none | own | all, "at" } ] }. NOTHING but the BFF writes it: the
    column is field-secured by scripts/Set-DocumentPointerFieldSecurity.ps1 (-Target DocumentPointers covers it with the
    pointer columns). A ledger entry can never be used to delete an unrelated file: on a repeat call the BFF deletes a
    source only when it is byte-identical (size and quickXorHash) to the document's current file and no row uses it.

    ⚠️ DEPLOY ORDER. The BFF build of task 166 f1-v1 READS this column on every relocation. Until it exists every
    relocation fails closed (nothing is moved, reported Failed) — so run -Apply BEFORE the migration's -Apply (task note
    §21 gate 23b) and before task 150's Make Secure ships. The pointer-attach route does not read it.

    STEPS (-Apply): (a) create the column (Multiple lines of text, 4000, not required) in -SolutionUniqueName;
    (b) publish sprk_document. -Verify: the column exists, is Memo with MaxLength >= 4000, and travels with the solution
    (sprk_document is a root component with rootcomponentbehavior 0, or the attribute is a component of the solution).

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER SolutionUniqueName
    The unmanaged solution that carries sprk_document. Default SpaarkeCore.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-DocumentRelocationSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    .\Set-DocumentRelocationSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply

.EXAMPLE
    .\Set-DocumentRelocationSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify

.NOTES
    unified-access-control-r2 task 166 f1-v1. Auth: the operator's own az CLI identity (System Administrator in the
    environment). No secrets. Live runs = main-session manual gates (task note §21).
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

# ── Constants (the BFF reads this exact name: DocumentContainerRelocator.RelocationLedgerColumn) ────────────────────
$Table = 'sprk_document'
$Column = 'sprk_relocationpending'
$SchemaName = 'sprk_RelocationPending'
$MaxLength = 4000

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
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json
}
function Try-DvGet([string]$Path) { try { Invoke-DvGet $Path } catch { $null } }
# Metadata writes propagate asynchronously (task 141's live run): poll until readable, never treat "not visible yet" as absent.
function Wait-DvRead([string]$Path, [string]$What, [int]$Attempts = 12, [int]$DelaySeconds = 10) {
    for ($i = 1; $i -le $Attempts; $i++) {
        $r = Try-DvGet $Path
        if ($r) { return $r }
        Write-Host "    waiting for $What to become readable ($i/$Attempts)..."
        Start-Sleep -Seconds $DelaySeconds
    }
    throw "$What was created but is still not readable after $($Attempts * $DelaySeconds)s; re-run the script (it is idempotent)."
}
function New-Label([string]$Text) {
    @{ '@odata.type' = 'Microsoft.Dynamics.CRM.Label'; LocalizedLabels = @(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.LocalizedLabel'; Label = $Text; LanguageCode = 1033 }) }
}

$IsDryRun = -not $Apply.IsPresent
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

$solution = @((Invoke-DvGet "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "solution '$SolutionUniqueName' not found"; }

# ── (a) the column ──────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(a) $Table.$Column"
$attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')"
$attr = Try-DvGet "$attrPath/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=LogicalName,MaxLength,MetadataId,IsSecured"
$anyAttr = if ($attr) { $attr } else { Try-DvGet "$attrPath`?`$select=LogicalName,AttributeType" }
if ($attr) {
    if ($attr.MaxLength -ge $MaxLength) { Report 'OK' "$Table.$Column exists (Multiple lines of text, MaxLength $($attr.MaxLength))" }
    else { Report 'FAIL' "$Table.$Column has MaxLength $($attr.MaxLength); at least $MaxLength is required (a ledger of several entries)" }
    if ($attr.IsSecured) { Report 'OK' "$Table.$Column is field-secured (Set-DocumentPointerFieldSecurity.ps1)" }
    else { Report 'INFO' "$Table.$Column is not field-secured yet — Set-DocumentPointerFieldSecurity.ps1 -Apply secures it with the pointer columns (gate 23)" }
} elseif ($anyAttr) {
    Report 'FAIL' "$Table.$Column exists but is a $($anyAttr.AttributeType), not Multiple lines of text — an administrator must resolve it"
} elseif ($Verify) { Report 'MISSING' "$Table.$Column — every relocation fails closed until it exists" }
elseif ($IsDryRun) { Report 'WOULD' "create $Table.$Column (Multiple lines of text, $MaxLength, not required) in $SolutionUniqueName" }
elseif (-not $solution) { Report 'FAIL' "not created: solution '$SolutionUniqueName' not found" }
else {
    Invoke-DvWrite POST "EntityDefinitions(LogicalName='$Table')/Attributes" @{
        '@odata.type' = 'Microsoft.Dynamics.CRM.MemoAttributeMetadata'
        SchemaName    = $SchemaName
        DisplayName   = (New-Label 'Relocation Pending')
        Description   = (New-Label ('The relocation ledger: what a move of this document''s file between SharePoint Embedded containers still owes for the OLD item (re-key, index, source delete). Written ONLY by the BFF in the same update as the re-point and cleared when settled; field-secured. Do not edit by hand: an unreadable value stops the document''s file from being moved again until an administrator repairs it. unified-access-control-r2 task 166 f1-v1, owner round 37.'))
        RequiredLevel = @{ Value = 'None' }
        MaxLength     = $MaxLength
        Format        = 'TextArea'
    } @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName } | Out-Null
    Report 'DONE' "created $Table.$Column in $SolutionUniqueName"
    $attr = Wait-DvRead "$attrPath/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=LogicalName,MaxLength,MetadataId,IsSecured" "$Table.$Column metadata"
    # The data endpoint can lag the metadata endpoint; the BFF selects the column on every relocation.
    Wait-DvRead "sprk_documents?`$select=sprk_documentid,$Column&`$top=1" "$Table.$Column in data queries" | Out-Null
}

# ── travels with the solution ───────────────────────────────────────────────────────────────────────────────────
if ($solution -and $attr) {
    $entityMeta = Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId"
    $root = @((Invoke-DvGet ("solutioncomponents?`$select=rootcomponentbehavior&`$filter=_solutionid_value eq $($solution.solutionid) " +
        "and componenttype eq 1 and objectid eq $($entityMeta.MetadataId)")).value) | Select-Object -First 1
    $asComponent = @((Invoke-DvGet ("solutioncomponents?`$select=solutioncomponentid&`$filter=_solutionid_value eq $($solution.solutionid) " +
        "and componenttype eq 2 and objectid eq $($attr.MetadataId)")).value) | Select-Object -First 1
    if ($root -and $root.rootcomponentbehavior -eq 0) { Report 'OK' "$Table is a root component of $SolutionUniqueName with rootcomponentbehavior 0 — the column travels" }
    elseif ($asComponent) { Report 'OK' "$Table.$Column is a component of $SolutionUniqueName" }
    elseif ($Verify) { Report 'FAIL' "$Table.$Column does not travel with $SolutionUniqueName" }
    elseif ($IsDryRun) { Report 'WOULD' "add $Table.$Column to $SolutionUniqueName" }
    else {
        Invoke-DvWrite POST 'AddSolutionComponent' @{
            ComponentId = $attr.MetadataId; ComponentType = 2; SolutionUniqueName = $SolutionUniqueName
            AddRequiredComponents = $false; DoNotIncludeSubcomponents = $false
        } | Out-Null
        Report 'DONE' "added $Table.$Column to $SolutionUniqueName"
    }
}

# ── (b) publish ──────────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $Table." -ForegroundColor Green
}

if ($Verify -or $Apply) {
    if ($gaps.Count -eq 0) { Write-Host "`nPASS: $Table.$Column exists and travels with $SolutionUniqueName; the BFF can record what a relocation owes." -ForegroundColor Green; exit 0 }
    Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "`nDRY RUN complete — nothing was written." -ForegroundColor Cyan
