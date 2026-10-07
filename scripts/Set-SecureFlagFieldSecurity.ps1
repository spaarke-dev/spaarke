#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Locks sprk_issecure on sprk_project, sprk_matter, sprk_workassignment and sprk_invoice with field-level security, so
    ONLY the BFF (through the secure/unsecure endpoints) can set or clear it on the three roots, while EVERY user still
    reads its true value (unified-access-control-r2 task 150; owner round 2 item 2). sprk_invoice is locked the same way
    (owner round 10 item 11): an invoice follows its matter, so its flag is no longer a security input anywhere in the BFF
    (SecurableEntityRegistry.FlagIsNotASecurityInput) and NOTHING writes it; the lock keeps users from setting a value
    that looks meaningful. Dry run by default; -Apply writes; -Verify checks.

.DESCRIPTION
    It REUSES task 133's two profiles — it never creates or edits their membership:
      "Spaarke BFF-Managed Field Readers"  Read; on EVERY business unit's default team (every user keeps reading).
      "Spaarke BFF-Managed Field Writers"  Read + Create + Update; the BFF application user(s) ONLY.
    Both, and their members, are created and maintained by scripts/Set-RecordCreatorPersonSchema.ps1 — one mechanism
    for every column only the BFF writes. This script checks them and REFUSES -Apply when they are not in place,
    because securing the column before every reader holds Read masks it: Dataverse then returns it EMPTY, no error,
    and every reader that maps empty to "not secure" treats a secure record as an ordinary one.

    PRECONDITIONS (checked in every mode; -Apply refuses unless all pass):
      (p1) both profiles exist and are in -SolutionUniqueName;
      (p2) the reader profile is on EVERY business unit's default team;
      (p3) the writer profile's members are exactly the -BffApplicationIds application users (no human, no team);
      (p4) no row holds NULL sprk_issecure (scripts/Repair-SecureFlagNulls.ps1 -Verify);
      (p5) -ClientNoLongerWritesFlag is passed: the client that stops writing sprk_issecure on create is deployed and
           no cached older bundle is served. Securing the column before that refuses EVERY secure project create made
           by a user (the old payload names a column the user may not create).
      (p6) no sprk_fieldmappingrule, sprk_aitopicregistry or sprk_emailupdatefield row targets sprk_issecure: each is
           maker-authored configuration that writes a column outside the endpoints (task 150 r2, verifier F5).

    STEPS (-Apply), per table, in this order:
      (a) secure the column (IsSecured = true);
      (b) IMMEDIATELY grant the reader profile read=4, then the writer profile read=4 create=4 update=4.
          The Web API cannot create a field permission on a column that is not yet secured (0x8004f508), so there is a
          window between (a) and (b) in which non-administrators read the column EMPTY. It is measured and printed per
          table; with the BFF deployed (task 150) the BFF refuses — never mis-routes — during it.
          IF A GRANT FAILS after (a) (12 retries on 0x8004f508 exhausted, or any other error), that table would be
          left secured with no reader permission — the FAIL-OPEN state: non-administrators read the column EMPTY and
          the client readers treat a secure record as an ordinary one. The script then REVERTS IsSecured on that
          table (and publishes it) before stopping; if the revert fails too it prints "RECOVERY REQUIRED NOW" with
          the two remedies. Re-running -Apply is the resume path: on a column already secured it grants only the
          missing permissions (task 150 r2, verifier F3).
      (c) report any OTHER profile that can create or update the column (FAIL). The platform's own System
          Administrator profile gets full access automatically and cannot be narrowed — owner decision F4 accepts it;
          every holder of the System Administrator role is LISTED (informational).
      (d) publish the four tables.
    sprk_accesspermission is NOT touched (owner-accepted: it stays editable by Write-holders).

    LIVE ORDER (task 150 step 6): Repair-SecureFlagNulls.ps1 -Apply → deploy the BFF → deploy the client (confirm no
    cached old bundle) → Set-RecordCreatorPersonSchema.ps1 -Apply (profiles + members) → THIS SCRIPT -Apply → -Verify
    → the standing assertion SecureFlagFieldSecurityAssertionTests live run.

    ⚠️ A business unit created later needs its default team added to the reader profile: re-run
    Set-RecordCreatorPersonSchema.ps1 -Apply (it adds every default team), then this script's -Verify.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users (dev: 5967251e-171c-46fe-a6c2-ef843c90309d and,
    while it is still an application user, 1e40baad-e065-4aea-a8d4-4b7ab273458c). Required for every mode: they are
    what (p3) checks — the per-customer BFF registration (D-13) is an environment-setup input, never hard-coded.

