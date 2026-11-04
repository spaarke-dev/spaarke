#!/usr/bin/env pwsh
<#
.SYNOPSIS
    eBilling R1 (task 095c-5; uac-r2 2026-11-04) - creates sprk_invoice.sprk_firmhold, the invoice's own "firm needed" hold, field-secures it so
    ONLY the server sets it ("Spaarke BFF-Managed Field Writers": read, create, update; "Spaarke BFF-Managed Field Readers": read only), and
    backfills it on every invoice a linked submission already holds for a firm. -WhatIf is a dry run, -Apply writes, -Verify checks.

.DESCRIPTION
    WHY (095c-5). Task 095c-4 finds the firm_missing hold through the submissions linked to an invoice (sprk_invoicesubmission.sprk_invoice).
    That link is not field-secured, so a direct write that removes or repoints it, or a delete of the submission, hides the hold. The hold
    now also lives on the invoice: the persister sets sprk_firmhold in the same create as the held outcome, 095d's assign-firm is the only
    clearer (typed writer), and the BFF reads it app-only (InvoiceFirmHold). Held = the invoice's own hold OR any linked submission's.

    THE COLUMN. sprk_invoice.sprk_firmhold, Single line of text (100), optional, audited. It stores the reason code
    (ebilling.submission.firm_missing); any non-blank value holds (fail closed). A reason-code string rather than Two Options: it names
    which hold it is in the same vocabulary as sprk_outcomereasoncode, it needs no option set, and an unrecognised value still holds.

    PROFILES (REUSED, never created or re-membered here):
      "Spaarke BFF-Managed Field Writers" (unified-access-control-r2 task 133; members: the BFF application user(s) ONLY): read=4 create=4
          update=4 on the column.
      "Spaarke BFF-Managed Field Readers" (same task; on EVERY business unit's default team): read=4 create=0 update=0. The BFF never reads
          the column on a user's token (a user outside this profile would read null, which is "no hold"); the grant lets the form and a
          view show it.
      Any other profile that can create or update the column is FAIL in every mode, except the platform's own "System Administrator"
      profile, matched by its fixed id.

    PRECONDITIONS (every mode; -Apply refuses unless all pass):
      (p0) the column exists and can be secured for read, create and update - or, when it does not exist yet, sprk_invoice exists and the
           column is created by -Apply (step s) before anything else;
      (p1) the writer profile exists, exactly once, and is in -SolutionUniqueName;
      (p2) its members are exactly the -BffApplicationIds application users (no other user, no team);
      (p3) no business unit's default team holds the writer profile;
      (p4) no sprk_fieldmappingrule, sprk_aitopicregistry or sprk_emailupdatefield row targets the column;
      (p5) the column is in -SolutionUniqueName (its own row, or its table with rootcomponentbehavior 0);
      (p6) informational: the forms and views that bind the column, how many invoices carry a hold now, and whether each BFF application
           user holds System Administrator;
      (p7) the reader profile exists, exactly once, and is in -SolutionUniqueName;
      (p8) the reader profile is on EVERY business unit's default team;
      (p9) the operator (this az identity) holds System Administrator: the backfill reads sprk_invoicesubmission.sprk_findingsjson, which is
           field-secured (task 122), so another identity would read it as null and under-count.

    STEPS (-Apply), in this order:
      (s) create the column, unsecured, in -SolutionUniqueName (header MSCRM.SolutionUniqueName), when it does not exist; wait until it is
          visible. A new column holds no value, so nobody is masked by securing it next;
      (a) secure the column, then IMMEDIATELY grant the writer profile read=4 create=4 update=4 and the reader profile read=4 create=0
          update=0 (12 retries each on 0x8004f508). If a grant fails, the column is REVERTED to not secured and published before the script
          stops; if the revert fails too it prints "RECOVERY REQUIRED NOW". Re-running is the resume path;
      (b) the permissions are exactly as above;
      (c) any OTHER profile that can create or update the column is FAIL (the System Administrator profile excepted);
      (d) publish sprk_invoice - only when this run wrote metadata;
      (e) BACKFILL: every invoice that a linked submission holds for a firm (the reason ebilling.submission.firm_missing, the firm_missing
          pipeline finding, a rules.not_checked line finding with that cause, or findings that cannot be parsed - the BFF's
          InvoiceFirmHold.IsHeld) and whose sprk_firmhold is empty gets sprk_firmhold = ebilling.submission.firm_missing. Only after (b)
          passed. -WhatIf and -Verify only count.

    A second -Apply reports every item OK and writes nothing (no publish either).

    -VERIFY passes only when the column exists and IS secured, the writer profile holds read=4 create=4 update=4, the reader profile holds
    read=4 create=0 update=0, NO other profile can create or update it (the System Administrator profile excepted), and no held invoice is
    left without its hold (e).

    WHAT IT IS NOT. Not an access column: it decides no visibility. It is refused to the BFF's app-only generic writers
    (InheritedAccessPermission.AccessColumns, sprk_invoice) together with sprk_invoice.sprk_vendororg. Rollback: un-secure the column by
    hand (make.powerapps.com) and revert the SpaarkeMaster source (Entity.xml, FieldSecurityProfiles.xml) by a commit; the column is not
    deleted by this script.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users, the ONLY members of the writer profile (dev:
    5967251e-171c-46fe-a6c2-ef843c90309d and, while it is still an application user, 1e40baad-e065-4aea-a8d4-4b7ab273458c).
    Per-customer input (D-13), never hard-coded.

.PARAMETER SolutionUniqueName
    The unmanaged solution the column and the profiles travel in. Default SpaarkeMaster.

.PARAMETER Apply
    Write: create, secure, grant, publish, backfill. Without -Apply, -Verify or -WhatIf the script refuses to run.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -WhatIf or -Apply.

.EXAMPLE
    pwsh scripts/ebilling/Set-EbillingInvoiceFirmHoldColumn.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -WhatIf
    pwsh scripts/ebilling/Set-EbillingInvoiceFirmHoldColumn.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Apply
    pwsh scripts/ebilling/Set-EbillingInvoiceFirmHoldColumn.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify

.NOTES
    Project: ebilling-r1, task 095c-5. Shape: scripts/ebilling/Set-EbillingResubmissionOfFieldSecurity.ps1 (preconditions, the reader grant,
    revert-on-failed-grant, publish only on a write, solution membership) and Set-EbillingFindingsJsonFieldSecurity.ps1. Pinned against the
    SpaarkeMaster source by InvoiceFirmHoldFieldSecurityAgreementTests; every BFF reader of the column is pinned app-only by
    InvoiceFirmHoldAppOnlyGuardTests. Auth: the operator's own az CLI identity (System Administrator in the environment). No secrets.
    Exit codes: 0 = pass / done, 1 = a gap or an error.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$IsDryRun = [bool]$WhatIfPreference
if ($IsDryRun -and $Verify) { throw '-WhatIf and -Verify are separate modes.' }
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes.' }
if (-not $Apply -and -not $Verify -and -not $IsDryRun) { throw 'Choose a mode: -WhatIf (dry run), -Apply (write) or -Verify (check).' }
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($BffApplicationIds.Count -eq 0) { throw '-BffApplicationIds: at least one application id is required.' }
foreach ($id in $BffApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-BffApplicationIds: '$id' is not a GUID." } }
$IsWrite = $Apply -and -not $IsDryRun
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (pinned by InvoiceFirmHoldFieldSecurityAgreementTests) ────────────────────────────────────────
$Table = 'sprk_invoice'
$Column = 'sprk_firmhold'
$ColumnSchemaName = 'sprk_FirmHold'
$ColumnMaxLength = 100
$HoldValue = 'ebilling.submission.firm_missing'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
# The platform's own System Administrator field security profile: a fixed id in every environment, and Dataverse gives it a row on
# every secured column. The one profile besides the writer and the reader allowed a row; matched by id, never by name.
$SystemAdministratorProfileId = '572329c1-a042-4e22-be47-367c6374ea45'

$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
}
function Invoke-DvGet([string]$Path, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $uri = if ($Path -like 'https://*') { $Path } else { "$Api/$Path" }
    Invoke-RestMethod -Uri $uri -Headers $h -Method Get
}
function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json
}
function Get-AllPages([string]$Path) {
    $rows = [System.Collections.Generic.List[object]]::new()
    $next = $Path
    while ($next) {
        $page = Invoke-DvGet $next @{ Prefer = 'odata.maxpagesize=500' }
        foreach ($r in @($page.value)) { $rows.Add($r) }
        $link = $page.PSObject.Properties['@odata.nextLink']
        $next = if ($link) { $link.Value } else { $null }
    }
    return , $rows
}

