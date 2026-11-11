#!/usr/bin/env pwsh
<#
.SYNOPSIS
    eBilling R1 task 166 (EB-D-200 part 1; owner + uac-r2 option D) - field-secures the seven sprk_invoice money and firm columns so
    ONLY the server writes them: the BFF application user(s) through "Spaarke BFF-Managed Field Writers" (read, create, update), and
    every other user keeps READING them through "Spaarke BFF-Managed Field Readers" (read only). -WhatIf is a dry run; a plain run
    writes; -Verify checks; -Revert undoes a run (with -WhatIf: a dry run of it).

.DESCRIPTION
    WHY (EB-D-200). The invoice's billed total, its four derived totals, its approved total and its firm decide approval, spend and
    firm scope. Totals are computed only on the server (FR-07, I-1), invoices are created only by the submission operation (FR-17,
    FR-20) and the firm is set only by the submission pipeline and Set firm. A user with record-level Write (D-77, unchanged) could
    still set any of them with the platform's own form, grid, Excel import or Web API. Secured, a write by anyone but the BFF
    application user is refused natively; every user keeps reading the columns (Invoice Review, AP export, the workspace).

    COLUMNS (sprk_invoice):  sprk_totalamount (the billed total), sprk_approvedtotal, sprk_feestotal, sprk_expensestotal,
    sprk_lineadjustmentstotal, sprk_invoiceadjustmentstotal, sprk_vendororg (the firm lookup).

    PROFILES (REUSED, never created or re-membered here):
      "Spaarke BFF-Managed Field Writers" (unified-access-control-r2 task 133; members: the BFF application user(s) ONLY). It gets
          read=4 create=4 update=4 on each column.
      "Spaarke BFF-Managed Field Readers" (same task) is on EVERY business unit's default team, so every user keeps reading the
          columns. It gets read=4 create=0 update=0 on each column. This script never adds or removes a member of it: when a default
          team is missing, (p8) FAILs and nothing is written (a team change is the owner's).
      Any OTHER profile with any access to a column is FAIL in every mode (never removed here), except the platform's own "System
      Administrator" profile, matched by its fixed id.

    PRECONDITIONS (every mode; a write run refuses unless all pass):
      (p0) each column exists and can be secured for read, create and update;
      (p1) the writer profile exists, exactly once, and is in -SolutionUniqueName;
      (p2) its members are exactly the -BffApplicationIds application users: each is a member, and there is no other user and no team
           (a team would let every member write the columns);
      (p3) no business unit's default team holds the writer profile;
      (p4) no sprk_fieldmappingrule, sprk_aitopicregistry or sprk_emailupdatefield row targets a column by name (maker configuration
           writing outside the BFF, refused once secured; a row for another table's column of the same name is a false hit to retarget
           or ignore by hand);
      (p5) each column is in -SolutionUniqueName (its own row, or its table with rootcomponentbehavior 0), so the secured state travels;
      (p6) informational: the forms that bind a column and whether the control is disabled, each money column's _base companion
           (whether the platform reports it secured), and whether each BFF application user holds System Administrator (full field
           access by platform rule; then there is no masked window for the BFF between (a) and its grants);
      (p7) the reader profile exists, exactly once, and is in -SolutionUniqueName;
      (p8) the reader profile is on EVERY business unit's default team.

    STEPS (a plain run), one column at a time, in this order:
      (a) secure the column, then IMMEDIATELY grant the writer profile read=4 create=4 update=4 and the reader profile read=4
          create=0 update=0 (12 retries each on 0x8004f508). If a grant fails, every column this run secured is REVERTED to not
          secured and the table published before the script stops; if the revert fails too it prints "RECOVERY REQUIRED NOW".
          Re-running is the resume path: on a column already secured it grants only what is missing;
      (b) each column's permissions are exactly: writer read=4 create=4 update=4; reader read=4 create=0 update=0;
      (c) any OTHER profile with read, create or update on a column is FAIL (the System Administrator profile by id excepted). System
          Administrator role holders are listed;
      (d) publish sprk_invoice - only when this run wrote something.

    A second run reports every item OK and writes nothing (no publish either).

    FAILURE HANDLING. The column definition is read and written through its typed path (Money cast; the firm is a Lookup cast) and, after
    each write, re-read: the Money precision and range (or the lookup's targets) must be unchanged, else the run stops and says so. If
    securing column N fails, the earlier columns stay secured AND granted, whatever was written is published, the output names the columns
    that completed, failed and were not started, and the exit code is 1. -Revert behaves alike (a failure on one column is reported, the
    rest still run, what was written is published, exit 1); a column whose un-securing is declined at -Confirm or fails keeps its permission
    rows (r2 is skipped for it), so no column is left secured with no grants.

    -Confirm: every write point (each secure + grants, a resume grant, the publish, and each -Revert write) goes through
    ShouldProcess, so -Confirm asks before each. The automatic revert after a failed grant is a recovery and is never asked.

    -VERIFY passes only when every column IS secured, the writer profile holds read=4 create=4 update=4 and the reader profile read=4
    create=0 update=0 on each, NO other profile (the System Administrator profile excepted) grants any access to any column, and every
    -BffApplicationIds application user is a member of the writer profile.

    -REVERT (rollback after a successful run), in this order: (r1) un-secure each column (so no user is ever left masked), (r2) delete
    the writer and reader profiles' permission rows on it if any are left, (r3) publish sprk_invoice. The preconditions are not
    required. With -WhatIf it only reports what it would do. The SpaarkeMaster source (Entity.xml IsSecured, the
    FieldSecurityProfiles.xml rows) is reverted by a commit, not by this script.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users, the ONLY members of the writer profile (dev:
    5967251e-171c-46fe-a6c2-ef843c90309d and, while it is still an application user, 1e40baad-e065-4aea-a8d4-4b7ab273458c).
    Per-customer input (D-13), never hard-coded.

.PARAMETER SolutionUniqueName
    The unmanaged solution the profiles and sprk_invoice travel in. Default SpaarkeMaster.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -WhatIf or -Revert.

.PARAMETER Revert
    Undo a run: un-secure the columns, remove the writer and reader profiles' rows on them, publish. Combine with -WhatIf for a dry run.

.EXAMPLE
    pwsh scripts/ebilling/Set-EbillingInvoiceMoneyFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -WhatIf
    pwsh scripts/ebilling/Set-EbillingInvoiceMoneyFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
    pwsh scripts/ebilling/Set-EbillingInvoiceMoneyFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify
    pwsh scripts/ebilling/Set-EbillingInvoiceMoneyFieldSecurity.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Revert -WhatIf

.NOTES
    Project: ebilling-r1, task 166 (EB-D-200 part 1). Shape: scripts/ebilling/Set-EbillingResubmissionOfFieldSecurity.ps1 (task 097 F2:
    the writer and reader profiles, the (p7)/(p8) default-team checks, revert-on-failed-grant, publish only on a write) and
    Set-EbillingInvoiceFirmHoldColumn.ps1 (task 095c-5: the same profile shape on sprk_invoice), run over a column list. SpaarkeMaster
    source carries IsSecured=1 (Entity.xml) and both rows per column (Other/FieldSecurityProfiles.xml); both are pinned against this
    script by InvoiceMoneyFieldSecurityAgreementTests. Every BFF writer of the columns is app-only (notes/166-invoice-field-security.md).
    Auth: the operator's own az CLI identity (System Administrator in the environment). No secrets. NOT run live by the task that adds
    it: a live run (and -Verify) needs the owner's OK and, like every field-security change, a notice to uac-r2 before and after;
    -WhatIf is read only. Exit codes: 0 = pass / done, 1 = a gap or an error.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$Verify,
    [switch]$Revert
)

$ErrorActionPreference = 'Stop'
# Every write point asks through $Cmdlet (the script's own $PSCmdlet), so the Pester file can answer the operator's confirmations.
$Cmdlet = $PSCmdlet
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$IsDryRun = [bool]$WhatIfPreference
if ($IsDryRun -and $Verify) { throw '-WhatIf and -Verify are separate modes.' }
if ($Revert -and $Verify) { throw '-Revert and -Verify are separate modes; run -Revert, then check IsSecured.' }
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($BffApplicationIds.Count -eq 0) { throw '-BffApplicationIds: at least one application id is required.' }
foreach ($id in $BffApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-BffApplicationIds: '$id' is not a GUID." } }
$IsWrite = -not $IsDryRun -and -not $Verify
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (pinned by InvoiceMoneyFieldSecurityAgreementTests) ───────────────────────────────────────────
$Table = 'sprk_invoice'
$Columns = @(
    'sprk_totalamount'
    'sprk_approvedtotal'
    'sprk_feestotal'
    'sprk_expensestotal'
    'sprk_lineadjustmentstotal'
    'sprk_invoiceadjustmentstotal'
    'sprk_vendororg'
)
# The column definition is read and written through its TYPED path (the cast to its metadata type), never the plain Attributes(...) path:
# the plain path returns the base AttributeMetadata, whose PUT can reset type-specific properties (a Money column's precision and range, a
# lookup's targets). Every column is Money except the firm lookup (task 166 fix round, review K1; as Deploy-EbillingInvoiceTotalsRange.ps1).
$LookupColumns = @('sprk_vendororg')
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
function Invoke-DvGet([string]$Path) { Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Get }
function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json
}

. (Join-Path $PSScriptRoot '..' 'common' 'DataverseSolutionMembership.ps1')
. (Join-Path $PSScriptRoot '../common/OntologyWriterUnit.ps1')
$gaps = [System.Collections.Generic.List[string]]::new()
$script:Wrote = $false
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
    if ($State -eq 'DONE') { $script:Wrote = $true }
}
function Get-AttrPath([string]$Col) { "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Col')" }
function Get-TypedAttrPath([string]$Col) {
    $cast = if ($LookupColumns -contains $Col) { 'LookupAttributeMetadata' } else { 'MoneyAttributeMetadata' }
    "$(Get-AttrPath $Col)/Microsoft.Dynamics.CRM.$cast"
}
function Format-Number($Value) { if ($null -eq $Value) { 'null' } else { ([double]$Value).ToString([System.Globalization.CultureInfo]::InvariantCulture) } }
# What a secure / un-secure PUT must leave unchanged: a Money column's precision and range, a lookup's targets.
function Get-ColumnShape([string]$Col, $Attr) {
    if ($LookupColumns -contains $Col) { return "Targets=$((@($Attr.Targets) | Sort-Object) -join ',')" }
    "Precision=$($Attr.Precision);MinValue=$(Format-Number $Attr.MinValue);MaxValue=$(Format-Number $Attr.MaxValue)"
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host "Columns     : $Table.{$($Columns -join ', ')} (only the server writes them: BFF writer profile writes, reader profile reads)"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($Revert -and $IsDryRun) { 'REVERT DRY RUN (-WhatIf, no writes)' } elseif ($Revert) { 'REVERT' } elseif ($IsDryRun) { 'DRY RUN (-WhatIf, no writes)' } else { 'WRITE' }))
# ── Column operations (functions, so the Pester file lifts and runs them against a test double) ─────────────
function Get-Permission([string]$ProfileId, [string]$Col) {
    @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $ProfileId and entityname eq '$Table' and attributelogicalname eq '$Col'").value) | Select-Object -First 1
}
function Grant-Permission([string]$ProfileId, [string]$Col, [int]$Create) {
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-DvWrite POST 'fieldpermissions' @{
                entityname = $Table; attributelogicalname = $Col
                canread = 4; cancreate = $Create; canupdate = $Create
                'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($ProfileId)"
            } | Out-Null
            return
        } catch {
            $notYetSecured = "$($_.ErrorDetails.Message) $($_.Exception.Message)" -match '0x8004f508'
            if (-not $notYetSecured -or $attempt -ge 12) { throw }
            Write-Host "    $Table.$Col not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
            Start-Sleep -Seconds 5
        }
    }
}
function Set-ColumnSecured([string]$Col, [bool]$Value) {
    # GET-then-PUT of the TYPED attribute definition (Money / Lookup cast, as Deploy-EbillingInvoiceTotalsRange.ps1), then a re-read that
    # asserts the type-specific properties did not move (task 166 fix round, review K1). Fails loudly otherwise.
    $path = Get-TypedAttrPath $Col
    $typed = Invoke-DvGet $path
    $before = Get-ColumnShape $Col $typed
    $typed.IsSecured = $Value
    Invoke-DvWrite PUT $path $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null

    # Metadata is eventually consistent: wait for a read that shows the new state, then compare.
    $chk = $null
    for ($i = 0; $i -lt 12; $i++) {
        $chk = Invoke-DvGet $path
        if ([bool]$chk.IsSecured -eq $Value) { break }
        Start-Sleep -Seconds 5
    }
    $after = Get-ColumnShape $Col $chk
    if ($after -ne $before) {
        throw "$Table.$Col changed shape when its IsSecured was set to $Value (before: $before; after: $after). The definition must be checked and restored by hand before anything else is secured."
    }
    if ([bool]$chk.IsSecured -ne $Value) { Write-Host "    note: $Table.$Col still reads IsSecured=$($chk.IsSecured) after the update (metadata lag); the next steps re-read it." -ForegroundColor Yellow }
}
# The writer first (the BFF's own read and write), then the reader (every other user's read). Create = the value of create AND update.
$specs = @()
function Grant-MissingPermissions([string]$Col) {
    foreach ($s in $specs) {
        if (-not (Get-Permission $s.Id $Col) -and $Cmdlet.ShouldProcess("$Table.$Col", "Grant $($s.Name) read=4 create=$($s.Create) update=$($s.Create)")) {
            Grant-Permission $s.Id $Col $s.Create
            Report 'DONE' "granted $($s.Name) on $Table.$Col (read=4 create=$($s.Create) update=$($s.Create))"
        }
    }
}
function Write-MaskedRecovery([string]$Cause, [string[]]$Cols) {
    Write-Host ''
    Write-Host "  !!! RECOVERY REQUIRED NOW - $Table.{$($Cols -join ', ')} may be field-secured WITHOUT its profile permissions !!!" -ForegroundColor Red
    Write-Host '  A user, and a BFF application user without System Administrator, then reads the invoice totals and firm as empty/null:' -ForegroundColor Red
    Write-Host '  Invoice Review, approval, AP export and spend read wrong values, and the BFF cannot write an invoice.' -ForegroundColor Red
    Write-Host "  Cause: $Cause" -ForegroundColor Red
    Write-Host '  Do ONE of these, now:' -ForegroundColor Red
    Write-Host '    (1) fix the cause and re-run this script: on an already-secured column it grants only the missing profile' -ForegroundColor Red
    Write-Host '        permissions (it never re-secures), then run -Verify;' -ForegroundColor Red
    Write-Host "    (2) or run this script with -Revert, or by hand: make.powerapps.com > Tables > $Table > Columns > each column > Advanced options >" -ForegroundColor Red
    Write-Host "        clear 'Enable column security', save, publish $Table." -ForegroundColor Red
}

