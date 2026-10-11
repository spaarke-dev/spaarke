#!/usr/bin/env pwsh
<#
.SYNOPSIS
    spaarke-SPA-external-access-platform-r3 task 020 - dev schema. Adds the server-stamped "Created By (Contact)" column
    sprk_createdbycontact (a lookup to contact) to sprk_document, sprk_todo, sprk_event and sprk_communication, locks it
    with field-level security so ONLY the BFF can write it, and adds the two Legal Front Door intake columns to
    sprk_servicerequest: sprk_requesttype (local choice) and sprk_intakedata (memo, field-secured). Dry run by default;
    -Apply writes; -Verify checks (read-only); -Rollback removes the six columns. Idempotent. The contact twin of
    scripts/Set-ChildRecordCreatorPersonSchema.ps1 (unified-access-control-r2 task 146).

.DESCRIPTION
    WHY. The external SPA creates records as the BFF application user, so createdby is the app, not the person. R3 stamps
    the CONTACT who asked in sprk_createdbycontact (never from client input) so the delete-own check (task 032) and the
    message sender stamp (task 034) can compare it with the caller's contact id. sprk_createdbyperson is the systemuser
    twin. Service requests do NOT get this column: they stamp the creator in the existing sprk_requestedby. The intake
    columns hold the submitter's answers (FR-05). Owner decision 2026-10-11: sprk_intakedata follows the
    sprk_createdbyperson settings, i.e. it is field-secured with the same two profiles.

    The script provisions, in order:
      (a) COLUMNS    sprk_createdbycontact (schema sprk_CreatedByContact, navigation property sprk_CreatedByContact) on each
                     of the four tables: a lookup to contact through a 1:N relationship sprk_contact_<table>_createdbycontact
                     whose cascade is NoCascade for Assign/Share/Unshare/Reparent/Merge and RemoveLink for Delete (and for
                     Archive, when the environment's Web API exposes that cascade).
                     sprk_servicerequest.sprk_requesttype: local choice, 100000000 NDA Assessment, 100000001 Invention
                     Submission, 100000002 Policy Question, 100000003 Trademark Search.
                     sprk_servicerequest.sprk_intakedata: memo, MaxLength 1,048,576, format Text.
                     Each create is get-before-create. Existing and identical = OK, no write. Existing and different =
                     reported FAIL (drift) and NOTHING about that column is changed (not secured, not granted).
      (b) PROFILES   The SAME two profiles tasks 133/146 created: "Spaarke BFF-Managed Field Readers" (Read; every business
                     unit's default team) and "Spaarke BFF-Managed Field Writers" (Read + Create + Update; the BFF
                     application user(s) ONLY). Created here only when absent. Members are associated BEFORE the columns
                     are secured, so the BFF never loses access. Any OTHER member of the writer profile is FAIL in every
                     mode (the profile's membership IS the lock). Reported, never removed.
      (c) FLS        sprk_createdbycontact (x4) and sprk_intakedata become field-secured; the reader profile gets read=4,
                     the writer profile read=4 create=4 update=4. Any OTHER profile (besides System Administrator) that can
                     create or update a secured column is reported FAIL. sprk_requesttype is NOT secured.
      (d) SOLUTION   The columns, relationships and both profiles are in -SolutionUniqueName. A column or relationship counts
                     as included when its table is in the solution with rootcomponentbehavior = 0.
      (e) PUBLISH    the five tables.

    CONSEQUENCE of securing sprk_intakedata: a user who is NOT in "Spaarke BFF-Managed Field Readers" (directly or through a
    team) cannot see intake answers in the model-driven app. The repo grants the reader profile to every business unit's
    default team (step b here; H7b in provisioning), so a user in a business unit whose default team is not yet in the
    profile loses the column. A business unit created later needs its default team added (re-run -Apply).

    DEPLOY ORDER. A BFF build that writes these columns fails its creates until -Apply has run (Dataverse refuses a
    column that does not exist). Apply this script - and see -Verify exit 0 - before deploying such a BFF.

    ROLLBACK. -Rollback lists what it would delete (read-only) and deletes NOTHING unless -ConfirmRollback is also given.
    It deletes the six columns (Dataverse drops the relationships, the local option set and the field-permission rows with
    them; any leftover relationship or field-permission row is removed too), then publishes. It refuses a column that holds
    data unless -AcceptDataLoss is also given. The two profiles are shared with tasks 133/146 and are never deleted.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids whose Dataverse application users must be members of the writer profile - the BFF managed
    identity (dev: 5967251e-171c-46fe-a6c2-ef843c90309d) and, while it is still an application user, the BFF app
    registration (dev: 1e40baad-e065-4aea-a8d4-4b7ab273458c). Required for -Apply and -Verify.

.PARAMETER SolutionUniqueName
    The unmanaged solution that carries the components. Default SpaarkeCore.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply or -Rollback.

.PARAMETER Rollback
    Rollback mode. Read-only listing unless -ConfirmRollback is also given. Cannot be combined with -Apply or -Verify.

.PARAMETER ConfirmRollback
    With -Rollback: actually delete the six columns. Without it nothing is deleted.

.PARAMETER AcceptDataLoss
    With -Rollback -ConfirmRollback: allow deleting a column that holds data (or whose row count could not be read).

.EXAMPLE
    .\Set-ChildRecordCreatorContactSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
    Dry run: every step's present / missing / drift state; zero writes.

.EXAMPLE
    .\Set-ChildRecordCreatorContactSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Apply

.EXAMPLE
    .\Set-ChildRecordCreatorContactSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify

.EXAMPLE
    .\Set-ChildRecordCreatorContactSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Rollback
    Lists what a rollback would delete. Deletes nothing.

.EXAMPLE
    .\Set-ChildRecordCreatorContactSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Rollback -ConfirmRollback

.NOTES
    Run it inside PowerShell (`& ./scripts/Set-ChildRecordCreatorContactSchema.ps1 ...`): `pwsh -File` passes the comma
    list as ONE string (the script splits it either way). Auth: the operator's own az CLI identity (System Administrator in
    the environment). No secrets; the access token is never printed. Notes: projects/spaarke-SPA-external-access-platform-r3/notes/task-020-notes.md.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [string[]]$BffApplicationIds = @(),
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$Apply,
    [switch]$Verify,
    [switch]$Rollback,
    [switch]$ConfirmRollback,
    [switch]$AcceptDataLoss
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if (@($Apply.IsPresent, $Verify.IsPresent, $Rollback.IsPresent | Where-Object { $_ }).Count -gt 1) { throw '-Apply, -Verify and -Rollback are separate modes; run one at a time.' }
if (($ConfirmRollback -or $AcceptDataLoss) -and -not $Rollback) { throw '-ConfirmRollback and -AcceptDataLoss are only meaningful with -Rollback.' }
if ($AcceptDataLoss -and -not $ConfirmRollback) { throw '-AcceptDataLoss needs -ConfirmRollback.' }
if (($Apply -or $Verify) -and $BffApplicationIds.Count -eq 0) {
    throw '-BffApplicationIds is required for -Apply and -Verify: the writer profile must name the BFF application user(s) explicitly.'
}
# A single comma-joined string (pwsh -File) is split, never sent as one id.
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$Api = "$EnvironmentUrl/api/data/v9.2"

# -- Constants ------------------------------------------------------------------------------------------------------
$LookupColumn = 'sprk_createdbycontact'
$LookupSchemaName = 'sprk_CreatedByContact'
$NavigationProperty = 'sprk_CreatedByContact'
$TargetEntity = 'contact'
$LookupTables = @('sprk_document', 'sprk_todo', 'sprk_event', 'sprk_communication')
$ServiceRequestTable = 'sprk_servicerequest'
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
$IntakeMaxLength = 1048576
$IntakeFormat = 'Text'
# A plain hashtable on purpose: an [ordered] dictionary indexed by an int is positional, not by key.
$RequestTypeOptions = @{ 100000000 = 'NDA Assessment'; 100000001 = 'Invention Submission'; 100000002 = 'Policy Question'; 100000003 = 'Trademark Search' }
$ExpectedCascadeBase = [ordered]@{ Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade'; Merge = 'NoCascade'; Delete = 'RemoveLink' }
function RelationshipSchemaName([string]$Table) { "sprk_contact_$($Table)_createdbycontact" }

$LookupDescription = 'The CONTACT who created this record from the external SPA. Stamped by the BFF only (field-secured), from the resolved contact principal and never from client input; createdby is the BFF application user for a record the BFF creates app-only. The delete-own check reads this column for a contact caller (sprk_createdbyperson for a systemuser caller). spaarke-SPA-external-access-platform-r3 task 020, FR-25.'
$RequestTypeDescription = 'Which Legal Front Door intake wizard produced this request. Written by the BFF at intake submit; the Service Requests tab filters on it. spaarke-SPA-external-access-platform-r3 task 020, FR-05.'
$IntakeDescription = 'The wizard answers of the submitter as a JSON document (up to 1,048,576 characters). Written by the BFF at intake submit; read by the record detail view. Not a human summary - see Record Summary. spaarke-SPA-external-access-platform-r3 task 020, FR-05.'

# The six columns. Kind: Lookup / Picklist / Memo. Secured: field-level security applies (profiles + IsSecured).
$Specs = @(
    foreach ($t in $LookupTables) { @{ Table = $t; Column = $LookupColumn; Kind = 'Lookup'; Secured = $true } }
    @{ Table = $ServiceRequestTable; Column = 'sprk_requesttype'; Kind = 'Picklist'; Secured = $false }
    @{ Table = $ServiceRequestTable; Column = 'sprk_intakedata'; Kind = 'Memo'; Secured = $true }
)
$AllTables = @($Specs | ForEach-Object { $_.Table } | Select-Object -Unique)
$AttributeTypeOf = @{ Lookup = 'Lookup'; Picklist = 'Picklist'; Memo = 'Memo' }
function SpecKey($s) { "$($s.Table).$($s.Column)" }

# -- Auth + helpers -------------------------------------------------------------------------------------------------
$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
}
function Get-DvErrorText($ErrorRecord) {
    $detail = $ErrorRecord.ErrorDetails.Message
    if ($detail) { "$($ErrorRecord.Exception.Message) :: $detail" } else { "$($ErrorRecord.Exception.Message)" }
}
function Invoke-DvGet([string]$Path) { Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Get }
# Returns $null ONLY for a 404 (the thing does not exist); any other failure (401, 5xx, network) throws, so it is never
# mistaken for "absent".
function Try-DvGet([string]$Path) {
    try { Invoke-DvGet $Path }
    catch {
        $err = $_
        $code = $null
        try { $code = [int]$err.Exception.Response.StatusCode } catch { $code = $null }
        if ($code -eq 404) { return $null }
        throw $err
    }
}
# Any failure reads as "not there yet" - used only while polling for metadata that was just written.
function Try-DvGetAny([string]$Path) { try { Invoke-DvGet $Path } catch { $null } }
function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    try { Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json }
    catch { throw "$Method $Path failed: $(Get-DvErrorText $_)" }
}
# Counts every row a query matches, following @odata.nextLink past Dataverse's 5,000-row page.
function Measure-DvRows([string]$Path) {
    $h = $headers.Clone(); $h['Prefer'] = 'odata.maxpagesize=5000'
    $count = 0; $next = "$Api/$Path"
    while ($next) {
        $page = Invoke-RestMethod -Uri $next -Headers $h -Method Get
        $count += @($page.value).Count
        $next = $page.'@odata.nextLink'
    }
    $count
}
# Metadata writes propagate asynchronously: poll a read until it answers, or give up loudly - never treat "not visible
# yet" as "absent".
function Wait-DvRead([string]$Path, [string]$What, [int]$Attempts = 12, [int]$DelaySeconds = 10) {
    for ($i = 1; $i -le $Attempts; $i++) {
        $r = Try-DvGetAny $Path
        if ($r) { return $r }
        Write-Host "    waiting for $What to become readable ($i/$Attempts)..."
        Start-Sleep -Seconds $DelaySeconds
    }
    throw "$What was created but is still not readable after $($Attempts * $DelaySeconds)s; re-run the script (it is idempotent)."
}
function New-Label([string]$Text) {
    @{ '@odata.type' = 'Microsoft.Dynamics.CRM.Label'; LocalizedLabels = @(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.LocalizedLabel'; Label = $Text; LanguageCode = 1033 }) }
}

$IsDryRun = -not ($Apply.IsPresent -or $Rollback.IsPresent)
. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
$gaps = [System.Collections.Generic.List[string]]::new()
$fails = [System.Collections.Generic.List[string]]::new()
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -eq 'MISSING') { $gaps.Add($What) }
    if ($State -eq 'FAIL') { $gaps.Add($What); $fails.Add($What) }
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($Rollback) { $(if ($ConfirmRollback) { 'ROLLBACK (DELETES the six columns)' } else { 'ROLLBACK listing (read-only; add -ConfirmRollback to delete)' }) } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))
Write-Host "Solution    : $SolutionUniqueName"
Write-Host "Columns     : $(($Specs | ForEach-Object { SpecKey $_ }) -join ', ')"

