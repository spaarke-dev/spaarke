<#
.SYNOPSIS
    Adds the five request-intake columns to sprk_servicerequest and locks sprk_intakestage so only the BFF can write it
    (spaarke-intake-pipeline-r1, task 010, FR-05).

.DESCRIPTION
    Adds, to the EXISTING sprk_servicerequest entity (no new entity). Names and option values were agreed with
    spaarke-ontology-platform-r1 on 2026-11-27 (projects/spaarke-intake-pipeline-r1/notes/decisions.md); none of the five
    collides with an ontology column, and this script never creates, renames or alters sprk_direction, sprk_disposition
    or sprk_responseduedate (ontology-owned).

      sprk_intakechannel     local choice  Front Door 100000000, Console 100000001, Email 100000002, Copilot 100000003.
                             The door the request came in by.
      sprk_intakestage       local choice  Submitted 100000000, In review 100000001, Needs triage 100000002,
                             Awaiting approval 100000003, Returned 100000004, Closed 100000005.
                             COLUMN-SECURED (see below).
      sprk_currentrun        lookup -> sprk_processingjob (Delete = RemoveLink). The run currently driving the request.
      sprk_sendemailupdates  boolean, column default No. The per-channel default (ON for email-door requests, OFF for
                             Front Door and Console) is applied by the door code at create, NOT by the column default.
      sprk_correlationid     string, max length 36. Canonical lowercase hyphenated GUID text (ADR-044), the same format the
                             BFF writes to sprk_processingjob.sprk_correlationid.

    Reused, NOT created here (checked and reported): sprk_requesttype (local choice) is the request type, and
    sprk_assignedtointernal (lookup -> contact, owner decision 2026-11-27) is the assigned attorney. No attorney column.

    SINGLE WRITER = THE BFF (ADR-002: no plugin, no client write path). Stage is the one column a user could otherwise
    change from a form or Xrm.WebApi, so it is field-secured through the EXISTING task-133 profile pair (this script never
    creates or edits a profile or its members):
      "Spaarke BFF-Managed Field Readers"  Read; on every business unit's default team (every user keeps reading stage).
      "Spaarke BFF-Managed Field Writers"  Read + Create + Update; the BFF application users ONLY.
    The other four columns are written by the BFF by convention and are not secured (nothing keys a security decision on
    them); the data-model doc says so.

    PRECONDITIONS for the security step (checked in every mode; a real run refuses unless all pass, writing nothing):
      (p1) both profiles exist;
      (p2) the reader profile is on EVERY business unit's default team (except the ontology writer unit's, which must be on
           no profile - scripts/common/OntologyWriterUnit.ps1);
      (p3) the writer profile's members are exactly the -BffApplicationIds application users, and no team;
      (p4) sprk_servicerequest is in -SolutionUniqueName (the columns are added to that solution);
      (p5) none of the five names exists with another type, option set, length, default or target (a conflict is an
           escalation: exit 2, nothing changed; this script never alters an existing column).

    STEPS: create what is missing (check first, skip what exists) -> secure sprk_intakestage and IMMEDIATELY grant the writer
    row then the reader row (Set-SecureFlagFieldSecurity.ps1 pattern: if a grant fails after securing, the securing is
    reverted so no reader is left masked) -> report any OTHER profile that can write stage (FAIL; the platform System
    Administrator profile cannot be narrowed and is excused) -> entity-scoped publish -> read-back.

    MODES
      -WhatIf   read-only metadata calls only; prints what a real run would create, secure and grant; writes nothing.
      (none)    real run. Idempotent: a second run reports every column "unchanged" and writes nothing but the publish.
      -Verify   read-only pass/fail check (exit 0 = pass, 1 = a gap, each one named).
    Exit codes: 0 = done / pass, 1 = a gap or an error, 2 = escalation (a conflicting column; nothing changed).

    Requires Azure CLI authentication (az login). Auth is the operator's own identity (System Administrator).