# -Revert (r1) un-secure each column, (r2) delete the writer and reader profiles' rows on it. A column whose un-securing was declined
# at -Confirm or failed keeps its rows (deleting them would leave it secured with no grants: masked for everyone). A failure on one column
# is recorded and the rest still run; the caller publishes whatever was written and exits non-zero on a failure (review K2).
function Invoke-RevertColumns {
    $wrote = $false
    $failures = [System.Collections.Generic.List[string]]::new()
    foreach ($Column in $Columns) {
        try {
            $attrPath = Get-AttrPath $Column
            $secured = (Invoke-DvGet "$attrPath`?`$select=IsSecured").IsSecured
            $ourRows = @((Invoke-DvGet ("fieldpermissions?`$select=fieldpermissionid&`$filter=entityname eq '$Table' and attributelogicalname eq '$Column'" +
                "&`$expand=fieldsecurityprofileid(`$select=name)")).value | Where-Object { $_.fieldsecurityprofileid.name -in $WriterProfileName, $ReaderProfileName })
            $unsecured = $true
            if (-not $secured) { Write-Host "  OK       $Table.$Column is not field-secured" -ForegroundColor Green }
            elseif ($IsDryRun) { Write-Host "  WOULD    (r1) un-secure $Table.$Column" -ForegroundColor Cyan }
            elseif ($Cmdlet.ShouldProcess("$Table.$Column", 'Revert: un-secure the column')) {
                $wrote = $true
                Set-ColumnSecured $Column $false
                Write-Host "  DONE     (r1) un-secured $Table.$Column" -ForegroundColor Green
            }
            else { $unsecured = $false }
            if (-not $unsecured) {
                Write-Host "  SKIPPED  (r2) $Table.$Column is still field-secured (un-securing it was declined), so its permission rows are kept: deleting them would leave it secured with no grants" -ForegroundColor Yellow
                continue
            }
            if ($ourRows.Count -eq 0) { Write-Host "  OK       no $WriterProfileName or $ReaderProfileName row on $Table.$Column" -ForegroundColor Green }
            foreach ($row in $ourRows) {
                if ($IsDryRun) { Write-Host "  WOULD    (r2) delete $($row.fieldsecurityprofileid.name) row $($row.fieldpermissionid) on $Table.$Column" -ForegroundColor Cyan }
                elseif ($Cmdlet.ShouldProcess("fieldpermission $($row.fieldpermissionid)", "Revert: delete the $($row.fieldsecurityprofileid.name) row on $Table.$Column")) {
                    $wrote = $true
                    try { Invoke-DvWrite DELETE "fieldpermissions($($row.fieldpermissionid))" $null | Out-Null; Write-Host "  DONE     (r2) deleted $($row.fieldsecurityprofileid.name) row $($row.fieldpermissionid)" -ForegroundColor Green }
                    catch { if ("$($_.Exception.Message)" -notmatch '404') { throw }; Write-Host "  OK       (r2) the row went with the un-secure" -ForegroundColor Green }
                }
            }
        } catch {
            $failures.Add("$Table.$Column - $($_.Exception.Message)")
            Write-Host "  FAIL     $Table.$Column - $($_.Exception.Message)" -ForegroundColor Red
        }
    }
    [pscustomobject]@{ Wrote = $wrote; Failures = @($failures) }
}