# -- Attribute read helpers -----------------------------------------------------------------------------------------
function AttrBase($s) { "EntityDefinitions(LogicalName='$($s.Table)')/Attributes(LogicalName='$($s.Column)')" }
function TypedAttrPath($s) {
    switch ($s.Kind) {
        'Lookup' { "$(AttrBase $s)/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=LogicalName,Targets,IsSecured,MetadataId" }
        'Picklist' { "$(AttrBase $s)/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName,IsSecured,MetadataId&`$expand=OptionSet(`$select=Options,IsGlobal)" }
        'Memo' { "$(AttrBase $s)/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=LogicalName,IsSecured,MetadataId,MaxLength,Format" }
    }
}
# Returns @{ Exists; Attr; Problems } - Problems lists every way an existing column differs from what this script creates.
function Get-Column($s) {
    $any = Try-DvGet "$(AttrBase $s)?`$select=LogicalName,AttributeType,MetadataId,IsSecured"
    if (-not $any) { return @{ Exists = $false; Attr = $null; Problems = @() } }
    if ("$($any.AttributeType)" -ne $AttributeTypeOf[$s.Kind]) {
        return @{ Exists = $true; Attr = $null; Problems = @("is a $($any.AttributeType) column, expected $($AttributeTypeOf[$s.Kind])") }
    }
    $attr = Invoke-DvGet (TypedAttrPath $s)
    $problems = @()
    switch ($s.Kind) {
        'Lookup' {
            $targets = @($attr.Targets)
            if (-not ($targets.Count -eq 1 -and $targets[0] -eq $TargetEntity)) { $problems += "targets [$($targets -join ', ')], expected exactly $TargetEntity" }
        }
        'Picklist' {
            if ($attr.OptionSet.IsGlobal) { $problems += 'uses a GLOBAL option set, expected a local one' }
            $actual = @($attr.OptionSet.Options | Sort-Object Value | ForEach-Object {
                    $lbl = $_.Label.UserLocalizedLabel.Label; if (-not $lbl) { $lbl = @($_.Label.LocalizedLabels)[0].Label }
                    "$($_.Value)=$lbl" })
            $expected = @($RequestTypeOptions.Keys | Sort-Object | ForEach-Object { "$_=$($RequestTypeOptions[$_])" })
            if (($actual -join '|') -ne ($expected -join '|')) { $problems += "options [$($actual -join ', ')], expected [$($expected -join ', ')]" }
        }
        'Memo' {
            if ($attr.MaxLength -ne $IntakeMaxLength) { $problems += "MaxLength $($attr.MaxLength), expected $IntakeMaxLength" }
            if ("$($attr.Format)" -ne $IntakeFormat) { $problems += "Format $($attr.Format), expected $IntakeFormat" }
        }
    }
    @{ Exists = $true; Attr = $attr; Problems = $problems }
}
function Get-RelationshipPath([string]$Table) {
    "RelationshipDefinitions(SchemaName='$(RelationshipSchemaName $Table)')/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?`$select=SchemaName,ReferencedEntity,ReferencingEntity,ReferencingAttribute,ReferencingEntityNavigationPropertyName,CascadeConfiguration,MetadataId"
}
function Get-FieldPermissions([string]$Table, [string]$Column) {
    @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$Table' and attributelogicalname eq '$Column'").value)
}

