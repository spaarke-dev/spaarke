<#
.SYNOPSIS
    Creates sprk_accessinheritance (Multiple lines of text) on sprk_workassignment and sprk_project, ships it in SpaarkeCore
    and LOCKS it with field-level security so only the BFF application users can read or write it (task 175, owner round 87
    and the fix round's verifier F1-1). DRY RUN by default; -Verify checks it at any time.

.DESCRIPTION
    Owner round 87 (2026-10-09, binding): the parent sets a FLOOR for a filed work assignment's or project's Secure
    designation and Access Permission; a user may make the child stricter, and a value set ON the child stays when the
    parent later loosens - only inherited values follow the parent down; a re-file never loosens a child.

    The BFF records, per work assignment and project, what its stored values were derived from - the floor, the parents it
    came from, and what was set on the record - as one small versioned JSON document in sprk_accessinheritance. That record
    decides whether a Secure designation is INHERITED (and so follows its parent out of secure) or the record's OWN (and so
    stays). A user who could write it could forge "inherited" on a child someone secured by hand and have the BFF un-secure
    it without F3 (verifier F1-1). So:

      - the column is FIELD-SECURED, and only task 133's "Spaarke BFF-Managed Field Writers" profile (whose members are the
        BFF application users only) holds it: read=4 create=4 update=4. NO reader profile gets it - no user, form or client
        reads it. (Task 133's "Readers" profile, which every default team holds for sprk_issecure, must NOT hold this column.)
      - every BFF writer refuses a caller-supplied write that names it (sdap.access.access_record_server_only);
      - no maker configuration may target it ((p6) below).

    This script makes that hold in an environment:
      - Multiple lines of text (Memo), MaxLength 4000, not required, audited, on both tables, CREATED SECURED;
      - it ships in the SpaarkeCore solution (its own component row, or the table with rootcomponentbehavior 0), decided ONE
        way through scripts/common/DataverseSolutionMembership.ps1 (Test-DvInSolution);
      - the writer profile is granted read/create/update on it on both tables.

    PRECONDITIONS (every mode; -Apply refuses unless all pass, nothing written):
      (p1) the writer profile exists (exactly one) and is in -SolutionUniqueName. This script never creates it or edits its
           membership: scripts/Set-RecordCreatorPersonSchema.ps1 -Apply does (task 133).
      (p3) the writer profile's members are exactly the -BffApplicationIds application users (no human, no team).
      (p6) no sprk_fieldmappingrule (sprk_targetfield), sprk_aitopicregistry (sprk_targetfield) or sprk_emailupdatefield
           (sprk_targetfieldlogicalname) row targets sprk_accessinheritance - the same three maker-authored write channels
           task 150 checks for sprk_issecure.
      (p7) a column that already exists NOT secured holds no value (anyone with Write could have written one; the BFF would
           trust it). Clear such values before -Apply.

    STEPS (-Apply), per table:
      (a) absent: create the column ALREADY SECURED (IsSecured = true in the create - no moment in which a user can write
          it); present but not secured: secure it. Then IMMEDIATELY grant the writer profile read=4 create=4 update=4 (the
          field-permission service may refuse 0x8004f508 while the securing propagates: 12 retries, 5 s apart).
          IF THE GRANT FAILS the column is LEFT SECURED (never reverted: an unsecured column is the user-writable state this
          script exists to end). That is fail-closed: only System Administrators can then read or write it; a BFF that
          cannot read it reads every record EMPTY, and an empty record never loosens anything (the backfill rule), and a
          record the BFF writes and then reads back empty fails its job run (sdap.inherit.access_record_hidden). Re-running
          -Apply is the resume path: on a secured column it grants only what is missing.
      (b) a present column not in SpaarkeCore is added to it (AddSolutionComponent).
      (c) publish both tables.

    CHECKS (every mode; -Verify exits 0 only when all pass):
      - the column exists on both tables, is Memo (MaxLength >= 1000), IsSecured, and ships in SpaarkeCore;
      - the writer profile holds read=4 create=4 update=4 on it on both tables;
      - NO other profile (task 133's reader profile included) can read, create or update it - except the platform's own
        System Administrator profile, which cannot be narrowed (owner decision F4, as for sprk_issecure);
      - THE BFF CAN READ IT: for each BFF application user, either it holds the System Administrator role, or the platform's
        own answer (RetrievePrincipalAttributePrivileges on the user) gives CanRead on the column on both tables. If the
        platform cannot be asked, -Verify FAILS unless -AcceptDerivedReadCheck is passed, in which case the derivation from
        the profile membership and permission above is accepted with a warning. An FLS read loss would make every record
        read EMPTY - the BFF treats that as "no record yet" and never loosens on it, but nothing would follow its parent.
      - when a row already holds an access record (after the BFF is deployed), it is also READ AS each BFF application user
        (MSCRMCallerID): an empty answer FAILS. Impersonation refused is reported (INFO), not failed: the platform answer
        above is the gate.

    Fail closed (ADR-003). REFUSES (exit 2, nothing written):
      COLUMN_MISMATCH   the column exists on a table but is not Multiple lines of text (never altered silently);
      SOLUTION_MISSING  the SpaarkeCore solution is not in the environment.

    DEPLOY ORDER (task 175): THIS SCRIPT -Apply -> -Verify (exit 0) -> deploy the BFF -> the BFF's job writes every record
    within one run (5 minutes) -> -Verify again (now also the read probe).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users (dev: 5967251e-171c-46fe-a6c2-ef843c90309d and, while
    it is still an application user, 1e40baad-e065-4aea-a8d4-4b7ab273458c). Required for every live mode: (p3) and the read
    check use them. Never hard-coded (per-customer BFF registration, D-13).

.PARAMETER Apply
    Create / secure / grant / add to the solution, publish, read back. Without a mode switch: a read-only dry run.

.PARAMETER Verify
    Read-only check (exit 0 / 1).

.PARAMETER SelfTest
    Offline: runs the pure checks over inline cases; exits 1 on any mismatch. Needs no parameters.

.PARAMETER AcceptDerivedReadCheck
    Accept the BFF read check derived from profile membership + permission when the platform's
    RetrievePrincipalAttributePrivileges cannot be called. A WARN is printed. Use only after confirming the derivation.

.PARAMETER SolutionUniqueName
    The unmanaged solution the column and the writer profile ship in. Default SpaarkeCore.

.EXAMPLE
    .\Set-AccessInheritanceSchema.ps1 -SelfTest
    .\Set-AccessInheritanceSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c            # dry run
    .\Set-AccessInheritanceSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Apply
    .\Set-AccessInheritanceSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify

.NOTES
    Project : unified-access-control-r2
    Task    : 175 (#1478) - owner rounds 84 and 87; fix round (verifier F1-1)
    Created : 2026-10-09
    Docs    : docs/data-model/access-inheritance.md; projects/unified-access-control-r2/notes/task-175-child-access-cascade.md
    Pinned  : AccessInheritanceSchemaScriptAgreementTests (column, tables, writer profile, (p6) channels = the BFF's).

    OPERATOR-RUN ONLY: -Apply is the main session's manual gate, run BEFORE deploying a BFF with task 175. Auth: the
    operator's own az CLI identity (System Administrator in the environment). No secrets.

    Backfill: none by script. The BFF writes the column the first time its job (or an inline follow) sees each record, by
    the backfill rule (a value equal to the parents' floor is inherited; a value stricter than it is set on the record).

    Exit codes: 0 = done / nothing to do / dry run complete / VERIFY PASS / SELF-TEST PASS;
                2 = refused (nothing was written); 1 = VERIFY FAIL, APPLY left a gap, SELF-TEST FAIL, or an unexpected error.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [string[]]$BffApplicationIds = @(),

    [Parameter(Mandatory = $false)]
    [switch]$Apply,

    [Parameter(Mandatory = $false)]
    [switch]$Verify,

    [Parameter(Mandatory = $false)]
    [switch]$SelfTest,

    [Parameter(Mandatory = $false)]
    [switch]$AcceptDerivedReadCheck,

    [Parameter(Mandatory = $false)]
    [string]$SolutionUniqueName = 'SpaarkeCore'
)

$ErrorActionPreference = "Stop"

$modeCount = @($Apply.IsPresent, $Verify.IsPresent, $SelfTest.IsPresent) | Where-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
if ($modeCount -gt 1) { throw "-Apply, -Verify and -SelfTest are separate modes; pass at most one." }

# ── Constants (pinned by AccessInheritanceSchemaScriptAgreementTests) ─────────────────────────────────────────────
$Column = 'sprk_accessinheritance'
$Tables = @('sprk_workassignment', 'sprk_project')
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
$Secured = $true
$MaxLength = 4000
$SystemAdministratorProfileName = 'System Administrator'
$ConfiguredWriterChannels = @(
    @{ T = 'sprk_fieldmappingrule'; C = 'sprk_targetfield' },
    @{ T = 'sprk_aitopicregistry'; C = 'sprk_targetfield' },
    @{ T = 'sprk_emailupdatefield'; C = 'sprk_targetfieldlogicalname' }
)

# ============================================================================
# Pure checks (the self-test runs these offline)
# ============================================================================

<# $Attr: $null (absent) or @{ AttributeType; MaxLength }. Returns 'absent', 'match' or a mismatch text. #>
function Get-ColumnState([object]$Attr) {
    if ($null -eq $Attr) { return 'absent' }
    if ("$($Attr.AttributeType)" -ne 'Memo') { return "mismatch: type $($Attr.AttributeType) (want Memo)" }
    if ($null -ne $Attr.MaxLength -and [int]$Attr.MaxLength -lt 1000) { return "mismatch: MaxLength $($Attr.MaxLength) (want at least 1000)" }
    return 'match'
}

<# $SolutionFound: bool. $How: Test-DvInSolution's answer ('Direct', 'ViaTable' or $null). #>
function Get-SolutionProblem([string]$Table, [bool]$SolutionFound, $How) {
    if (-not $SolutionFound) { return "solution $SolutionUniqueName not found" }
    if ($How -in 'Direct', 'ViaTable') { return $null }
    return "$Table.$Column is not in ${SolutionUniqueName}: neither its own component row nor $Table with rootcomponentbehavior 0"
}

<#
 (p3) $Members: @(@{ Id; Name; AppId }) direct user members; $Teams: @(@{ Name }) team members; $BffUserIds: the
 systemuserids of the -BffApplicationIds users. Returns the problems (empty = exactly the BFF users).
#>
function Get-WriterMemberProblems([object[]]$Members, [object[]]$Teams, [string[]]$BffUserIds) {
    $problems = @()
    $memberIds = @($Members | ForEach-Object { "$($_.Id)".ToLowerInvariant() })
    $allowed = @($BffUserIds | ForEach-Object { "$_".ToLowerInvariant() })
    foreach ($id in $allowed) {
        if ($memberIds -notcontains $id) { $problems += "BFF application user $id is NOT on the writer profile - the BFF could not read or write $Column (unless System Administrator)" }
    }
    foreach ($m in @($Members | Where-Object { "$($_.Id)".ToLowerInvariant() -notin $allowed })) {
        $kind = if ($m.AppId) { "an application user (appId $($m.AppId)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        $problems += "writer profile member '$($m.Name)' is $kind - it could read and forge $Column"
    }
    foreach ($t in @($Teams)) { $problems += "writer profile has TEAM '$($t.Name)' - every member could read and forge $Column" }
    return , $problems
}

<# A field permission row (or $null) for the writer profile on one table. Returns a problem text or $null. #>
function Get-WriterPermissionProblem([string]$Table, $Perm) {
    if ($null -eq $Perm) { return "$WriterProfileName holds no permission on $Table.$Column" }
    if ($Perm.canread -eq 4 -and $Perm.cancreate -eq 4 -and $Perm.canupdate -eq 4) { return $null }
    return "$WriterProfileName on $Table.$Column is read=$($Perm.canread) create=$($Perm.cancreate) update=$($Perm.canupdate); want 4/4/4"
}

<# Every OTHER profile that can read, create or update the column (System Administrator excepted - owner decision F4). #>
function Get-OtherProfileProblems([string]$Table, [object[]]$Perms) {
    $problems = @()
    foreach ($p in @($Perms | Where-Object { $_.ProfileName -notin $WriterProfileName, $SystemAdministratorProfileName })) {
        if ($p.canread -eq 4 -or $p.cancreate -eq 4 -or $p.canupdate -eq 4) {
            $problems += "profile '$($p.ProfileName)' can read/create/update $Table.$Column (read=$($p.canread) create=$($p.cancreate) update=$($p.canupdate)) - only the BFF may"
        }
    }
    return , $problems
}

<#
 Whether one BFF user can READ the column on one table. $IsSysAdmin: holds the System Administrator role.
 $Privileges: $null (the platform could not be asked) or the RetrievePrincipalAttributePrivileges rows @(@{ AttributeId; CanRead }).
 $AttributeId: the column's MetadataId on that table. Returns @{ Problem; Derived } - Derived = the platform answer was missing.
#>
function Get-BffReadProblem([bool]$IsSysAdmin, $Privileges, [string]$AttributeId, [string]$User, [string]$Table) {
    if ($IsSysAdmin) { return @{ Problem = $null; Derived = $false } }
    if ($null -eq $Privileges) { return @{ Problem = "the platform could not be asked whether '$User' can read $Table.$Column"; Derived = $true } }
    $row = @($Privileges | Where-Object { "$($_.AttributeId)".ToLowerInvariant() -eq "$AttributeId".ToLowerInvariant() }) | Select-Object -First 1
    if ($null -ne $row -and ($row.CanRead -eq 4 -or $row.CanRead -eq $true)) { return @{ Problem = $null; Derived = $false } }
    return @{ Problem = "'$User' CANNOT READ $Table.$Column (field security) - every record would read EMPTY to the BFF"; Derived = $false }
}

<# The read probe: the operator sees $OperatorValue (non-empty); the BFF user, impersonated, sees $BffValue. #>
function Get-ProbeProblem([string]$OperatorValue, [string]$BffValue, [string]$User) {
    if ([string]::IsNullOrEmpty($OperatorValue)) { return $null }
    if ([string]::IsNullOrEmpty($BffValue)) { return "read as '$User', a record's $Column comes back EMPTY - the BFF cannot read it" }
    return $null
}

. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')

if ($SelfTest) {
    $tableRcb0 = [guid]::NewGuid(); $tableRcb1 = [guid]::NewGuid(); $column = [guid]::NewGuid(); $directColumn = [guid]::NewGuid()
    $ids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$ids.Add("$tableRcb0"); [void]$ids.Add("$tableRcb1"); [void]$ids.Add("$directColumn")
    $withSub = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$withSub.Add("$tableRcb0")
    $m = [pscustomobject]@{ ObjectIds = $ids; TablesWithSubcomponents = $withSub }
    $bff = '11111111-1111-1111-1111-111111111111'; $human = '22222222-2222-2222-2222-222222222222'; $attr = [guid]::NewGuid()
    Write-Host "Set-AccessInheritanceSchema  -  SELF-TEST (offline)" -ForegroundColor White
    $cases = @(
        @{ Name = 'column absent'; Got = (Get-ColumnState $null); Want = 'absent' },
        @{ Name = 'memo column, 4000'; Got = (Get-ColumnState @{ AttributeType = 'Memo'; MaxLength = 4000 }); Want = 'match' },
        @{ Name = 'memo column too short'; Got = ((Get-ColumnState @{ AttributeType = 'Memo'; MaxLength = 100 }) -like 'mismatch*'); Want = $true },
        @{ Name = 'single line of text'; Got = ((Get-ColumnState @{ AttributeType = 'String'; MaxLength = 4000 }) -like 'mismatch*'); Want = $true },
        @{ Name = 'solution: table with rcb 0 (ViaTable)'; Got = (Get-SolutionProblem 't' $true (Test-DvInSolution -Membership $m -ComponentId $column -TableMetadataId $tableRcb0)); Want = $null },
        @{ Name = 'solution: the column''s own row (Direct)'; Got = (Get-SolutionProblem 't' $true (Test-DvInSolution -Membership $m -ComponentId $directColumn -TableMetadataId $tableRcb1)); Want = $null },
        @{ Name = 'solution: table with rcb 1, no column row'; Got = ($null -ne (Get-SolutionProblem 't' $true (Test-DvInSolution -Membership $m -ComponentId $column -TableMetadataId $tableRcb1))); Want = $true },
        @{ Name = 'solution: not found'; Got = ($null -ne (Get-SolutionProblem 't' $false $null)); Want = $true },
        @{ Name = '(p3) writers exactly the BFF user'; Got = (Get-WriterMemberProblems @(@{ Id = $bff; Name = 'bff'; AppId = 'a' }) @() @($bff)).Count; Want = 0 },
        @{ Name = '(p3) a human writer fails'; Got = (Get-WriterMemberProblems @(@{ Id = $bff; Name = 'bff'; AppId = 'a' }, @{ Id = $human; Name = 'Ann'; AppId = $null }) @() @($bff)).Count; Want = 1 },
        @{ Name = '(p3) a team on the writer profile fails'; Got = (Get-WriterMemberProblems @(@{ Id = $bff; Name = 'bff'; AppId = 'a' }) @(@{ Name = 'Sales' }) @($bff)).Count; Want = 1 },
        @{ Name = '(p3) the BFF user missing fails'; Got = (Get-WriterMemberProblems @() @() @($bff)).Count; Want = 1 },
        @{ Name = 'writer permission 4/4/4'; Got = (Get-WriterPermissionProblem 't' @{ canread = 4; cancreate = 4; canupdate = 4 }); Want = $null },
        @{ Name = 'writer permission without read fails'; Got = ($null -ne (Get-WriterPermissionProblem 't' @{ canread = 0; cancreate = 4; canupdate = 4 })); Want = $true },
        @{ Name = 'writer permission missing fails'; Got = ($null -ne (Get-WriterPermissionProblem 't' $null)); Want = $true },
        @{ Name = 'the reader profile holding it fails'; Got = (Get-OtherProfileProblems 't' @(@{ ProfileName = 'Spaarke BFF-Managed Field Readers'; canread = 4; cancreate = 0; canupdate = 0 })).Count; Want = 1 },
        @{ Name = 'System Administrator and the writer are fine'; Got = (Get-OtherProfileProblems 't' @(@{ ProfileName = $SystemAdministratorProfileName; canread = 4; cancreate = 4; canupdate = 4 }, @{ ProfileName = $WriterProfileName; canread = 4; cancreate = 4; canupdate = 4 })).Count; Want = 0 },
        @{ Name = 'a profile with nothing is fine'; Got = (Get-OtherProfileProblems 't' @(@{ ProfileName = 'Other'; canread = 0; cancreate = 0; canupdate = 0 })).Count; Want = 0 },
        @{ Name = 'BFF read: System Administrator'; Got = (Get-BffReadProblem $true $null "$attr" 'bff' 't').Problem; Want = $null },
        @{ Name = 'BFF read: platform says CanRead 4'; Got = (Get-BffReadProblem $false @(@{ AttributeId = "$attr"; CanRead = 4 }) "$attr" 'bff' 't').Problem; Want = $null },
        @{ Name = 'BFF read: platform says CanRead 0 fails'; Got = ($null -ne (Get-BffReadProblem $false @(@{ AttributeId = "$attr"; CanRead = 0 }) "$attr" 'bff' 't').Problem); Want = $true },
        @{ Name = 'BFF read: column not in the answer fails'; Got = ($null -ne (Get-BffReadProblem $false @(@{ AttributeId = "$([guid]::NewGuid())"; CanRead = 4 }) "$attr" 'bff' 't').Problem); Want = $true },
        @{ Name = 'BFF read: platform not asked is derived'; Got = (Get-BffReadProblem $false $null "$attr" 'bff' 't').Derived; Want = $true },
        @{ Name = 'probe: the BFF reads the value'; Got = (Get-ProbeProblem '{"v":1}' '{"v":1}' 'bff'); Want = $null },
        @{ Name = 'probe: the BFF reads EMPTY fails'; Got = ($null -ne (Get-ProbeProblem '{"v":1}' '' 'bff')); Want = $true },
        @{ Name = 'probe: nothing to read'; Got = (Get-ProbeProblem '' '' 'bff'); Want = $null }
    )
    $failures = 0
    foreach ($c in $cases) {
        if ($c.Got -eq $c.Want) { Write-Host ("  PASS  {0}" -f $c.Name) -ForegroundColor Green }
        else { $failures++; Write-Host ("  FAIL  {0} (got '{1}')" -f $c.Name, $c.Got) -ForegroundColor Red }
    }
    if ($failures -gt 0) { Write-Host "`nSELF-TEST FAIL: $failures case(s)." -ForegroundColor Red; exit 1 }
    Write-Host "`nSELF-TEST PASS: $($cases.Count) check(s)." -ForegroundColor Green
    exit 0
}

# ============================================================================
# Live
# ============================================================================

# `pwsh -File` passes "a,b" as ONE string; accept both shapes.
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($BffApplicationIds.Count -eq 0) { throw "-BffApplicationIds is required for a live run (the BFF application users: (p3) and the read check)." }
foreach ($id in $BffApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-BffApplicationIds: '$id' is not a GUID." } }

$BaseUrl = $EnvironmentUrl.TrimEnd('/')
$Api = "$BaseUrl/api/data/v9.2"
$Token = $null

function Get-DataverseToken {
    $tokenResult = az account get-access-token --resource $BaseUrl --query "accessToken" -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Failed to get a token from Azure CLI: $tokenResult. Run 'az login' first." }
    return "$tokenResult".Trim()
}

function Invoke-Dv {
    param([string]$Endpoint, [string]$Method = "GET", [object]$Body = $null, [switch]$AllowNotFound, [hashtable]$ExtraHeaders = @{})
    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
    }
    foreach ($k in $ExtraHeaders.Keys) { $headers[$k] = $ExtraHeaders[$k] }
    $params = @{ Uri = "$Api/$Endpoint"; Method = $Method; Headers = $headers }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20) }
    try { return Invoke-RestMethod @params }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($AllowNotFound -and $status -eq 404) { return $null }
        $detail = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $err = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($err.error.message) { $detail = $err.error.message }
        }
        throw "API error ($Method $Endpoint): $detail"
    }
}

function New-Label([string]$Text) {
    return @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = $Text; "LanguageCode" = 1033 })
    }
}