# (a) secure each column, then grant the profiles at once. On a failure on column N the earlier columns are left as they are (each is
# secured AND granted, or untouched), the work written so far is published, and the result names what completed (review K2). A failed grant
# still reverts every column this run secured (a masked column must never be left behind).
function Invoke-SecureColumns {
    $completed = [System.Collections.Generic.List[string]]::new()
    $securedThisRun = [System.Collections.Generic.List[string]]::new()
    $result = [pscustomobject]@{ Completed = $completed; SecuredThisRun = $securedThisRun; FailedColumn = $null; Error = $null; Reverted = $false }
    $current = $null
    try {
        foreach ($Column in $Columns) {
            $current = $Column
            $attr = $attrs[$Column]
            if (-not $attr) { Report 'FAIL' "$Table.$Column cannot be secured: it does not exist"; continue }
            if ($attr.IsSecured) {
                Report 'OK' "$Table.$Column is field-secured"
                if ($IsWrite) {
                    try { Grant-MissingPermissions $Column }
                    catch { Write-MaskedRecovery "granting a missing profile permission on the already-secured $Column failed: $($_.Exception.Message)" @($Column); throw }
                    $completed.Add($Column)
                }
                continue
            }
            if ($Verify) { Report 'MISSING' "$Table.$Column is NOT field-secured - any user with Write on the invoice can set it"; continue }
            if ($IsDryRun -or $specs.Count -lt 2) { Report 'WOULD' "secure $Table.$Column, then at once grant $WriterProfileName read/create/update and $ReaderProfileName read only"; continue }
            if (-not $Cmdlet.ShouldProcess("$Table.$Column", "Field-secure the column, then at once grant $WriterProfileName read/create/update and $ReaderProfileName read")) {
                Report 'FAIL' "$Table.$Column was not secured (declined at the confirmation)"; continue
            }
            try { Set-ColumnSecured $Column $true }
            catch {
                $secureError = $_
                Write-Host "  FAIL     securing $Table.$Column failed: $($secureError.Exception.Message)" -ForegroundColor Red
                # The PUT may have been applied before the failure (a changed definition is found after it): find out, and never leave the column masked.
                $now = try { (Invoke-DvGet "$(Get-AttrPath $Column)`?`$select=IsSecured").IsSecured } catch { $null }
                if ($now -ne $false) {
                    $securedThisRun.Add($Column); $script:Wrote = $true
                    try { Grant-MissingPermissions $Column }
                    catch { Write-MaskedRecovery "securing $Column failed ($($secureError.Exception.Message)) after it may have been secured, and its grants failed too: $($_.Exception.Message)" @($Column) }
                }
                throw $secureError
            }
            $securedThisRun.Add($Column)
            Report 'DONE' "secured $Table.$Column"
            try {
                Grant-MissingPermissions $Column
            } catch {
                $grantError = $_
                Write-Host "  FAIL     granting the profiles on $Table.$Column failed after it was secured: $($grantError.Exception.Message)" -ForegroundColor Red
                Write-Host "           reverting every column this run secured ($($securedThisRun -join ', ')) to NOT field-secured, so no reader or BFF user is left masked..." -ForegroundColor Yellow
                try {
                    foreach ($c in $securedThisRun) { Set-ColumnSecured $c $false }
                    Invoke-DvWrite POST 'PublishXml' $publishBody | Out-Null
                    $stillSecured = @($securedThisRun | Where-Object { (Invoke-DvGet "$(Get-AttrPath $_)`?`$select=IsSecured").IsSecured })
                    if ($stillSecured.Count -gt 0) { throw "the revert was accepted but $Table.{$($stillSecured -join ', ')} still reads IsSecured=true" }
                    foreach ($c in $securedThisRun) { [void]$completed.Remove($c) }
                    $result.Reverted = $true
                    Write-Host "  REVERTED $Table.{$($securedThisRun -join ', ')} is NOT field-secured again. Fix the cause, then re-run (columns secured in an earlier run keep their state)." -ForegroundColor Yellow
                } catch {
                    Write-MaskedRecovery "the grant failed ($($grantError.Exception.Message)) AND the revert failed ($($_.Exception.Message))" @($securedThisRun)
                }
                throw $grantError
            }
            $completed.Add($Column)
        }
    } catch {
        $result.Error = $_
        $result.FailedColumn = $current
    }
    # Publish whatever this run wrote before it stopped (the automatic revert already published its own).
    if ($result.Error -and $script:Wrote -and -not $result.Reverted -and $Cmdlet.ShouldProcess($Table, 'Publish what this run wrote before it stopped')) {
        try { Invoke-DvWrite POST 'PublishXml' $publishBody | Out-Null; Write-Host "`nPublished $Table (the work written before the stop)." -ForegroundColor Green }
        catch { Write-Host "`nFAIL     the publish after the stop failed: $($_.Exception.Message). Publish $Table by hand." -ForegroundColor Red }
    }
    $result
}