# =====================================================================================================================
# ROLLBACK
# =====================================================================================================================
if ($Rollback) {
    Write-Host "`nRollback: the six columns (profiles are shared with tasks 133/146 and are never deleted)"
    foreach ($s in $Specs) {
        $key = SpecKey $s
        $col = Get-Column $s
        if (-not $col.Exists) { Report 'OK' "$key does not exist - nothing to delete"; continue }
        $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$($s.Table)')?`$select=EntitySetName").EntitySetName
        $rows = -1
        try {
            $filter = if ($s.Kind -eq 'Lookup') { "_$($s.Column)_value ne null" } else { "$($s.Column) ne null" }
            $rows = Measure-DvRows "$set`?`$select=$($s.Table)id&`$filter=$filter"
        } catch { $rows = -1 }
        $rowText = if ($rows -lt 0) { 'row count could not be read' } else { "$rows row(s) hold a value" }
        if (-not $ConfirmRollback) { Report 'WOULD' "delete $key ($rowText)$(if ($s.Kind -eq 'Lookup') { "; relationship $(RelationshipSchemaName $s.Table)" })"; continue }
        if ($rows -ne 0 -and -not $AcceptDataLoss) { Report 'FAIL' "refusing to delete ${key}: $rowText. Re-run with -AcceptDataLoss if that data may be lost."; continue }
        try {
            Invoke-DvWrite DELETE (AttrBase $s) $null | Out-Null
            Report 'DONE' "deleted $key ($rowText)"
        } catch { Report 'FAIL' "could not delete ${key}: $($_.Exception.Message)"; continue }
        if ($s.Kind -eq 'Lookup') {
            $rel = Try-DvGetAny (Get-RelationshipPath $s.Table)
            if ($rel) {
                Invoke-DvWrite DELETE "RelationshipDefinitions($($rel.MetadataId))" $null | Out-Null
                Report 'DONE' "deleted leftover relationship $(RelationshipSchemaName $s.Table)"
            } else { Report 'OK' "relationship $(RelationshipSchemaName $s.Table) is gone" }
        }
        foreach ($p in Get-FieldPermissions $s.Table $s.Column) {
            Invoke-DvWrite DELETE "fieldpermissions($($p.fieldpermissionid))" $null | Out-Null
            Report 'DONE' "deleted leftover field permission row for $key ($($p.fieldsecurityprofileid.name))"
        }
    }
    if ($ConfirmRollback) {
        $entities = ($AllTables | ForEach-Object { "<entity>$_</entity>" }) -join ''
        Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities>$entities</entities></importexportxml>" } | Out-Null
        Write-Host "`nPublished $($AllTables -join ', ')." -ForegroundColor Green
    } else {
        Write-Host "`nROLLBACK LISTING complete - nothing was deleted. Add -ConfirmRollback to delete." -ForegroundColor Cyan
    }
    if ($fails.Count -gt 0) { exit 1 }
    exit 0
}