.PARAMETER SolutionUniqueName
    The unmanaged solution carrying the profiles. Default SpaarkeCore.

.PARAMETER ClientNoLongerWritesFlag
    The operator's confirmation for (p5). Required with -Apply.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-SecureFlagFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
    Dry run: every precondition and step's state; zero writes.

.EXAMPLE
    .\Set-SecureFlagFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c `
        -ClientNoLongerWritesFlag -Apply

.EXAMPLE
    .\Set-SecureFlagFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify

.NOTES
    unified-access-control-r2 task 150 (#1067). Pinned against the BFF and the standing assertion by
    SecureFlagFieldSecurityScriptAgreementTests (column, tables, both profile names — equal to task 133's script).
    Guide: docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md §7d. Auth: the operator's own az CLI identity
    (System Administrator in the environment). No secrets.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$ClientNoLongerWritesFlag,
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
# `pwsh -File` passes "a,b" as ONE string; accept both shapes.
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($id in $BffApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-BffApplicationIds: '$id' is not a GUID." } }
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (pinned by SecureFlagFieldSecurityScriptAgreementTests) ───────────────────────────────────────
$Column = 'sprk_issecure'
$Tables = @('sprk_project', 'sprk_matter', 'sprk_workassignment', 'sprk_invoice')
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
$Secured = $true

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

# ── PRECONDITIONS ───────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`nPreconditions"
function Get-Profile([string]$Name) {
    @((Invoke-DvGet ("fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$Name'" +
        '&$expand=teamprofiles_association($select=teamid,name,isdefault),systemuserprofiles_association($select=systemuserid,fullname,applicationid)')).value)
}
$readers = Get-Profile $ReaderProfileName
$writers = Get-Profile $WriterProfileName
$reader = $null; $writer = $null
foreach ($pair in @(@{ Rows = $readers; Name = $ReaderProfileName }, @{ Rows = $writers; Name = $WriterProfileName })) {
    if ($pair.Rows.Count -eq 1) { Report 'OK' "(p1) profile '$($pair.Name)' exists" }
    else { Report 'FAIL' "(p1) $($pair.Rows.Count) profile(s) named '$($pair.Name)' — exactly one is required; create it with scripts/Set-RecordCreatorPersonSchema.ps1 -Apply (this script never creates it)" }
}
if ($readers.Count -eq 1) { $reader = $readers[0] }
if ($writers.Count -eq 1) { $writer = $writers[0] }