$publishBody = @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" }

# ── -REVERT: un-secure first, then remove the profile rows, then publish ─────────────────────────────────────
if ($Revert) {
    Write-Host "`nRevert"
    $revertResult = Invoke-RevertColumns
    if ($IsDryRun) { Write-Host "  WOULD    (r3) publish $Table (only if r1 or r2 wrote)" -ForegroundColor Cyan; Write-Host "`nREVERT DRY RUN complete - nothing was written." -ForegroundColor Cyan; exit 0 }
    $revertFailures = [System.Collections.Generic.List[string]]::new()
    foreach ($f in $revertResult.Failures) { $revertFailures.Add($f) }
    if ($revertResult.Wrote -and $Cmdlet.ShouldProcess($Table, 'Revert: publish')) {
        try { Invoke-DvWrite POST 'PublishXml' $publishBody | Out-Null; Write-Host "`nPublished $Table." -ForegroundColor Green }
        catch { $revertFailures.Add("the publish failed - publish $Table by hand: $($_.Exception.Message)") }
    }
    $still = @($Columns | Where-Object { (Invoke-DvGet "$(Get-AttrPath $_)`?`$select=IsSecured").IsSecured })
    if ($still.Count -gt 0) { Write-Host "`nFAIL: $Table.{$($still -join ', ')} is still field-secured (any permission rows on it were kept)." -ForegroundColor Red }
    if ($revertFailures.Count -gt 0) {
        Write-Host "`nREVERT FAILED on $($revertFailures.Count) item(s); what was written is published:" -ForegroundColor Red
        $revertFailures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    }
    if ($still.Count -gt 0 -or $revertFailures.Count -gt 0) { exit 1 }
    Write-Host "`nREVERTED: no $Table column of this task is field-secured. Revert the SpaarkeMaster source (Entity.xml IsSecured, the FieldSecurityProfiles.xml rows) by a commit." -ForegroundColor Green
    exit 0
}