# =====================================================================================================================
# (a) COLUMNS
# =====================================================================================================================
# Does this environment's Web API expose the Archive cascade? Read from an existing relationship; never assumed.
$probe = Try-DvGet "RelationshipDefinitions(SchemaName='sprk_systemuser_sprk_document_createdbyperson')/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?`$select=SchemaName,CascadeConfiguration"
$SupportsArchive = [bool]($probe -and ($probe.CascadeConfiguration.PSObject.Properties.Name -contains 'Archive'))
$ExpectedCascade = [ordered]@{}; foreach ($k in $ExpectedCascadeBase.Keys) { $ExpectedCascade[$k] = $ExpectedCascadeBase[$k] }
if ($SupportsArchive) { $ExpectedCascade['Archive'] = 'RemoveLink' }

Write-Host "`n(a) Columns"
if (-not $SupportsArchive) { Report 'INFO' 'this environment does not expose an Archive cascade on relationships; the source XML says CascadeArchive RemoveLink - confirm it at re-export' }
$attrs = @{}      # SpecKey -> attribute metadata, ONLY for columns that exist and match (a drifted column is never touched)
$blocked = @{}    # SpecKey -> $true when the column drifted
foreach ($s in $Specs) {
    $key = SpecKey $s
    $col = Get-Column $s
    if ($col.Exists) {
        if ($col.Problems.Count -gt 0) {
            Report 'FAIL' "DRIFT $key exists but $($col.Problems -join '; '). Nothing was changed for this column."
            $blocked[$key] = $true
        } else {
            Report 'OK' "$key ($($s.Kind) matches)"
            $attrs[$key] = $col.Attr
        }
    } elseif ($Verify) { Report 'MISSING' $key }
    elseif ($IsDryRun) { Report 'WOULD' "create $key ($($s.Kind)$(if ($s.Kind -eq 'Lookup') { " -> $TargetEntity, relationship $(RelationshipSchemaName $s.Table)" }))" }
    else {
        $solutionHeader = @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName }
        switch ($s.Kind) {
            'Lookup' {
                $cascade = @{ Assign = 'NoCascade'; Delete = 'RemoveLink'; Merge = 'NoCascade'; Reparent = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade' }
                if ($SupportsArchive) { $cascade['Archive'] = 'RemoveLink' }
                Invoke-DvWrite POST 'RelationshipDefinitions' @{
                    '@odata.type'        = 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata'
                    SchemaName           = (RelationshipSchemaName $s.Table)
                    ReferencedEntity     = $TargetEntity
                    ReferencingEntity    = $s.Table
                    ReferencingEntityNavigationPropertyName = $NavigationProperty
                    CascadeConfiguration = $cascade
                    AssociatedMenuConfiguration = @{ Behavior = 'DoNotDisplay'; Group = 'Details'; Order = 10000 }
                    Lookup               = @{
                        '@odata.type' = 'Microsoft.Dynamics.CRM.LookupAttributeMetadata'
                        SchemaName    = $LookupSchemaName
                        DisplayName   = (New-Label 'Created By (Contact)')
                        Description   = (New-Label $LookupDescription)
                        RequiredLevel = @{ Value = 'None' }
                    }
                } $solutionHeader | Out-Null
            }
            'Picklist' {
                $options = @($RequestTypeOptions.Keys | Sort-Object | ForEach-Object { @{ Value = [int]$_; Label = (New-Label $RequestTypeOptions[$_]) } })
                Invoke-DvWrite POST "EntityDefinitions(LogicalName='$($s.Table)')/Attributes" @{
                    '@odata.type' = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'
                    SchemaName    = 'sprk_RequestType'
                    DisplayName   = (New-Label 'Request Type')
                    Description   = (New-Label $RequestTypeDescription)
                    RequiredLevel = @{ Value = 'None' }
                    OptionSet     = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.OptionSetMetadata'; IsGlobal = $false; OptionSetType = 'Picklist'; Options = $options }
                } $solutionHeader | Out-Null
            }
            'Memo' {
                Invoke-DvWrite POST "EntityDefinitions(LogicalName='$($s.Table)')/Attributes" @{
                    '@odata.type' = 'Microsoft.Dynamics.CRM.MemoAttributeMetadata'
                    SchemaName    = 'sprk_IntakeData'
                    DisplayName   = (New-Label 'Intake Data')
                    Description   = (New-Label $IntakeDescription)
                    RequiredLevel = @{ Value = 'None' }
                    MaxLength     = $IntakeMaxLength
                    Format        = $IntakeFormat
                } $solutionHeader | Out-Null
            }
        }
        Report 'DONE' "created $key"
        $attrs[$key] = Wait-DvRead (TypedAttrPath $s) "$key metadata"
        $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$($s.Table)')?`$select=EntitySetName").EntitySetName
        $probeCol = if ($s.Kind -eq 'Lookup') { "_$($s.Column)_value" } else { $s.Column }
        Wait-DvRead "$set`?`$select=$probeCol&`$top=1" "$key in data queries" | Out-Null
    }

    if ($s.Kind -ne 'Lookup') { continue }
    # The relationship's cascade and navigation property: nothing about the CONTACT may move, share or reparent the record,
    # and a Web API write binds the name the BFF uses.
    $rel = Try-DvGet (Get-RelationshipPath $s.Table)
    # Relationship metadata is read through a cache: right after the create it can still answer 404 although the
    # relationship exists. Poll for up to 2 minutes before reporting it missing.
    for ($try = 1; -not $rel -and $attrs[$key] -and $try -le 24; $try++) {
        Start-Sleep -Seconds 5
        $rel = Try-DvGet (Get-RelationshipPath $s.Table)
    }
    if ($rel) {
        $bad = @($ExpectedCascade.Keys | Where-Object { $rel.CascadeConfiguration.$_ -ne $ExpectedCascade[$_] } |
            ForEach-Object { "$_=$($rel.CascadeConfiguration.$_) (expected $($ExpectedCascade[$_]))" })
        if ($rel.ReferencingAttribute -ne $LookupColumn) { Report 'FAIL' "DRIFT $(RelationshipSchemaName $s.Table) references $($rel.ReferencingAttribute), not $LookupColumn. Nothing was changed."; $blocked[$key] = $true; $attrs.Remove($key) }
        elseif ($rel.ReferencingEntityNavigationPropertyName -ne $NavigationProperty) { Report 'FAIL' "DRIFT $(RelationshipSchemaName $s.Table) navigation property is $($rel.ReferencingEntityNavigationPropertyName), not $NavigationProperty - the BFF's Web API binds would be refused. Nothing was changed."; $blocked[$key] = $true; $attrs.Remove($key) }
        elseif ($bad.Count -gt 0) { Report 'FAIL' "DRIFT $(RelationshipSchemaName $s.Table) cascade: $($bad -join '; '). Nothing was changed."; $blocked[$key] = $true; $attrs.Remove($key) }
        else { Report 'OK' "$(RelationshipSchemaName $s.Table) (navigation $NavigationProperty; no Assign/Share cascade; Delete RemoveLink)" }
    } elseif ($attrs[$key]) { Report 'FAIL' "$key exists but relationship $(RelationshipSchemaName $s.Table) was not found - it was created another way; check its cascade and navigation property by hand"; $blocked[$key] = $true; $attrs.Remove($key) }
    elseif ($Verify -and -not $blocked[$key]) { Report 'MISSING' "relationship $(RelationshipSchemaName $s.Table)" }
}