function Stop-Refused([string]$Reason) {
    Write-Host ""
    Write-Host "REFUSED: $Reason" -ForegroundColor Red
    Write-Host "Nothing was changed." -ForegroundColor Red
    exit 2
}

$gaps = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } 'WARN' { 'Yellow' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
    if ($State -eq 'WARN') { $warnings.Add($What) }
}

function Get-Column([string]$OnTable) {
    $attr = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$OnTable')/Attributes(LogicalName='$Column')?`$select=LogicalName,AttributeType,MetadataId,IsSecured" -AllowNotFound
    if ($null -eq $attr) { return $null }
    $max = $null
    if ("$($attr.AttributeType)" -eq 'Memo') {
        $memo = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$OnTable')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=MaxLength"
        $max = $memo.MaxLength
    }
    return @{ AttributeType = [string]$attr.AttributeType; MaxLength = $max; MetadataId = [string]$attr.MetadataId; IsSecured = [bool]$attr.IsSecured }
}

function Read-State {
    $sol = Invoke-Dv -Endpoint "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'"
    $found = @($sol.value).Count -gt 0
    $membership = $null
    if ($found) {
        $dvHeaders = @{ Authorization = "Bearer $Token"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
        $membership = Get-DvSolutionMembership -Api $Api -Headers $dvHeaders -SolutionId $sol.value[0].solutionid
    }
    $states = @()
    foreach ($table in $Tables) {
        $col = Get-Column $table
        $entity = Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$table')?`$select=MetadataId,EntitySetName"
        $how = $null
        if ($found) {
            $componentId = if ($null -ne $col -and $col.MetadataId) { $col.MetadataId } else { [guid]::Empty }
            $how = Test-DvInSolution -Membership $membership -ComponentId $componentId -TableMetadataId $entity.MetadataId
        }
        $states += @{
            Table           = $table
            EntitySet       = [string]$entity.EntitySetName
            Column          = $col
            ColumnState     = (Get-ColumnState $col)
            SolutionProblem = (Get-SolutionProblem $table $found $how)
            How             = $how
        }
    }
    return @{ SolutionFound = $found; Membership = $membership; Tables = $states }
}

function Get-WriterProfiles {
    @((Invoke-Dv -Endpoint ("fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$WriterProfileName'" +
        '&$expand=teamprofiles_association($select=teamid,name),systemuserprofiles_association($select=systemuserid,fullname,applicationid)')).value)
}

function Get-Permissions([string]$Table) {
    @((Invoke-Dv -Endpoint ("fieldpermissions?`$select=canread,cancreate,canupdate,_fieldsecurityprofileid_value" +
        "&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$Table' and attributelogicalname eq '$Column'")).value |
        ForEach-Object { @{ ProfileName = [string]$_.fieldsecurityprofileid.name; ProfileId = [string]$_._fieldsecurityprofileid_value; canread = $_.canread; cancreate = $_.cancreate; canupdate = $_.canupdate } })
}

function Grant-Writer([string]$ProfileId, [string]$Table) {
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-Dv -Endpoint 'fieldpermissions' -Method POST -Body @{
                entityname = $Table; attributelogicalname = $Column
                canread = 4; cancreate = 4; canupdate = 4
                'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($ProfileId)"
            } | Out-Null
            return
        } catch {
            # Securing propagates asynchronously (task 141 live run): a grant right after it may be refused 0x8004f508.
            if ("$($_.Exception.Message)" -notmatch '0x8004f508' -or $attempt -ge 12) { throw }
            Write-Host "    $Table.$Column not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
            Start-Sleep -Seconds 5
        }
    }
}

