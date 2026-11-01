<#
.SYNOPSIS
    eBilling R1 task 088 (EB-D-148, procedure-and-file-model.md sections 4.1, 4.2, 5.1-5.2, 9.1, 10) - Stored File schema: the
    sprk_storedfilepurpose global choice, the sprk_storedfile table, field security on its authorization columns, the submission
    marker sprk_document.sprk_restrictedbysubmission, and the invoice status reason Superseded.

.DESCRIPTION
    Idempotent, additive Dataverse Web API script (dev only in R1). Every item is check-then-create; a second run reports every item
    as skipped, writes no row and does not publish. RUN -WhatIf FIRST: it reads everything, prints what it would create, change or
    grant, and writes nothing. The real run needs ontology-r1's full schema-review PASS, the before-notices to ontology-r1 and
    uac-r2 (and uac-r2's answer on the profile reuse), the owner's OK, and task 080's real run (the Stored File's
    sprk_procedureversion lookup needs that table) (task 088 steps 6-8). Never run by the agent that wrote it.

    Items (F1-F5 of the task):
      F1  Global choice sprk_storedfilepurpose: Procedure source 100000000, Invoice PDF 100000001, Invoice supporting file 100000002,
          Export file 100000003. An existing set with other values stops the script (never edited).
      F2  Table sprk_storedfile (user-owned, IsAuditEnabled, primary sprk_name = the file name, max length 400):
            parent lookups (exactly one is set on a row, enforced by the BFF, task 090), each RemoveLink and optional:
              sprk_procedureversion (-> sprk_procedureversion), sprk_invoice (-> sprk_invoice), sprk_billingexport (-> sprk_billingexport);
            sprk_purpose (global choice F1); sprk_firmvisible (two options, default No);
            sprk_filestatus (LOCAL choice: Not yet servable 100000000, Servable 100000001, Removed 100000002);
            sprk_sourcedocument (-> sprk_document, Restrict); sprk_sourcecommunication (-> sprk_communication, Restrict);
            sprk_sourceattachmentidentity (text 1000); sprk_sourceversion (text 20: ONE canonical string form of the row version);
            sprk_sourcesha256 and sprk_sha256 (text 64); sprk_contenttype (text 255); sprk_sizebytes (whole number 0 to 2147483647);
            sprk_blobpath (text 1024); sprk_createdbyperson (-> systemuser, RemoveLink; the Deploy-EbillingSubmissionSchema.ps1
            shape; created UNSECURED - task 086 secures it).
      F3  Field security, reusing unified-access-control-r2's profile pair as task 080 does (permission rows only, never a member):
            writer ("Spaarke BFF-Managed Field Writers", the BFF application users only): Read / Create / Update on every F2 column
            except sprk_name and sprk_createdbyperson, and on F4;
            reader ("Spaarke BFF-Managed Field Readers", the default team of each business unit named in -ReaderBusinessUnits): Read on the same columns EXCEPT
            sprk_blobpath, which has the writer row only (server-only, like the old S8 column). A reader row found on
            sprk_blobpath is reported as a finding (exit 1), never deleted.
      F4  sprk_document.sprk_restrictedbysubmission (two options "Restricted by invoice submission", default No). Field-secured
          with F3. Written ONLY by the submission path (task 095) in the create that writes the .eml's own Restricted narrowing;
          (a') reads it (task 098). The BFF's app-only generic writers and the field-mapping push refuse it
          (InheritedAccessPermission.RefuseAppOnlyWrite, same task).
      F5  sprk_invoice statuscode "Superseded" = 659490005 at statecode 1 (Inactive). The pre-flight refuses (exit 2, nothing
          written) when 659490005 is used by another label or state, when the label exists at another value, when one of the
          seven task-012 values is missing or relabelled, or when statuscode has any other value. Set only by task 097's
          corrected-invoice close; this script sets no invoice to it and adds no code constant.

    DELETE BEHAVIOUR (model 4.2, 5.2). The three parent lookups are RemoveLink: a parent deleted outside the BFF leaves the row for
    task 090's orphan reconcile, which reads the parent from the blob path; 090's download refuses a row whose parent lookup does not
    match the path. RESTRICT only on the two source lookups (uac-r2 answer B): a source document or communication referenced by a
    Stored File cannot be deleted (the BFF single-document delete answers 409 first; a communication delete only deactivates it).
    Every lookup is Assign / Share / Unshare / Reparent NoCascade: no relationship of sprk_storedfile is parental; a reassign or share
    of a parent never reaches the row (the secure-child job, task 086/089, owns its shares). An existing lookup with another target
    or delete behaviour stops the pre-flight (exit 2, nothing changed).

    THE ROW ID is supplied by the BFF (task 090: f(submission id, source version)); no column or key for it here.

    NAMES. The table's SchemaName is sprk_StoredFile and its primary column's sprk_Name (a SchemaName cannot be changed after the
    create); every other column's SchemaName is PascalCase after the prefix as well. Logical names are the lowercase forms and are
    what every pre-flight "name is free" check and the verify compare.

    RELATED MENU. Each of the six lookups is created with AssociatedMenuConfiguration Behavior = DoNotDisplay, so none adds a
    "Stored Files" entry to the Related menu of its target form (procedure version, invoice, billing export, document,
    communication, user); the verify reads each back.

    COLUMN TYPES (run 1 on dev, 2026-10-22). A BigInt column reads CanBeSecuredFor* true but the field-permission service refuses its
    permission rows (0x8004f508 "NOT secured for entity fieldpermission"), so both were retyped: sprk_sourceversion is a text 20 and
    sprk_sizebytes a whole number 0 to 2147483647 (a file over 2 GiB cannot be stored). No BigInt column is planned. An existing
    column of a planned logical name with another type (a BigInt still resolving after its delete) stops the pre-flight (exit 2,
    before any write). A re-run against a partly built table skips everything present and creates / secures the rest.
    The securability pre-pass over every sprk_storedfile column runs before the marker is created and before any column is secured.

    PUBLISH. Only what this script and its relationships touched: sprk_storedfile, sprk_document, sprk_invoice, sprk_communication,
    sprk_procedureversion, sprk_billingexport, systemuser and the sprk_storedfilepurpose choice. Never a tenant-wide publish.

    NEVER: set any invoice to Superseded; write the marker on any row; create a form, view or subgrid for sprk_storedfile (the
    platform's own default system form and views come with every new table; none is created or changed here); change a role
    privilege (tasks 081, 086); add or remove a field security profile member; touch SpaarkeMaster solution source (task 079).
    No plugin, business rule or calculated column (ADR-002).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment. Any other URL is refused (R1 is dev only).

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.PARAMETER ReaderProfileName
    Field security profile that reads the F3 columns for the users of the named business units. Default: uac-r2's "Spaarke BFF-Managed Field Readers".

.PARAMETER ReaderBusinessUnits
    Names of the business units whose default team must hold the reader profile (the pre-flight stops, exit 2, when one does not, or
    when a named business unit has no enabled default team). Default: the six dev business units whose default teams hold it today.
    Default teams of any OTHER business unit are not checked and never fail the run; they are listed in one informational line.

.PARAMETER WriterProfileName
    Field security profile that creates / updates the F3 columns. Default: uac-r2's "Spaarke BFF-Managed Field Writers".

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF application users that must be members of the writer profile. Default: the two dev ones.

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1

.NOTES
    Project: ebilling-r1, task 088. Dev only in R1. The Dataverse helpers live in EbillingSchema.psm1. The pre-flight, the global
    choice and the field security are copied from Deploy-EbillingProcedureSchema.ps1 (task 080); F5 from
    Deploy-EbillingInvoiceHeaderSchema.ps1 (Add-StatusValues). Exit codes: 0 = done (created / skipped), 1 = error or a security
    finding, 2 = escalation (nothing further changed). The plan and the F5 decision are tested by
    tests/scripts/Deploy-EbillingStoredFileSchema.Tests.ps1.
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [string]$ReaderProfileName = 'Spaarke BFF-Managed Field Readers',
    [string[]]$ReaderBusinessUnits = @('Secure Record', 'Spaarke Business Unit 1', 'Spaarke Demo', 'Spaarke Dev 1', 'Spaarke Test 1', 'Spaarke'),
    [string]$WriterProfileName = 'Spaarke BFF-Managed Field Writers',
    [string[]]$BffApplicationIds = @('5967251e-171c-46fe-a6c2-ef843c90309d', '1e40baad-e065-4aea-a8d4-4b7ab273458c')
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$DevUrl = 'https://spaarkedev1.crm.dynamics.com'
# `pwsh -File` passes a comma list as ONE string: split it.
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

# ---------------------------------------------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------------------------------------------

$StoredFile = 'sprk_storedfile'
$Document = 'sprk_document'
$Invoice = 'sprk_invoice'
$Version = 'sprk_procedureversion'
$Export = 'sprk_billingexport'
$Communication = 'sprk_communication'
$CreatorColumn = 'sprk_createdbyperson'
$MarkerColumn = 'sprk_restrictedbysubmission'
$BlobPathColumn = 'sprk_blobpath'
# SchemaNames the create bodies carry (a table's SchemaName cannot change after the create): PascalCase after the prefix. The
# logical names above are their lowercase forms.
$StoredFileSchema = 'sprk_StoredFile'
$StoredFileNameSchema = 'sprk_Name'
# AssociatedMenuConfiguration.Behavior of every lookup: no entry on the target's Related menu.
$RelatedMenuBehavior = 'DoNotDisplay'

# F1: values are assigned 100000000, 100000001, ... in label order.
$PurposeChoice = @{ Name = 'sprk_storedfilepurpose'; Display = 'Stored File Purpose'; Description = 'What a Stored File is for: a Procedure Version''s source document, an invoice PDF, an invoice supporting file or an export file (ebilling-r1 task 088, model 4.1).'; Labels = @('Procedure source', 'Invoice PDF', 'Invoice supporting file', 'Export file') }

# F2: the local choice sprk_filestatus, values 100000000.. in label order.
$FileStatusLabels = @('Not yet servable', 'Servable', 'Removed')

# F5: the seven statuscode values of task 012 (verified, never changed) and the one inserted.
$KnownStatus = @(
    @{ Value = 1; State = 0; Label = 'In Review' }
    @{ Value = 2; State = 1; Label = 'Cancelled' }
    @{ Value = 659490000; State = 0; Label = 'Approved' }
    @{ Value = 659490001; State = 0; Label = 'Sent to AP' }
    @{ Value = 659490002; State = 1; Label = 'Paid' }
    @{ Value = 659490003; State = 1; Label = 'Rejected' }
    @{ Value = 659490004; State = 1; Label = 'Returned to Firm' }
)
$SupersededStatus = @{ Value = 659490005; State = 1; Label = 'Superseded' }

# Shared Dataverse helpers (scripts/ebilling/EbillingSchema.psm1). The state lists are shared by reference.
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $StoredFile
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect
$script:Planned = $State.Planned
$SolutionHeader = $State.SolutionHeader
$Gaps = [System.Collections.Generic.List[string]]::new()
$script:Wrote = $false
$script:Lookups = @()
$script:Columns = @()
$script:Secured = @()

# ---------------------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------------------

function New-TwoOptionDef([string]$Schema, [string]$Display, [string]$Description, [bool]$Default) {
    return @{
        '@odata.type' = 'Microsoft.Dynamics.CRM.BooleanAttributeMetadata'
        SchemaName    = $Schema
        DisplayName   = New-Label $Display
        Description   = New-Label $Description
        RequiredLevel = @{ Value = 'None' }
        DefaultValue  = $Default
        OptionSet     = @{
            '@odata.type' = 'Microsoft.Dynamics.CRM.BooleanOptionSetMetadata'
            OptionSetType = 'Boolean'
            TrueOption    = @{ Value = 1; Label = (New-Label 'Yes') }
            FalseOption   = @{ Value = 0; Label = (New-Label 'No') }
        }
    }
}

# A whole number column (IntegerAttributeMetadata; copied from Deploy-EbillingProcedureSchema.ps1).
function New-WholeNumberDef([string]$Schema, [string]$Display, [string]$Description, [int]$Min, [int]$Max) {
    return @{
        '@odata.type' = 'Microsoft.Dynamics.CRM.IntegerAttributeMetadata'
        SchemaName    = $Schema
        DisplayName   = New-Label $Display
        Description   = New-Label $Description
        RequiredLevel = @{ Value = 'None' }
        Format        = 'None'
        MinValue      = $Min
        MaxValue      = $Max
    }
}

# Case and space blind comparison of option labels.
function ConvertTo-LabelKey([string]$Label) { return (($Label -replace '\s', '').ToLowerInvariant()) }

# The many-to-one relationship of a lookup column, found by the column. $null when the column has none.
function Get-LookupRelationship([string]$Table, [string]$Column) {
    $r = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/ManyToOneRelationships?`$select=SchemaName,ReferencedEntity,CascadeConfiguration,AssociatedMenuConfiguration&`$filter=ReferencingAttribute eq '$Column'" -Retries 2
    return @($r.value) | Select-Object -First 1
}

function Write-Report([string]$StateText, [string]$What) {
    $color = switch ($StateText) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $StateText, $What) -ForegroundColor $color
    if ($StateText -in 'MISSING', 'FAIL') { $Gaps.Add($What) }
    if ($StateText -eq 'DONE') { $script:Wrote = $true }
}

# ---- global choice (copied from Deploy-EbillingProcedureSchema.ps1) --------------------------------------------------

function Get-GlobalOptionSet([string]$Name, [int]$Retries = 0) {
    return Get-DvOrNull "GlobalOptionSetDefinitions(Name='$Name')/Microsoft.Dynamics.CRM.OptionSetMetadata" -Retries $Retries
}

function Get-ChoiceOptions($Labels) {
    $v = 100000000
    return @($Labels | ForEach-Object { $o = [pscustomobject]@{ Value = $v; Label = $_ }; $v++; $o })
}

# $null when the set is absent or has exactly the wanted Value=Label pairs; else the conflict text.
function Get-GlobalChoiceConflict([string]$Name, $Options) {
    $os = Get-GlobalOptionSet $Name
    if (-not $os) { return $null }
    $want = @($Options | ForEach-Object { "$($_.Value)=$($_.Label)" } | Sort-Object)
    $have = @($os.Options | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" } | Sort-Object)
    if (($have -join '|') -ne ($want -join '|')) {
        return "Global option set $Name exists with [$($have -join ', ')] but this task needs [$($want -join ', ')]. Editing or removing options needs an owner decision."
    }
    return $null
}

function Add-GlobalChoice([string]$Name, [string]$Display, [string]$Description, $Options) {
    $conflict = Get-GlobalChoiceConflict $Name $Options
    if ($conflict) { throw [EscalationException]::new($conflict) }
    if (Get-GlobalOptionSet $Name) {
        $script:Skipped.Add("optionset $Name"); Write-Host "  skipped  global option set $Name (exists, $(@($Options).Count) options)" -ForegroundColor Yellow
        return
    }
    if ($DryRun) { $script:Created.Add("optionset $Name"); Write-Host "  WOULD CREATE global option set $Name ($((@($Options) | ForEach-Object { "$($_.Value)=$($_.Label)" }) -join ', '))" -ForegroundColor Cyan; return }
    $body = @{
        '@odata.type' = 'Microsoft.Dynamics.CRM.OptionSetMetadata'
        Name          = $Name
        DisplayName   = New-Label $Display
        Description   = New-Label $Description
        IsGlobal      = $true
        OptionSetType = 'Picklist'
        Options       = @($Options | ForEach-Object { @{ Value = $_.Value; Label = (New-Label $_.Label) } })
    }
    try {
        Invoke-Dv -Path 'GlobalOptionSetDefinitions' -Method POST -Body $body -ExtraHeaders $SolutionHeader | Out-Null
    }
    catch {
        if (-not (Test-DuplicateRefusal $_.Exception.Message -Loose)) { throw }
        if (-not (Get-GlobalOptionSet $Name -Retries 12)) { throw }
        $script:Skipped.Add("optionset $Name"); Write-Host "  skipped  global option set $Name (exists, found via create refusal)" -ForegroundColor Yellow
        return
    }
    if (-not (Get-GlobalOptionSet $Name -Retries 20)) { throw "Global option set $Name was created but is not visible in metadata after 60 s." }
    $script:Created.Add("optionset $Name"); $script:Wrote = $true; Write-Host "  CREATED  global option set $Name" -ForegroundColor Green
}

# A choice column bound to a global option set. If it exists it must be a picklist bound to that same set.
function Add-GlobalChoiceColumn([string]$Column, [string]$Schema, [string]$Display, [string]$Description, [string]$OptionSetName) {
    $table = Get-EbEntity
    $attr = Get-Attr $table $Column
    if ($attr) {
        Assert-AttrType $attr $Column 'Picklist'
        $pk = Get-DvOrNull "EntityDefinitions(LogicalName='$table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Name,IsGlobal)"
        if (-not $pk.OptionSet.IsGlobal -or $pk.OptionSet.Name -ne $OptionSetName) {
            throw [EscalationException]::new("$table.$Column already exists as a picklist bound to '$($pk.OptionSet.Name)' (global: $($pk.OptionSet.IsGlobal)) but this task needs the global option set '$OptionSetName'. Owner decision needed.")
        }
        $script:Expect.Add(@{ Table = $table; Column = $Column; Type = 'Picklist' })
        $script:Skipped.Add("$table.$Column"); Write-Host "  skipped  $Column (exists, choice bound to $OptionSetName)" -ForegroundColor Yellow
        return
    }
    $bind = '/GlobalOptionSetDefinitions(00000000-0000-0000-0000-000000000000)'
    if (-not $DryRun) {
        $os = Get-GlobalOptionSet $OptionSetName -Retries 12
        if (-not $os) { throw "Global option set $OptionSetName is not readable; cannot create $table.$Column." }
        $bind = "/GlobalOptionSetDefinitions($($os.MetadataId))"
    }
    $def = @{
        '@odata.type'                = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'
        SchemaName                   = $Schema
        DisplayName                  = New-Label $Display
        Description                  = New-Label $Description
        RequiredLevel                = @{ Value = 'None' }
        'GlobalOptionSet@odata.bind' = $bind
    }
    Add-Column -Column $Column -Definition $def -ExpectedType 'Picklist'
}

# ---- table ----------------------------------------------------------------------------------------------------------

# Auditing on the table itself (copied from Deploy-EbillingProcedureSchema.ps1): read and, when off, switched on with a full PUT.
function Set-TableAudit([string]$Table) {
    $path = "EntityDefinitions(LogicalName='$Table')"
    $ent = Get-DvOrNull $path -Retries 4
    if (-not $ent) {
        if ($DryRun) { Write-Host "  WOULD SET auditing on $Table" -ForegroundColor Cyan; return }
        throw "$Table was not found."
    }
    if ($ent.IsAuditEnabled.Value -eq $true) { $script:Skipped.Add("$Table auditing"); Write-Host "  skipped  $Table auditing already on" -ForegroundColor Yellow; return }
    if ($DryRun) { $script:Changed.Add("$Table auditing"); Write-Host "  WOULD SET auditing on $Table" -ForegroundColor Cyan; return }
    $ent.IsAuditEnabled.Value = $true
    Invoke-WhilePropagating { Invoke-Dv -Path "EntityDefinitions($($ent.MetadataId))" -Method PUT -Body $ent -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    $chk = $null
    for ($i = 0; $i -lt 12; $i++) {
        $chk = Get-DvOrNull "$path`?`$select=IsAuditEnabled"
        if ($chk -and $chk.IsAuditEnabled.Value -eq $true) { break }
        Start-Sleep -Seconds 5
    }
    if (-not $chk -or $chk.IsAuditEnabled.Value -ne $true) { throw "$Table auditing is still off after the update." }
    $script:Changed.Add("$Table auditing"); $script:Wrote = $true; Write-Host "  CHANGED  $Table auditing off -> on" -ForegroundColor Green
}

# ---- lookups --------------------------------------------------------------------------------------------------------

# One lookup to create. Rel is the relationship schema name; Delete is the delete behaviour it must read. Nothing else cascades.
function New-LookupSpec([string]$Table, [string]$Column, [string]$Schema, [string]$Rel, [string]$Target, [string]$Display, [string]$Desc, [string]$Delete) {
    return [pscustomobject]@{ Table = $Table; Column = $Column; Schema = $Schema; Rel = $Rel; Target = $Target; Display = $Display; Desc = $Desc; Delete = $Delete; Menu = $RelatedMenuBehavior }
}

# Relationship name for a lookup on a NEW table: {table}_{column}_{target}, sprk_ prefixes dropped from the column and the target
# (the Deploy-EbillingSubmissionSchema.ps1 / task 080 shape).
function Get-NewTableRel([string]$Table, [string]$Column, [string]$Target) {
    return ('{0}_{1}_{2}' -f $Table, ($Column -replace '^sprk_', ''), ($Target -replace '^sprk_', ''))
}

# The CascadeConfiguration members a relationship must read back: its delete behaviour, and Assign / Share / Unshare / Reparent
# NoCascade (no relationship of sprk_storedfile is parental).
function Get-ExpectedCascade($s) {
    return @{ Delete = $s.Delete; Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade' }
}

# $null when a relationship's AssociatedMenuConfiguration reads back with the wanted Behavior (DoNotDisplay); else the problem text.
function Get-AssociatedMenuProblem($Actual, [string]$Expected) {
    $b = if ($Actual) { [string]$Actual.Behavior } else { '' }
    if ($b -eq $Expected) { return $null }
    return "AssociatedMenuConfiguration.Behavior is '$b', expected '$Expected'"
}

# The members of an actual CascadeConfiguration that differ from the expected ones; empty when it is exactly as expected.
function Get-CascadeMismatches($Actual, [hashtable]$Expected) {
    $out = @()
    foreach ($k in ($Expected.Keys | Sort-Object)) {
        if ([string]$Actual.$k -ne [string]$Expected[$k]) { $out += "$k is '$($Actual.$k)', expected '$($Expected[$k])'" }
    }
    return $out
}

# The full plan: lookups, other columns (for the pre-flight type check and the verify) and the F3 secured set. Pure: reads nothing.
# Secured = every F2 column except sprk_name and sprk_createdbyperson, plus F4; Reader = $false only on sprk_blobpath.
function Get-StoredFilePlan {
    $sf = $StoredFile
    $lookups = @(
        (New-LookupSpec $sf $Version 'sprk_ProcedureVersion' (Get-NewTableRel $sf $Version $Version) $Version 'Procedure Version' 'Parent, for a Procedure source file. Exactly one parent lookup is set on a row (enforced by the BFF, task 090). REMOVELINK: a Procedure Version deleted outside the BFF leaves the row for the orphan reconcile, which reads the parent from the blob path (model 4.2). Written only by the BFF (field-secured).' 'RemoveLink')
        (New-LookupSpec $sf $Invoice 'sprk_Invoice' (Get-NewTableRel $sf $Invoice $Invoice) $Invoice 'Invoice' 'Parent, for an invoice PDF or supporting file. Exactly one parent lookup is set on a row (enforced by the BFF, task 090). REMOVELINK: an invoice deleted outside the BFF leaves the row for the orphan reconcile (model 4.2). Written only by the BFF (field-secured).' 'RemoveLink')
        (New-LookupSpec $sf $Export 'sprk_BillingExport' (Get-NewTableRel $sf $Export $Export) $Export 'Billing Export' 'Parent, for an export file. Exactly one parent lookup is set on a row (enforced by the BFF, task 090). REMOVELINK: an export deleted outside the BFF leaves the row for the orphan reconcile (model 4.2, 10). Written only by the BFF (field-secured).' 'RemoveLink')
        (New-LookupSpec $sf 'sprk_sourcedocument' 'sprk_SourceDocument' (Get-NewTableRel $sf 'sprk_sourcedocument' $Document) $Document 'Source Document' 'For a Submit Invoice copy: the Document the bytes were copied from (the .eml for an Outlook add-in submission). The copy rule reads the caller''s own Read on it on every access (model 9.1, B). RESTRICT: a referenced document cannot be deleted (uac-r2 answer B). Written only by the BFF (field-secured).' 'Restrict')
        (New-LookupSpec $sf 'sprk_sourcecommunication' 'sprk_SourceCommunication' (Get-NewTableRel $sf 'sprk_sourcecommunication' $Communication) $Communication 'Source Communication' 'For a Submit Invoice copy: the email (communication) the file came from (model 9.1). RESTRICT: a referenced communication cannot be deleted; a communication delete only deactivates it, so the copy rule still reads it (uac-r2 answer B). Written only by the BFF (field-secured).' 'Restrict')
        (New-LookupSpec $sf $CreatorColumn 'sprk_CreatedByPerson' "sprk_systemuser_${sf}_createdbyperson" 'systemuser' 'Created By (Person)' 'The PERSON who asked for this file. Stamped by the BFF only (field-secured later by task 086): createdby is the BFF application user for a row the BFF creates app-only, so this column is what records the person. ebilling-r1 task 088 (O-23; the Deploy-EbillingSubmissionSchema.ps1 shape).' 'RemoveLink')
    )
    $columns = @(
        @{ Table = $sf; Column = 'sprk_name'; Type = 'String'; MaxLength = 400 }
        @{ Table = $sf; Column = 'sprk_purpose'; Type = 'Picklist' }
        @{ Table = $sf; Column = 'sprk_firmvisible'; Type = 'Boolean' }
        @{ Table = $sf; Column = 'sprk_filestatus'; Type = 'Picklist' }
        @{ Table = $sf; Column = 'sprk_sourceattachmentidentity'; Type = 'String'; MaxLength = 1000 }
        @{ Table = $sf; Column = 'sprk_sourceversion'; Type = 'String'; MaxLength = 20 }
        @{ Table = $sf; Column = 'sprk_sourcesha256'; Type = 'String'; MaxLength = 64 }
        @{ Table = $sf; Column = 'sprk_sha256'; Type = 'String'; MaxLength = 64 }
        @{ Table = $sf; Column = 'sprk_contenttype'; Type = 'String'; MaxLength = 255 }
        @{ Table = $sf; Column = 'sprk_sizebytes'; Type = 'Integer'; MinValue = 0; MaxValue = 2147483647 }
        @{ Table = $sf; Column = $BlobPathColumn; Type = 'String'; MaxLength = 1024 }
        @{ Table = $Document; Column = $MarkerColumn; Type = 'Boolean' }
    )
    $unsecured = @('sprk_name', $CreatorColumn)
    $secured = @()
    foreach ($l in $lookups) {
        if ($l.Column -notin $unsecured) { $secured += @{ Table = $l.Table; Column = $l.Column; Kind = 'Lookup'; Reader = $true } }
    }
    foreach ($c in $columns) {
        if ($c.Column -in $unsecured) { continue }
        $secured += @{ Table = $c.Table; Column = $c.Column; Kind = $c.Type; Reader = ($c.Column -ne $BlobPathColumn) }
    }
    return [pscustomobject]@{ Lookups = $lookups; Columns = $columns; Secured = $secured }
}

# The entities PublishXml names: the new table, the two tables that gain a column or a status, and the four tables the new
# relationships touch (their forms and Related menus). Never every entity.
function Get-PublishEntities { return @($StoredFile, $Document, $Invoice, $Communication, $Version, $Export, 'systemuser') }

# The PublishXml ParameterXml: the entities above and the purpose choice.
function Get-PublishXml {
    return "<importexportxml><entities>$((Get-PublishEntities | ForEach-Object { "<entity>$_</entity>" }) -join '')</entities><optionsets><optionset>$($PurposeChoice.Name)</optionset></optionsets></importexportxml>"
}

# Nothing may change an existing lookup: an existing one must already target the right table with the right delete behaviour,
# and a missing one's relationship name must be free.
function Test-LookupSpecPreflight($s, [System.Collections.Generic.List[string]]$Problems) {
    $attr = Get-Attr $s.Table $s.Column
    if ($attr) {
        if ($attr.AttributeType -ne 'Lookup') { $Problems.Add("$($s.Table).$($s.Column) exists as $($attr.AttributeType), expected a lookup"); return }
        $lk = Get-DvOrNull "EntityDefinitions(LogicalName='$($s.Table)')/Attributes(LogicalName='$($s.Column)')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
        if ($lk -and $lk.Targets -and ($lk.Targets -notcontains $s.Target)) { $Problems.Add("$($s.Table).$($s.Column) exists as a lookup to '$($lk.Targets -join ',')', expected '$($s.Target)'") }
        $rel = Get-LookupRelationship $s.Table $s.Column
        if ($rel) {
            $diff = @(Get-CascadeMismatches $rel.CascadeConfiguration (Get-ExpectedCascade $s))
            if ($diff.Count -gt 0) { $Problems.Add("$($s.Table).$($s.Column) ($($rel.SchemaName)) has a different CascadeConfiguration: $($diff -join '; '). Changing it needs an owner decision; not changed") }
        }
    }
    else {
        $rel = Get-DvOrNull "RelationshipDefinitions(SchemaName='$($s.Rel)')"
        if ($rel) { $Problems.Add("Relationship $($s.Rel) already exists ($($rel.RelationshipType)) but $($s.Table).$($s.Column) does not: the name is taken with another meaning") }
    }
}

# ---- F5 statuscode (pattern: Deploy-EbillingInvoiceHeaderSchema.ps1 Add-StatusValues) ------------------------------------

function Get-StatusOptions {
    $a = Get-DvOrNull "EntityDefinitions(LogicalName='$Invoice')/Attributes(LogicalName='statuscode')/Microsoft.Dynamics.CRM.StatusAttributeMetadata?`$expand=OptionSet(`$select=Options)" -Retries 4
    if (-not $a) { throw "$Invoice.statuscode metadata could not be read." }
    return @($a.OptionSet.Options | ForEach-Object {
            [pscustomobject]@{ Value = [int]$_.Value; State = [int]$_.State; Label = $_.Label.UserLocalizedLabel.Label }
        })
}

# The F5 decision on the statuscode options as read: 'Insert' (659490005 free) or 'Skip' (already exactly Superseded at statecode
# 1). Throws an EscalationException (exit 2, nothing written) when 659490005 or the label "Superseded" exists with another meaning,
# when a task-012 value is missing or differs, or when statuscode holds any value this task does not know. Pure: reads nothing.
function Get-StatusDecision($Options) {
    $problems = @()
    foreach ($k in $KnownStatus) {
        $cur = @($Options | Where-Object { $_.Value -eq $k.Value }) | Select-Object -First 1
        if (-not $cur) { $problems += "value $($k.Value) '$($k.Label)' (task 012) is missing"; continue }
        if ((ConvertTo-LabelKey $cur.Label) -ne (ConvertTo-LabelKey $k.Label) -or $cur.State -ne $k.State) {
            $problems += "value $($k.Value) is '$($cur.Label)' at statecode $($cur.State), expected '$($k.Label)' at statecode $($k.State) (task 012)"
        }
    }
    $s = $SupersededStatus
    $byValue = @($Options | Where-Object { $_.Value -eq $s.Value }) | Select-Object -First 1
    if ($byValue -and ((ConvertTo-LabelKey $byValue.Label) -ne (ConvertTo-LabelKey $s.Label) -or $byValue.State -ne $s.State)) {
        $problems += "value $($s.Value) is already used by '$($byValue.Label)' at statecode $($byValue.State)"
    }
    foreach ($o in @($Options | Where-Object { (ConvertTo-LabelKey $_.Label) -eq (ConvertTo-LabelKey $s.Label) -and $_.Value -ne $s.Value })) {
        $problems += "the label '$($o.Label)' already exists as value $($o.Value) (statecode $($o.State))"
    }
    $knownValues = @($KnownStatus | ForEach-Object { $_.Value }) + @($s.Value)
    foreach ($o in @($Options | Where-Object { $_.Value -notin $knownValues })) {
        $problems += "statuscode has value $($o.Value) '$($o.Label)' (statecode $($o.State)) that this task does not know"
    }
    if ($problems.Count -gt 0) {
        throw [EscalationException]::new("$Invoice statuscode is not as task 012 left it, or Superseded is taken with another meaning (nothing was written, nothing renumbered or relabelled):`n    - " + ($problems -join "`n    - "))
    }
    if ($byValue) { return 'Skip' }
    return 'Insert'
}

function Add-SupersededStatus {
    $s = $SupersededStatus
    $name = "$Invoice.statuscode=$($s.Value)"
    if ((Get-StatusDecision (Get-StatusOptions)) -eq 'Skip') { $script:Skipped.Add($name); Write-Host "  skipped  '$($s.Label)' = $($s.Value) (statecode $($s.State), already present)" -ForegroundColor Yellow; return }
    if ($DryRun) { $script:Created.Add($name); Write-Host "  WOULD INSERT '$($s.Label)' = $($s.Value) (statecode $($s.State))" -ForegroundColor Cyan; return }
    try {
        Invoke-Dv -Path 'InsertStatusValue' -Method POST -ExtraHeaders $SolutionHeader -Body @{
            AttributeLogicalName = 'statuscode'; EntityLogicalName = $Invoice; Value = $s.Value; StateCode = $s.State
            Label = (New-Label $s.Label); SolutionUniqueName = $SolutionUniqueName
        } | Out-Null
    }
    catch {
        # A stale read hid an option that is already there: the refusal is authoritative, verify on a settled read.
        if (-not (Test-DuplicateRefusal $_.Exception.Message -Loose)) { throw }
        $found = $null
        for ($i = 0; $i -lt 12 -and -not $found; $i++) {
            $found = Get-StatusOptions | Where-Object { $_.Value -eq $s.Value -and $_.State -eq $s.State -and (ConvertTo-LabelKey $_.Label) -eq (ConvertTo-LabelKey $s.Label) }
            if (-not $found) { Start-Sleep -Seconds 5 }
        }
        if (-not $found) { throw }
        $script:Skipped.Add($name); Write-Host "  skipped  '$($s.Label)' (exists, found via create refusal)" -ForegroundColor Yellow
        return
    }
    $script:Created.Add($name); $script:Wrote = $true; Write-Host "  INSERTED '$($s.Label)' = $($s.Value) (statecode $($s.State))" -ForegroundColor Green
}

# ---------------------------------------------------------------------------------------------------------------
# Field security (F3; copied from Deploy-EbillingProcedureSchema.ps1 P9, without -CreateProfiles: reuse only)
# ---------------------------------------------------------------------------------------------------------------

$script:ReaderId = $null
$script:WriterId = $null

function Get-ProfileByName([string]$Name) {
    $safe = $Name.Replace("'", "''")
    return @((Invoke-Dv "fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$safe'").value)
}

# The members of the pair as read (sorted ids), so the verify can show no member changed.
function Get-ProfileMembers {
    $r = @((Invoke-Dv "fieldsecurityprofiles($($script:ReaderId))/teamprofiles_association?`$select=teamid").value | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() } | Sort-Object)
    $wu = @((Invoke-Dv "fieldsecurityprofiles($($script:WriterId))/systemuserprofiles_association?`$select=systemuserid").value | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() } | Sort-Object)
    $wt = @((Invoke-Dv "fieldsecurityprofiles($($script:WriterId))/teamprofiles_association?`$select=teamid").value | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() } | Sort-Object)
    return "reader teams [$($r -join ',')] writer users [$($wu -join ',')] writer teams [$($wt -join ',')]"
}

# Preconditions of the profile pair. A failure stops the script (escalation) in every mode: nothing is written.
function Test-ProfilePreconditions {
    $problems = [System.Collections.Generic.List[string]]::new()
    $defaultTeams = @((Invoke-Dv "teams?`$select=teamid,name&`$filter=isdefault eq true&`$expand=businessunitid(`$select=name,isdisabled)").value |
            Where-Object { -not $_.businessunitid.isdisabled })
    if ($defaultTeams.Count -eq 0) { $problems.Add('no business-unit default team was read - the query is wrong') }

    $readers = Get-ProfileByName $ReaderProfileName
    if ($readers.Count -gt 1) { $problems.Add("$($readers.Count) profiles are named '$ReaderProfileName' - exactly one is allowed") }
    elseif ($readers.Count -eq 1) {
        $script:ReaderId = $readers[0].fieldsecurityprofileid
        $onReader = @((Invoke-Dv "fieldsecurityprofiles($($script:ReaderId))/teamprofiles_association?`$select=teamid,name").value | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
        # The reader profile is checked against an EXPLICIT, named set of business units (-ReaderBusinessUnits), never "every default team":
        # a field-security reader row grants COLUMN read, not ROW read, so the profile must never be the boundary for flagged-external
        # users. UAC task 191 adds an "External Collaborators" business unit whose default team must NEVER get this profile; a check
        # over every default team would fail on it, or push someone to add the profile there. Default teams of business units outside
        # the set are reported once and never fail the run. This script never adds a team to the profile.
        $named = @($defaultTeams | Where-Object { $_.businessunitid.name -in $ReaderBusinessUnits })
        $others = @($defaultTeams | Where-Object { $_.businessunitid.name -notin $ReaderBusinessUnits })
        foreach ($bu in $ReaderBusinessUnits) {
            if (-not ($named | Where-Object { $_.businessunitid.name -eq $bu })) { $problems.Add("-ReaderBusinessUnits names '$bu', but no enabled business unit of that name has a default team: fix the name") }
        }
        foreach ($t in $named) {
            if ($onReader -contains $t.teamid.ToString().ToLowerInvariant()) { Write-Report 'OK' "reader profile '$ReaderProfileName' is on default team '$($t.name)' (BU '$($t.businessunitid.name)')" }
            else { $problems.Add("default team '$($t.name)' (BU '$($t.businessunitid.name)') is NOT on '$ReaderProfileName': its users would read the F3 columns EMPTY. Adding a team is a team change (the owner's / uac-r2's); this script never adds a member to a profile") }
        }
        if ($others.Count -gt 0) {
            Write-Report 'INFO' "default teams of business units outside -ReaderBusinessUnits are not checked (column read is not row read; they may or may not hold '$ReaderProfileName'): $((($others | ForEach-Object { "'$($_.businessunitid.name)'" }) | Sort-Object) -join ', ')"
        }
    }
    else { $problems.Add("reader profile '$ReaderProfileName' does not exist (uac-r2 creates it). This script reuses the pair and never creates a profile") }

    $bffUsers = @()
    foreach ($appId in $BffApplicationIds) {
        $u = @((Invoke-Dv "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $appId").value) | Select-Object -First 1
        if ($u) { $bffUsers += $u } else { $problems.Add("no application user for appId $appId in this environment") }
    }
    if ($BffApplicationIds.Count -eq 0) { $problems.Add('-BffApplicationIds is empty: the writer profile must name the BFF application user(s)') }

    $writers = Get-ProfileByName $WriterProfileName
    if ($writers.Count -gt 1) { $problems.Add("$($writers.Count) profiles are named '$WriterProfileName' - exactly one is allowed") }
    elseif ($writers.Count -eq 1) {
        $script:WriterId = $writers[0].fieldsecurityprofileid
        $members = @((Invoke-Dv "fieldsecurityprofiles($($script:WriterId))/systemuserprofiles_association?`$select=systemuserid,fullname,applicationid").value)
        $allowed = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
        foreach ($u in $bffUsers) {
            if ($members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -eq $u.systemuserid.ToString().ToLowerInvariant() }) { Write-Report 'OK' "writer profile '$WriterProfileName' holds BFF application user '$($u.fullname)'" }
            else { $problems.Add("BFF application user '$($u.fullname)' is NOT a member of '$WriterProfileName': the BFF could not write the F3 columns after they are secured") }
        }
        foreach ($m in $members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowed }) {
            $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
            $problems.Add("'$WriterProfileName' has member '$($m.fullname)', $kind - the profile's membership IS the lock; only the BFF may write these columns")
        }
        $teams = @((Invoke-Dv "fieldsecurityprofiles($($script:WriterId))/teamprofiles_association?`$select=teamid,name").value)
        foreach ($tm in $teams) { $problems.Add("'$WriterProfileName' has TEAM '$($tm.name)' as a member - every member of it could write these columns") }
    }
    else { $problems.Add("writer profile '$WriterProfileName' does not exist (uac-r2 creates it). This script reuses the pair and never creates a profile") }

    if ($problems.Count -gt 0) {
        throw [EscalationException]::new("Field security profile preconditions failed (nothing was written):`n    - " + ($problems -join "`n    - "))
    }
}