# =====================================================================================================================
# (b) PROFILES + MEMBERS
# =====================================================================================================================
Write-Host "`n(b) Field security profiles and members (shared with tasks 133 / 146)"
function Ensure-Profile([string]$Name, [string]$Description) {
    $p = @((Invoke-DvGet "fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$Name'").value) | Select-Object -First 1
    if ($p) { Report 'OK' "profile '$Name'"; return $p.fieldsecurityprofileid }
    if ($Verify) { Report 'MISSING' "profile '$Name'"; return $null }
    if ($IsDryRun) { Report 'WOULD' "create profile '$Name'"; return $null }
    $created = Invoke-DvWrite POST 'fieldsecurityprofiles' @{ name = $Name; description = $Description } @{ Prefer = 'return=representation' }
    Report 'DONE' "created profile '$Name'"
    return $created.fieldsecurityprofileid
}
$readerId = Ensure-Profile $ReaderProfileName 'Read on columns ONLY the BFF writes (sprk_createdbyperson, sprk_createdbycontact, sprk_servicerequest.sprk_intakedata, sprk_issecure). Associated with EVERY business unit default team, so every user keeps reading them - a secured column is HIDDEN from anyone without Read.'
$writerId = Ensure-Profile $WriterProfileName 'Read/Create/Update on columns ONLY the BFF writes (sprk_createdbyperson, sprk_createdbycontact, sprk_servicerequest.sprk_intakedata, sprk_issecure). Members: the BFF application user(s) ONLY.'