function Set-Secured([string]$Table) {
    $attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')"
    $typed = Invoke-Dv -Endpoint "$attrPath/Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    $typed.IsSecured = $true
    Invoke-Dv -Endpoint $attrPath -Method PUT -Body $typed -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
}

function Write-GrantFailed([string]$Table, [string]$Cause) {
    Write-Host ''
    Write-Host "  !!! $Table.$Column is field-secured but the writer profile's grant FAILED: $Cause" -ForegroundColor Red
    Write-Host "  It is left SECURED on purpose (fail closed): no user can read or write it; a BFF that is not System" -ForegroundColor Red
    Write-Host "  Administrator cannot either - it then reads every record EMPTY and loosens nothing, and its job run fails" -ForegroundColor Red
    Write-Host "  (sdap.inherit.access_record_hidden / access_record_not_written). Fix the cause and re-run -Apply: on a" -ForegroundColor Red
    Write-Host "  secured column it grants only what is missing. Then -Verify. Do NOT un-secure the column." -ForegroundColor Red
}

$Mode = if ($Apply) { "APPLY" } elseif ($Verify) { "VERIFY (read-only)" } else { "DRY RUN (read-only; pass -Apply to perform the writes)" }
Write-Host "Set-AccessInheritanceSchema  -  $Mode" -ForegroundColor White
Write-Host "Environment: $BaseUrl"
Write-Host "Column     : $Column (Multiple lines of text, $MaxLength, field-secured) on $($Tables -join ', '), solution $SolutionUniqueName"
Write-Host "Writers    : '$WriterProfileName' = the BFF application users $($BffApplicationIds -join ', ') (no other profile)"