. (Join-Path $PSScriptRoot '..' 'common' 'DataverseSolutionMembership.ps1')
$gaps = [System.Collections.Generic.List[string]]::new()
$script:Wrote = $false
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
    if ($State -eq 'DONE') { $script:Wrote = $true }
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host "Column      : $Table.$Column (the invoice's own firm hold; only the server sets it: BFF writer profile writes, reader profile reads)"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (-WhatIf, no writes)' } else { 'APPLY (writes)' }))
$attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')"
$publishBody = @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" }

# ── PRECONDITIONS ───────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`nPreconditions"
$tableMeta = try { Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId,EntitySetName" } catch { $null }
if (-not $tableMeta) { throw "$Table does not exist in this environment." }
function Get-ColumnMeta { try { Invoke-DvGet "$attrPath`?`$select=MetadataId,AttributeType,IsSecured,CanBeSecuredForCreate,CanBeSecuredForRead,CanBeSecuredForUpdate" } catch { $null } }
$attr = Get-ColumnMeta
$columnMissing = -not $attr
if ($columnMissing) {
    if ($Verify) { Report 'MISSING' "(p0) $Table.$Column does not exist in this environment" }
    else { Report 'INFO' "(p0) $Table.$Column does not exist yet - step (s) creates it (String, $ColumnMaxLength, optional, audited)" }
}
elseif ($attr.AttributeType -ne 'String') { Report 'FAIL' "(p0) $Table.$Column exists but is $($attr.AttributeType), not String - resolve by hand; this script changes no existing column type" }
elseif (-not ($attr.CanBeSecuredForCreate -and $attr.CanBeSecuredForRead -and $attr.CanBeSecuredForUpdate)) {
    Report 'FAIL' "(p0) $Table.$Column cannot be secured for read, create and update"
}
else { Report 'OK' "(p0) $Table.$Column exists (String) and can be secured for read, create and update" }

$writers = @((Invoke-DvGet ("fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$WriterProfileName'" +
    '&$expand=teamprofiles_association($select=teamid,name,isdefault),systemuserprofiles_association($select=systemuserid,fullname,applicationid)')).value)
$writer = $null
if ($writers.Count -eq 1) { $writer = $writers[0]; Report 'OK' "(p1) profile '$WriterProfileName' exists ($($writer.fieldsecurityprofileid))" }
else { Report 'FAIL' "(p1) $($writers.Count) profile(s) named '$WriterProfileName' - exactly one is required; it is created by scripts/Set-RecordCreatorPersonSchema.ps1 (this script never creates it)" }

$readers = @((Invoke-DvGet ("fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$ReaderProfileName'" +
    '&$expand=teamprofiles_association($select=teamid,name)')).value)
$reader = $null
if ($readers.Count -eq 1) { $reader = $readers[0]; Report 'OK' "(p7) profile '$ReaderProfileName' exists ($($reader.fieldsecurityprofileid))" }
else { Report 'FAIL' "(p7) $($readers.Count) profile(s) named '$ReaderProfileName' - exactly one is required; it is created by scripts/Set-RecordCreatorPersonSchema.ps1 (this script never creates it)" }

$solution = @((Invoke-DvGet "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
$membership = $null
if (-not $solution) { Report 'FAIL' "(p1) solution '$SolutionUniqueName' not found" }
else {
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
    foreach ($p in @(@{ Id = 'p1'; Profile = $writer; Name = $WriterProfileName }, @{ Id = 'p7'; Profile = $reader; Name = $ReaderProfileName })) {
        if (-not $p.Profile) { continue }
        # A field security profile is a ROOT component: only its own solutioncomponents row ('Direct') counts.
        if (Test-DvInSolution -Membership $membership -ComponentId $p.Profile.fieldsecurityprofileid) { Report 'OK' "($($p.Id)) '$($p.Name)' is in $SolutionUniqueName" }
        else { Report 'FAIL' "($($p.Id)) '$($p.Name)' is not in $SolutionUniqueName - its permission on $Column would not travel" }
    }
}

$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname,isdisabled&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if (-not $u) { Report 'FAIL' "(p2) no application user for appId $appId in this environment"; continue }
    $bffUsers += $u
}
if ($writer) {
    $members = @($writer.systemuserprofiles_association)
    $memberIds = @($members | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    $allowed = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    foreach ($u in $bffUsers) {
        if ($memberIds -contains $u.systemuserid.ToString().ToLowerInvariant()) { Report 'OK' "(p2) BFF app user '$($u.fullname)' is a member of $WriterProfileName" }
        else { Report 'FAIL' "(p2) BFF app user '$($u.fullname)' is NOT a member of $WriterProfileName - it could not write or read $Column once secured (unless System Administrator). Membership is maintained by Set-RecordCreatorPersonSchema.ps1, never here" }
    }
    foreach ($m in $members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowed }) {
        $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        Report 'FAIL' "(p2) $WriterProfileName member '$($m.fullname)' is $kind - it could set or clear every invoice's firm hold"
    }
    $teams = @($writer.teamprofiles_association)
    foreach ($tm in $teams) {
        $what = if ($tm.isdefault) { "(p3) $WriterProfileName is on business-unit DEFAULT team '$($tm.name)'" } else { "(p2) $WriterProfileName has TEAM '$($tm.name)'" }
        Report 'FAIL' "$what - every member could clear the firm hold"
    }
    if (@($teams | Where-Object { $_.isdefault }).Count -eq 0) { Report 'OK' "(p3) no business-unit default team holds $WriterProfileName" }
}

$configuredWriters = 0
foreach ($ch in @(@{ T = 'sprk_fieldmappingrule'; C = 'sprk_targetfield' }, @{ T = 'sprk_aitopicregistry'; C = 'sprk_targetfield' }, @{ T = 'sprk_emailupdatefield'; C = 'sprk_targetfieldlogicalname' })) {
    $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$($ch.T)')?`$select=EntitySetName").EntitySetName
    foreach ($row in @((Invoke-DvGet "$set`?`$select=$($ch.T)id&`$filter=$($ch.C) eq '$Column'").value)) {
        $configuredWriters++
        Report 'FAIL' "(p4) $($ch.T) row $($row."$($ch.T)id") targets $Column - only the BFF writes it; remove or retarget the row"
    }
}
if ($configuredWriters -eq 0) { Report 'OK' "(p4) no field-mapping rule, AI topic-registry row or email update field targets $Column" }

if ($membership -and $attr) {
    $how = Test-DvInSolution -Membership $membership -ComponentId $attr.MetadataId -TableMetadataId $tableMeta.MetadataId
    if ($how) { Report 'OK' "(p5) $Table.$Column is in $SolutionUniqueName ($how)" }
    else { Report 'FAIL' "(p5) $Table.$Column is not in $SolutionUniqueName - the secured state would not travel" }
}
elseif ($columnMissing) { Report 'INFO' "(p5) $Table.$Column will be created in $SolutionUniqueName (header MSCRM.SolutionUniqueName); checked after (s)" }

if ($attr) {
    $withHold = @((Invoke-DvGet "$($tableMeta.EntitySetName)?`$select=$($Table)id&`$filter=$Column ne null").value).Count
    Report 'INFO' "(p6) $withHold $Table row(s) carry a firm hold now"
}
$forms = @((Invoke-DvGet "systemforms?`$select=name,type,formxml&`$filter=objecttypecode eq '$Table'").value | Where-Object { $_.formxml -match "datafieldname=`"$Column`"" })
foreach ($f in $forms) { Report 'INFO' "(p6) form '$($f.name)' (type $($f.type)) binds $Column - shown read-only to users outside $WriterProfileName" }
$views = @((Invoke-DvGet "savedqueries?`$select=name,fetchxml&`$filter=returnedtypecode eq '$Table'").value | Where-Object { $_.fetchxml -match "name=`"$Column`"" })
foreach ($v in $views) { Report 'INFO' "(p6) view '$($v.name)' names $Column - its readers are in $ReaderProfileName (every default team)" }
if ($forms.Count -eq 0 -and $views.Count -eq 0) { Report 'INFO' "(p6) no form or system view binds $Column (no existing reader to blank)" }
foreach ($u in $bffUsers) {
    $isAdmin = @((Invoke-DvGet "systemusers($($u.systemuserid))/systemuserroles_association?`$select=name&`$filter=name eq 'System Administrator'").value).Count -gt 0
    $adminNote = if ($isAdmin) { 'holds System Administrator (full field access; no masked window for it)' } else { 'does NOT hold System Administrator - it reads the column as null between (a) securing and the writer grant (seconds); no invoice holds a value before (e)' }
    Report 'INFO' "(p6) BFF app user '$($u.fullname)'$(if ($u.isdisabled) { ' (DISABLED)' }) $adminNote"
}

if ($reader) {
    $onReader = @($reader.teamprofiles_association | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
    $defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name&$filter=isdefault eq true&$expand=businessunitid($select=name)').value)
    if ($defaultTeams.Count -eq 0) { Report 'FAIL' '(p8) no business-unit default team was read - the query is wrong' }
    foreach ($t in $defaultTeams) {
        if ($onReader -contains $t.teamid.ToString().ToLowerInvariant()) { Report 'OK' "(p8) reader profile on default team '$($t.name)' (BU '$($t.businessunitid.name)')" }
        else { Report 'FAIL' "(p8) default team '$($t.name)' (BU '$($t.businessunitid.name)') is NOT on the reader profile - its users would see the hold EMPTY on a form. Adding it is a team change (the owner's); this script never makes it" }
    }
}

$me = Invoke-DvGet 'WhoAmI'
$operatorAdmin = @((Invoke-DvGet "systemusers($($me.UserId))/systemuserroles_association?`$select=name&`$filter=name eq 'System Administrator'").value).Count -gt 0
if ($operatorAdmin) { Report 'OK' "(p9) the operator ($($me.UserId)) holds System Administrator: the backfill reads the field-secured submission findings" }
else { Report 'FAIL' "(p9) the operator ($($me.UserId)) does NOT hold System Administrator - sprk_invoicesubmission.sprk_findingsjson (field-secured) would read as null and the backfill would under-count" }

if ($IsWrite -and $gaps.Count -gt 0) {
    Write-Host "`nREFUSED: -Apply needs every precondition to pass ($($gaps.Count) gap(s)). Nothing was written." -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

# ── (s) CREATE the column when it does not exist ────────────────────────────────────────────────────────────
Write-Host "`n(s) The column"
if (-not $columnMissing) { Report 'OK' "$Table.$Column exists" }
elseif ($Verify) { Report 'MISSING' "$Table.$Column does not exist" }
elseif ($IsDryRun) { Report 'WOULD' "create $Table.$Column (String $ColumnMaxLength, optional, audited, '$SolutionUniqueName'), unsecured; (a) secures it next" }
elseif ($PSCmdlet.ShouldProcess("$Table.$Column", 'Create the column')) {
    $label = { param($t) @{ '@odata.type' = 'Microsoft.Dynamics.CRM.Label'; LocalizedLabels = @(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.LocalizedLabel'; Label = $t; LanguageCode = 1033 }) } }
    $definition = @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'
        SchemaName     = $ColumnSchemaName
        DisplayName    = & $label 'Firm Hold'
        Description    = & $label 'Set by Spaarke when the invoice was submitted without a firm (the reason code ebilling.submission.firm_missing). While set, the invoice cannot be approved, returned to a firm, corrected or exported. Only Spaarke sets or clears it (field-secured).'
        RequiredLevel  = @{ Value = 'None' }
        MaxLength      = $ColumnMaxLength
        FormatName     = @{ Value = 'Text' }
        IsAuditEnabled = @{ Value = $true }
    }
    Invoke-DvWrite POST "EntityDefinitions(LogicalName='$Table')/Attributes" $definition @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName } | Out-Null
    for ($i = 0; $i -lt 12 -and -not $attr; $i++) { Start-Sleep -Seconds 5; $attr = Get-ColumnMeta }
    if (-not $attr) { throw "$Table.$Column was created but is not visible in metadata after 60 s; re-run -Apply (it resumes)." }
    Report 'DONE' "created $Table.$Column in $SolutionUniqueName"
    if (-not ($attr.CanBeSecuredForCreate -and $attr.CanBeSecuredForRead -and $attr.CanBeSecuredForUpdate)) { throw "$Table.$Column cannot be secured for read, create and update; nothing else was written." }
}
else { Report 'FAIL' "$Table.$Column was not created (declined at the confirmation)" }

# ── (a) SECURE, then grant the profiles at once ─────────────────────────────────────────────────────────────
Write-Host "`n(a) Field-level security"
function Get-Permission([string]$ProfileId) {
    @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $ProfileId and entityname eq '$Table' and attributelogicalname eq '$Column'").value) | Select-Object -First 1
}
function Grant-Permission([string]$ProfileId, [int]$Create) {
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-DvWrite POST 'fieldpermissions' @{
                entityname = $Table; attributelogicalname = $Column
                canread = 4; cancreate = $Create; canupdate = $Create
                'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($ProfileId)"
            } | Out-Null
            return
        } catch {
            $notYetSecured = "$($_.ErrorDetails.Message) $($_.Exception.Message)" -match '0x8004f508'
            if (-not $notYetSecured -or $attempt -ge 12) { throw }
            Write-Host "    $Table.$Column not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
            Start-Sleep -Seconds 5
        }
    }
}
function Set-ColumnSecured([bool]$Value) {
    # GET-then-PUT of the typed attribute definition (scripts/Set-RegardingPairFieldSecurity.ps1: proven live on string and lookup columns).
    $typed = Invoke-DvGet $attrPath
    $typed.IsSecured = $Value
    Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
}
# The writer first (the BFF's own read and write), then the reader. Create = the value of create AND update.
$specs = @()
if ($writer) { $specs += @{ Id = $writer.fieldsecurityprofileid; Name = $WriterProfileName; Create = 4 } }
if ($reader) { $specs += @{ Id = $reader.fieldsecurityprofileid; Name = $ReaderProfileName; Create = 0 } }
function Grant-MissingPermissions {
    foreach ($s in $specs) {
        if (-not (Get-Permission $s.Id) -and $PSCmdlet.ShouldProcess("$Table.$Column", "Grant $($s.Name) read=4 create=$($s.Create) update=$($s.Create)")) {
            Grant-Permission $s.Id $s.Create
            Report 'DONE' "granted $($s.Name) on $Table.$Column (read=4 create=$($s.Create) update=$($s.Create))"
        }
    }
}
function Write-MaskedRecovery([string]$Cause) {
    Write-Host ''
    Write-Host "  !!! RECOVERY REQUIRED NOW - $Table.$Column may be field-secured WITHOUT its profile permissions !!!" -ForegroundColor Red
    Write-Host '  A BFF application user without System Administrator then reads the hold as empty and cannot write it:' -ForegroundColor Red
    Write-Host '  a submission with no firm cannot be committed, and a backfilled hold is read only through its submissions.' -ForegroundColor Red
    Write-Host "  Cause: $Cause" -ForegroundColor Red
    Write-Host '  Do ONE of these, now:' -ForegroundColor Red
    Write-Host '    (1) fix the cause and re-run -Apply: on an already-secured column it grants only the missing profile' -ForegroundColor Red
    Write-Host '        permissions (it never re-secures), then run -Verify;' -ForegroundColor Red
    Write-Host "    (2) or un-secure the column: make.powerapps.com > Tables > $Table > Columns > $Column > Advanced options >" -ForegroundColor Red
    Write-Host "        clear 'Enable column security', save, publish $Table." -ForegroundColor Red
}

if (-not $attr) {
    if ($IsDryRun) { Report 'WOULD' "secure $Table.$Column, then at once grant $WriterProfileName read/create/update and $ReaderProfileName read only" }
    elseif ($Verify) { Report 'MISSING' "$Table.$Column is not field-secured (it does not exist)" }
}
elseif ($attr.IsSecured) {
    Report 'OK' "$Table.$Column is field-secured"
    if ($IsWrite) {
        try { Grant-MissingPermissions }
        catch { Write-MaskedRecovery "granting a missing profile permission on the already-secured column failed: $($_.Exception.Message)"; throw }
    }
}
elseif ($Verify) { Report 'MISSING' "$Table.$Column is NOT field-secured - any user with Write on an invoice can clear its firm hold" }
elseif ($IsDryRun -or $specs.Count -lt 2) { Report 'WOULD' "secure $Table.$Column, then at once grant $WriterProfileName read/create/update and $ReaderProfileName read only" }
elseif (-not $PSCmdlet.ShouldProcess("$Table.$Column", "Field-secure the column, then at once grant $WriterProfileName read/create/update and $ReaderProfileName read")) { Report 'FAIL' "$Table.$Column was not secured (declined at the confirmation)" }
else {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    Set-ColumnSecured $true
    Report 'DONE' "secured $Table.$Column"
    try {
        Grant-MissingPermissions
    } catch {
        $grantError = $_
        Write-Host "  FAIL     granting the profiles on $Table.$Column failed after it was secured: $($grantError.Exception.Message)" -ForegroundColor Red
        Write-Host "           reverting $Table.$Column to NOT field-secured, so no BFF user is left masked..." -ForegroundColor Yellow
        try {
            Set-ColumnSecured $false
            Invoke-DvWrite POST 'PublishXml' $publishBody | Out-Null
            $back = (Invoke-DvGet "$attrPath`?`$select=IsSecured").IsSecured
            if ($back) { throw "the revert was accepted but $Table.$Column still reads IsSecured=true" }
            Write-Host "  REVERTED $Table.$Column is NOT field-secured again. Fix the cause, then re-run -Apply. The backfill (e) did not run." -ForegroundColor Yellow
        } catch {
            Write-MaskedRecovery "the grant failed ($($grantError.Exception.Message)) AND the revert failed ($($_.Exception.Message))"
        }
        throw $grantError
    }
    $clock.Stop()
    Report 'INFO' ("masked window {0:N1}s (secure -> both grants)" -f $clock.Elapsed.TotalSeconds)
}

# ── (b) THE PERMISSIONS ─────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Permissions on $Table.$Column"
$permissionsOk = $true
foreach ($s in $specs) {
    $perm = if ($attr) { Get-Permission $s.Id } else { $null }
    if ($perm) {
        if ($perm.canread -eq 4 -and $perm.cancreate -eq $s.Create -and $perm.canupdate -eq $s.Create) { Report 'OK' "$($s.Name) (read=4 create=$($s.Create) update=$($s.Create))" }
        else { $permissionsOk = $false; Report 'FAIL' "$($s.Name) is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($s.Create) update=$($s.Create)" }
    }
    elseif ($Verify) { $permissionsOk = $false; Report 'MISSING' "$($s.Name) on $Table.$Column" }
    elseif ($IsDryRun) { $permissionsOk = $false; Report 'WOULD' "grant $($s.Name) on $Table.$Column (read=4 create=$($s.Create) update=$($s.Create))" }
    else { $permissionsOk = $false; Report 'FAIL' "$($s.Name) on $Table.$Column is still missing after the run granted it" }
}

# ── (c) every OTHER profile that can create or update ───────────────────────────────────────────────────────
Write-Host "`n(c) Other profiles"
$writerId = if ($writer) { $writer.fieldsecurityprofileid.ToString().ToLowerInvariant() } else { '' }
$readerId = if ($reader) { $reader.fieldsecurityprofileid.ToString().ToLowerInvariant() } else { '' }
$rows = @((Invoke-DvGet "fieldpermissions?`$select=canread,cancreate,canupdate&`$expand=fieldsecurityprofileid(`$select=fieldsecurityprofileid,name)&`$filter=entityname eq '$Table' and attributelogicalname eq '$Column' and (canread eq 4 or canupdate eq 4 or cancreate eq 4)").value |
    Where-Object { $_.fieldsecurityprofileid.fieldsecurityprofileid.ToString().ToLowerInvariant() -notin $SystemAdministratorProfileId, $writerId, $readerId })
$others = @($rows | Where-Object { $_.cancreate -eq 4 -or $_.canupdate -eq 4 })
foreach ($o in $others) { Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' ($($o.fieldsecurityprofileid.fieldsecurityprofileid)) can write $Table.$Column (read=$($o.canread) create=$($o.cancreate) update=$($o.canupdate)) - only the BFF writer profile may" }
foreach ($o in @($rows | Where-Object { $_.cancreate -ne 4 -and $_.canupdate -ne 4 })) { Report 'INFO' "profile '$($o.fieldsecurityprofileid.name)' only reads $Table.$Column (read=$($o.canread))" }
if ($others.Count -eq 0) { Report 'OK' "no profile besides '$WriterProfileName' and the System Administrator profile ($SystemAdministratorProfileId) can create or update $Table.$Column ('$ReaderProfileName' is checked in (b): read only)" }

# ── (d) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($IsWrite -and $script:Wrote -and $PSCmdlet.ShouldProcess($Table, 'Publish')) {
    Invoke-DvWrite POST 'PublishXml' $publishBody | Out-Null
    Write-Host "`nPublished $Table." -ForegroundColor Green
}
elseif ($IsWrite) { Write-Host "`nNo metadata written; no publish." -ForegroundColor Green }

if ($IsWrite -and $attr -and -not ($membership -and (Test-DvInSolution -Membership (Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid) -ComponentId $attr.MetadataId -TableMetadataId $tableMeta.MetadataId))) {
    Report 'FAIL' "(p5) $Table.$Column is not in $SolutionUniqueName after the run - add it to the solution"
}

# ── (e) BACKFILL: the invoices a linked submission already holds for a firm ─────────────────────────────────
Write-Host "`n(e) Backfill"
# The BFF's InvoiceFirmHold.IsHeld over one submission: the reason, the pipeline finding, a caused rules.not_checked line, or unreadable findings.
function Test-SubmissionHeld($s) {
    if ($s.sprk_outcomereasoncode -eq $HoldValue) { return $true }
    $json = $s.sprk_findingsjson
    if ([string]::IsNullOrWhiteSpace($json)) { return $false }
    try { $root = $json | ConvertFrom-Json -AsHashtable -ErrorAction Stop } catch { return $true }
    if ($root -isnot [System.Collections.IDictionary]) { return $true }
    foreach ($p in @($root['pipelineFindings'])) { if ($p -is [System.Collections.IDictionary] -and $p['reasonCode'] -eq $HoldValue) { return $true } }
    foreach ($l in @($root['lineFindings'])) { if ($l -is [System.Collections.IDictionary] -and $l['code'] -eq 'rules.not_checked' -and $l['cause'] -eq $HoldValue) { return $true } }
    return $false
}
$submissions = Get-AllPages "sprk_invoicesubmissions?`$select=sprk_invoicesubmissionid,_sprk_invoice_value,sprk_outcomereasoncode,sprk_findingsjson&`$filter=_sprk_invoice_value ne null"
$heldInvoices = @($submissions | Where-Object { Test-SubmissionHeld $_ } | ForEach-Object { $_._sprk_invoice_value.ToString().ToLowerInvariant() } | Sort-Object -Unique)
$noFirm = @((Get-AllPages "$($tableMeta.EntitySetName)?`$select=$($Table)id&`$filter=_sprk_vendororg_value eq null")).Count
Report 'INFO' "$($submissions.Count) submission(s) linked to an invoice read; $($heldInvoices.Count) invoice(s) held for a firm by a linked submission; $noFirm invoice(s) with no firm"
$toSet = @()
if ($attr) {
    $withHoldNow = @((Get-AllPages "$($tableMeta.EntitySetName)?`$select=$($Table)id&`$filter=$Column ne null") | ForEach-Object { $_."$($Table)id".ToString().ToLowerInvariant() })
    $toSet = @($heldInvoices | Where-Object { $_ -notin $withHoldNow })
}
else { $toSet = $heldInvoices }
Report 'INFO' "BACKFILL COUNT: $($toSet.Count) invoice(s) need $Column = $HoldValue"
if ($toSet.Count -eq 0) { Report 'OK' "every invoice held by a linked submission carries its own hold" }
elseif ($Verify) { foreach ($id in $toSet) { Report 'MISSING' "invoice $id is held by a linked submission but $Column is empty" } }
elseif ($IsDryRun) { foreach ($id in $toSet) { Report 'WOULD' "set $Table($id).$Column = $HoldValue" } }
elseif (-not $permissionsOk -or -not $attr) { Report 'FAIL' "the backfill did not run: the column or its permissions are not in place ((s) to (b))" }
else {
    foreach ($id in $toSet) {
        if ($PSCmdlet.ShouldProcess("$Table($id)", "Set $Column = $HoldValue")) {
            Invoke-DvWrite PATCH "$($tableMeta.EntitySetName)($id)" @{ $Column = $HoldValue } @{ 'If-Match' = '*' } | Out-Null
            Report 'DONE' "set $Table($id).$Column = $HoldValue"
        }
    }
}

if ($IsDryRun) {
    if ($gaps.Count -gt 0) {
        Write-Host "`nDRY RUN complete - nothing was written. -Apply would be REFUSED ($($gaps.Count) gap(s)):" -ForegroundColor Yellow
        $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    }
    else { Write-Host "`nDRY RUN complete - nothing was written." -ForegroundColor Cyan }
    exit 0
}
if ($gaps.Count -eq 0) { Write-Host "`nPASS: $Table.$Column exists and is field-secured, the BFF writer profile holds read/create/update, the reader profile holds read, no other profile (the System Administrator profile excepted) can create or update it, and every held invoice carries its hold." -ForegroundColor Green; exit 0 }
Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
$gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit 1