$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if (-not $u) { Report 'FAIL' "no application user for appId $appId in this environment"; continue }
    $bffUsers += $u
}
$defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name&$filter=isdefault eq true').value)
function Ensure-Member([string]$ProfileId, [string]$ProfileName, [string]$Nav, [string]$Set, $Id, [string]$Label) {
    if (-not $ProfileId) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "$ProfileName member: $Label"; return }
    $key = if ($Set -eq 'teams') { 'teamid' } else { 'systemuserid' }
    $existing = @((Invoke-DvGet "fieldsecurityprofiles($ProfileId)/$Nav`?`$select=$key").value)
    if ($existing | Where-Object { $_.$key -eq $Id }) { Report 'OK' "$ProfileName member: $Label"; return }
    if ($Verify) { Report 'MISSING' "$ProfileName member: $Label"; return }
    if ($IsDryRun) { Report 'WOULD' "add $Label to $ProfileName"; return }
    Invoke-DvWrite POST "fieldsecurityprofiles($ProfileId)/$Nav/`$ref" @{ '@odata.id' = "$Api/$Set($Id)" } | Out-Null
    Report 'DONE' "added $Label to $ProfileName"
}
foreach ($u in $bffUsers) { Ensure-Member $writerId $WriterProfileName 'systemuserprofiles_association' 'systemusers' $u.systemuserid "app user '$($u.fullname)'" }
foreach ($t in $defaultTeams) { Ensure-Member $readerId $ReaderProfileName 'teamprofiles_association' 'teams' $t.teamid "default team '$($t.name)'" }