try {
    $Token = Get-DataverseToken
    $state = Read-State
}
catch {
    if ($Verify) { Write-Host "`nVERIFY FAIL: read fault - $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
    throw
}

# ── PRECONDITIONS ───────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`nPreconditions"
$writers = Get-WriterProfiles
$writer = $null
if ($writers.Count -eq 1) { $writer = $writers[0]; Report 'OK' "(p1) profile '$WriterProfileName' exists" }
else { Report 'FAIL' "(p1) $($writers.Count) profile(s) named '$WriterProfileName' - exactly one is required; scripts/Set-RecordCreatorPersonSchema.ps1 -Apply creates it (this script never does)" }
if (-not $state.SolutionFound) { Report 'FAIL' "(p1) solution '$SolutionUniqueName' not found" }
elseif ($writer) {
    if (Test-DvInSolution -Membership $state.Membership -ComponentId $writer.fieldsecurityprofileid) { Report 'OK' "(p1) '$WriterProfileName' is in $SolutionUniqueName" }
    else { Report 'FAIL' "(p1) '$WriterProfileName' is not in $SolutionUniqueName - Set-RecordCreatorPersonSchema.ps1 -Apply adds it" }
}

$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-Dv -Endpoint "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if (-not $u) { Report 'FAIL' "(p3) no application user for appId $appId in this environment"; continue }
    $bffUsers += $u
}
if ($writer) {
    $members = @($writer.systemuserprofiles_association | ForEach-Object { @{ Id = [string]$_.systemuserid; Name = [string]$_.fullname; AppId = $_.applicationid } })
    $teams = @($writer.teamprofiles_association | ForEach-Object { @{ Name = [string]$_.name } })
    $memberProblems = Get-WriterMemberProblems $members $teams @($bffUsers | ForEach-Object { [string]$_.systemuserid })
    if ($memberProblems.Count -eq 0) { Report 'OK' "(p3) the writer profile's members are exactly the BFF application user(s): $(($bffUsers | ForEach-Object { $_.fullname }) -join ', ')" }
    foreach ($p in $memberProblems) { Report 'FAIL' "(p3) $p" }
}

