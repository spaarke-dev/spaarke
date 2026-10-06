#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Creates the two columns of a document file's relocation record (unified-access-control-r2 task 166):
      sprk_document.sprk_relocationpending — the relocation LEDGER (f1-v1, owner rounds 37 and 45): what a move still owes
                                             for the old item, and the old item's witness;
      sprk_document.sprk_relocatedversions — the relocation's VERSION RECORD (owner round 45 item 1): the original author,
                                             date and size of every version a move replayed into the copy.
    Both are created FIELD-SECURED (no window in which a user could write them). Dry run by default; -Apply writes;
    -Verify checks. Idempotent.

.DESCRIPTION
    WHY THE LEDGER. DocumentContainerRelocator (the legacy migration and every Make Secure move) copies a document's file
    into its derived container, re-points the row at the copy, and then still owes three things for the OLD item:
    re-keying the rows that hold the old item id for this document (a child attachment's sprk_parentgraphitemid, its
    communication attachment row), re-indexing (the new item indexed, the old item's chunks removed) and deleting the
    source once no row uses it. Any of them can fail after the re-point, and a source can be KEPT because a row of another
    record still uses it. Owner round 37 / finding F2: "a repeat call must recognise a row that is already re-pointed and
    settle it". The row itself must therefore say what is still owed — the ledger, written by the BFF in the SAME update as
    the re-point and cleared when everything is settled. Each entry also carries the source's WITNESS (its size,
    quickXorHash and version when the copy was verified, owner round 45 item 4): a later call deletes the source only if it
    still matches the witness, and a source edited after the move is re-copied by the relocator itself.

    WHY THE VERSION RECORD. A move replays the source's version history into the copy through the BFF identity (round 45
    item 1). Graph cannot set a version's author or date, so the record keeps, against each NEW version id, the ORIGINAL
    author, date and size; GET /api/documents/{id}/versions and the external version list report them. It is permanent
    (the ledger is cleared when settled) and as long as the history, so it is its own column.

    WHAT THEY HOLD. Ledger JSON: { "v": 1, "entries": [ { "sourceDrive", "sourceItem", "source": pending |
    keptForOtherRecords | removed | delegated, "rekeyPending", "indexed": none | own | all, "at", "witness": { "size",
    "quickXorHash", "version" } } ] }. Version record JSON: { "v": 1, "item", "versions": [ { "id", "by", "byUser", "byApp",
    "at", "size", "fromItem", "fromVersion" } ] }.

    WHY SECURED FROM BIRTH. NOTHING but the BFF may write either column: a hand-written ledger entry would name a source
    for the BFF to delete or copy, and a hand-written version record would rewrite who wrote a document's history. Creating
    them secured leaves no window between this script and the field-security grants (gate 23,
    scripts/Set-DocumentPointerFieldSecurity.ps1 -Target DocumentPointers, which finds them already secured and grants
    the two task-133 profiles). Until gate 23 runs, a BFF application user that does not hold System Administrator cannot
    write them, so every relocation fails closed (nothing is moved, reported Failed, retried) — run gate 23 right after
    this one.

    ⚠️ DEPLOY ORDER. The BFF build of task 166 f1-v1 / f1-v2 READS both columns on every relocation. Until they exist every
    relocation fails closed — so run -Apply, then gate 23 (field security), BEFORE the migration's -Apply (task note
    §21.11 / §22.11 gate 23a) and before task 150's Make Secure ships. The pointer-attach route does not read them.

    STEPS (-Apply), per column: (a) create it (Multiple lines of text, not required, IsSecured = true) in
    -SolutionUniqueName — or, when it exists unsecured, secure it; (b) publish sprk_document. -Verify: each column exists,
    is Memo with at least its MaxLength, is field-secured, and travels with the solution (sprk_document is a root
    component with rootcomponentbehavior 0, or the attribute is a component of the solution).

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
    unified-access-control-r2 task 166 f1-v1 (ledger), f1-v2 (version record, witness, secured from birth). Auth: the
    operator's own az CLI identity (System Administrator in the environment). No secrets. Live runs = main-session
    manual gates (task note §22.11).
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
. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (the BFF reads these exact names: DocumentContainerRelocator.RelocationLedgerColumn and
#    RelocatedVersionHistory.Column) ──────────────────────────────────────────────────────────────────────────────
$Table = 'sprk_document'
$ColumnSpecs = @(
    @{
        Column      = 'sprk_relocationpending'
        SchemaName  = 'sprk_RelocationPending'
        MaxLength   = 4000
        Display     = 'Relocation Pending'
        Description = 'The relocation ledger: what a move of this document''s file between SharePoint Embedded containers still owes for the OLD item (re-key, index, source delete), with the old item''s witness. Written ONLY by the BFF in the same update as the re-point and cleared when settled; field-secured. Do not edit by hand: an unreadable value stops the document''s file from being moved again until an administrator repairs it. unified-access-control-r2 task 166, owner rounds 37 and 45.'
    },
    @{
        Column      = 'sprk_relocatedversions'
        SchemaName  = 'sprk_RelocatedVersions'
        MaxLength   = 1048576
        Display     = 'Relocated Versions'
        Description = 'The relocation''s version record: the ORIGINAL author, date and size of every version a move replayed into this document''s file (Graph cannot set them), against the new version id. Written ONLY by the BFF in the same update as the re-point; the version history reports it; field-secured. Do not edit by hand. unified-access-control-r2 task 166, owner round 45 item 1.'
    }
)

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
$entityMeta = Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId"
# Solution membership through the ONE shared helper (scripts/common/DataverseSolutionMembership.ps1): a column of a table
# added with rootcomponentbehavior 0 has no row of its own, and the solution's components are read across every page.
$membership = if ($solution) {
    Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
} else { $null }

foreach ($spec in $ColumnSpecs) {
    $Column = $spec.Column
    $MaxLength = $spec.MaxLength
    # ── (a) the column, field-secured ───────────────────────────────────────────────────────────────────────────
    Write-Host "`n(a) $Table.$Column"
    $attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')"
    $typedPath = "$attrPath/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=LogicalName,MaxLength,MetadataId,IsSecured"
    $attr = Try-DvGet $typedPath
    $anyAttr = if ($attr) { $attr } else { Try-DvGet "$attrPath`?`$select=LogicalName,AttributeType" }
    if ($attr) {
        if ($attr.MaxLength -ge $MaxLength) { Report 'OK' "$Table.$Column exists (Multiple lines of text, MaxLength $($attr.MaxLength))" }
        else { Report 'FAIL' "$Table.$Column has MaxLength $($attr.MaxLength); at least $MaxLength is required" }
        if ($attr.IsSecured) { Report 'OK' "$Table.$Column is field-secured" }
        elseif ($Verify) { Report 'FAIL' "$Table.$Column is NOT field-secured — a user with Write on a document could write it" }
        elseif ($IsDryRun) { Report 'WOULD' "secure $Table.$Column (IsSecured = true; gate 23 grants the BFF-managed profiles)" }
        else {
            $typed = Invoke-DvGet $attrPath
            $typed.IsSecured = $true
            Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
            Report 'DONE' "secured $Table.$Column (gate 23 grants the BFF-managed profiles)"
        }
    } elseif ($anyAttr) {
        Report 'FAIL' "$Table.$Column exists but is a $($anyAttr.AttributeType), not Multiple lines of text — an administrator must resolve it"
    } elseif ($Verify) { Report 'MISSING' "$Table.$Column — every relocation fails closed until it exists" }
    elseif ($IsDryRun) { Report 'WOULD' "create $Table.$Column (Multiple lines of text, $MaxLength, not required, field-secured) in $SolutionUniqueName" }
    elseif (-not $solution) { Report 'FAIL' "not created: solution '$SolutionUniqueName' not found" }
    else {
        Invoke-DvWrite POST "EntityDefinitions(LogicalName='$Table')/Attributes" @{
            '@odata.type' = 'Microsoft.Dynamics.CRM.MemoAttributeMetadata'
            SchemaName    = $spec.SchemaName
            DisplayName   = (New-Label $spec.Display)
            Description   = (New-Label $spec.Description)
            RequiredLevel = @{ Value = 'None' }
            MaxLength     = $MaxLength
            Format        = 'TextArea'
            IsSecured     = $true
        } @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName } | Out-Null
        Report 'DONE' "created $Table.$Column, field-secured, in $SolutionUniqueName"
        $attr = Wait-DvRead $typedPath "$Table.$Column metadata"
        # The data endpoint can lag the metadata endpoint; the BFF selects the column on every relocation.
        Wait-DvRead "sprk_documents?`$select=sprk_documentid,$Column&`$top=1" "$Table.$Column in data queries" | Out-Null
    }

    # ── travels with the solution ───────────────────────────────────────────────────────────────────────────────
    if ($solution -and $attr) {
        $how = Test-DvInSolution -Membership $membership -ComponentId $attr.MetadataId -TableMetadataId $entityMeta.MetadataId
        if ($how -eq 'ViaTable') { Report 'OK' "$Table is a root component of $SolutionUniqueName with rootcomponentbehavior 0 — the column travels" }
        elseif ($how -eq 'Direct') { Report 'OK' "$Table.$Column is a component of $SolutionUniqueName" }
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
}

# ── (b) publish ──────────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $Table." -ForegroundColor Green
}

if ($Verify -or $Apply) {
    if ($gaps.Count -eq 0) {
        Write-Host ("`nPASS: {0} exist, are field-secured and travel with {1}. Next: gate 23 (Set-DocumentPointerFieldSecurity.ps1) grants the BFF-managed profiles." -f
            (($ColumnSpecs | ForEach-Object { "$Table.$($_.Column)" }) -join ' and '), $SolutionUniqueName) -ForegroundColor Green
        exit 0
    }
    Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "`nDRY RUN complete — nothing was written." -ForegroundColor Cyan