# The writer profile's membership IS the lock: a human - or a team - added to it can name anyone as a record's creator.
# So every member that is not one of the -BffApplicationIds users is FAIL, in every mode.
if ($writerId) {
    $allowedWriters = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    $writerUsers = @((Invoke-DvGet "fieldsecurityprofiles($writerId)/systemuserprofiles_association?`$select=systemuserid,fullname,applicationid").value)
    foreach ($m in $writerUsers | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowedWriters }) {
        $kind = if ($m.applicationid) { "application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        Report 'FAIL' "$WriterProfileName member '$($m.fullname)' ($($m.systemuserid)) is $kind - only the BFF may write these columns"
    }
    $writerTeams = @((Invoke-DvGet "fieldsecurityprofiles($writerId)/teamprofiles_association?`$select=teamid,name").value)
    foreach ($tm in $writerTeams) {
        Report 'FAIL' "$WriterProfileName has team '$($tm.name)' ($($tm.teamid)) as a member - every member of it could write these columns"
    }
    if (($writerUsers | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowedWriters }).Count -eq 0 -and $writerTeams.Count -eq 0) {
        Report 'OK' "$WriterProfileName has no member besides the BFF application user(s)"
    }
}

# =====================================================================================================================
# (c) FIELD-LEVEL SECURITY (sprk_createdbycontact x4 and sprk_intakedata; NOT sprk_requesttype)
# =====================================================================================================================
Write-Host "`n(c) Field-level security"
foreach ($s in $Specs | Where-Object { $_.Secured }) {
    $key = SpecKey $s; $t = $s.Table; $Column = $s.Column
    if ($blocked[$key]) { Report 'INFO' "skipped securing $key (the column drifted; see (a))"; continue }
    if (-not $attrs[$key]) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "secure $key and grant both profiles (after the column exists)"; continue }

    if ($attrs[$key].IsSecured -eq $true) { Report 'OK' "$key is field-secured" }
    elseif ($Verify) { Report 'MISSING' "$key is NOT field-secured" }
    elseif ($IsDryRun) { Report 'WOULD' "secure $key" }
    else {
        $typed = Invoke-DvGet (AttrBase $s)
        $typed.IsSecured = $true
        Invoke-DvWrite PUT (AttrBase $s) $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        Report 'DONE' "secured $key"
    }

    foreach ($spec in @(@{ Id = $readerId; Name = $ReaderProfileName; Create = 0; Update = 0 }, @{ Id = $writerId; Name = $WriterProfileName; Create = 4; Update = 4 })) {
        if (-not $spec.Id) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "$($spec.Name) permission on $key"; continue }
        $perm = @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $($spec.Id) and entityname eq '$t' and attributelogicalname eq '$Column'").value) | Select-Object -First 1
        if ($perm) {
            if ($perm.canread -eq 4 -and $perm.cancreate -eq $spec.Create -and $perm.canupdate -eq $spec.Update) { Report 'OK' "$($spec.Name) on $key" }
            else { Report 'FAIL' "DRIFT $($spec.Name) on $key is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($spec.Create) update=$($spec.Update). Nothing was changed." }
            continue
        }
        if ($Verify) { Report 'MISSING' "$($spec.Name) on $key"; continue }
        if ($IsDryRun) { Report 'WOULD' "grant $($spec.Name) on $key (read=4 create=$($spec.Create) update=$($spec.Update))"; continue }
        # Securing a column propagates asynchronously: a grant right after it may be refused 0x8004f508 "... is NOT
        # secured for entity fieldpermission". Retry that one error; anything else throws.
        for ($attempt = 1; ; $attempt++) {
            try {
                Invoke-DvWrite POST 'fieldpermissions' @{
                    entityname = $t; attributelogicalname = $Column
                    canread = 4; cancreate = $spec.Create; canupdate = $spec.Update
                    'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($($spec.Id))"
                } | Out-Null
                break
            } catch {
                $notYetSecured = "$($_.Exception.Message)" -match '0x8004f508'
                if (-not $notYetSecured -or $attempt -ge 12) { throw }
                Write-Host "    $key not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
                Start-Sleep -Seconds 10
            }
        }
        Report 'DONE' "granted $($spec.Name) on $key"
    }

    $writers = @((Invoke-DvGet "fieldpermissions?`$select=canupdate,cancreate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$t' and attributelogicalname eq '$Column' and (canupdate eq 4 or cancreate eq 4)").value)
    foreach ($w in $writers | Where-Object { $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' }) {
        Report 'FAIL' "profile '$($w.fieldsecurityprofileid.name)' can also write $key - only the BFF may"
    }
}