$configuredWriters = 0
foreach ($ch in $ConfiguredWriterChannels) {
    $set = (Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$($ch.T)')?`$select=EntitySetName").EntitySetName
    foreach ($row in @((Invoke-Dv -Endpoint "$set`?`$select=$($ch.T)id&`$filter=$($ch.C) eq '$Column'").value)) {
        $configuredWriters++
        Report 'FAIL' "(p6) $($ch.T) row $($row."$($ch.T)id") targets $Column - remove or retarget it; only the BFF's cascade writes it"
    }
}
if ($configuredWriters -eq 0) { Report 'OK' "(p6) no field-mapping rule, AI topic-registry row or email update field targets $Column" }

# (p7) a column that exists NOT secured may already hold values someone other than the BFF wrote (anyone with Write could):
# the BFF would trust them as its own record. Securing does not make them trustworthy, so -Apply refuses until they are
# cleared (an empty record never loosens anything; the BFF writes a fresh one by the backfill rule).
foreach ($t in $state.Tables | Where-Object { $_.Column -and -not $_.Column.IsSecured }) {
    $held = @((Invoke-Dv -Endpoint "$($t.EntitySet)?`$select=$($t.Table)id&`$filter=$Column ne null&`$top=1").value)
    if ($held.Count -gt 0) { Report 'FAIL' "(p7) $($t.Table).$Column is not field-secured and rows already hold values nobody can vouch for - clear them (set to empty) before -Apply" }
    else { Report 'OK' "(p7) $($t.Table).$Column is not secured yet and holds no value" }
}

# ── COLUMN ──────────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`nColumn"
foreach ($t in $state.Tables) {
    Write-Host ("  {0,-22}: column {1}{2}; {3}" -f $t.Table, $t.ColumnState,
        $(if ($t.Column) { $(if ($t.Column.IsSecured) { ', field-secured' } else { ', NOT field-secured' }) } else { '' }),
        $(if ($t.SolutionProblem) { $t.SolutionProblem } else { "ships in $SolutionUniqueName ($($t.How))" }))
}
if (-not $Verify) {
    if (-not $state.SolutionFound) { Stop-Refused "SOLUTION_MISSING: $SolutionUniqueName is not in the environment." }
    $mismatch = @($state.Tables | Where-Object { $_.ColumnState -like 'mismatch*' })
    if ($mismatch.Count -gt 0) { Stop-Refused "COLUMN_MISMATCH: $(($mismatch | ForEach-Object { "$($_.Table): $($_.ColumnState)" }) -join '; '); never altered silently." }
}
if ($Apply -and $gaps.Count -gt 0) {
    Write-Host "`nREFUSED: -Apply needs every precondition to pass ($($gaps.Count) gap(s)). Nothing was written." -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 2
}

# ── APPLY: create SECURED / secure, then grant the writer at once; add to the solution; publish ─────────────
if ($Apply) {
    Write-Host "`nApply"
    foreach ($t in $state.Tables) {
        $secureNow = $false
        if ($t.ColumnState -eq 'absent') {
            $definition = @{
                "@odata.type"    = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
                "SchemaName"     = "sprk_AccessInheritance"
                "RequiredLevel"  = @{ "Value" = "None" }
                "IsAuditEnabled" = @{ "Value" = $true }
                "IsSecured"      = $Secured
                "MaxLength"      = $MaxLength
                "Format"         = "TextArea"
                "DisplayName"    = New-Label "Access Inheritance (system)"
                "Description"    = New-Label "Written by the Spaarke BFF only (field-secured): what this record's Secure designation and Access Permission were derived from (the floor its parents set, the parents, and what was set on this record). Owner round 87."
            }
            Invoke-Dv -Endpoint "EntityDefinitions(LogicalName='$($t.Table)')/Attributes" -Method POST -Body $definition -ExtraHeaders @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName } | Out-Null
            Report 'DONE' "created $($t.Table).$Column SECURED under $SolutionUniqueName"
            $secureNow = $true
        }
        elseif (-not $t.Column.IsSecured) {
            Set-Secured $t.Table
            Report 'DONE' "secured $($t.Table).$Column (no user can write it from now)"
            $secureNow = $true
        }
        elseif ($t.SolutionProblem) {
            Invoke-Dv -Endpoint "AddSolutionComponent" -Method POST -Body @{
                ComponentId = $t.Column.MetadataId; ComponentType = 2; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false
            } | Out-Null
            Report 'DONE' "added $($t.Table).$Column to $SolutionUniqueName"
        }
        if ($t.ColumnState -ne 'absent' -and -not $t.Column.IsSecured -and $t.SolutionProblem) {
            Invoke-Dv -Endpoint "AddSolutionComponent" -Method POST -Body @{
                ComponentId = $t.Column.MetadataId; ComponentType = 2; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false
            } | Out-Null
            Report 'DONE' "added $($t.Table).$Column to $SolutionUniqueName"
        }

        # The writer's grant: at once after securing; on an already-secured column, whatever is missing (the resume path).
        $writerPerm = @(Get-Permissions $t.Table | Where-Object { $_.ProfileId -eq "$($writer.fieldsecurityprofileid)" }) | Select-Object -First 1
        if (-not $writerPerm) {
            $clock = [System.Diagnostics.Stopwatch]::StartNew()
            try { Grant-Writer $writer.fieldsecurityprofileid $t.Table }
            catch { Write-GrantFailed $t.Table $_.Exception.Message; exit 1 }
            $clock.Stop()
            Report 'DONE' ("granted '$WriterProfileName' read/create/update on $($t.Table).$Column{0}" -f $(if ($secureNow) { " ({0:N1}s after securing)" -f $clock.Elapsed.TotalSeconds } else { '' }))
        }
    }
    $entitiesXml = ($Tables | ForEach-Object { "<entity>$_</entity>" }) -join ''
    Invoke-Dv -Endpoint "PublishXml" -Method POST -Body @{ ParameterXml = "<importexportxml><entities>$entitiesXml</entities></importexportxml>" } | Out-Null
    Report 'DONE' "published $($Tables -join ', ')"
    $state = Read-State
}