# ── PRECONDITIONS ───────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`nPreconditions"
$attrs = @{}
foreach ($Column in $Columns) {
    $attr = try { Invoke-DvGet "$(Get-AttrPath $Column)`?`$select=MetadataId,IsSecured,CanBeSecuredForCreate,CanBeSecuredForRead,CanBeSecuredForUpdate" } catch { $null }
    $attrs[$Column] = $attr
    if (-not $attr) { Report 'FAIL' "(p0) $Table.$Column does not exist in this environment" }
    elseif (-not ($attr.CanBeSecuredForCreate -and $attr.CanBeSecuredForRead -and $attr.CanBeSecuredForUpdate)) {
        Report 'FAIL' "(p0) $Table.$Column cannot be secured for read, create and update"
    }
    elseif (-not $(try { Invoke-DvGet "$(Get-TypedAttrPath $Column)`?`$select=MetadataId" } catch { $null })) {
        Report 'FAIL' "(p0) $Table.$Column is not the expected type ($(if ($LookupColumns -contains $Column) { 'Lookup' } else { 'Money' })); its definition cannot be read through the typed path"
    }
    else { Report 'OK' "(p0) $Table.$Column exists ($(if ($LookupColumns -contains $Column) { 'Lookup' } else { 'Money' })) and can be secured for read, create and update" }
}

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
        else { Report 'FAIL' "($($p.Id)) '$($p.Name)' is not in $SolutionUniqueName - its permissions on the $Table columns would not travel" }
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
        else { Report 'FAIL' "(p2) BFF app user '$($u.fullname)' is NOT a member of $WriterProfileName - it could not write the invoice money and firm columns once secured (unless System Administrator). Membership is maintained by Set-RecordCreatorPersonSchema.ps1, never here" }
    }
    foreach ($m in $members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowed }) {
        $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        Report 'FAIL' "(p2) $WriterProfileName member '$($m.fullname)' is $kind - it could set every invoice's totals and firm"
    }
    $teams = @($writer.teamprofiles_association)
    foreach ($tm in $teams) {
        $what = if ($tm.isdefault) { "(p3) $WriterProfileName is on business-unit DEFAULT team '$($tm.name)'" } else { "(p2) $WriterProfileName has TEAM '$($tm.name)'" }
        Report 'FAIL' "$what - every member could set the invoice totals and firm"
    }
    if (@($teams | Where-Object { $_.isdefault }).Count -eq 0) { Report 'OK' "(p3) no business-unit default team holds $WriterProfileName" }
}