.PARAMETER EnvironmentUrl
    Dataverse environment URL (default https://spaarkedev1.crm.dynamics.com). Dev only for this project.

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users. Dev: 5967251e-171c-46fe-a6c2-ef843c90309d
    (mi-bff-api-dev) and 1e40baad-e065-4aea-a8d4-4b7ab273458c (SDAP-BFF-SPE-API, while it is still an application user).
    They are what (p3) checks; the id is looked up as an application user, never assumed.

.PARAMETER SolutionUniqueName
    The unmanaged solution the columns are added to (default SpaarkeMaster). It must contain sprk_servicerequest.

.PARAMETER Verify
    Read-only check with a pass/fail exit code. Cannot be combined with -WhatIf.

.EXAMPLE
    pwsh scripts/Add-ServiceRequestIntakeColumns.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -WhatIf

.EXAMPLE
    pwsh scripts/Add-ServiceRequestIntakeColumns.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c

.EXAMPLE
    pwsh scripts/Add-ServiceRequestIntakeColumns.ps1 -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify

.NOTES
    Project: spaarke-intake-pipeline-r1, task 010. Structure copied from Add-BindingContextTypeTagsColumn.ps1 and
    Add-ProcessingJobIntakeRunColumns.ps1; column security from Set-SecureFlagFieldSecurity.ps1. A real run needs the
    owner's explicit OK (project CLAUDE.md section 5; given 2026-11-27 for dev). This script never edits the SpaarkeMaster
    source files; a solution export sync is a separate step.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
if ($DryRun -and $Verify) { throw '-WhatIf and -Verify are separate modes.' }
# `pwsh -File` passes "a,b" as ONE string; accept both shapes.
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($id in $BffApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-BffApplicationIds: '$id' is not a GUID." } }

$Api = "$EnvironmentUrl/api/data/v9.2"
$Table = 'sprk_servicerequest'
$RunTable = 'sprk_processingjob'
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
# The platform's own System Administrator profile: fixed id in every environment, gets full access automatically, cannot be narrowed.
$SystemAdminProfileId = '572329c1-a042-4e22-be47-367c6374ea45'

# ── The specification (names and option values agreed with ontology-r1 on 2026-11-27) ──────────────────────────────────────
# Arrays of @{Value; Label}, not a dictionary: an ordered dictionary's [int] indexer is POSITIONAL, so $options[100000000] would read nothing.
$ChannelOptions = @(
    @{ Value = 100000000; Label = 'Front Door' }, @{ Value = 100000001; Label = 'Console' },
    @{ Value = 100000002; Label = 'Email' }, @{ Value = 100000003; Label = 'Copilot' })
$StageOptions = @(
    @{ Value = 100000000; Label = 'Submitted' }, @{ Value = 100000001; Label = 'In review' }, @{ Value = 100000002; Label = 'Needs triage' },
    @{ Value = 100000003; Label = 'Awaiting approval' }, @{ Value = 100000004; Label = 'Returned' }, @{ Value = 100000005; Label = 'Closed' })
$StageColumn = 'sprk_intakestage'
$LookupRelationship = 'sprk_processingjob_servicerequest_currentrun'

# ── Dataverse plumbing ─────────────────────────────────────────────────────────────────────────────────────────────────────
$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$Headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
    # Attribute metadata (IsSecured) is cached per server; a plain read soon after a change can be stale (#2833).
    Consistency        = 'Strong'
}

function Invoke-Dv {
    param([string]$Path, [string]$Method = 'GET', $Body = $null, [hashtable]$Extra = @{})
    $h = $Headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $p = @{ Uri = "$Api/$Path"; Method = $Method; Headers = $h }
    if ($null -ne $Body) { $p.Body = ($Body | ConvertTo-Json -Depth 20 -Compress) }
    try { return Invoke-RestMethod @p }
    catch {
        $status = $null
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        $detail = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $j = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($j.error.message) { $detail = $j.error.message }
        }
        $ex = [System.Exception]::new("API Error ($Method $Path): $detail")
        $ex.Data['StatusCode'] = $status
        throw $ex
    }
}

# Returns $null only on a 404; any other failure (expired token, 5xx) stops the run instead of reading as "missing".
function Get-DvOrNull([string]$Path, [int]$Retries = 0) {
    for ($i = 0; ; $i++) {
        try { return Invoke-Dv $Path }
        catch {
            if ($_.Exception.Data['StatusCode'] -ne 404) { throw }
            if ($i -ge $Retries) { return $null }
            Start-Sleep -Seconds 3
        }
    }
}