$solution = @((Invoke-DvGet "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "(p1) solution '$SolutionUniqueName' not found" }
else {
    # Membership through the ONE shared helper (scripts/common/DataverseSolutionMembership.ps1 — paged; pinned by
    # SchemaScriptSolutionMembershipGuardTests). A field security profile is a ROOT component, never a table
    # subcomponent, so no TableMetadataId is passed: only its own solutioncomponents row ('Direct') counts.
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
    foreach ($p in @($reader, $writer) | Where-Object { $_ }) {
        if (Test-DvInSolution -Membership $membership -ComponentId $p.fieldsecurityprofileid) { Report 'OK' "(p1) '$($p.name)' is in $SolutionUniqueName" }
        else { Report 'FAIL' "(p1) '$($p.name)' is not in $SolutionUniqueName — Set-RecordCreatorPersonSchema.ps1 -Apply adds it" }
    }
}

if ($reader) {
    $onReader = @($reader.teamprofiles_association | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
    $defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name&$filter=isdefault eq true&$expand=businessunitid($select=name)').value)
    if ($defaultTeams.Count -eq 0) { Report 'FAIL' '(p2) no business-unit default team was read — the query is wrong' }
    foreach ($t in $defaultTeams) {
        if ($onReader -contains $t.teamid.ToString().ToLowerInvariant()) { Report 'OK' "(p2) reader profile on default team '$($t.name)' (BU '$($t.businessunitid.name)')" }
        else { Report 'FAIL' "(p2) default team '$($t.name)' (BU '$($t.businessunitid.name)') is NOT on the reader profile — its users would read $Column EMPTY; run Set-RecordCreatorPersonSchema.ps1 -Apply" }
    }
}

$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if (-not $u) { Report 'FAIL' "(p3) no application user for appId $appId in this environment"; continue }
    $bffUsers += $u
}
if ($writer) {
    $members = @($writer.systemuserprofiles_association)
    $memberIds = @($members | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    $allowed = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    foreach ($u in $bffUsers) {
        if ($memberIds -contains $u.systemuserid.ToString().ToLowerInvariant()) { Report 'OK' "(p3) BFF app user '$($u.fullname)' is a writer (explicitly)" }
        else { Report 'FAIL' "(p3) BFF app user '$($u.fullname)' is NOT on the writer profile — the BFF could not read or set $Column wherever it lacks System Administrator; run Set-RecordCreatorPersonSchema.ps1 -Apply" }
    }
    foreach ($m in $members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowed }) {
        $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        Report 'FAIL' "(p3) writer profile member '$($m.fullname)' is $kind — it could set or clear $Column"
    }
    foreach ($tm in @($writer.teamprofiles_association)) { Report 'FAIL' "(p3) writer profile has TEAM '$($tm.name)' — every member could set or clear $Column" }
}

$nullRows = 0
foreach ($t in $Tables) {
    $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$t')?`$select=EntitySetName").EntitySetName
    $n = @((Invoke-DvGet "$set`?`$select=$($Column)&`$filter=$Column eq null&`$top=1").value).Count
    if ($n -gt 0) { $nullRows++; Report 'FAIL' "(p4) $t still has NULL $Column rows — run scripts/Repair-SecureFlagNulls.ps1 -Apply first" }
}
if ($nullRows -eq 0) { Report 'OK' "(p4) no NULL $Column on any table" }

# (p6) task 150 r2 (verifier F5): no maker-authored configuration row writes the flag. Once the column is locked, a Field
# Mapping Framework Copy rule onto it (or an AI topic-registry / email update-field target) fails every write it drives.
# Same three channels as the standing assertion's clause 5 (SecureFlagFieldSecurityAssertion.ConfiguredWriterChannels).
$configuredWriters = 0
foreach ($ch in @(@{ T = 'sprk_fieldmappingrule'; C = 'sprk_targetfield' }, @{ T = 'sprk_aitopicregistry'; C = 'sprk_targetfield' }, @{ T = 'sprk_emailupdatefield'; C = 'sprk_targetfieldlogicalname' })) {
    $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$($ch.T)')?`$select=EntitySetName").EntitySetName
    foreach ($row in @((Invoke-DvGet "$set`?`$select=$($ch.T)id&`$filter=$($ch.C) eq '$Column'").value)) {
        $configuredWriters++
        Report 'FAIL' "(p6) $($ch.T) row $($row."$($ch.T)id") targets $Column — remove or retarget it; only the BFF endpoints write the flag"
    }
}
if ($configuredWriters -eq 0) { Report 'OK' "(p6) no field-mapping rule, AI topic-registry row or email update field targets $Column" }

if ($ClientNoLongerWritesFlag) { Report 'OK' '(p5) operator confirms the client no longer writes the flag and no old bundle is served' }
elseif ($Apply) { Report 'FAIL' '(p5) -ClientNoLongerWritesFlag not passed: securing the column before that client is live refuses every secure project create' }
else { Report 'INFO' '(p5) -ClientNoLongerWritesFlag is required for -Apply' }

if ($Apply -and $gaps.Count -gt 0) {
    Write-Host "`nREFUSED: -Apply needs every precondition to pass ($($gaps.Count) gap(s)). Nothing was written." -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

# ── (a)+(b) SECURE, then grant at once ──────────────────────────────────────────────────────────────────────
Write-Host "`n(a)+(b) Field-level security"
function Get-Permission([string]$ProfileId, [string]$Table) {
    @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $ProfileId and entityname eq '$Table' and attributelogicalname eq '$Column'").value) | Select-Object -First 1
}
function Grant-Permission([string]$ProfileId, [string]$Table, [int]$Create) {
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-DvWrite POST 'fieldpermissions' @{
                entityname = $Table; attributelogicalname = $Column
                canread = 4; cancreate = $Create; canupdate = $Create
                'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($ProfileId)"
            } | Out-Null
            return
        } catch {
            # Securing propagates asynchronously (task 141 live run): a grant right after it may be refused 0x8004f508.
            $notYetSecured = "$($_.ErrorDetails.Message) $($_.Exception.Message)" -match '0x8004f508'
            if (-not $notYetSecured -or $attempt -ge 12) { throw }
            Write-Host "    $Table.$Column not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
            Start-Sleep -Seconds 5
        }
    }
}
function Set-ColumnSecured([string]$Table, [bool]$Value) {
    $attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')"
    $typed = Invoke-DvGet $attrPath
    $typed.IsSecured = $Value
    try {
        Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
    } catch {
        # The GET-then-PUT shape is proven live on string and lookup columns only (tasks 141/133). A Boolean column's
        # definition carries its OptionSet as a navigation property the plain GET omits, and the PUT may need it.
        # Retry ONCE with the Boolean cast and the OptionSet expanded; any other type, or a second refusal, throws.
        # Either way the failed PUT left IsSecured unchanged on this table, so nothing is masked (task 150 r1).
        if ($typed.'@odata.type' -ne '#Microsoft.Dynamics.CRM.BooleanAttributeMetadata') { throw }
        Write-Host "    PUT of $Table.$Column refused without its OptionSet ($($_.ErrorDetails.Message)); retrying once with the Boolean cast and `$expand=OptionSet..."
        $typed = Invoke-DvGet "$attrPath/Microsoft.Dynamics.CRM.BooleanAttributeMetadata?`$expand=OptionSet"
        $typed.IsSecured = $Value
        Invoke-DvWrite PUT $attrPath $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
    }
}
function Grant-MissingPermissions([string]$Table, $Specs) {
    foreach ($s in $Specs) {
        if (-not (Get-Permission $s.P.fieldsecurityprofileid $Table)) { Grant-Permission $s.P.fieldsecurityprofileid $Table $s.Create }
    }
}
# A secured column with no reader permission is the FAIL-OPEN state: every non-administrator reads it EMPTY, and the
# client readers (RecordContainerResolver.ts, TrackingFieldTrio, AccessGrantModal) treat empty as "not secure". The BFF
# is unaffected (System Administrator + writer profile) and refuses. Printed whenever this script may have left it.
function Write-MaskedRecovery([string]$Table, [string]$Cause) {
    Write-Host ''
    Write-Host "  !!! RECOVERY REQUIRED NOW — $Table.$Column may be field-secured WITHOUT its reader permission !!!" -ForegroundColor Red
    Write-Host "  Every non-administrator then reads $Column EMPTY and the client treats SECURE records as ordinary ones." -ForegroundColor Red
    Write-Host "  Cause: $Cause" -ForegroundColor Red
    Write-Host '  Do ONE of these, now:' -ForegroundColor Red
    Write-Host "    (1) fix the cause and re-run this script with -Apply: on an already-secured column it grants only the missing" -ForegroundColor Red
    Write-Host '        profile permissions (it never re-secures), then run -Verify;' -ForegroundColor Red
    Write-Host "    (2) or un-secure the column: make.powerapps.com > Tables > $Table > Columns > $Column > Advanced options >" -ForegroundColor Red
    Write-Host "        clear 'Enable column security', save, publish $Table — then -Verify reports it NOT secured (expected)." -ForegroundColor Red
}
foreach ($t in $Tables) {
    $attr = Invoke-DvGet "EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='$Column')?`$select=IsSecured"
    $specs = @(@{ P = $reader; Name = $ReaderProfileName; Create = 0 }, @{ P = $writer; Name = $WriterProfileName; Create = 4 })

    if ($attr.IsSecured -eq $Secured) {
        Report 'OK' "$t.$Column is field-secured"
        if ($Apply) {
            # The resume path after an interrupted run: grant whatever is missing on a column already secured.
            try { Grant-MissingPermissions $t $specs }
            catch { Write-MaskedRecovery $t "granting a missing profile permission on the already-secured column failed: $($_.Exception.Message)"; throw }
        }
    }
    elseif ($Verify) { Report 'MISSING' "$t.$Column is NOT field-secured — any user with Write can change it" }
    elseif ($IsDryRun) { Report 'WOULD' "secure $t.$Column ($(if ($attr.'@odata.type') { $attr.'@odata.type' } else { 'type not annotated' })), then at once grant the reader (read) and writer (read/create/update) profiles" }
    else {
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        Set-ColumnSecured $t $Secured
        try {
            Grant-MissingPermissions $t $specs
        } catch {
            # Task 150 r2 (verifier F3): the column is now secured and a grant failed (12 retries on 0x8004f508 exhausted,
            # or any other error). Left as is, that table is in the fail-open state above. Undo THIS run's securing, so the
            # table is back where -Apply found it; if the undo fails too, say so loudly. Either way the run stops here.
            $grantError = $_
            Write-Host "  FAIL     granting the profiles on $t.$Column failed after it was secured: $($grantError.Exception.Message)" -ForegroundColor Red
            Write-Host "           reverting $t.$Column to NOT field-secured, so no reader is left masked..." -ForegroundColor Yellow
            try {
                Set-ColumnSecured $t (-not $Secured)
                Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$t</entity></entities></importexportxml>" } | Out-Null
                $back = (Invoke-DvGet "EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='$Column')?`$select=IsSecured").IsSecured
                if ($back -eq $Secured) { throw "the revert was accepted but $t.$Column still reads IsSecured=true" }
                Write-Host "  REVERTED $t.$Column is NOT field-secured again (as -Apply found it; any grant already made is inert). Fix the cause, then re-run -Apply." -ForegroundColor Yellow
            } catch {
                Write-MaskedRecovery $t "the grant failed ($($grantError.Exception.Message)) AND the revert failed ($($_.Exception.Message))"
            }
            throw $grantError
        }
        $clock.Stop()
        Report 'DONE' ("secured $t.$Column and granted both profiles — masked window {0:N1}s (secure → reader grant)" -f $clock.Elapsed.TotalSeconds)
    }

    foreach ($s in $specs) {
        if (-not $s.P) { continue }
        $perm = Get-Permission $s.P.fieldsecurityprofileid $t
        if ($perm) {
            if ($perm.canread -eq 4 -and $perm.cancreate -eq $s.Create -and $perm.canupdate -eq $s.Create) { Report 'OK' "$($s.Name) on $t.$Column (read=4 create=$($s.Create) update=$($s.Create))" }
            else { Report 'FAIL' "$($s.Name) on $t.$Column is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($s.Create) update=$($s.Create)" }
        } elseif ($Verify) { Report 'MISSING' "$($s.Name) on $t.$Column" }
        elseif ($IsDryRun) { Report 'WOULD' "grant $($s.Name) on $t.$Column" }
        else { Report 'FAIL' "$($s.Name) on $t.$Column is still missing after -Apply granted it" }
    }

    # (c) every OTHER writer
    $otherWriters = @((Invoke-DvGet "fieldpermissions?`$select=canupdate,cancreate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$t' and attributelogicalname eq '$Column' and (canupdate eq 4 or cancreate eq 4)").value |
        Where-Object { $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' })
    foreach ($o in $otherWriters) { Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' can also write $t.$Column — only the BFF may" }
}

# ── INFORMATIONAL: the residual writer set (owner decision F4) ──────────────────────────────────────────────
Write-Host "`nInformational: System Administrator role holders (full field access by platform rule — owner decision F4)"
foreach ($role in @((Invoke-DvGet "roles?`$select=roleid&`$filter=name eq 'System Administrator'").value)) {
    foreach ($u in @((Invoke-DvGet "roles($($role.roleid))/systemuserroles_association?`$select=fullname,applicationid,isdisabled").value | Where-Object { -not $_.isdisabled })) {
        Report 'INFO' ("user '{0}'{1}" -f $u.fullname, $(if ($u.applicationid) { ' (application user)' } else { '' }))
    }
    foreach ($tm in @((Invoke-DvGet "roles($($role.roleid))/teamroles_association?`$select=name").value)) { Report 'INFO' "TEAM '$($tm.name)' — every member" }
}

# ── (d) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
    $entities = ($Tables | ForEach-Object { "<entity>$_</entity>" }) -join ''
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities>$entities</entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $($Tables -join ', ')." -ForegroundColor Green
}

if ($Verify -or $Apply) {
    if ($gaps.Count -eq 0) { Write-Host "`nPASS: $Column is locked, every business unit's default team reads it, and only the BFF (and System Administrator) can write it." -ForegroundColor Green; exit 0 }
    Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "`nDRY RUN complete — nothing was written." -ForegroundColor Cyan