$configuredWriters = 0
foreach ($ch in @(@{ T = 'sprk_fieldmappingrule'; C = 'sprk_targetfield' }, @{ T = 'sprk_aitopicregistry'; C = 'sprk_targetfield' }, @{ T = 'sprk_emailupdatefield'; C = 'sprk_targetfieldlogicalname' })) {
    $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$($ch.T)')?`$select=EntitySetName").EntitySetName
    foreach ($Column in $Columns) {
        foreach ($row in @((Invoke-DvGet "$set`?`$select=$($ch.T)id&`$filter=$($ch.C) eq '$Column'").value)) {
            $configuredWriters++
            Report 'FAIL' "(p4) $($ch.T) row $($row."$($ch.T)id") targets $Column - only the BFF writes $Table.$Column; remove or retarget the row (a row for another table's column of the same name is a false hit)"
        }
    }
}
if ($configuredWriters -eq 0) { Report 'OK' "(p4) no field-mapping rule, AI topic-registry row or email update field targets any of the $($Columns.Count) columns" }

$tableMeta = Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId,EntitySetName"
if ($membership) {
    foreach ($Column in $Columns) {
        if (-not $attrs[$Column]) { continue }
        $how = Test-DvInSolution -Membership $membership -ComponentId $attrs[$Column].MetadataId -TableMetadataId $tableMeta.MetadataId
        if ($how) { Report 'OK' "(p5) $Table.$Column is in $SolutionUniqueName ($how)" }
        else { Report 'FAIL' "(p5) $Table.$Column is not in $SolutionUniqueName - the secured state would not travel" }
    }
}
$forms = @((Invoke-DvGet "systemforms?`$select=name,type,formxml&`$filter=objecttypecode eq '$Table'").value)
foreach ($Column in $Columns) {
    foreach ($f in $forms) {
        $m = [regex]::Match($f.formxml, "<control\b[^>]*datafieldname=`"$Column`"[^>]*>")
        if (-not $m.Success) { continue }
        $state = if ($m.Value -match 'disabled="true"') { 'disabled' } else { 'ENABLED (shown read-only to users once secured; redeploy the forms with Deploy-EbillingFormsViews.ps1)' }
        Report 'INFO' "(p6) form '$($f.name)' (type $($f.type)) binds $Column - control $state"
    }
    if ($attrs[$Column] -and $Column -ne 'sprk_vendororg') {
        $base = try { Invoke-DvGet "$(Get-AttrPath "$($Column)_base")`?`$select=IsSecured,CanBeSecuredForRead" } catch { $null }
        if ($base) { Report 'INFO' "(p6) $($Column)_base (the base-currency companion) reports IsSecured=$($base.IsSecured) CanBeSecuredForRead=$($base.CanBeSecuredForRead)" }
        else { Report 'INFO' "(p6) $($Column)_base: no companion attribute read" }
    }
}
foreach ($u in $bffUsers) {
    $isAdmin = @((Invoke-DvGet "systemusers($($u.systemuserid))/systemuserroles_association?`$select=name&`$filter=name eq 'System Administrator'").value).Count -gt 0
    $adminNote = if ($isAdmin) { 'holds System Administrator (full field access; no masked window for it)' } else { 'does NOT hold System Administrator - it reads the columns as null between (a) securing and the grants (seconds)' }
    Report 'INFO' "(p6) BFF app user '$($u.fullname)'$(if ($u.isdisabled) { ' (DISABLED)' }) $adminNote"
}

# The ontology writer's business unit's default team (scripts/common/OntologyWriterUnit.ps1) is left out of the set that must hold the
# reader profile, and is a FAIL when it does hold it (reported, never removed).
function Test-ReaderOnDefaultTeam([string[]]$OnReader, $ReaderProfileId) {
    $defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name,_businessunitid_value&$filter=isdefault eq true&$expand=businessunitid($select=name)').value)
    if ($defaultTeams.Count -eq 0) { Report 'FAIL' '(p8) no business-unit default team was read - the query is wrong' }
    $split = Split-DvDefaultTeamsByOntologyWriterUnit -Api $Api -Headers $headers -DefaultTeams $defaultTeams
    $left = @($split.WriterUnitTeams | ForEach-Object { "'$($_.name)' ($($_.teamid))" })
    Report 'INFO' "(p8) ontology writer business unit: default team left out of the reader profile: $(if ($left.Count) { $left -join ', ' } else { 'none' })"
    foreach ($w in @(Get-DvOntologyWriterUnitTeamsOnProfile -Api $Api -Headers $headers -ProfileId $ReaderProfileId -WriterUnitTeams $split.WriterUnitTeams)) {
        Report 'FAIL' "(p8) the ontology writer business unit's default team '$($w.name)' holds the reader profile - it must hold no field-security profile. Removing it is a team change (the owner's); this script never removes a member"
    }
    foreach ($t in $split.ReaderTeams) {
        if ($OnReader -contains $t.teamid.ToString().ToLowerInvariant()) { Report 'OK' "(p8) reader profile on default team '$($t.name)' (BU '$($t.businessunitid.name)')" }
        else { Report 'FAIL' "(p8) default team '$($t.name)' (BU '$($t.businessunitid.name)') is NOT on the reader profile - its users would read the invoice totals and firm EMPTY. Adding it is a team change (the owner's); this script never makes it" }
    }
}
if ($reader) {
    Test-ReaderOnDefaultTeam @($reader.teamprofiles_association | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() }) $reader.fieldsecurityprofileid
}

if ($IsWrite -and $gaps.Count -gt 0) {
    Write-Host "`nREFUSED: a write run needs every precondition to pass ($($gaps.Count) gap(s)). Nothing was written." -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

# ── (a) SECURE each column, then grant the profiles at once ─────────────────────────────────────────────────
Write-Host "`n(a) Field-level security"
if ($writer) { $specs += @{ Id = $writer.fieldsecurityprofileid; Name = $WriterProfileName; Create = 4 } }
if ($reader) { $specs += @{ Id = $reader.fieldsecurityprofileid; Name = $ReaderProfileName; Create = 0 } }

$clock = [System.Diagnostics.Stopwatch]::StartNew()
$secure = Invoke-SecureColumns
$securedThisRun = $secure.SecuredThisRun
$clock.Stop()
if ($secure.Error) {
    $notStarted = @($Columns | Where-Object { $_ -ne $secure.FailedColumn -and $_ -notin $secure.Completed -and $_ -notin $secure.SecuredThisRun -and -not $attrs[$_].IsSecured })
    Write-Host "`nSTOPPED at $Table.$($secure.FailedColumn): $($secure.Error.Exception.Message)" -ForegroundColor Red
    Write-Host "  completed (secured and granted): $(if ($secure.Completed.Count) { $secure.Completed -join ', ' } else { 'none' })$(if ($secure.Reverted) { ' - this run''s secured columns were then REVERTED' })" -ForegroundColor Red
    Write-Host "  failed / not completed         : $($secure.FailedColumn)" -ForegroundColor Red
    Write-Host "  not started                    : $(if ($notStarted.Count) { $notStarted -join ', ' } else { 'none' })" -ForegroundColor Red
    Write-Host '  Re-run to resume (it grants only what is missing and secures what is left), run -Verify, or -Revert.' -ForegroundColor Red
    exit 1
}
if ($securedThisRun.Count -gt 0) { Report 'INFO' ("masked window per column: a few seconds; whole run {0:N1}s ({1} column(s) secured, each granted at once)" -f $clock.Elapsed.TotalSeconds, $securedThisRun.Count) }

# ── (b) THE PERMISSIONS ─────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Permissions on $Table"
foreach ($Column in $Columns) {
    foreach ($s in $specs) {
        $perm = Get-Permission $s.Id $Column
        if ($perm) {
            if ($perm.canread -eq 4 -and $perm.cancreate -eq $s.Create -and $perm.canupdate -eq $s.Create) { Report 'OK' "$Column - $($s.Name) (read=4 create=$($s.Create) update=$($s.Create))" }
            else { Report 'FAIL' "$Column - $($s.Name) is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($s.Create) update=$($s.Create)" }
        }
        elseif ($Verify) { Report 'MISSING' "$($s.Name) on $Table.$Column" }
        elseif ($IsDryRun) { Report 'WOULD' "grant $($s.Name) on $Table.$Column (read=4 create=$($s.Create) update=$($s.Create))" }
        else { Report 'FAIL' "$($s.Name) on $Table.$Column is still missing after the run granted it" }
    }
}

# ── (c) every OTHER profile with any access ─────────────────────────────────────────────────────────────────
Write-Host "`n(c) Other profiles"
# Any access (read, create or update) by any profile but the writer, the reader (both checked in (b), so never counted twice) and the
# platform's System Administrator profile (all by id).
$writerId = if ($writer) { $writer.fieldsecurityprofileid.ToString().ToLowerInvariant() } else { '' }
$readerId = if ($reader) { $reader.fieldsecurityprofileid.ToString().ToLowerInvariant() } else { '' }
$otherCount = 0
foreach ($Column in $Columns) {
    $others = @((Invoke-DvGet "fieldpermissions?`$select=canread,cancreate,canupdate&`$expand=fieldsecurityprofileid(`$select=fieldsecurityprofileid,name)&`$filter=entityname eq '$Table' and attributelogicalname eq '$Column' and (canread eq 4 or canupdate eq 4 or cancreate eq 4)").value |
        Where-Object { $_.fieldsecurityprofileid.fieldsecurityprofileid.ToString().ToLowerInvariant() -notin $SystemAdministratorProfileId, $writerId, $readerId })
    foreach ($o in $others) { $otherCount++; Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' ($($o.fieldsecurityprofileid.fieldsecurityprofileid)) grants access to $Table.$Column (read=$($o.canread) create=$($o.cancreate) update=$($o.canupdate)) - only the BFF writer and reader profiles may" }
}
if ($otherCount -eq 0) { Report 'OK' "no profile besides '$WriterProfileName', '$ReaderProfileName' and the System Administrator profile ($SystemAdministratorProfileId) grants any access to the $($Columns.Count) columns" }
foreach ($role in @((Invoke-DvGet "roles?`$select=roleid&`$filter=name eq 'System Administrator'").value)) {
    foreach ($u in @((Invoke-DvGet "roles($($role.roleid))/systemuserroles_association?`$select=fullname,applicationid,isdisabled").value | Where-Object { -not $_.isdisabled })) {
        Report 'INFO' ("System Administrator '{0}'{1} (full field access by platform rule)" -f $u.fullname, $(if ($u.applicationid) { ' (application user)' } else { '' }))
    }
    foreach ($tm in @((Invoke-DvGet "roles($($role.roleid))/teamroles_association?`$select=name").value)) { Report 'INFO' "System Administrator TEAM '$($tm.name)' - every member" }
}

# ── (d) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($IsWrite -and $script:Wrote -and $Cmdlet.ShouldProcess($Table, 'Publish')) {
    Invoke-DvWrite POST 'PublishXml' $publishBody | Out-Null
    Write-Host "`nPublished $Table." -ForegroundColor Green
}
elseif ($IsWrite) { Write-Host "`nNothing written; no publish." -ForegroundColor Green }

if ($IsDryRun) {
    if ($gaps.Count -gt 0) {
        Write-Host "`nDRY RUN complete - nothing was written. A write run would be REFUSED ($($gaps.Count) gap(s)):" -ForegroundColor Yellow
        $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    }
    else { Write-Host "`nDRY RUN complete - nothing was written." -ForegroundColor Cyan }
    exit 0
}
if ($gaps.Count -eq 0) { Write-Host "`nPASS: the $($Columns.Count) $Table columns are field-secured, the BFF writer profile holds read/create/update, the reader profile holds read, and no other profile (the System Administrator profile excepted) grants any access." -ForegroundColor Green; exit 0 }
Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
$gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit 1