function New-Label([string]$Text) {
    return @{
        '@odata.type'   = 'Microsoft.Dynamics.CRM.Label'
        LocalizedLabels = @(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.LocalizedLabel'; Label = $Text; LanguageCode = 1033 })
    }
}

class EscalationException : System.Exception {
    EscalationException([string]$Message) : base($Message) {}
}

. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
. (Join-Path $PSScriptRoot 'common/OntologyWriterUnit.ps1')

$gaps = [System.Collections.Generic.List[string]]::new()
$created = [System.Collections.Generic.List[string]]::new()
$unchanged = [System.Collections.Generic.List[string]]::new()
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ('  {0,-8} {1}' -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
}

# ── Column specifications ──────────────────────────────────────────────────────────────────────────────────────────────────
function New-PicklistDef([string]$SchemaName, [string]$Label, [string]$Description, $Options) {
    $opts = @($Options | ForEach-Object { @{ Value = [int]$_.Value; Label = (New-Label $_.Label) } })
    return @{
        '@odata.type'   = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'
        SchemaName      = $SchemaName
        RequiredLevel   = @{ Value = 'None' }
        DisplayName     = New-Label $Label
        Description     = New-Label $Description
        OptionSet       = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.OptionSetMetadata'; IsGlobal = $false; OptionSetType = 'Picklist'; Options = $opts }
    }
}

$ColumnSpecs = @(
    @{
        Column = 'sprk_intakechannel'; Kind = 'Picklist'; Options = $ChannelOptions
        Def = (New-PicklistDef 'sprk_IntakeChannel' 'Intake Channel' 'The door this request came in by: Front Door, Console, Email or Copilot. Written only by the BFF at create.' $ChannelOptions)
    }
    @{
        Column = $StageColumn; Kind = 'Picklist'; Options = $StageOptions
        Def = (New-PicklistDef 'sprk_IntakeStage' 'Intake Stage' 'Where the request is in the intake pipeline. COLUMN-SECURED: written only by the BFF (the Spaarke BFF-Managed Field Writers profile). A row with no stage is not an intake request.' $StageOptions)
    }
    @{
        Column = 'sprk_sendemailupdates'; Kind = 'Boolean'
        Def = @{
            '@odata.type'  = 'Microsoft.Dynamics.CRM.BooleanAttributeMetadata'
            SchemaName     = 'sprk_SendEmailUpdates'
            RequiredLevel  = @{ Value = 'None' }
            DefaultValue   = $false
            DisplayName    = New-Label 'Send Email Updates'
            Description    = New-Label 'Whether the requester gets a status email at each status change. The column default is No; the per-channel default (on for email-door requests, off for Front Door and Console) is applied by the door code at create. Written only by the BFF.'
            OptionSet      = @{
                '@odata.type' = 'Microsoft.Dynamics.CRM.BooleanOptionSetMetadata'
                TrueOption    = @{ Value = 1; Label = (New-Label 'Yes') }
                FalseOption   = @{ Value = 0; Label = (New-Label 'No') }
            }
        }
    }
    @{
        Column = 'sprk_correlationid'; Kind = 'String'
        Def = @{
            '@odata.type'  = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'
            SchemaName     = 'sprk_CorrelationId'
            RequiredLevel  = @{ Value = 'None' }
            MaxLength      = 36
            DisplayName    = New-Label 'Correlation Id'
            Description    = New-Label 'Canonical lowercase hyphenated GUID text (ADR-044), the same format as sprk_processingjob.sprk_correlationid. Ties the request to its runs, logs and emails. Written only by the BFF.'
        }
    }
)
$LookupColumn = 'sprk_currentrun'

# Every way a column differs from the specification above; empty when it is as specified. Security is checked separately.
function Get-ColumnGaps($Spec) {
    $c = $Spec.Column
    $out = [System.Collections.Generic.List[string]]::new()
    $base = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$c')"
    $a = Get-DvOrNull "${base}?`$select=LogicalName,AttributeType"
    if (-not $a) { $out.Add("$Table.$c does not exist"); return $out.ToArray() }
    if ($a.AttributeType -ne $Spec.Kind) { $out.Add("$Table.$c is '$($a.AttributeType)', not $($Spec.Kind)"); return $out.ToArray() }
    switch ($Spec.Kind) {
        'String' {
            $s = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength"
            if ([int]$s.MaxLength -ne 36) { $out.Add("$Table.$c has MaxLength $($s.MaxLength), expected 36") }
        }
        'Boolean' {
            $b = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.BooleanAttributeMetadata?`$select=DefaultValue"
            if ($b.DefaultValue -ne $false) { $out.Add("$Table.$c default is '$($b.DefaultValue)', expected No") }
        }
        'Picklist' {
            $p = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal)"
            if ($p.OptionSet.IsGlobal -eq $true) { $out.Add("$Table.$c uses a global option set, expected a local one") }
            $have = @{}; foreach ($o in @($p.OptionSet.Options)) { $have[[int]$o.Value] = $o.Label.UserLocalizedLabel.Label }
            foreach ($o in $Spec.Options) {
                if (-not $have.ContainsKey([int]$o.Value)) { $out.Add("$Table.$c lacks option $($o.Value) '$($o.Label)'") }
                elseif ($have[[int]$o.Value] -ne $o.Label) { $out.Add("$Table.$c option $($o.Value) is '$($have[[int]$o.Value])', expected '$($o.Label)'") }
            }
            $wanted = @($Spec.Options | ForEach-Object { [int]$_.Value })
            foreach ($k in $have.Keys) { if ($wanted -notcontains [int]$k) { $out.Add("$Table.$c has an unexpected option $k '$($have[$k])'") } }
        }
    }
    return $out.ToArray()
}

function Get-LookupGaps {
    $out = [System.Collections.Generic.List[string]]::new()
    $base = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$LookupColumn')"
    $a = Get-DvOrNull "${base}?`$select=LogicalName,AttributeType"
    if (-not $a) { $out.Add("$Table.$LookupColumn does not exist"); return $out.ToArray() }
    if ($a.AttributeType -ne 'Lookup') { $out.Add("$Table.$LookupColumn is '$($a.AttributeType)', not Lookup"); return $out.ToArray() }
    $l = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
    if (@($l.Targets) -notcontains $RunTable -or @($l.Targets).Count -ne 1) { $out.Add("$Table.$LookupColumn targets '$(@($l.Targets) -join ',')', expected only $RunTable") }
    return $out.ToArray()
}

# ── Header ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
$org = (Invoke-Dv 'organizations?$select=name').value[0].name
Write-Host ''
Write-Host "Add intake columns to $Table (spaarke-intake-pipeline-r1, task 010)" -ForegroundColor Cyan
Write-Host "Environment : $EnvironmentUrl (org '$org')   Solution: $SolutionUniqueName"
Write-Host ('Mode        : {0}' -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($DryRun) { 'WHATIF (read-only; nothing is created, secured or published)' } else { 'APPLY' }))

try {
    # ── Preconditions ──────────────────────────────────────────────────────────────────────────────────────────────────────
    Write-Host "`nPreconditions"
    foreach ($e in @($Table, $RunTable)) {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$e')?`$select=LogicalName" 2)) { throw "$e does not exist on $EnvironmentUrl." }
    }
    Report 'OK' "$Table and $RunTable exist"

    $tableMeta = Invoke-Dv "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId"
    $solution = @((Invoke-Dv "solutions?`$select=solutionid,ismanaged&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
    if (-not $solution) { Report 'FAIL' "(p4) solution '$SolutionUniqueName' not found" }
    elseif ($solution.ismanaged) { Report 'FAIL' "(p4) solution '$SolutionUniqueName' is managed; columns can only be added to an unmanaged solution" }
    else {
        $membership = Get-DvSolutionMembership -Api $Api -Headers $Headers -SolutionId $solution.solutionid
        if (Test-DvInSolution -Membership $membership -ComponentId $tableMeta.MetadataId) { Report 'OK' "(p4) $Table is in $SolutionUniqueName" }
        else { Report 'FAIL' "(p4) $Table is not in $SolutionUniqueName; owner decision needed on which solution carries these columns" }
    }

    # Reused columns: record the types (task 010 step 2; owner chose to reuse sprk_assignedtointernal as the contact lookup).
    $rt = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='sprk_requesttype')?`$select=AttributeType"
    $ai = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='sprk_assignedtointernal')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=AttributeType,Targets"
    Report 'INFO' ("reused sprk_requesttype is {0}; reused sprk_assignedtointernal is {1} -> {2} (neither is created or changed here)" -f $rt.AttributeType, $ai.AttributeType, (@($ai.Targets) -join ','))

    # (p5) a conflicting existing column is an escalation, never altered.
    $conflicts = [System.Collections.Generic.List[string]]::new()
    foreach ($spec in $ColumnSpecs) {
        if (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$($spec.Column)')?`$select=LogicalName") {
            foreach ($g in (Get-ColumnGaps $spec)) { $conflicts.Add($g) }
        }
    }
    if (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$LookupColumn')?`$select=LogicalName") {
        foreach ($g in (Get-LookupGaps)) { $conflicts.Add($g) }
    }
    if ($conflicts.Count -gt 0) {
        throw [EscalationException]::new("(p5) an intake column exists but is not as specified: $($conflicts -join '; '). Owner decision needed (this script does not alter an existing column).")
    }
    Report 'OK' '(p5) no intake column exists in a conflicting shape'

    # Field security profiles: exist, reader on every default team, writer = exactly the BFF application users.
    function Get-Profile([string]$Name) {
        @((Invoke-Dv ("fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$Name'" +
                    '&$expand=teamprofiles_association($select=teamid,name,isdefault),systemuserprofiles_association($select=systemuserid,fullname,applicationid)')).value)
    }
    $readers = Get-Profile $ReaderProfileName
    $writers = Get-Profile $WriterProfileName
    $reader = $null; $writer = $null
    foreach ($pair in @(@{ Rows = $readers; Name = $ReaderProfileName }, @{ Rows = $writers; Name = $WriterProfileName })) {
        if ($pair.Rows.Count -eq 1) { Report 'OK' "(p1) profile '$($pair.Name)' exists" }
        else { Report 'FAIL' "(p1) $($pair.Rows.Count) profile(s) named '$($pair.Name)' - exactly one is required (scripts/Set-RecordCreatorPersonSchema.ps1 -Apply creates it; this script never does)" }
    }
    if ($readers.Count -eq 1) { $reader = $readers[0] }
    if ($writers.Count -eq 1) { $writer = $writers[0] }

    if ($reader) {
        $onReader = @($reader.teamprofiles_association | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
        $allDefaultTeams = @((Invoke-Dv 'teams?$select=teamid,name,_businessunitid_value&$filter=isdefault eq true&$expand=businessunitid($select=name)').value)
        if ($allDefaultTeams.Count -eq 0) { Report 'FAIL' '(p2) no business-unit default team was read - the query is wrong' }
        $split = Split-DvDefaultTeamsByOntologyWriterUnit -Api $Api -Headers $Headers -DefaultTeams $allDefaultTeams
        foreach ($t in $split.WriterUnitTeams) {
            if ($onReader -contains $t.teamid.ToString().ToLowerInvariant()) { Report 'FAIL' "(p2) default team '$($t.name)' of the ontology writer's unit is on the reader profile - it must be on no profile; remove it by hand" }
        }
        foreach ($t in $split.ReaderTeams) {
            if ($onReader -notcontains $t.teamid.ToString().ToLowerInvariant()) { Report 'FAIL' "(p2) default team '$($t.name)' is NOT on the reader profile - its users would read the stage EMPTY" }
        }
        if (($gaps | Where-Object { $_ -like '(p2)*' }).Count -eq 0) { Report 'OK' "(p2) the reader profile is on all $($split.ReaderTeams.Count) default teams that need it" }
    }

    $bffUsers = @()
    foreach ($appId in $BffApplicationIds) {
        $u = @((Invoke-Dv "systemusers?`$select=systemuserid,fullname,isdisabled&`$filter=applicationid eq $appId").value) | Select-Object -First 1
        if (-not $u) { Report 'FAIL' "(p3) no application user for appId $appId in this environment"; continue }
        Report 'INFO' "BFF application user '$($u.fullname)' appId $appId systemuserid $($u.systemuserid)"
        $bffUsers += $u
    }
    if ($writer) {
        $members = @($writer.systemuserprofiles_association)
        $memberIds = @($members | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
        $allowed = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
        $p3Before = $gaps.Count
        foreach ($u in $bffUsers) {
            if ($memberIds -notcontains $u.systemuserid.ToString().ToLowerInvariant()) { Report 'FAIL' "(p3) BFF application user '$($u.fullname)' is NOT on the writer profile" }
        }
        foreach ($m in $members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowed }) {
            Report 'FAIL' "(p3) writer profile member '$($m.fullname)' is not one of -BffApplicationIds - it could set the stage"
        }
        foreach ($tm in @($writer.teamprofiles_association)) { Report 'FAIL' "(p3) writer profile has TEAM '$($tm.name)' - every member could set the stage" }
        if ($gaps.Count -eq $p3Before) { Report 'OK' "(p3) the writer profile's members are exactly the $($bffUsers.Count) BFF application user(s), and no team" }
    }

    if (-not $DryRun -and -not $Verify -and $gaps.Count -gt 0) {
        Write-Host "`nREFUSED: a real run needs every precondition to pass ($($gaps.Count) gap(s)). Nothing was written." -ForegroundColor Red
        $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        exit 1
    }

    # ── Verify mode ────────────────────────────────────────────────────────────────────────────────────────────────────────
    function Get-Permission([string]$ProfileId) {
        @((Invoke-Dv "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $ProfileId and entityname eq '$Table' and attributelogicalname eq '$StageColumn'").value) | Select-Object -First 1
    }
    function Test-OtherWriters {
        $other = @((Invoke-Dv "fieldpermissions?`$select=canupdate,cancreate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$Table' and attributelogicalname eq '$StageColumn' and (canupdate eq 4 or cancreate eq 4)").value |
                Where-Object { $_.fieldsecurityprofileid.name -ne $WriterProfileName -and $_.fieldsecurityprofileid.fieldsecurityprofileid -ne $SystemAdminProfileId })
        foreach ($o in $other) { Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' can also write $Table.$StageColumn - only the BFF may" }
        if ($other.Count -eq 0) { Report 'OK' "no profile other than the writer profile (and the platform System Administrator profile) can create or update $StageColumn" }
    }
    function Test-StageSecurity {
        $a = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$StageColumn')?`$select=IsSecured"
        if (-not $a) { return }
        if ($a.IsSecured -eq $true) { Report 'OK' "$Table.$StageColumn is IsSecured" } else { Report 'MISSING' "$Table.$StageColumn is NOT IsSecured - any user with write access can change it" }
        foreach ($s in @(@{ P = $reader; N = $ReaderProfileName; W = 0 }, @{ P = $writer; N = $WriterProfileName; W = 4 })) {
            if (-not $s.P) { continue }
            $perm = Get-Permission $s.P.fieldsecurityprofileid
            if (-not $perm) { Report 'MISSING' "$($s.N) has no row on $StageColumn" }
            elseif ($perm.canread -eq 4 -and $perm.cancreate -eq $s.W -and $perm.canupdate -eq $s.W) { Report 'OK' "$($s.N) on $StageColumn (read=4 create=$($s.W) update=$($s.W))" }
            else { Report 'FAIL' "$($s.N) on $StageColumn is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($s.W) update=$($s.W)" }
        }
        Test-OtherWriters
    }

    if ($Verify) {
        Write-Host "`nColumns"
        foreach ($spec in $ColumnSpecs) {
            $g = @(Get-ColumnGaps $spec)
            if ($g.Count -eq 0) { Report 'OK' "$Table.$($spec.Column) is as specified" } else { $g | ForEach-Object { Report 'MISSING' $_ } }
        }
        $g = @(Get-LookupGaps)
        if ($g.Count -eq 0) { Report 'OK' "$Table.$LookupColumn is a lookup to $RunTable" } else { $g | ForEach-Object { Report 'MISSING' $_ } }
        Write-Host "`nColumn security"
        Test-StageSecurity
        if ($gaps.Count -eq 0) { Write-Host "`nPASS: the five intake columns exist as specified and $StageColumn is writable only by the BFF." -ForegroundColor Green; exit 0 }
        Write-Host "`nFAIL ($($gaps.Count)):" -ForegroundColor Red
        $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        exit 1
    }

    # ── Create the columns ─────────────────────────────────────────────────────────────────────────────────────────────────
    Write-Host "`nColumns"
    $createHeaders = @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName }
    foreach ($spec in $ColumnSpecs) {
        $c = $spec.Column
        if (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$c')?`$select=LogicalName") {
            Report 'OK' "unchanged: $Table.$c exists as specified"; $unchanged.Add($c); continue
        }
        if ($PSCmdlet.ShouldProcess("$Table.$c", "Create $($spec.Kind) column in solution $SolutionUniqueName")) {
            Invoke-Dv "EntityDefinitions(LogicalName='$Table')/Attributes" 'POST' $spec.Def $createHeaders | Out-Null
            Report 'DONE' "created $Table.$c ($($spec.Kind))"; $created.Add($c)
        }
        else { Report 'WOULD' "create $Table.$c ($($spec.Kind)) in solution $SolutionUniqueName"; $created.Add("$c (would)") }
    }

    if (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$LookupColumn')?`$select=LogicalName") {
        Report 'OK' "unchanged: $Table.$LookupColumn exists as specified"; $unchanged.Add($LookupColumn)
    }
    elseif (Get-DvOrNull "RelationshipDefinitions(SchemaName='$LookupRelationship')?`$select=SchemaName") {
        throw [EscalationException]::new("Relationship $LookupRelationship exists but $Table.$LookupColumn does not. Owner decision needed.")
    }
    else {
        $rel = @{
            '@odata.type'        = 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata'
            SchemaName           = $LookupRelationship
            ReferencedEntity     = $RunTable
            ReferencingEntity    = $Table
            CascadeConfiguration = @{ Assign = 'NoCascade'; Delete = 'RemoveLink'; Merge = 'NoCascade'; Reparent = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade' }
            Lookup               = @{
                '@odata.type' = 'Microsoft.Dynamics.CRM.LookupAttributeMetadata'
                SchemaName    = 'sprk_CurrentRun'
                DisplayName   = New-Label 'Current Run'
                Description   = New-Label 'The run (sprk_processingjob) currently driving this request. A re-drive or re-run moves it to the new run. Written only by the BFF. Deleting a run clears it; the request stays.'
                RequiredLevel = @{ Value = 'None' }
            }
        }
        if ($PSCmdlet.ShouldProcess("$Table.sprk_CurrentRun", "Create lookup to $RunTable (Delete=RemoveLink) in solution $SolutionUniqueName")) {
            Invoke-Dv 'RelationshipDefinitions' 'POST' $rel $createHeaders | Out-Null
            Report 'DONE' "created $Table.$LookupColumn (lookup -> $RunTable, Delete=RemoveLink)"; $created.Add($LookupColumn)
        }
        else { Report 'WOULD' "create $Table.$LookupColumn lookup -> $RunTable (Delete=RemoveLink) in solution $SolutionUniqueName"; $created.Add("$LookupColumn (would)") }
    }

    # ── Column security on stage ───────────────────────────────────────────────────────────────────────────────────────────
    Write-Host "`nColumn security ($StageColumn)"
    function Set-ColumnSecured([bool]$Value) {
        $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$StageColumn')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        $a = Get-DvOrNull $path 4
        if (-not $a) { throw "$Table.$StageColumn was not found." }
        $a.IsSecured = $Value
        try { Invoke-Dv $path 'PUT' $a @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
        catch {
            # A choice's definition carries its OptionSet as a navigation property the plain GET omits; retry ONCE with it expanded.
            Write-Host "    PUT refused without its OptionSet ($($_.Exception.Message)); retrying once with `$expand=OptionSet..." -ForegroundColor DarkYellow
            $a = Get-DvOrNull "${path}?`$expand=OptionSet" 2
            $a.IsSecured = $Value
            Invoke-Dv $path 'PUT' $a @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        }
    }
    function Grant-Permission([string]$ProfileId, [int]$Write) {
        for ($attempt = 1; ; $attempt++) {
            try {
                Invoke-Dv 'fieldpermissions' 'POST' @{
                    entityname = $Table; attributelogicalname = $StageColumn
                    canread = 4; cancreate = $Write; canupdate = $Write
                    'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($ProfileId)"
                } | Out-Null
                return
            }
            catch {
                # Securing propagates asynchronously: a grant right after it may be refused 0x8004f508.
                if ("$($_.Exception.Message)" -notmatch '0x8004f508' -or $attempt -ge 12) { throw }
                Write-Host "    $StageColumn not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
                Start-Sleep -Seconds 5
            }
        }
    }

    $stage = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$StageColumn')?`$select=IsSecured,CanBeSecuredForRead,CanBeSecuredForCreate,CanBeSecuredForUpdate"
    $grants = @(@{ P = $writer; N = $WriterProfileName; W = 4 }, @{ P = $reader; N = $ReaderProfileName; W = 0 })
    if (-not $stage) {
        # Dry run, column not yet created.
        Report 'WOULD' "secure $Table.$StageColumn after it is created, then grant: $WriterProfileName (read=4 create=4 update=4), $ReaderProfileName (read=4 create=0 update=0)"
    }
    else {
        if (-not ($stage.CanBeSecuredForRead -and $stage.CanBeSecuredForCreate -and $stage.CanBeSecuredForUpdate)) {
            throw [EscalationException]::new("$Table.$StageColumn cannot be secured for read, create and update. FR-05b would fail; owner decision needed.")
        }
        $missing = @($grants | Where-Object { -not (Get-Permission $_.P.fieldsecurityprofileid) })
        if ($stage.IsSecured -eq $true -and $missing.Count -eq 0) {
            Report 'OK' "unchanged: $Table.$StageColumn is secured and both profiles hold their rows"; $unchanged.Add("$StageColumn security")
        }
        elseif ($DryRun) {
            if ($stage.IsSecured -ne $true) { Report 'WOULD' "secure $Table.$StageColumn" }
            foreach ($g in $missing) { Report 'WOULD' "grant $($g.N) on $StageColumn (read=4 create=$($g.W) update=$($g.W))" }
        }
        else {
            $clock = [System.Diagnostics.Stopwatch]::StartNew()
            $needSecure = $stage.IsSecured -ne $true
            if ($needSecure) { Set-ColumnSecured $true; Report 'DONE' "secured $Table.$StageColumn"; $created.Add("$StageColumn security") }
            try {
                foreach ($g in $missing) {
                    Grant-Permission $g.P.fieldsecurityprofileid $g.W
                    Report 'DONE' "granted $($g.N) on $StageColumn (read=4 create=$($g.W) update=$($g.W))"
                }
            }
            catch {
                $grantError = $_
                Write-Host "  FAIL     granting the profiles failed after securing: $($grantError.Exception.Message)" -ForegroundColor Red
                if ($needSecure) {
                    Write-Host "           reverting $StageColumn to NOT field-secured so no reader is left masked..." -ForegroundColor Yellow
                    try {
                        Set-ColumnSecured $false
                        Invoke-Dv 'PublishXml' 'POST' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
                        $back = (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$StageColumn')?`$select=IsSecured").IsSecured
                        if ($back -eq $true) { throw 'the revert was accepted but the column still reads IsSecured=true' }
                        Write-Host "  REVERTED $StageColumn is NOT field-secured again. Fix the cause, then re-run." -ForegroundColor Yellow
                    }
                    catch {
                        Write-Host "  !!! RECOVERY REQUIRED NOW - $StageColumn may be secured WITHOUT its grants. Cause: $($grantError.Exception.Message); revert failed: $($_.Exception.Message)" -ForegroundColor Red
                        Write-Host "      Re-run this script (it grants only what is missing) or clear 'Enable column security' on the column in make.powerapps.com." -ForegroundColor Red
                    }
                }
                throw $grantError
            }
            Write-Host ('  masked window: {0:N1}s (secure -> grants)' -f $clock.Elapsed.TotalSeconds)
        }
        Test-OtherWriters
    }

    # ── Publish and read back ──────────────────────────────────────────────────────────────────────────────────────────────
    Write-Host "`nPublish"
    if ($PSCmdlet.ShouldProcess("$Table, $RunTable", 'Publish customizations (entity-scoped)')) {
        # Also on a re-run that wrote nothing: an earlier run's publish may not have finished. PublishXml is idempotent.
        Invoke-Dv 'PublishXml' 'POST' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity><entity>$RunTable</entity></entities></importexportxml>" } | Out-Null
        Report 'DONE' "published $Table, $RunTable"
        Write-Host "`nRead-back"
        foreach ($spec in $ColumnSpecs) {
            $g = @(Get-ColumnGaps $spec)
            if ($g.Count -eq 0) { Report 'OK' "$Table.$($spec.Column) is as specified" } else { $g | ForEach-Object { Report 'FAIL' $_ } }
        }
        $g = @(Get-LookupGaps)
        if ($g.Count -eq 0) { Report 'OK' "$Table.$LookupColumn is a lookup to $RunTable" } else { $g | ForEach-Object { Report 'FAIL' $_ } }
        Test-StageSecurity
    }
    else { Report 'WOULD' 'publish (entity-scoped)' }
}
catch [EscalationException] {
    Write-Host "`nESCALATION - STOPPED, nothing further was changed: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
catch {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'A column, if created, stays (the script is re-runnable).' -ForegroundColor Red
    exit 1
}

Write-Host "`n================ Summary ================" -ForegroundColor Cyan
Write-Host ('created   : {0}  {1}' -f $created.Count, ($created -join ', '))
Write-Host ('unchanged : {0}  {1}' -f $unchanged.Count, ($unchanged -join ', '))
Write-Host ('gaps      : {0}' -f $gaps.Count)
if ($DryRun) { Write-Host 'WhatIf run: nothing was written.' -ForegroundColor Cyan }
if ($gaps.Count -gt 0 -and -not $DryRun) { $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }; exit 1 }
exit 0