function Get-FieldPermission([string]$ProfileId, [string]$Table, [string]$Column) {
    return @((Invoke-Dv "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $ProfileId and entityname eq '$Table' and attributelogicalname eq '$Column'").value) | Select-Object -First 1
}

function Grant-FieldPermission([string]$ProfileId, [string]$Table, [string]$Column, [int]$Write) {
    # Securing propagates asynchronously: a grant right after it can be refused 0x8004f508 "... is NOT secured". Retry that one error.
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-Dv -Path 'fieldpermissions' -Method POST -Body @{
                entityname = $Table; attributelogicalname = $Column
                canread = 4; cancreate = $Write; canupdate = $Write
                'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($ProfileId)"
            } | Out-Null
            return
        }
        catch {
            if ("$($_.Exception.Message)" -notmatch '0x8004f508' -or $attempt -ge 12) { throw }
            Write-Host "    $Table.$Column not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
            Start-Sleep -Seconds 5
        }
    }
}

function Set-ColumnSecured([string]$Table, [string]$Column, [string]$Kind, [bool]$Value) {
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.${Kind}AttributeMetadata"
    $a = Get-DvOrNull $path -Retries 4
    if (-not $a) { throw "$Table.$Column was not found ($Kind)." }
    $a.IsSecured = $Value
    try {
        Invoke-WhilePropagating { Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    }
    catch {
        # A choice's definition carries its OptionSet as a navigation property the plain GET omits and the PUT may need it: retry
        # ONCE with the OptionSet expanded. A refused PUT leaves IsSecured unchanged, so nothing is masked.
        if ($Kind -notin 'Picklist', 'Boolean') { throw }
        Write-Host "    PUT of $Table.$Column refused without its OptionSet ($($_.Exception.Message)); retrying once with `$expand=OptionSet..." -ForegroundColor DarkYellow
        $a = Get-DvOrNull "${path}?`$expand=OptionSet" -Retries 2
        $a.IsSecured = $Value
        Invoke-WhilePropagating { Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    }
}

function Write-MaskedRecovery([string]$Table, [string]$Column, [string]$Cause) {
    Write-Host ''
    Write-Host "  !!! RECOVERY REQUIRED NOW - $Table.$Column may be field-secured WITHOUT its grants !!!" -ForegroundColor Red
    Write-Host "  Cause: $Cause" -ForegroundColor Red
    Write-Host '  Either fix the cause and re-run this script (on an already-secured column it grants only what is missing), or un-secure the' -ForegroundColor Red
    Write-Host "  column: make.powerapps.com > Tables > $Table > Columns > $Column > Advanced options > clear 'Enable column security', save, publish $Table." -ForegroundColor Red
}

# The permission rows a secured column gets: the writer row always (read, create, update), then the reader row (read only) unless
# the column is server-only (Reader = $false: sprk_blobpath). Writer first (review K2 of task 080).
function Get-Grants($spec) {
    $g = @(@{ Id = $script:WriterId; Name = $WriterProfileName; Write = 4 })
    if ($spec.Reader) { $g += @{ Id = $script:ReaderId; Name = $ReaderProfileName; Write = 0 } }
    return $g
}

function Invoke-ColumnSecurity($spec) {
    $t = $spec.Table; $c = $spec.Column
    $tag = "$t.$c"
    $attr = Get-Attr $t $c 'LogicalName,AttributeType,IsSecured,CanBeSecuredForRead,CanBeSecuredForCreate,CanBeSecuredForUpdate,MetadataId'
    $grants = @(Get-Grants $spec)
    $note = if ($spec.Reader) { '' } else { ' (server-only: NO reader row)' }

    if (-not $attr) {
        if (-not $DryRun) { throw "$tag is missing after its create; cannot secure it." }
        $script:Changed.Add("secure $tag"); Write-Host "  WOULD SECURE $tag after it is created, then grant: $(($grants | ForEach-Object { "$($_.Name) (read=4 create=$($_.Write) update=$($_.Write))" }) -join '; ')$note" -ForegroundColor Cyan
        foreach ($g in $grants) { $script:Created.Add("field permission $($g.Name) on $tag") }
        return
    }
    if (-not ($attr.CanBeSecuredForRead -and $attr.CanBeSecuredForCreate -and $attr.CanBeSecuredForUpdate)) {
        throw [EscalationException]::new("$tag cannot be secured for read, create and update. Owner decision needed.")
    }
    # A server-only column must not carry a reader row: report it (exit 1), never delete it.
    if (-not $spec.Reader) {
        $r = Get-FieldPermission $script:ReaderId $t $c
        if ($r) { Write-Report 'FAIL' "$ReaderProfileName has a row on server-only $tag (read=$($r.canread)); only the writer profile may read it. Not removed by script" }
    }

    $needSecure = -not $attr.IsSecured
    $missing = @()
    foreach ($g in $grants) {
        $perm = if ($g.Id) { Get-FieldPermission $g.Id $t $c } else { $null }
        if ($perm) {
            if ($perm.canread -eq 4 -and $perm.cancreate -eq $g.Write -and $perm.canupdate -eq $g.Write) { Write-Report 'OK' "$($g.Name) on $tag (read=4 create=$($g.Write) update=$($g.Write))" }
            else { Write-Report 'FAIL' "$($g.Name) on $tag is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($g.Write) update=$($g.Write)" }
        }
        else { $missing += $g }
    }

    if (-not $needSecure -and $missing.Count -eq 0) {
        $script:Skipped.Add("secure $tag"); Write-Host "  skipped  $tag (IsSecured, profiles granted$note)" -ForegroundColor Yellow
        return
    }
    if ($DryRun) {
        if ($needSecure) { $script:Changed.Add("secure $tag"); Write-Host "  WOULD SECURE $tag$note" -ForegroundColor Cyan }
        else { $script:Skipped.Add("secure $tag"); Write-Host "  skipped  $tag already IsSecured" -ForegroundColor Yellow }
        foreach ($g in $missing) { $script:Created.Add("field permission $($g.Name) on $tag"); Write-Host "  WOULD GRANT $($g.Name) on $tag (read=4 create=$($g.Write) update=$($g.Write))" -ForegroundColor Cyan }
        return
    }

    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    if ($needSecure) {
        Set-ColumnSecured $t $c $spec.Kind $true
        $script:Changed.Add("secure $tag"); Write-Report 'DONE' "secured $tag$note"
    }
    try {
        foreach ($g in $missing) {
            Grant-FieldPermission $g.Id $t $c $g.Write
            $script:Created.Add("field permission $($g.Name) on $tag"); Write-Report 'DONE' "granted $($g.Name) on $tag (read=4 create=$($g.Write) update=$($g.Write))"
        }
    }
    catch {
        $grantError = $_
        Write-Host "  FAIL     granting the profiles on $tag failed: $($grantError.Exception.Message)" -ForegroundColor Red
        if ($needSecure) {
            Write-Host "           reverting $tag to NOT field-secured, so no reader is left masked..." -ForegroundColor Yellow
            try {
                Set-ColumnSecured $t $c $spec.Kind $false
                Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = "<importexportxml><entities><entity>$t</entity></entities></importexportxml>" } | Out-Null
                $back = (Get-Attr $t $c 'LogicalName,IsSecured').IsSecured
                if ($back) { throw "the revert was accepted but $tag still reads IsSecured=true" }
                Write-Host "  REVERTED $tag is NOT field-secured again. Fix the cause, then re-run." -ForegroundColor Yellow
            }
            catch { Write-MaskedRecovery $t $c "the grant failed ($($grantError.Exception.Message)) AND the revert failed ($($_.Exception.Message))" }
        }
        throw $grantError
    }
    $clock.Stop()
    if ($needSecure) { Write-Host ("  masked window for {0}: {1:N1}s (secure -> grants)" -f $tag, $clock.Elapsed.TotalSeconds) }
}

# Nobody besides the writer profile and System Administrator may create or update a secured column, and nobody besides them may
# READ a server-only one.
function Test-OtherProfiles {
    foreach ($spec in $script:Secured) {
        $t = $spec.Table; $c = $spec.Column
        if (-not (Get-Attr $t $c 'LogicalName')) { continue }
        $rows = @((Invoke-Dv "fieldpermissions?`$select=canread,cancreate,canupdate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$t' and attributelogicalname eq '$c'").value)
        $otherWriters = @($rows | Where-Object { ($_.canupdate -eq 4 -or $_.cancreate -eq 4) -and $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' })
        foreach ($o in $otherWriters) { Write-Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' can also write $t.$c - only the BFF writer profile may" }
        $otherReaders = @()
        if (-not $spec.Reader) {
            $otherReaders = @($rows | Where-Object { $_.canread -eq 4 -and $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' })
            foreach ($o in $otherReaders) { Write-Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' can read server-only $t.$c - only the BFF writer profile may" }
        }
        if ($otherWriters.Count -eq 0 -and $otherReaders.Count -eq 0) { Write-Report 'OK' "no profile besides '$WriterProfileName' and System Administrator can write$(if (-not $spec.Reader) { ' or read' }) $t.$c" }
    }
}

# Apply mode: every column of $Specs must read CanBeSecuredForRead / Create / Update true, or the run stops (exit 2) BEFORE any
# of them is secured. $Then says what the state is at that stop. -WhatIf: nothing to read yet (the columns are not created).
function Assert-Securable($Specs, [string]$Then) {
    if ($DryRun) { return }
    $unsecurable = @()
    foreach ($spec in @($Specs)) {
        $sa = $null
        for ($i = 0; $i -lt 6 -and -not $sa; $i++) {
            $sa = Get-Attr $spec.Table $spec.Column 'LogicalName,CanBeSecuredForRead,CanBeSecuredForCreate,CanBeSecuredForUpdate'
            if (-not $sa) { Start-Sleep -Seconds 5 }
        }
        if (-not $sa) { throw "$($spec.Table).$($spec.Column) is missing after its create; cannot secure it." }
        if (-not ($sa.CanBeSecuredForRead -and $sa.CanBeSecuredForCreate -and $sa.CanBeSecuredForUpdate)) { $unsecurable += "$($spec.Table).$($spec.Column) ($($spec.Kind))" }
    }
    if ($unsecurable.Count -gt 0) {
        throw [EscalationException]::new("These columns cannot be secured for read, create and update: $($unsecurable -join ', '). $Then Owner decision needed.")
    }
    Write-Report 'OK' "securable: $((@($Specs) | ForEach-Object { "$($_.Table).$($_.Column)" }) -join ', ')"
}

# Entity-scoped publish of what this run changed (never a tenant-wide publish). Also called when a run stops after writing, so a re-run
# (which then skips every item and publishes nothing) never leaves an earlier run's change unpublished (review K2).
function Publish-Changes {
    if ($DryRun) { Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan; return }
    if ($script:Created.Count -eq 0 -and $script:Changed.Count -eq 0) { Write-Host "  nothing changed - no publish" -ForegroundColor Yellow; return }
    Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = (Get-PublishXml) } | Out-Null
    Write-Host "  customizations published ($((Get-PublishEntities) -join ', '), $($PurposeChoice.Name))" -ForegroundColor Green
}

# After a stop: publish what this run already changed, without masking the stop's own error.
function Publish-AfterStop {
    if ($DryRun -or ($script:Created.Count -eq 0 -and $script:Changed.Count -eq 0)) { return }
    Write-Host "Publishing what this run changed before it stopped..." -ForegroundColor Yellow
    try { Publish-Changes }
    catch { Write-Host "  publish after the stop FAILED ($($_.Exception.Message)): publish $((Get-PublishEntities) -join ', ') and $($PurposeChoice.Name) by hand, or the re-run's changes stay unpublished." -ForegroundColor Red }
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 088 - Stored File schema, submission marker, invoice status Superseded (F1-F5)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host "Profiles   : reader '$ReaderProfileName', writer '$WriterProfileName' (reused; members are never changed)"
Write-Host ""

try {
    if ($EnvironmentUrl -ne $DevUrl) {
        throw [EscalationException]::new("This script runs only against $DevUrl in R1 (project constraint); got $EnvironmentUrl. Nothing was changed.")
    }

    # --- 0. Pre-flight: every refusal that can be known before the first write ----------------------------------------------
    Write-Host "0. Pre-flight (read-only)" -ForegroundColor Cyan

    # 0a. Task 080's table must exist: the Stored File's Procedure Version lookup needs it.
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Version')?`$select=LogicalName" -Retries 4)) {
        throw [EscalationException]::new("$Version does not exist in $EnvironmentUrl. Task 080's real run must come first (sprk_storedfile.sprk_procedureversion needs that table). Nothing was changed.")
    }
    Write-Host "  $Version exists (task 080 has run)"
    foreach ($ref in @($Invoice, $Export, $Document, $Communication, 'systemuser')) {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$ref')?`$select=LogicalName" -Retries 4)) { throw [EscalationException]::new("$ref does not exist in $EnvironmentUrl. Nothing was changed.") }
    }
    $plan = Get-StoredFilePlan
    $script:Lookups = @($plan.Lookups); $script:Columns = @($plan.Columns); $script:Secured = @($plan.Secured)

    # 0b. F1: the global choice is absent or already exactly as planned.
    $conflict = Get-GlobalChoiceConflict $PurposeChoice.Name (Get-ChoiceOptions $PurposeChoice.Labels)
    if ($conflict) { throw [EscalationException]::new($conflict + ' Nothing was changed.') }
    Write-Host "  global choice $($PurposeChoice.Name): $(if (Get-GlobalOptionSet $PurposeChoice.Name) { 'exists, identical' } else { 'absent' })"

    # 0c. F2 / F4: the table is absent, or user-owned with primary name sprk_name; no column of another type; no lookup to another
    #     table or with another delete behaviour; no relationship name taken with another meaning.
    $problems = [System.Collections.Generic.List[string]]::new()
    $e = Get-DvOrNull "EntityDefinitions(LogicalName='$StoredFile')?`$select=LogicalName,OwnershipType,PrimaryNameAttribute"
    if ($e) {
        if ($e.OwnershipType -ne 'UserOwned') { $problems.Add("$StoredFile exists as $($e.OwnershipType), expected UserOwned") }
        if ($e.PrimaryNameAttribute -ne 'sprk_name') { $problems.Add("$StoredFile exists with primary name '$($e.PrimaryNameAttribute)', expected sprk_name") }
        Write-Host "  NOTE: table $StoredFile ALREADY EXISTS - it is verified and skipped, not recreated or changed." -ForegroundColor Yellow
    }
    else { Write-Host "  table ${StoredFile}: absent" }
    foreach ($col in $script:Columns) {
        $a = Get-Attr $col.Table $col.Column
        if ($a -and $a.AttributeType -ne $col.Type) { $problems.Add("$($col.Table).$($col.Column) exists as $($a.AttributeType), expected $($col.Type)") }
    }
    foreach ($s in $script:Lookups) { Test-LookupSpecPreflight $s $problems }
    # An existing two-option column must also MEAN the same: default No (an existing marker defaulting to Yes would be secured
    # before the verify could catch it; review K4).
    foreach ($bc in @(@($StoredFile, 'sprk_firmvisible'), @($Document, $MarkerColumn))) {
        if (-not (Get-Attr $bc[0] $bc[1])) { continue }
        $b = Get-DvOrNull "EntityDefinitions(LogicalName='$($bc[0])')/Attributes(LogicalName='$($bc[1])')/Microsoft.Dynamics.CRM.BooleanAttributeMetadata?`$select=DefaultValue"
        if (-not $b -or $b.DefaultValue -ne $false) { $problems.Add("$($bc[0]).$($bc[1]) exists with default '$($b.DefaultValue)', expected No") }
    }
    # ... and an existing sprk_filestatus must be the local choice with exactly the planned values and labels.
    if (Get-Attr $StoredFile 'sprk_filestatus') {
        $pf = Get-DvOrNull "EntityDefinitions(LogicalName='$StoredFile')/Attributes(LogicalName='sprk_filestatus')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal)"
        $have = (@($pf.OptionSet.Options) | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', '
        $want = (Get-ChoiceOptions $FileStatusLabels | ForEach-Object { "$($_.Value)=$($_.Label)" }) -join ', '
        if (-not $pf -or $pf.OptionSet.IsGlobal -or $have -ne $want) { $problems.Add("$StoredFile.sprk_filestatus exists with [$have], expected the local choice [$want]") }
    }
    if ($problems.Count -gt 0) {
        throw [EscalationException]::new("Existing items conflict with this task (nothing was written, nothing is renamed, renumbered or changed):`n    - " + ($problems -join "`n    - "))
    }
    Write-Host "  $Document.${MarkerColumn}: $(if (Get-Attr $Document $MarkerColumn) { 'exists (Boolean)' } else { 'absent' })"

    # 0d. F5: statuscode is exactly task 012's seven values (plus Superseded if a run already added it).
    $opts = Get-StatusOptions
    Write-Host "  $Invoice statuscode: $(($opts | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label)/s$($_.State)" }) -join ', ')"
    $decision = Get-StatusDecision $opts
    Write-Host "  Superseded $($SupersededStatus.Value) at statecode $($SupersededStatus.State): $(if ($decision -eq 'Skip') { 'present' } else { 'free (will be inserted)' })"
    Write-Host "  no existing item conflicts" -ForegroundColor Green

    # 0e. The profile pair (F3).
    Write-Host "  field security profiles:"
    Test-ProfilePreconditions
    $membersBefore = Get-ProfileMembers

    # The plan, for the schema review.
    Write-Host "  lookup plan ($($script:Lookups.Count) lookups): table.column -> target | relationship | delete | assign/share/unshare/reparent | Related menu" -ForegroundColor Cyan
    foreach ($s in $script:Lookups) {
        $c = Get-ExpectedCascade $s
        Write-Host ("    {0}.{1} -> {2} | {3} | {4} | {5}/{6}/{7}/{8} | Related menu {9}" -f $s.Table, $s.Column, $s.Target, $s.Rel, $s.Delete, $c.Assign, $c.Share, $c.Unshare, $c.Reparent, $s.Menu)
    }
    Write-Host "  field security plan ($($script:Secured.Count) columns): writer = read/create/update; reader = read" -ForegroundColor Cyan
    foreach ($spec in $script:Secured) { Write-Host ("    {0}.{1} ({2}): writer{3}" -f $spec.Table, $spec.Column, $spec.Kind, $(if ($spec.Reader) { ' + reader' } else { ' ONLY (server-only)' })) }

    # --- 1. F1 global choice -----------------------------------------------------------------------------------------------
    Write-Host "1. F1 global choice $($PurposeChoice.Name) (values from 100000000)" -ForegroundColor Cyan
    Add-GlobalChoice $PurposeChoice.Name $PurposeChoice.Display $PurposeChoice.Description (Get-ChoiceOptions $PurposeChoice.Labels)

    # --- 2. F2 sprk_storedfile ---------------------------------------------------------------------------------------------
    Write-Host "2. F2 table $StoredFile (user-owned, audited, primary sprk_name = file name, 400)" -ForegroundColor Cyan
    Add-Table -Table $StoredFile -Display 'Stored File' -Plural 'Stored Files' `
        -Description 'One file the BFF stores outside document management: a Procedure Version''s source, an invoice PDF or supporting file, or an export file. Bytes live in Blob storage at {tenant}/{parent table}/{parent id}/{file id}; this row is the file''s authority (purpose, status, parent, source ids, hashes). Written only by the BFF (ebilling-r1 task 088, model 4.1).' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'File Name' -PrimaryDescription 'The file name.' -PrimaryLength 400 -Ownership UserOwned `
        -SchemaName $StoredFileSchema -PrimarySchema $StoredFileNameSchema
    if ($DryRun -and $script:Planned -contains $StoredFile) {
        Write-Host "  WOULD SET sprk_name max length -> 400 and required level -> ApplicationRequired after the create (Dataverse ignores both in the create body)" -ForegroundColor Cyan
    }
    Set-PrimaryNameLength $StoredFile 'sprk_name' 400
    Set-PrimaryNameRequired $StoredFile 'sprk_name'
    Set-TableAudit $StoredFile
    foreach ($s in $script:Lookups) {
        Set-EbEntity $s.Table
        Add-Lookup -Column $s.Column -SchemaName $s.Schema -RelSchema $s.Rel -Target $s.Target -Display $s.Display -Description $s.Desc -DeleteBehavior $s.Delete -MenuBehavior $s.Menu
    }
    Set-EbEntity $StoredFile
    Add-GlobalChoiceColumn 'sprk_purpose' 'sprk_Purpose' 'Purpose' 'Procedure source, Invoice PDF, Invoice supporting file or Export file. Written only by the BFF (field-secured).' $PurposeChoice.Name
    Add-Column 'sprk_firmvisible' (New-TwoOptionDef 'sprk_FirmVisible' 'Firm-visible' 'Yes only for an invoice PDF the firm uploaded through the SPA; a Submit Invoice copy, a supporting file, a Procedure file and an export file are always No (uac-r2 F1-d). The firm file route serves only Invoice PDF + Firm-visible + Servable. Written only by the BFF (field-secured). Default No.' $false) 'Boolean'
    Add-LocalChoice -Column 'sprk_filestatus' -Schema 'sprk_FileStatus' -Display 'File Status' -Description 'Not yet servable (staged, or committed but not yet re-checked), Servable, or Removed. Every reader refuses a row that is not Servable (model 4.1, 9.1 steps 4-6). Written only by the BFF (field-secured).' -Labels $FileStatusLabels
    Add-Column 'sprk_sourceattachmentidentity' (New-StringDef 'sprk_SourceAttachmentIdentity' 'Source Attachment Identity' 'For a Submit Invoice copy: the marked attachment''s identity inside the source (the .eml''s MIME part: ordinal, name; model 9.1 step 3). Never an Exchange item id. Written only by the BFF (field-secured).' 1000) 'String'
    Add-Column 'sprk_sourceversion' (New-StringDef 'sprk_SourceVersion' 'Source Version' 'The source document''s version number at staging, as ONE canonical string (invariant culture, no padding, no separators; task 090); the commit''s IfRowVersionMatches guard pins it (uac-r2 F2-c). Written only by the BFF (field-secured).' 20) 'String'
    Add-Column 'sprk_sourcesha256' (New-StringDef 'sprk_SourceSha256' 'Source SHA-256' 'SHA-256 of the source bytes at staging (hex), checked again by the flip to Servable (uac-r2 F2-1). Written only by the BFF (field-secured).' 64) 'String'
    Add-Column 'sprk_sha256' (New-StringDef 'sprk_Sha256' 'SHA-256' 'SHA-256 of the stored blob (hex). Written only by the BFF (field-secured).' 64) 'String'
    Add-Column 'sprk_contenttype' (New-StringDef 'sprk_ContentType' 'Content Type' 'MIME content type of the stored file. Written only by the BFF (field-secured).' 255) 'String'
    Add-Column 'sprk_sizebytes' (New-WholeNumberDef 'sprk_SizeBytes' 'Size (bytes)' 'Size of the stored file in bytes, 0 to 2147483647 (a larger file cannot be stored; the BFF refuses it, task 090). Written only by the BFF (field-secured).' 0 2147483647) 'Integer'
    Add-Column $BlobPathColumn (New-StringDef 'sprk_BlobPath' 'Blob Path' 'Server-only: {tenantId}/{parent table}/{parent id}/{file id}. The download checks that its parent id equals the row''s parent (model 4.1, 4.2). Readable and writable only by the BFF (field-secured, no reader row).' 1024) 'String'

    # Every sprk_storedfile column must be securable BEFORE the marker is created and before any column is secured (review F1):
    # a refusal (for example a column whose CanBeSecuredFor* reads false) then stops with the marker NOT created
    # and no column secured (exit 2), never with a marker that any user with Write on a document could set.
    Write-Host "  securable check of the $StoredFile F3 columns (before the marker exists)" -ForegroundColor Cyan
    Assert-Securable @($script:Secured | Where-Object { $_.Table -eq $StoredFile }) "The marker $Document.$MarkerColumn was NOT created and no column was secured. The $StoredFile columns exist unsecured; nothing writes them before task 090."

    # --- 3. F4 the submission marker on sprk_document: created and secured back to back ---------------------------------------
    Write-Host "3. F4 $Document.$MarkerColumn (two options, default No), secured right after its create" -ForegroundColor Cyan
    Set-EbEntity $Document
    Add-Column $MarkerColumn (New-TwoOptionDef 'sprk_RestrictedBySubmission' 'Restricted by invoice submission' 'Yes only on an .eml Document that the Outlook add-in invoice submission (from-email, task 095) saved own-Restricted: written in the SAME create as that narrowing, never in an update. The (a'') predicate skips only a Restricted narrowing that carries this marker, so the firm still sees its own invoice (model 9.1). Written only by the BFF (field-secured); app-only generic writers and the field-mapping push refuse it (InheritedAccessPermission.RefuseAppOnlyWrite). Default No.' $false) 'Boolean'
    $markerSpec = @($script:Secured | Where-Object { $_.Table -eq $Document -and $_.Column -eq $MarkerColumn })
    Assert-Securable $markerSpec "The marker $Document.$MarkerColumn EXISTS UNSECURED: any user with Write on a document can set it until it is secured. Secure it by hand (writer + reader rows) or remove the column before task 095 / 098 ship."
    foreach ($spec in $markerSpec) { Invoke-ColumnSecurity $spec }

    # --- 4. F5 invoice status reason Superseded ----------------------------------------------------------------------------
    Write-Host "4. F5 $Invoice statuscode Superseded = $($SupersededStatus.Value) at statecode $($SupersededStatus.State) (Inactive)" -ForegroundColor Cyan
    Add-SupersededStatus

    # --- 5. F3 field security of the Stored File columns (the marker was secured in step 3) ---------------------------------
    Write-Host "5. F3 field security (writer: BFF application user(s) only; reader: the named business units' users, except on $BlobPathColumn)" -ForegroundColor Cyan
    foreach ($spec in @($script:Secured | Where-Object { $_.Table -eq $StoredFile })) { Invoke-ColumnSecurity $spec }
    Test-OtherProfiles

    # --- Publish (only when something changed; entity-scoped, never a tenant-wide publish) -------------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    Publish-Changes

    # --- Verification (metadata and row-count reads of everything this script owns) -----------------------------------------
    if (-not $DryRun) {
        Write-Host "Verify" -ForegroundColor Cyan
        $bad = [System.Collections.Generic.List[string]]::new()

        foreach ($x in $script:Expect) {
            $a = $null
            for ($i = 0; $i -lt 12 -and -not $a; $i++) {
                $a = Get-Attr $x.Table $x.Column
                if (-not $a) { Start-Sleep -Seconds 5 }
            }
            if (-not $a) { $bad.Add("$($x.Table).$($x.Column) missing"); continue }
            if ($a.AttributeType -ne $x.Type) { $bad.Add("$($x.Table).$($x.Column) is $($a.AttributeType), expected $($x.Type)"); continue }
            if ($x.Type -eq 'Lookup') {
                $lk = Get-DvOrNull "EntityDefinitions(LogicalName='$($x.Table)')/Attributes(LogicalName='$($x.Column)')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
                if (-not $lk -or $lk.Targets -notcontains $x.Target) { $bad.Add("$($x.Table).$($x.Column) does not target $($x.Target)") }
            }
        }

        # F1 exactly the four values; sprk_purpose bound to it.
        $os = Get-GlobalOptionSet $PurposeChoice.Name -Retries 4
        if (-not $os) { $bad.Add("global choice $($PurposeChoice.Name) missing") }
        else {
            Write-Host "  $($PurposeChoice.Name): $(($os.Options | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', ')"
            $conflict = Get-GlobalChoiceConflict $PurposeChoice.Name (Get-ChoiceOptions $PurposeChoice.Labels)
            if ($conflict) { $bad.Add($conflict) }
        }
        $pk = Get-DvOrNull "EntityDefinitions(LogicalName='$StoredFile')/Attributes(LogicalName='sprk_purpose')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Name,IsGlobal)"
        if (-not $pk -or -not $pk.OptionSet.IsGlobal -or $pk.OptionSet.Name -ne $PurposeChoice.Name) { $bad.Add("$StoredFile.sprk_purpose is not bound to $($PurposeChoice.Name)") }

        # F2 table: user-owned, audited, primary name sprk_name at 400.
        $t = Get-DvOrNull "EntityDefinitions(LogicalName='$StoredFile')?`$select=OwnershipType,IsAuditEnabled,PrimaryNameAttribute" -Retries 4
        if (-not $t) { $bad.Add("table $StoredFile missing") }
        else {
            Write-Host "  table ${StoredFile}: $($t.OwnershipType), audited = $($t.IsAuditEnabled.Value), primary name $($t.PrimaryNameAttribute)"
            if ($t.OwnershipType -ne 'UserOwned') { $bad.Add("$StoredFile is $($t.OwnershipType), expected UserOwned") }
            if ($t.IsAuditEnabled.Value -ne $true) { $bad.Add("$StoredFile is not audited") }
            if ($t.PrimaryNameAttribute -ne 'sprk_name') { $bad.Add("$StoredFile primary name is $($t.PrimaryNameAttribute)") }
        }
        foreach ($col in $script:Columns | Where-Object { $_.MaxLength }) {
            $sa = Get-DvOrNull "EntityDefinitions(LogicalName='$($col.Table)')/Attributes(LogicalName='$($col.Column)')/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength"
            if (-not $sa -or [int]$sa.MaxLength -ne $col.MaxLength) { $bad.Add("$($col.Table).$($col.Column) max length is $($sa.MaxLength), expected $($col.MaxLength)") } else { Write-Host "  $($col.Table).$($col.Column): text $($col.MaxLength)" }
        }
        foreach ($col in $script:Columns | Where-Object { $_.Type -eq 'Integer' }) {
            $ia = Get-DvOrNull "EntityDefinitions(LogicalName='$($col.Table)')/Attributes(LogicalName='$($col.Column)')/Microsoft.Dynamics.CRM.IntegerAttributeMetadata?`$select=MinValue,MaxValue"
            if (-not $ia -or [int64]$ia.MinValue -ne $col.MinValue -or [int64]$ia.MaxValue -ne $col.MaxValue) { $bad.Add("$($col.Table).$($col.Column) range is $($ia.MinValue) to $($ia.MaxValue), expected $($col.MinValue) to $($col.MaxValue)") } else { Write-Host "  $($col.Table).$($col.Column): whole number $($col.MinValue) to $($col.MaxValue)" }
        }
        foreach ($bc in @(@($StoredFile, 'sprk_firmvisible'), @($Document, $MarkerColumn))) {
            $b = Get-DvOrNull "EntityDefinitions(LogicalName='$($bc[0])')/Attributes(LogicalName='$($bc[1])')/Microsoft.Dynamics.CRM.BooleanAttributeMetadata?`$select=DefaultValue,DisplayName"
            if (-not $b -or $b.DefaultValue -ne $false) { $bad.Add("$($bc[0]).$($bc[1]) default is not No") } else { Write-Host "  $($bc[0]).$($bc[1]): two options '$($b.DisplayName.UserLocalizedLabel.Label)', default No" }
        }
        $fsp = Get-DvOrNull "EntityDefinitions(LogicalName='$StoredFile')/Attributes(LogicalName='sprk_filestatus')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal)"
        $fs = (@($fsp.OptionSet.Options) | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', '
        $fsWant = (Get-ChoiceOptions $FileStatusLabels | ForEach-Object { "$($_.Value)=$($_.Label)" }) -join ', '
        Write-Host "  $StoredFile.sprk_filestatus options: $fs (local: $(-not $fsp.OptionSet.IsGlobal))"
        if ($fs -ne $fsWant -or $fsp.OptionSet.IsGlobal) { $bad.Add("$StoredFile.sprk_filestatus options are [$fs], expected the local choice [$fsWant]") }

        # Forms and views: this script creates none. Creating a table makes the platform add its default system form(s) and
        # views; they are listed here for the record (task 088 acceptance: "no form, view or subgrid" means none authored here).
        $forms = @((Invoke-Dv "systemforms?`$select=name,type&`$filter=objecttypecode eq '$StoredFile'").value | ForEach-Object { "$($_.name) (type $($_.type))" })
        $views = @((Invoke-Dv "savedqueries?`$select=name,querytype,isdefault&`$filter=returnedtypecode eq '$StoredFile'").value | ForEach-Object { "$($_.name) (querytype $($_.querytype))" })
        Write-Host "  $StoredFile platform-default forms ($($forms.Count)): $($forms -join '; ')"
        Write-Host "  $StoredFile platform-default views ($($views.Count)): $($views -join '; ')"

        # Delete behaviour and cascade of every lookup.
        foreach ($s in $script:Lookups) {
            $rel = Get-LookupRelationship $s.Table $s.Column
            if (-not $rel) { $bad.Add("no many-to-one relationship for $($s.Table).$($s.Column)"); continue }
            $cc = $rel.CascadeConfiguration
            Write-Host ("  {0}.{1} -> {2}: {3}  delete {4}, assign {5}, share {6}, unshare {7}, reparent {8}" -f $s.Table, $s.Column, $s.Target, $rel.SchemaName, $cc.Delete, $cc.Assign, $cc.Share, $cc.Unshare, $cc.Reparent)
            foreach ($m in @(Get-CascadeMismatches $cc (Get-ExpectedCascade $s))) { $bad.Add("$($s.Table).$($s.Column) CascadeConfiguration: $m") }
            if ($rel.SchemaName -ne $s.Rel) { $bad.Add("$($s.Table).$($s.Column) relationship is named $($rel.SchemaName), expected $($s.Rel)") }
            $menu = Get-AssociatedMenuProblem $rel.AssociatedMenuConfiguration $s.Menu
            if ($menu) { $bad.Add("$($s.Table).$($s.Column) ($($rel.SchemaName)) $menu") } else { Write-Host "    $($rel.SchemaName): Related menu $($rel.AssociatedMenuConfiguration.Behavior)" }
        }

        # F3: IsSecured plus exactly the planned rows; sprk_name and sprk_createdbyperson stay unsecured.
        foreach ($spec in $script:Secured) {
            $a = Get-Attr $spec.Table $spec.Column 'LogicalName,IsSecured'
            if (-not $a -or -not $a.IsSecured) { $bad.Add("$($spec.Table).$($spec.Column) is not IsSecured"); continue }
            $r = Get-FieldPermission $script:ReaderId $spec.Table $spec.Column
            $w = Get-FieldPermission $script:WriterId $spec.Table $spec.Column
            $wOk = $w -and $w.canread -eq 4 -and $w.cancreate -eq 4 -and $w.canupdate -eq 4
            $rOk = if ($spec.Reader) { $r -and $r.canread -eq 4 -and $r.cancreate -eq 0 -and $r.canupdate -eq 0 } else { -not $r }
            Write-Host "  $($spec.Table).$($spec.Column): IsSecured = true, writer row $(if ($wOk) { 'read/create/update' } else { 'WRONG' }), reader row $(if ($spec.Reader) { if ($rOk) { 'read-only' } else { 'WRONG' } } else { if ($rOk) { 'none (server-only)' } else { 'PRESENT - WRONG' } })"
            if (-not $wOk) { $bad.Add("$($spec.Table).$($spec.Column): writer profile row is not Read/Create/Update") }
            if (-not $rOk) { $bad.Add("$($spec.Table).$($spec.Column): reader profile row is not as planned ($(if ($spec.Reader) { 'Read only' } else { 'none' }))") }
        }
        foreach ($u in 'sprk_name', $CreatorColumn) {
            $a = Get-Attr $StoredFile $u 'LogicalName,IsSecured'
            if ($a -and $a.IsSecured) { $bad.Add("$StoredFile.$u is secured; this task leaves it unsecured") }
        }
        $membersAfter = Get-ProfileMembers
        if ($membersAfter -ne $membersBefore) { $bad.Add("a profile's members changed during the run: before $membersBefore, after $membersAfter") } else { Write-Host "  profile members unchanged" }

        # F4: no document holds the marker.
        $yes = Get-RowCountExact 'sprk_documents' 'sprk_documentid' "$MarkerColumn eq true"
        if ($null -eq $yes -or $yes -ne 0) { $bad.Add("$Document rows holding $MarkerColumn = Yes: $yes (expected 0)") } else { Write-Host "  $Document rows holding ${MarkerColumn} = Yes: 0" }

        # F5: the seven task-012 values unchanged plus Superseded at statecode 1; no invoice holds it.
        $opts = Get-StatusOptions
        Write-Host "  $Invoice statuscode: $(($opts | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label)/s$($_.State)" }) -join ', ')"
        try { if ((Get-StatusDecision $opts) -ne 'Skip') { $bad.Add("$Invoice statuscode has no $($SupersededStatus.Value) Superseded") } } catch { $bad.Add($_.Exception.Message) }
        $held = Get-RowCountExact 'sprk_invoices' 'sprk_invoiceid' "statuscode eq $($SupersededStatus.Value)"
        if ($null -eq $held -or $held -ne 0) { $bad.Add("invoices at statuscode $($SupersededStatus.Value): $held (expected 0)") } else { Write-Host "  invoices at statuscode $($SupersededStatus.Value): 0" }

        if ($bad.Count -gt 0) { throw "Verification failed: $($bad -join '; ')" }
        Write-Host "  all $($script:Expect.Count) expected columns present with the right type and target; every check above passed" -ForegroundColor Green
    }
}
catch [EscalationException] {
    Write-Host ""
    Write-Host "ESCALATION - STOPPED, nothing further was changed (what this run already changed is published below): $($_.Exception.Message)" -ForegroundColor Red
    Publish-AfterStop
    exit 2
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Items already created stay (the script is re-runnable)." -ForegroundColor Red
    Publish-AfterStop
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("created : {0}" -f $script:Created.Count)
$script:Created | ForEach-Object { Write-Host "    + $_" }
Write-Host ("changed : {0}" -f $script:Changed.Count)
$script:Changed | ForEach-Object { Write-Host "    ~ $_" }
Write-Host ("skipped : {0}" -f $script:Skipped.Count)
$script:Skipped | ForEach-Object { Write-Host "    = $_" }
if ($State.Deferred.Count -gt 0) { Write-Host ("deferred: {0}  {1}" -f $State.Deferred.Count, ($State.Deferred -join ', ')) -ForegroundColor DarkYellow }
if ($Gaps.Count -gt 0) {
    Write-Host ""
    Write-Host "FINDINGS ($($Gaps.Count)):" -ForegroundColor Red
    $Gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
}
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
elseif ($script:Created.Count -eq 0 -and $script:Changed.Count -eq 0) { Write-Host "Idempotent: nothing was created or changed." -ForegroundColor Green }
if ($Gaps.Count -gt 0) { exit 1 }
exit 0