# =====================================================================================================================
# (d) SOLUTION
# =====================================================================================================================
Write-Host "`n(d) Solution components"
$solution = @((Invoke-DvGet "solutions?`$select=solutionid,uniquename&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "solution '$SolutionUniqueName' not found" }
else {
    # Paged, and a table in the solution with rootcomponentbehavior = 0 ("include subcomponents") carries its columns
    # and relationships implicitly (scripts/common/DataverseSolutionMembership.ps1).
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
    $tableIds = @{}
    foreach ($t in $AllTables) { $tableIds[$t] = (Invoke-DvGet "EntityDefinitions(LogicalName='$t')?`$select=MetadataId").MetadataId }

    $components = [System.Collections.Generic.List[object]]::new()
    foreach ($s in $Specs) {
        $key = SpecKey $s
        if ($attrs[$key]) { $components.Add(@{ Id = $attrs[$key].MetadataId; Type = 2; Label = $key; TableId = $tableIds[$s.Table] }) }
        if ($s.Kind -eq 'Lookup') {
            $rel = Try-DvGet "RelationshipDefinitions(SchemaName='$(RelationshipSchemaName $s.Table)')?`$select=MetadataId"
            if ($rel) { $components.Add(@{ Id = $rel.MetadataId; Type = 10; Label = "relationship $(RelationshipSchemaName $s.Table)"; TableId = $tableIds[$s.Table] }) }
        }
    }
    foreach ($profileId in $readerId, $writerId) { if ($profileId) { $components.Add(@{ Id = $profileId; Type = 70; Label = "field security profile $profileId" }) } }

    foreach ($c in $components) {
        $how = Test-DvInSolution -Membership $membership -ComponentId $c.Id -TableMetadataId $c['TableId']
        if ($how -eq 'Direct') { Report 'OK' "$($c.Label) in $SolutionUniqueName"; continue }
        if ($how -eq 'ViaTable') { Report 'OK' "$($c.Label) in $SolutionUniqueName (its table includes subcomponents)"; continue }
        if ($Verify) { Report 'MISSING' "$($c.Label) in $SolutionUniqueName"; continue }
        if ($IsDryRun) { Report 'WOULD' "add $($c.Label) to $SolutionUniqueName"; continue }
        Invoke-DvWrite POST 'AddSolutionComponent' @{ ComponentId = $c.Id; ComponentType = $c.Type; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false } | Out-Null
        Report 'DONE' "added $($c.Label) to $SolutionUniqueName"
    }
}

# =====================================================================================================================
# (e) PUBLISH
# =====================================================================================================================
if ($Apply) {
    $entities = ($AllTables | ForEach-Object { "<entity>$_</entity>" }) -join ''
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities>$entities</entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $($AllTables -join ', ')." -ForegroundColor Green
}

if ($Verify) {
    if ($gaps.Count -eq 0) { Write-Host "`nVERIFY PASS: the six columns are in place; sprk_createdbycontact and sprk_intakedata are secured and only the BFF can write them." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
if ($fails.Count -gt 0) {
    Write-Host "`n$($fails.Count) FAIL/drift item(s) - nothing was changed for them:" -ForegroundColor Red
    $fails | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
}
if ($IsDryRun) { Write-Host "`nDRY RUN complete - nothing was written. Re-run with -Apply." -ForegroundColor Cyan }
if ($fails.Count -gt 0) { exit 1 }