# ── CHECKS (every mode) ─────────────────────────────────────────────────────────────────────────────────────
Write-Host "`nChecks"
$sysAdminUsers = @{}
foreach ($u in $bffUsers) {
    $roles = @((Invoke-Dv -Endpoint "systemusers($($u.systemuserid))/systemuserroles_association?`$select=name&`$filter=name eq 'System Administrator'").value)
    $sysAdminUsers["$($u.systemuserid)"] = ($roles.Count -gt 0)
}
$privilegesOf = @{}
foreach ($u in $bffUsers) {
    if ($sysAdminUsers["$($u.systemuserid)"]) { continue }
    try {
        $answer = Invoke-Dv -Endpoint "systemusers($($u.systemuserid))/Microsoft.Dynamics.CRM.RetrievePrincipalAttributePrivileges()"
        $privilegesOf["$($u.systemuserid)"] = @($answer.AttributePrivileges)
    } catch {
        $privilegesOf["$($u.systemuserid)"] = $null
        Report 'INFO' "RetrievePrincipalAttributePrivileges for '$($u.fullname)' could not be called: $($_.Exception.Message)"
    }
}

foreach ($t in $state.Tables) {
    $state1 = if ($t.ColumnState -eq 'match' -and -not $t.SolutionProblem) { 'OK' } elseif ($Apply -or $Verify) { 'FAIL' } else { 'WOULD' }
    Report $state1 ("{0}.{1}: {2}; {3}" -f $t.Table, $Column, $t.ColumnState, $(if ($t.SolutionProblem) { $t.SolutionProblem } else { "in $SolutionUniqueName" }))
    if ($null -eq $t.Column) { continue }

    if ($t.Column.IsSecured) { Report 'OK' "$($t.Table).$Column is field-secured" }
    elseif ($Apply -or $Verify) { Report 'FAIL' "$($t.Table).$Column is NOT field-secured - any user with Write could forge it" }
    else { Report 'WOULD' "secure $($t.Table).$Column, then at once grant '$WriterProfileName'" }

    $perms = Get-Permissions $t.Table
    if ($writer) {
        $writerPerm = @($perms | Where-Object { $_.ProfileId -eq "$($writer.fieldsecurityprofileid)" }) | Select-Object -First 1
        $problem = Get-WriterPermissionProblem $t.Table $writerPerm
        if (-not $problem) { Report 'OK' "'$WriterProfileName' holds read/create/update on $($t.Table).$Column" }
        elseif ($Apply -or $Verify) { Report 'FAIL' $problem }
        else { Report 'WOULD' "grant '$WriterProfileName' read/create/update on $($t.Table).$Column" }
    }
    $others = Get-OtherProfileProblems $t.Table $perms
    if ($others.Count -eq 0) { Report 'OK' "no other profile can read or write $($t.Table).$Column (System Administrator excepted, F4)" }
    foreach ($o in $others) { Report 'FAIL' $o }

    foreach ($u in $bffUsers) {
        $read = Get-BffReadProblem $sysAdminUsers["$($u.systemuserid)"] $privilegesOf["$($u.systemuserid)"] $t.Column.MetadataId $u.fullname $t.Table
        if (-not $read.Problem) {
            Report 'OK' ("BFF '{0}' can READ {1}.{2} ({3})" -f $u.fullname, $t.Table, $Column, $(if ($sysAdminUsers["$($u.systemuserid)"]) { 'System Administrator' } else { 'platform: RetrievePrincipalAttributePrivileges' }))
        }
        elseif ($read.Derived) {
            $derived = $writer -and -not (Get-WriterPermissionProblem $t.Table (@($perms | Where-Object { $_.ProfileId -eq "$($writer.fieldsecurityprofileid)" }) | Select-Object -First 1)) -and
                (@($writer.systemuserprofiles_association | Where-Object { "$($_.systemuserid)" -eq "$($u.systemuserid)" }).Count -gt 0) -and $t.Column.IsSecured
            if ($derived -and $AcceptDerivedReadCheck) { Report 'WARN' "BFF '$($u.fullname)' can read $($t.Table).$Column BY DERIVATION (writer member + read=4); the platform was not asked (-AcceptDerivedReadCheck)" }
            elseif ($derived) { Report 'FAIL' "$($read.Problem); by derivation it can (writer member + read=4) - confirm, then re-run with -AcceptDerivedReadCheck" }
            elseif ($Apply -or $Verify) { Report 'FAIL' "$($read.Problem), and the derivation does not hold either" }
            else { Report 'WOULD' "after -Apply: BFF '$($u.fullname)' reads $($t.Table).$Column through '$WriterProfileName'" }
        }
        elseif ($Apply -or $Verify) { Report 'FAIL' $read.Problem }
        else { Report 'WOULD' "after -Apply: BFF '$($u.fullname)' reads $($t.Table).$Column through '$WriterProfileName' ($($read.Problem) now)" }
    }

    # The read probe: a row that already holds a record (after the BFF is deployed), read as each BFF user.
    if ($t.Column.IsSecured -and $t.EntitySet) {
        $idColumn = "$($t.Table)id"
        $sample = @((Invoke-Dv -Endpoint "$($t.EntitySet)?`$select=$idColumn,$Column&`$filter=$Column ne null&`$top=1").value) | Select-Object -First 1
        if (-not $sample) { Report 'INFO' "no $($t.Table) row holds a record yet - the read probe runs on the -Verify after the BFF's first job run" }
        else {
            foreach ($u in $bffUsers) {
                try {
                    $asBff = Invoke-Dv -Endpoint "$($t.EntitySet)($($sample.$idColumn))?`$select=$Column" -ExtraHeaders @{ MSCRMCallerID = "$($u.systemuserid)" }
                    $probe = Get-ProbeProblem ([string]$sample.$Column) ([string]$asBff.$Column) $u.fullname
                    if ($probe) { Report 'FAIL' "$($t.Table) $($sample.$idColumn): $probe" }
                    else { Report 'OK' "read as '$($u.fullname)', $($t.Table) $($sample.$idColumn) returns its record" }
                } catch {
                    Report 'INFO' "the read probe as '$($u.fullname)' could not impersonate it ($($_.Exception.Message)); the platform check above is the gate"
                }
            }
        }
    }
}

if ($Verify -or $Apply) {
    if ($gaps.Count -eq 0) {
        $suffix = if ($warnings.Count -gt 0) { " ($($warnings.Count) warning(s))" } else { '' }
        Write-Host "`nPASS$($suffix): $Column is field-secured on both tables, only the BFF (and System Administrator) can read or write it, and the BFF can read it." -ForegroundColor Green
        exit 0
    }
    Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "`nDRY RUN complete - nothing was written. Re-run with -Apply." -ForegroundColor Cyan
exit 0
