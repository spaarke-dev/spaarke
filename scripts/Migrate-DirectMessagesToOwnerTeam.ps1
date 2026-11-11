#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Existing Direct 1:1 messages: stamps their recipient (-Phase Stamp), and later moves them and their content rows to the
    Direct message owner team (-Phase Move) (field-mapping-server-write-path-r1 task 054c). Dry run by default; -Apply
    writes row by row; -Verify checks. Idempotent: a second -Apply selects nothing.

.DESCRIPTION
    A Direct message is a Message row (sprk_communicationtype = Message) whose thread (sprk_communicationthread) is typed
    Direct (sprk_threadtype), is not the per-user master thread (sprk_isdefaultthread), whose thread carries no typed
    regarding lookup (sprk_regarding*), and which carries none itself. A Direct message carries its own two participants:
    sprk_sentby and sprk_directrecipient.

    PRECONDITION (both phases, every mode; checked by this script, not only by the deploy order)
      Task 054b's field security is live on this environment: the five columns sprk_communicationthread.sprk_threadtype,
      sprk_communicationthread.sprk_isdefaultthread, sprk_communication.sprk_communicationthread, sprk_communication.sprk_sentby
      and sprk_communication.sprk_directrecipient are field-secured; the reader profile reads each and is on every business
      unit's default team; the writer profile reads and updates each and its members are exactly the -BffApplicationIds
      application users (no human, no team); no other profile but System Administrator can create or update any of them.
      Otherwise the script REFUSES and selects nothing (exit 1): the selection reads those columns, and a column a user
      can write is not a rule.

    -Phase Stamp   (runs before the Direct team exists; reads no Direct-team setting)
      Selects Direct messages with an empty sprk_directrecipient. A row is stamped ONLY when its thread yields exactly two
      distinct systemusers, one of them the row's sprk_sentby, both enabled and neither an application user. The thread
      yields its systemuser owner plus every systemuser holding a share on it whose mask has Read (bit 1): a team owner or a
      team share is never a participant, a share without the Read bit is not one. The BFF's recipient stamp reads the same
      rule (DirectMessageRecipient.ReadParticipantsAsync). The recipient is the other systemuser. The write is
      sprk_DirectRecipient@odata.bind, then a read-back.
      A thread whose owner is a team has no owner participant, whatever its user-owner column reads. A thread with any
      typed regarding lookup set (the thread's own columns, the set the BFF's ThreadResolver.ThreadRegardingColumns reads) is
      not a Direct thread.
      Every other selected row goes to the manual-review list (-ReviewPath) with a reason and is NOT touched:
        no_sentby, thread_default_flag_unread, thread_has_regarding, not_two_participants, sender_not_a_participant,
        participant_disabled, participant_application_user, participant_missing, read_fault, thread_shares_changed_after_create
        (a current share row on the thread whose changedon is later than the thread's createdon plus
        -ShareChangeToleranceSeconds; the audit log is not read), changed_since_selection (-Apply only).

    -Phase Move    (runs after the Direct team is set up and the owner-move code ships)
      Resolves the Direct team from -DirectMessageBusinessUnitName / -DirectMessageOwnerTeamName with the BFF lookup's
      checks (DirectMessageOwnerTeam): exactly one unit by that name, not the root, a direct child of the root, not the Secure
      Record unit (by id), exactly one named non-default Owner team there that is not the Secure Record team (by id) and not
      the unit's default team, the team has no members, the unit has no users. Any failure refuses (exit 1).
      Then, before anything is selected, the Assign cascade of sprk_communication and of each content table below is read from
      the relationship metadata: an Assign cascade to a child table that -AcceptedAssignCascade does not list refuses
      (exit 1), and every -GateRecordWriterApplicationIds id must have an application user here.
      Selects Direct messages with a VALID STAMPED PAIR (sprk_sentby and sprk_directrecipient: two distinct, non-empty
      systemusers). A row without one stays with its current owner and is listed (no_valid_pair). Then, per message, its
      content rows: sprk_communicationattachment, sprk_communicationparticipant and sprk_emailreviewlog (by their
      sprk_communication lookup), sprk_document (by sprk_relatedcommunication) and the gate sprk_decisionrecord (by
      sprk_regardingcommunication, unconfirmed, created by a -GateRecordWriterApplicationIds user: the same set
      GateDecisionRecordSharing.FindGateRecordsAsync re-owns; a confirmed record carries a person's review and stays with its
      owner). Rows owned by the Direct team or the Secure Record team are not moved. Content rows move before their message,
      so a resumed run still finds them.
      Per row (-Apply): re-read organization.sharetopreviousowneronassign (stop when true), count the row's shares, assign
      it to the Direct team, read the owner back (stop when it did not land), count again (stop when the count fell; a rise
      is reported). Before a message's rows move, the message and its thread are read again; a pair no longer valid, a
      message now owned by the Secure Record team, or a regarding lookup now set on the message or its thread moves nothing
      of it (it is listed). If a write throws, the review list gathered so far is written before the error is rethrown.
      PARTICIPANTS KEEP READ (per message, after that re-read, before any of its rows is assigned): the participants are
      sprk_sentby and sprk_directrecipient; a disabled (or missing) participant is skipped, as the BFF's Direct share pass
      does. The message's share rows are read strictly. Each enabled participant must hold a systemuser share with the Read
      bit (mask & 1). The one exception: the message's current owning user (no owning team) with no share is granted Read
      (GrantAccess, ReadAccess) while the message is still user-owned, and the grant is confirmed by reading the share rows
      back; that keeps access the owner already has and widens nobody's. Anyone else without Read is never granted here (a
      new grant would bypass the BFF's Restricted bar, RestrictedFilingShareBar.EligibleForGrantAsync), and a share without
      the Read bit is never upgraded (as DirectMessageParticipantShares.GrantContentRowAndConfirmAsync). Otherwise nothing of
      that message moves, and it is listed.
      Move review reasons: thread_has_regarding, thread_default_flag_unread, no_valid_pair, changed_since_selection,
      participants_not_readable (no enabled participant, or one without Read who is not the owning user),
      participant_share_without_read, participant_grant_unconfirmed (the grant threw or its read-back lacks Read),
      read_fault (participant or share read), participant_without_read (-Verify only).

    MODES
      -DryRun   (default) every check, the selection, the plan (Move: the owner Read grants it would make) and the review
                list; writes nothing to Dataverse.
      -Apply    writes the plan, one row at a time, verified by read-back; stops at the first failed check. -Phase Move
                -Apply also needs -DirectShareJobLive.
      -Verify   read-only gate. Stamp: exit 1 while a row would stamp. Move: exit 1 while a valid-pair message, or a content
                row of one, is not owned by the Direct team (Secure-owned rows excepted), or while a Direct-team-owned
                valid-pair message has an enabled participant without a Read share (participant_without_read) or its
                participant or share read faults. Otherwise exit 0.
      Every mode prints the review list with a count per reason ("review: <reason> <n>, ...").

    Exit codes: 0 done / pass; 1 refused, stopped or a Verify failure.

    AUTH
      The operator's own az CLI identity, a token for the explicit -EnvironmentUrl. Never printed. No secrets. The operator
      is a System Administrator, which field security does not restrict (a stated platform limit): the stamp is written by
      the operator, under the same rule the BFF applies.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER Phase
    Stamp or Move.

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users: the only allowed members of the writer profile.
    A comma-separated string is accepted; at least one id is required.

.PARAMETER GateRecordWriterApplicationIds
    Application (client) ids of the identities that create gate Decision Records (the ontology writer's managed identity;
    OntologyWriterDataverseClient). Mandatory for -Phase Move, used only to recognise gate-record creators; it never feeds
    the writer-profile membership check. A comma-separated string is accepted. Ignored by -Phase Stamp.

.PARAMETER AcceptedAssignCascade
    Child tables (logical names) whose Assign cascade from sprk_communication or a content table is accepted (the owner's
    answer). Default none: any Assign cascade refuses -Phase Move.

.PARAMETER ReviewPath
    Where the manual-review list is written (.json for JSON, anything else for CSV). Written in every mode; a local file.

.PARAMETER DryRun
    The default mode, stated explicitly.

.PARAMETER Apply
    Write mode.

.PARAMETER Verify
    Read-only gate with a pass/fail exit code.

.PARAMETER DirectMessageBusinessUnitName
    The Direct message business unit (the BFF setting DirectMessage:BusinessUnitName). Required for -Phase Move; never read
    by -Phase Stamp. No default.

.PARAMETER DirectMessageOwnerTeamName
    The Direct message owner team (the BFF setting DirectMessage:OwnerTeamName). Required for -Phase Move. No default.

.PARAMETER DirectShareJobLive
    The operator's acknowledgement, required by -Phase Move -Apply: without it the run refuses (exit 1) before anything is
    selected or written. -DryRun and -Verify do not need it; -Phase Stamp ignores it. Passing it asserts that the BFF build
    with task 198's Direct share pass (SecureChildShareReconciliationJob) is deployed on this environment, and that its
    settings DirectMessage:BusinessUnitName / DirectMessage:OwnerTeamName equal -DirectMessageBusinessUnitName /
    -DirectMessageOwnerTeamName. The script cannot read the BFF's deployment or settings; the operator asserts it.
    It cannot be passed honestly on an environment until those two settings are set there: that is an owner go in the
    unified-access-control-r2 session, which comes only after Set-ThreadFlagFieldSecurity.ps1 -Verify exits 0 there.
    The script keeps each participant's Read on the message itself; the moved content rows still rely on that job to
    grant the participants Read.

.PARAMETER SecureRecordBusinessUnitName
    The Secure Record unit (SecureRecord:BusinessUnitName). Default 'Secure Record'. -Phase Move only.

.PARAMETER SecureRecordOwnerTeamName
    The Secure Record owner team (SecureRecord:OwnerTeamName). Default 'Secure Record Owners'. -Phase Move only.

.PARAMETER NamePrefix
    Limits the selection to messages whose sprk_name starts with this text (test rows use 'FMSWP-').

.PARAMETER ShareChangeToleranceSeconds
    -Phase Stamp: a thread share row changed later than this after the thread's creation sends the thread's messages to
    manual review. Default 300.

.EXAMPLE
    .\Migrate-DirectMessagesToOwnerTeam.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Phase Stamp `
        -BffApplicationIds <bff-app-id>,<bff-mi-app-id> -ReviewPath .\stamp-review.json
    Dry run of the stamp: preconditions, selection, plan, review list; no Dataverse write.

.EXAMPLE
    .\Migrate-DirectMessagesToOwnerTeam.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Phase Move `
        -BffApplicationIds <bff-app-id> -GateRecordWriterApplicationIds <ontology-writer-app-id> -ReviewPath .\move-review.csv `
        -DirectMessageBusinessUnitName '<unit>' -DirectMessageOwnerTeamName '<team>' -Apply -DirectShareJobLive

.NOTES
    field-mapping-server-write-path-r1 task 054 (projects/field-mapping-server-write-path-r1/tasks/054-direct-message-owner-team.poml),
    notes: projects/field-mapping-server-write-path-r1/notes/task-054-notes.md "054c". Models: scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1
    (assign, read-back, share count), scripts/Set-SecureFlagFieldSecurity.ps1 (the profile checks).
    Tests: tests/scripts/Migrate-DirectMessagesToOwnerTeam.Tests.ps1. Each live run needs the owner's go.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [Parameter(Mandatory)][ValidateSet('Stamp', 'Move')][string]$Phase,
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [Parameter(Mandatory)][string]$ReviewPath,
    [string[]]$GateRecordWriterApplicationIds = @(),
    [string[]]$AcceptedAssignCascade = @(),
    [switch]$DryRun,
    [switch]$Apply,
    [switch]$Verify,
    [switch]$DirectShareJobLive,
    [string]$DirectMessageBusinessUnitName,
    [string]$DirectMessageOwnerTeamName,
    [string]$SecureRecordBusinessUnitName = 'Secure Record',
    [string]$SecureRecordOwnerTeamName = 'Secure Record Owners',
    [string]$NamePrefix,
    [int]$ShareChangeToleranceSeconds = 300
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if (@($DryRun, $Apply, $Verify | Where-Object { $_.IsPresent }).Count -gt 1) { throw '-DryRun, -Apply and -Verify are separate modes; pass one.' }
$Mode = if ($Apply) { 'Apply' } elseif ($Verify) { 'Verify' } else { 'DryRun' }
# `pwsh -File` passes "a,b" as ONE string; accept both shapes.
function Split-List([string[]]$Values) { @($Values | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
$BffApplicationIds = Split-List $BffApplicationIds
if ($BffApplicationIds.Count -eq 0) { throw '-BffApplicationIds names no application id (an empty list would leave the writer profile with no allowed member).' }
foreach ($id in $BffApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-BffApplicationIds: '$id' is not a GUID." } }
# Normalise to the plain dashed lower-case form: TryParse also accepts {..}, (..), no-dash and 0x forms, and the id goes
# unquoted into an OData filter.
$BffApplicationIds = @(foreach ($id in $BffApplicationIds) { [Guid]::Parse($id).ToString('D') })
$GateRecordWriterApplicationIds = Split-List $GateRecordWriterApplicationIds
if ($Phase -eq 'Move' -and $GateRecordWriterApplicationIds.Count -eq 0) {
    throw '-Phase Move needs -GateRecordWriterApplicationIds (the application id of the identity that creates gate Decision Records). It has no default and is not the BFF id list.'
}
foreach ($id in $GateRecordWriterApplicationIds) { if (-not [Guid]::TryParse($id, [ref][Guid]::Empty)) { throw "-GateRecordWriterApplicationIds: '$id' is not a GUID." } }
$GateRecordWriterApplicationIds = @(foreach ($id in $GateRecordWriterApplicationIds) { [Guid]::Parse($id).ToString('D') })   # foreach, not a pipe: an empty list arrives as $null and a pipe would run once
$AcceptedAssignCascade = Split-List $AcceptedAssignCascade
if ($Phase -eq 'Move' -and ([string]::IsNullOrWhiteSpace($DirectMessageBusinessUnitName) -or [string]::IsNullOrWhiteSpace($DirectMessageOwnerTeamName))) {
    throw '-Phase Move needs -DirectMessageBusinessUnitName and -DirectMessageOwnerTeamName (the BFF settings DirectMessage:BusinessUnitName / DirectMessage:OwnerTeamName). They have no default.'
}
# The Move -Apply acknowledgement, before any read or write (see .PARAMETER DirectShareJobLive).
if ($Phase -eq 'Move' -and $Mode -eq 'Apply' -and -not $DirectShareJobLive) {
    Write-Host ("REFUSED: -Phase Move -Apply needs -DirectShareJobLive: the operator's assertion that the BFF build with task 198's Direct share pass " +
        "is deployed on this environment with DirectMessage:BusinessUnitName / DirectMessage:OwnerTeamName equal to -DirectMessageBusinessUnitName / " +
        "-DirectMessageOwnerTeamName. Nothing was selected and nothing was written.") -ForegroundColor Red
    exit 1
}
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants ───────────────────────────────────────────────────────────────────────────────────────────────
$MessageType = 100000004          # sprk_communicationtype Message (CommunicationType.Message)
$ThreadTypeDirect = 100000001     # sprk_threadtype Direct
$ReadBit = 1                      # AccessRights.ReadAccess in accessrightsmask
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
# Task 054b's five columns (field-secured before this script selects anything).
$SecuredColumns = @(
    [pscustomobject]@{ Table = 'sprk_communicationthread'; Column = 'sprk_threadtype' }
    [pscustomobject]@{ Table = 'sprk_communicationthread'; Column = 'sprk_isdefaultthread' }
    [pscustomobject]@{ Table = 'sprk_communication'; Column = 'sprk_communicationthread' }
    [pscustomobject]@{ Table = 'sprk_communication'; Column = 'sprk_sentby' }
    [pscustomobject]@{ Table = 'sprk_communication'; Column = 'sprk_directrecipient' }
)
# The message's typed regarding lookups (RegardingFieldMap.AllRegardingFields). Any one set = filed under a record.
$RegardingColumns = @('sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardinginvoice', 'sprk_regardingservicerequest',
    'sprk_regardingworkassignment', 'sprk_regardingevent', 'sprk_regardingbudget', 'sprk_regardingreportcard',
    'sprk_regardinganalysis', 'sprk_regardingorganization', 'sprk_regardingaccount', 'sprk_regardingperson')
# The THREAD's own typed regarding lookups: ThreadResolver.ThreadRegardingColumns = RegardingFieldMap.All minus
# sprk_regardingreportcard (the thread table has no such column, and selecting it faults the whole read). A Direct-typed
# thread with any one of these set is filed under a record: not a Direct thread.
$ThreadRegardingColumns = @($RegardingColumns | Where-Object { $_ -ne 'sprk_regardingreportcard' })
# The built-in "System Administrator" field security profile, matched by its fixed id (a profile of that NAME is not it).
$SystemAdministratorProfileId = '572329c1-a042-4e22-be47-367c6374ea45'
# The content rows that follow a Direct message (RestrictedFilingShareBar.ContentRows + the gate Decision Record), equal to
# the Direct role's tables (config/direct-message-owner-role.json).
$ContentTables = @(
    [pscustomobject]@{ Table = 'sprk_communicationattachment'; Set = 'sprk_communicationattachments'; Id = 'sprk_communicationattachmentid'; Lookup = 'sprk_communication'; Gate = $false }
    [pscustomobject]@{ Table = 'sprk_communicationparticipant'; Set = 'sprk_communicationparticipants'; Id = 'sprk_communicationparticipantid'; Lookup = 'sprk_communication'; Gate = $false }
    [pscustomobject]@{ Table = 'sprk_emailreviewlog'; Set = 'sprk_emailreviewlogs'; Id = 'sprk_emailreviewlogid'; Lookup = 'sprk_communication'; Gate = $false }
    [pscustomobject]@{ Table = 'sprk_document'; Set = 'sprk_documents'; Id = 'sprk_documentid'; Lookup = 'sprk_relatedcommunication'; Gate = $false }
    [pscustomobject]@{ Table = 'sprk_decisionrecord'; Set = 'sprk_decisionrecords'; Id = 'sprk_decisionrecordid'; Lookup = 'sprk_regardingcommunication'; Gate = $true }
)

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
# Every page of a GET. Callers wrap the call in @(...) so Count means rows.
function Get-All([string]$Path) {
    $rows = New-Object System.Collections.Generic.List[object]
    $next = "$Api/$Path"
    while ($next) {
        $page = Invoke-RestMethod -Uri $next -Headers $headers -Method Get
        foreach ($item in @($page.value)) { if ($null -ne $item) { $rows.Add($item) } }
        $next = $page.'@odata.nextLink'
    }
    return $rows.ToArray()
}
# PATCH an existing row only (If-Match: * refuses to create one).
function Invoke-DvPatch([string]$Path, [hashtable]$Body) {
    $h = $headers.Clone(); $h['If-Match'] = '*'
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method Patch -Body ([Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Compress))) | Out-Null
}
# POST a Web API action (Move: GrantAccess only).
function Invoke-DvPost([string]$Path, [hashtable]$Body) {
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Post -Body ([Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Compress -Depth 6))) | Out-Null
}
# An OData string literal inside a URL: quotes doubled (OData), then percent-encoded so a name with &, #, + or a space cannot
# end the query value or change its meaning.
function Esc([string]$s) { [Uri]::EscapeDataString($s.Replace("'", "''")) }
function Lc($g) { if ($null -eq $g -or "$g" -eq '') { $null } else { "$g".ToLowerInvariant() } }# The ontology writer's business unit keeps its default team OFF every field-security profile (scripts/common/OntologyWriterUnit.ps1).
. (Join-Path $PSScriptRoot 'common/OntologyWriterUnit.ps1')

function NonEmpty($g) { $v = Lc $g; if ($v -and $v -ne '00000000-0000-0000-0000-000000000000') { $v } else { $null } }
# True when any of the named typed regarding lookups (columns) is set on the row.
function HasRegarding($Row, [string[]]$Columns) { [bool]($Columns | Where-Object { NonEmpty $Row."_$($_)_value" }) }
# The write-time re-read (Stamp and Move), so a row is written only if what selected it still holds.
$MessageRecheckSelect = (@('sprk_communicationid', 'sprk_name', '_sprk_communicationthread_value', '_sprk_sentby_value', '_sprk_directrecipient_value',
        '_owningteam_value', '_owninguser_value') + @($RegardingColumns | ForEach-Object { "_$($_)_value" })) -join ','
$ThreadRecheckSelect = (@('sprk_communicationthreadid', 'sprk_threadtype', 'sprk_isdefaultthread') +
    @($ThreadRegardingColumns | ForEach-Object { "_$($_)_value" })) -join ','
# The thread is still Direct-typed, still not the per-user master thread, and still carries no typed regarding lookup.
# Returns why it no longer qualifies, or $null.
function Get-ThreadChange([string]$ThreadId) {
    $t = Invoke-DvGet "sprk_communicationthreads($ThreadId)?`$select=$ThreadRecheckSelect"
    if ($t.sprk_threadtype -ne $ThreadTypeDirect) { return 'its thread is no longer Direct-typed' }
    if ($t.sprk_isdefaultthread -ne $false) { return 'its thread is the per-user master thread, or the flag is unread' }
    if (HasRegarding $t $ThreadRegardingColumns) { return 'its thread now carries a typed regarding lookup' }
    $null
}

$gaps = New-Object System.Collections.Generic.List[string]
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'INFO' { 'Gray' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -eq 'FAIL') { $gaps.Add($What) }
}
function Stop-Run([string]$Message) { Write-Host "STOP: $Message" -ForegroundColor Red; exit 1 }

$org = (Invoke-DvGet 'organizations?$select=name,sharetopreviousowneronassign').value[0]
Write-Host "Environment : $EnvironmentUrl (org '$($org.name)')"
Write-Host "Phase       : $Phase"
Write-Host ("Mode        : {0}" -f $(switch ($Mode) { 'Apply' { 'APPLY' } 'Verify' { 'VERIFY (read-only)' } default { 'DRY RUN (no Dataverse writes)' } }))
if ($NamePrefix) { Write-Host "Selection   : sprk_name starts with '$NamePrefix'" }

# ── PRECONDITION: task 054b's field security ────────────────────────────────────────────────────────────────
Write-Host "`nPrecondition: task 054b field security on the five columns"
foreach ($c in $SecuredColumns) {
    $attr = Invoke-DvGet "EntityDefinitions(LogicalName='$($c.Table)')/Attributes(LogicalName='$($c.Column)')?`$select=IsSecured"
    if ($attr.IsSecured -eq $true) { Report 'OK' "$($c.Table).$($c.Column) is field-secured" }
    else { Report 'FAIL' "$($c.Table).$($c.Column) is NOT field-secured (task 054b first)" }
}

function Get-Profile([string]$Name) {
    @(Get-All ("fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$(Esc $Name)'" +
            '&$expand=teamprofiles_association($select=teamid,name),systemuserprofiles_association($select=systemuserid,fullname,applicationid)'))
}
$readers = Get-Profile $ReaderProfileName
$writers = Get-Profile $WriterProfileName
$reader = if ($readers.Count -eq 1) { $readers[0] } else { Report 'FAIL' "$($readers.Count) profile(s) named '$ReaderProfileName'; exactly one is required"; $null }
$writer = if ($writers.Count -eq 1) { $writers[0] } else { Report 'FAIL' "$($writers.Count) profile(s) named '$WriterProfileName'; exactly one is required"; $null }

$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @(Get-All "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $appId") | Select-Object -First 1
    if ($u) { $bffUsers += $u } else { Report 'FAIL' "no application user for appId $appId in this environment" }
}
$bffUserIds = @($bffUsers | ForEach-Object { Lc $_.systemuserid })

if ($writer) {
    $members = @($writer.systemuserprofiles_association)
    $memberIds = @($members | ForEach-Object { Lc $_.systemuserid })
    foreach ($u in $bffUsers) {
        if ($memberIds -contains (Lc $u.systemuserid)) { Report 'OK' "BFF application user '$($u.fullname)' is on the writer profile" }
        else { Report 'FAIL' "BFF application user '$($u.fullname)' is NOT on the writer profile" }
    }
    foreach ($m in $members | Where-Object { $bffUserIds -notcontains (Lc $_.systemuserid) }) {
        $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        Report 'FAIL' "writer profile member '$($m.fullname)' is $kind; only the BFF identities may write these columns"
    }
    foreach ($tm in @($writer.teamprofiles_association)) { Report 'FAIL' "writer profile holds TEAM '$($tm.name)'; only the BFF identities may write these columns" }
}
if ($reader) {
    $onReader = @($reader.teamprofiles_association | ForEach-Object { Lc $_.teamid })
    $allDefaultTeams = @(Get-All 'teams?$select=teamid,name,_businessunitid_value&$filter=isdefault eq true')
    if ($allDefaultTeams.Count -eq 0) { Report 'FAIL' 'no business-unit default team was read' }
    # Every default team but the ontology writer unit's belongs on the reader profile; that one must be in NO field-security
    # profile (H7b S24), so finding it there is a FAIL and it is never required.
    $defaultTeamSplit = Split-DvDefaultTeamsByOntologyWriterUnit -Api $Api -Headers $headers -DefaultTeams $allDefaultTeams
    $defaultTeams = $defaultTeamSplit.ReaderTeams
    foreach ($t in $defaultTeamSplit.WriterUnitTeams | Where-Object { $onReader -contains (Lc $_.teamid) }) {
        Report 'FAIL' "default team '$($t.name)' of the '$OntologyWriterBusinessUnitName' business unit is on the reader profile; the ontology writer's unit team must be in NO field-security profile (H7b S24 refuses it). Remove it by hand"
    }
    foreach ($t in $defaultTeams | Where-Object { $onReader -notcontains (Lc $_.teamid) }) {
        Report 'FAIL' "default team '$($t.name)' is NOT on the reader profile; its users read the columns empty"
    }
}
foreach ($c in $SecuredColumns) {
    $perms = @(Get-All ("fieldpermissions?`$select=canread,cancreate,canupdate,_fieldsecurityprofileid_value" +
            "&`$filter=entityname eq '$($c.Table)' and attributelogicalname eq '$($c.Column)'" +
            '&$expand=fieldsecurityprofileid($select=name)'))
    $w = @($perms | Where-Object { $writer -and (Lc $_._fieldsecurityprofileid_value) -eq (Lc $writer.fieldsecurityprofileid) }) | Select-Object -First 1
    $r = @($perms | Where-Object { $reader -and (Lc $_._fieldsecurityprofileid_value) -eq (Lc $reader.fieldsecurityprofileid) }) | Select-Object -First 1
    if (-not ($w -and $w.canread -eq 4 -and $w.canupdate -eq 4)) { Report 'FAIL' "writer profile does not read and update $($c.Table).$($c.Column)" }
    if (-not ($r -and $r.canread -eq 4)) { Report 'FAIL' "reader profile does not read $($c.Table).$($c.Column)" }
    # The writer profile and the built-in System Administrator profile are exempt, each by id, never by name.
    $exemptIds = @($SystemAdministratorProfileId) + @($(if ($writer) { Lc $writer.fieldsecurityprofileid }))
    foreach ($o in $perms | Where-Object { ($_.cancreate -eq 4 -or $_.canupdate -eq 4) -and (Lc $_._fieldsecurityprofileid_value) -notin $exemptIds }) {
        Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' can also write $($c.Table).$($c.Column); only the BFF may"
    }
}
if ($gaps.Count -gt 0) {
    Write-Host "`nREFUSED: task 054b's field security is not in place on this environment ($($gaps.Count) gap(s)). Nothing was selected and nothing was written." -ForegroundColor Red
    exit 1
}
Report 'OK' 'the five columns are field-secured and only the BFF identities can write them'

# ── Shared selection: the Direct threads and the candidate messages (run by each phase after its own gates) ─
$threads = @{}
function Select-DirectMessages {
    $threadSelect = (@('sprk_communicationthreadid', 'sprk_isdefaultthread', '_owninguser_value', '_owningteam_value', 'createdon') +
        @($ThreadRegardingColumns | ForEach-Object { "_$($_)_value" })) -join ','
    foreach ($t in @(Get-All "sprk_communicationthreads?`$select=$threadSelect&`$filter=sprk_threadtype eq $ThreadTypeDirect")) {
        $threads[(Lc $t.sprk_communicationthreadid)] = $t
    }
    $messageSelect = (@('sprk_communicationid', 'sprk_name', '_sprk_communicationthread_value', '_sprk_sentby_value', '_sprk_directrecipient_value',
            '_owninguser_value', '_owningteam_value') + @($RegardingColumns | ForEach-Object { "_$($_)_value" })) -join ','
    $messageFilter = "sprk_communicationtype eq $MessageType and _sprk_communicationthread_value ne null"
    if ($Phase -eq 'Stamp') { $messageFilter += ' and _sprk_directrecipient_value eq null' }
    if ($NamePrefix) { $messageFilter += " and startswith(sprk_name,'$(Esc $NamePrefix)')" }
    $messages = @(Get-All "sprk_communications?`$select=$messageSelect&`$filter=$messageFilter")

    # A Direct message: its thread is a Direct thread, and it carries no typed regarding lookup. (A thread that carries
    # one is judged per phase, after this selection: it goes to review.)
    return @($messages | Where-Object {
            $m = $_
            $threads.ContainsKey((Lc $m._sprk_communicationthread_value)) -and
            -not (HasRegarding $m $RegardingColumns)
        })
}

# Each participant's standing (Stamp: the pair's eligibility; Move: who is enabled). A read fault propagates to the caller.
function Get-Eligibility([string[]]$Ids) {
    $rows = @(Get-All ("systemusers?`$select=systemuserid,isdisabled,applicationid&`$filter=" + (($Ids | ForEach-Object { "systemuserid eq $_" }) -join ' or ')))
    $result = @{}
    foreach ($id in $Ids) {
        $row = $rows | Where-Object { (Lc $_.systemuserid) -eq $id } | Select-Object -First 1
        $result[$id] = if (-not $row) { 'participant_missing' }
        elseif (NonEmpty $row.applicationid) { 'participant_application_user' }
        elseif ($row.isdisabled -eq $false) { 'eligible' }
        else { 'participant_disabled' }
    }
    return $result
}

$review = New-Object System.Collections.Generic.List[object]
function Add-Review($Message, [string]$Reason, [string]$Detail = '') {
    $review.Add([pscustomobject]@{
            Phase = $Phase; MessageId = (Lc $Message.sprk_communicationid); Name = $Message.sprk_name
            ThreadId = (Lc $Message._sprk_communicationthread_value); SentBy = (Lc $Message._sprk_sentby_value)
            Reason = $Reason; Detail = $Detail
        })
}
function Write-Review {
    $dir = Split-Path -Parent $ReviewPath
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    if ($ReviewPath -like '*.json') { ConvertTo-Json -InputObject @($review.ToArray()) -Depth 4 | Set-Content -LiteralPath $ReviewPath -Encoding UTF8 }
    elseif ($review.Count -eq 0) { 'Phase,MessageId,Name,ThreadId,SentBy,Reason,Detail' | Set-Content -LiteralPath $ReviewPath -Encoding UTF8 }
    else { @($review.ToArray()) | Export-Csv -LiteralPath $ReviewPath -NoTypeInformation -Encoding UTF8 }
    Write-Host "Manual-review list ($($review.Count) row(s)) written: $ReviewPath"
    # A count per reason: how many rows stay behind, and why.
    $counts = @($review.ToArray() | Group-Object Reason | Sort-Object Name | ForEach-Object { "$($_.Name) $($_.Count)" })
    Write-Host ("review: {0}" -f $(if ($counts.Count -gt 0) { $counts -join ', ' } else { 'none' }))
    foreach ($r in $review) { Write-Host "   REVIEW $($r.MessageId) '$($r.Name)' thread $($r.ThreadId): $($r.Reason)$(if ($r.Detail) { " ($($r.Detail))" })" -ForegroundColor Yellow }
}

# ══ PHASE STAMP ═════════════════════════════════════════════════════════════════════════════════════════════
if ($Phase -eq 'Stamp') {
    # The thread's pair, read once per thread: its systemuser owner plus every systemuser share with the Read bit.
    $pairCache = @{}
    function Get-ThreadParticipants([string]$ThreadId) {
        if ($pairCache.ContainsKey($ThreadId)) { return $pairCache[$ThreadId] }
        $thread = $threads[$ThreadId]
        $answer = [pscustomobject]@{ Participants = @(); Fault = $null; SharesChangedLater = $false; Detail = '' }
        try {
            $ids = New-Object System.Collections.Generic.List[string]
            # The owner counts only when it is a user: a team-owned thread has no owner participant, whatever its
            # user-owner column reads (the BFF counts ownerid only when it is a systemuser).
            if (-not (NonEmpty $thread._owningteam_value) -and (NonEmpty $thread._owninguser_value)) { $ids.Add((NonEmpty $thread._owninguser_value)) }
            $shares = @(Get-All "principalobjectaccessset?`$select=principalid,principaltypecode,accessrightsmask,changedon&`$filter=objectid eq $ThreadId")
            foreach ($s in $shares) {
                $p = NonEmpty $s.principalid
                if ($p -and $s.principaltypecode -eq 'systemuser' -and (([int64]$s.accessrightsmask) -band $ReadBit) -ne 0 -and -not $ids.Contains($p)) { $ids.Add($p) }
            }
            $answer.Participants = $ids.ToArray()
            if ($thread.createdon) {
                $limit = ([datetime]$thread.createdon).ToUniversalTime().AddSeconds($ShareChangeToleranceSeconds)
                $late = @($shares | Where-Object { $_.changedon -and ([datetime]$_.changedon).ToUniversalTime() -gt $limit })
                if ($late.Count -gt 0) { $answer.SharesChangedLater = $true; $answer.Detail = "$($late.Count) share row(s) changed after creation" }
            }
        } catch { $answer.Fault = $_.Exception.Message }
        $pairCache[$ThreadId] = $answer
        return $answer
    }
    $plan = New-Object System.Collections.Generic.List[object]
    $candidates = @(Select-DirectMessages)
    foreach ($m in $candidates) {
        $threadId = Lc $m._sprk_communicationthread_value
        $thread = $threads[$threadId]
        if ($thread.sprk_isdefaultthread -eq $true) { continue }   # the per-user master thread: never a Direct message
        if (HasRegarding $thread $ThreadRegardingColumns) { Add-Review $m 'thread_has_regarding' 'the thread is filed under a record'; continue }
        if ($thread.sprk_isdefaultthread -ne $false) { Add-Review $m 'thread_default_flag_unread'; continue }
        $senderId = NonEmpty $m._sprk_sentby_value
        if (-not $senderId) { Add-Review $m 'no_sentby'; continue }
        $pair = Get-ThreadParticipants $threadId
        if ($pair.Fault) { Add-Review $m 'read_fault' $pair.Fault; continue }
        $ids = @($pair.Participants)
        if ($ids.Count -ne 2) { Add-Review $m 'not_two_participants' "$($ids.Count) participant(s)"; continue }
        if ($ids -notcontains $senderId) { Add-Review $m 'sender_not_a_participant'; continue }
        if ($pair.SharesChangedLater) { Add-Review $m 'thread_shares_changed_after_create' $pair.Detail; continue }
        $recipient = @($ids | Where-Object { $_ -ne $senderId })[0]
        try { $eligibility = Get-Eligibility @($senderId, $recipient) }
        catch { Add-Review $m 'read_fault' $_.Exception.Message; continue }
        $bad = @($senderId, $recipient | ForEach-Object { $eligibility[$_] } | Where-Object { $_ -ne 'eligible' })
        if ($bad.Count -gt 0) { Add-Review $m $bad[0]; continue }
        $plan.Add([pscustomobject]@{ MessageId = (Lc $m.sprk_communicationid); Name = $m.sprk_name; ThreadId = $threadId; Sender = $senderId; Recipient = $recipient })
    }

    Write-Host "`nSelection: $($candidates.Count) Direct message(s) with an empty sprk_directrecipient."
    Write-Host "PLAN: $($plan.Count) row(s) to stamp."
    foreach ($p in $plan) { Write-Host "   stamp $($p.MessageId) '$($p.Name)' thread $($p.ThreadId): recipient $($p.Recipient) (sender $($p.Sender))" }

    if ($Mode -eq 'Verify') {
        Write-Review
        if ($plan.Count -eq 0) { Write-Host "`nVERIFY: PASS (no row would stamp; $($review.Count) on the manual-review list)" -ForegroundColor Green; exit 0 }
        foreach ($p in $plan) { Write-Host "VERIFY FAIL: $($p.MessageId) would be stamped (recipient $($p.Recipient))." -ForegroundColor Red }
        exit 1
    }
    if ($Mode -eq 'DryRun') { Write-Review; Write-Host "`nDRY RUN: nothing was written to Dataverse."; exit 0 }

    $stamped = 0
    try {
        foreach ($p in $plan) {
            $now = Invoke-DvGet "sprk_communications($($p.MessageId))?`$select=$MessageRecheckSelect"
            $planned = [pscustomobject]@{ sprk_communicationid = $p.MessageId; sprk_name = $p.Name; _sprk_communicationthread_value = $p.ThreadId; _sprk_sentby_value = $p.Sender }
            if ((NonEmpty $now._sprk_directrecipient_value) -or (NonEmpty $now._sprk_sentby_value) -ne $p.Sender -or (Lc $now._sprk_communicationthread_value) -ne $p.ThreadId) {
                Add-Review $planned 'changed_since_selection'
                continue
            }
            if (HasRegarding $now $RegardingColumns) { Add-Review $planned 'changed_since_selection' 'now carries a typed regarding lookup'; continue }
            $threadChange = Get-ThreadChange $p.ThreadId
            if ($threadChange) { Add-Review $planned 'changed_since_selection' $threadChange; continue }
            Invoke-DvPatch "sprk_communications($($p.MessageId))" @{ 'sprk_DirectRecipient@odata.bind' = "/systemusers($($p.Recipient))" }
            $back = Invoke-DvGet "sprk_communications($($p.MessageId))?`$select=_sprk_directrecipient_value,_sprk_sentby_value,_sprk_communicationthread_value"
            if ((Lc $back._sprk_directrecipient_value) -ne $p.Recipient) {
                Write-Review
                Stop-Run "read-back of $($p.MessageId): sprk_directrecipient is '$($back._sprk_directrecipient_value)', expected $($p.Recipient). Stopped after $stamped row(s)."
            }
            $stamped++
            Write-Host "   stamped $($p.MessageId): recipient verified"
        }
    }
    catch { Write-Review; throw }   # a request that throws: the review list gathered so far is kept, then the error goes on
    Write-Review
    Write-Host "`nAPPLIED: $stamped row(s) stamped. Re-run without -Apply: the plan must be empty. Then -Verify."
    exit 0
}

# ══ PHASE MOVE ══════════════════════════════════════════════════════════════════════════════════════════════
Write-Host "`nDirect team: business unit '$DirectMessageBusinessUnitName', team '$DirectMessageOwnerTeamName'"
function Get-NamedTeam([string]$BusinessUnitName, [string]$TeamName) {
    $answer = [pscustomobject]@{ BusinessUnit = $null; TeamId = $null; Refusal = $null }
    $units = @(Get-All "businessunits?`$select=businessunitid,name,_parentbusinessunitid_value&`$filter=name eq '$(Esc $BusinessUnitName)'&`$top=2")
    if ($units.Count -eq 0) { $answer.Refusal = 'bu_not_found'; return $answer }
    if ($units.Count -gt 1) { $answer.Refusal = 'bu_ambiguous'; return $answer }
    $answer.BusinessUnit = $units[0]
    $buId = Lc $units[0].businessunitid
    $teams = @(Get-All "teams?`$select=teamid,name&`$filter=_businessunitid_value eq $buId and name eq '$(Esc $TeamName)' and teamtype eq 0 and isdefault eq false&`$top=2")
    if ($teams.Count -eq 0) { $answer.Refusal = 'team_not_found'; return $answer }
    if ($teams.Count -gt 1) { $answer.Refusal = 'team_ambiguous'; return $answer }
    $answer.TeamId = Lc $teams[0].teamid
    return $answer
}

# The Secure Record unit and team, by their names (as the BFF reads them): only with the default names and no unit of that
# name is "no Secure Record unit here" accepted; any other miss cannot prove "not Secure" and refuses.
$secureNamed = $PSBoundParameters.ContainsKey('SecureRecordBusinessUnitName') -or $PSBoundParameters.ContainsKey('SecureRecordOwnerTeamName')
$secure = Get-NamedTeam $SecureRecordBusinessUnitName $SecureRecordOwnerTeamName
$secureBuId = if ($secure.BusinessUnit) { Lc $secure.BusinessUnit.businessunitid } else { $null }
$secureTeamId = $secure.TeamId
$refusal = $null
if ($secure.Refusal -and -not ($secure.Refusal -eq 'bu_not_found' -and -not $secureNamed)) { $refusal = "secure_record_$($secure.Refusal): whether the Direct team is the Secure Record team cannot be decided" }

$direct = Get-NamedTeam $DirectMessageBusinessUnitName $DirectMessageOwnerTeamName
$directBuId = if ($direct.BusinessUnit) { Lc $direct.BusinessUnit.businessunitid } else { $null }
$directTeamId = $direct.TeamId
if (-not $refusal -and $directBuId -and $directBuId -eq $secureBuId) { $refusal = 'bu_is_secure_record' }
if (-not $refusal -and $directTeamId -and $directTeamId -eq $secureTeamId) { $refusal = 'bu_is_secure_record (the team is the Secure Record team)' }
if (-not $refusal -and $direct.Refusal -eq 'team_not_found' -and $directBuId) {
    $defaults = @(Get-All "teams?`$select=teamid&`$filter=_businessunitid_value eq $directBuId and name eq '$(Esc $DirectMessageOwnerTeamName)' and isdefault eq true&`$top=1")
    $refusal = if ($defaults.Count -gt 0) { 'team_is_default' } else { 'team_not_found' }
}
if (-not $refusal -and $direct.Refusal) { $refusal = $direct.Refusal }
if (-not $refusal) {
    $parentId = NonEmpty $direct.BusinessUnit._parentbusinessunitid_value
    if (-not $parentId) { $refusal = 'bu_is_root' }
    else {
        $parent = Invoke-DvGet "businessunits($parentId)?`$select=businessunitid,_parentbusinessunitid_value"
        if (NonEmpty $parent._parentbusinessunitid_value) { $refusal = 'bu_not_root_child' }
    }
}
if (-not $refusal) {
    $members = @(Get-All "teammemberships?`$select=systemuserid&`$filter=teamid eq $directTeamId")
    if ($members.Count -gt 0) { $refusal = "team_has_members ($($members.Count))" }
}
if (-not $refusal) {
    $buUsers = @(Get-All "systemusers?`$select=systemuserid&`$filter=_businessunitid_value eq $directBuId&`$top=5")
    if ($buUsers.Count -gt 0) { $refusal = "bu_has_users ($($buUsers.Count) or more)" }
}
if ($refusal) {
    Write-Host "`nREFUSED: the Direct team is not usable ($refusal). Nothing was selected and nothing was written." -ForegroundColor Red
    exit 1
}
Report 'OK' "Direct team $directTeamId in unit $directBuId (a direct child of the root, no members, no users, not Secure Record)"

# The gate-record creators: the -GateRecordWriterApplicationIds application users (never the BFF list).
$gateWriterUserIds = @()
foreach ($appId in $GateRecordWriterApplicationIds) {
    $u = @(Get-All "systemusers?`$select=systemuserid&`$filter=applicationid eq $appId") | Select-Object -First 1
    if ($u) { $gateWriterUserIds += (Lc $u.systemuserid) } else { $refusal = "gate_record_writer_not_found (no application user for appId $appId in this environment)" }
}
if ($refusal) {
    Write-Host "`nREFUSED: the gate record writer is not usable ($refusal). Nothing was selected and nothing was written." -ForegroundColor Red
    exit 1
}

# The Assign cascade of sprk_communication and of each content table (what an assign of one of its rows re-owns): a cascade to
# a child table that -AcceptedAssignCascade does not list refuses, before anything is selected.
$cascading = @()
foreach ($table in @('sprk_communication') + @($ContentTables | ForEach-Object { $_.Table })) {
    $rels = (Invoke-DvGet "EntityDefinitions(LogicalName='$table')/OneToManyRelationships?`$select=SchemaName,ReferencingEntity,ReferencingAttribute,CascadeConfiguration").value
    foreach ($rel in @($rels | Where-Object { $_.CascadeConfiguration.Assign -ne 'NoCascade' })) {
        $cascading += [pscustomobject]@{ Parent = $table; Child = $rel.ReferencingEntity; Attribute = $rel.ReferencingAttribute; Assign = $rel.CascadeConfiguration.Assign }
    }
}
Write-Host "Assign-cascading relationships from the tables this script assigns: $($cascading.Count)"
foreach ($c in $cascading) {
    Write-Host ("   {0,-30} -> {1}.{2}  Assign={3}  {4}" -f $c.Parent, $c.Child, $c.Attribute, $c.Assign, $(if ($AcceptedAssignCascade -contains $c.Child) { 'ACCEPTED' } else { 'NOT ACCEPTED' }))
}
$unaccepted = @($cascading | Where-Object { $AcceptedAssignCascade -notcontains $_.Child } | ForEach-Object { "$($_.Parent)->$($_.Child)" } | Sort-Object -Unique)
if ($unaccepted.Count -gt 0) {
    Write-Host "`nREFUSED: an Assign cascade is not accepted ($($unaccepted -join ', ')). Assigning a row would re-own those children as a side effect. Pass the child tables the owner accepts as -AcceptedAssignCascade. Nothing was selected and nothing was written." -ForegroundColor Red
    exit 1
}
Write-Host "sharetopreviousowneronassign = $($org.sharetopreviousowneronassign)"
if ($org.sharetopreviousowneronassign -ne $false -and $Mode -eq 'Apply') {
    Stop-Run 'organization.sharetopreviousowneronassign is not false: every assign would share the row back to its previous owner. Nothing was written.'
}

function OwnerOf($Row) { if (NonEmpty $Row._owningteam_value) { "team:$(NonEmpty $Row._owningteam_value)" } elseif (NonEmpty $Row._owninguser_value) { "user:$(NonEmpty $Row._owninguser_value)" } else { 'unknown' } }
function IsExcludedOwner($Row) { $t = NonEmpty $Row._owningteam_value; $t -and ($t -eq $directTeamId -or ($secureTeamId -and $t -eq $secureTeamId)) }
function ValidPair($Row) { $s = NonEmpty $Row._sprk_sentby_value; $r = NonEmpty $Row._sprk_directrecipient_value; [bool]($s -and $r -and $s -ne $r) }
function Get-ContentRows([string]$MessageId) {
    $rows = @()
    foreach ($ct in $ContentTables) {
        $filter = "_$($ct.Lookup)_value eq $MessageId"
        if ($ct.Gate) { $filter += ' and _sprk_confirmedby_value eq null' }
        foreach ($row in @(Get-All "$($ct.Set)?`$select=$($ct.Id),_owningteam_value,_owninguser_value,_createdby_value&`$filter=$filter")) {
            if ($ct.Gate -and $gateWriterUserIds -notcontains (Lc $row._createdby_value)) { continue }   # not a gate record
            $rows += [pscustomobject]@{ Table = $ct.Table; Set = $ct.Set; Id = (Lc $row.($ct.Id)); Owner = (OwnerOf $row); Excluded = (IsExcludedOwner $row) }
        }
    }
    return $rows
}

# The message's participants (sprk_sentby, sprk_directrecipient) and their shares on it. Enabled: a systemuser row read with
# isdisabled false (a disabled or missing participant is skipped, as the BFF's Direct share pass skips a disabled one).
# Readers hold a systemuser share with the Read bit; Holders hold any non-zero systemuser share. Fault: a read that threw
# (strict: a faulted read is never "no shares").
function Get-ParticipantShares($Message) {
    $answer = [pscustomobject]@{ Fault = $null; Enabled = @(); Readers = @(); Holders = @(); OwnerUser = $null }
    $ids = @(@((NonEmpty $Message._sprk_sentby_value), (NonEmpty $Message._sprk_directrecipient_value)) | Where-Object { $_ } | Select-Object -Unique)
    try { $standing = Get-Eligibility $ids }
    catch { $answer.Fault = "participant read: $($_.Exception.Message)"; return $answer }
    $answer.Enabled = @($ids | Where-Object { $standing[$_] -notin @('participant_disabled', 'participant_missing') })
    if ($answer.Enabled.Count -eq 0) { return $answer }
    $messageId = Lc $Message.sprk_communicationid
    try { $shares = @(Get-All "principalobjectaccessset?`$select=principalid,principaltypecode,accessrightsmask&`$filter=objectid eq $messageId") }
    catch { $answer.Fault = "share read: $($_.Exception.Message)"; return $answer }
    $users = @($shares | Where-Object { $_.principaltypecode -eq 'systemuser' -and (NonEmpty $_.principalid) })
    $answer.Readers = @($users | Where-Object { (([int64]$_.accessrightsmask) -band $ReadBit) -ne 0 } | ForEach-Object { NonEmpty $_.principalid })
    $answer.Holders = @($users | Where-Object { [int64]$_.accessrightsmask -ne 0 } | ForEach-Object { NonEmpty $_.principalid })
    if (-not (NonEmpty $Message._owningteam_value)) { $answer.OwnerUser = NonEmpty $Message._owninguser_value }
    return $answer
}
# Whether every enabled participant keeps Read once the message leaves its owner: Reason (a review reason; nothing of the
# message moves) or Grants (the owning user to grant Read while the message is still user-owned). Read-only.
function Get-ParticipantAccess($Message) {
    $answer = [pscustomobject]@{ Reason = $null; Detail = ''; Grants = @() }
    $s = Get-ParticipantShares $Message
    if ($s.Fault) { $answer.Reason = 'read_fault'; $answer.Detail = $s.Fault; return $answer }
    if ($s.Enabled.Count -eq 0) { $answer.Reason = 'participants_not_readable'; $answer.Detail = 'no enabled participant'; return $answer }
    $grants = @()
    foreach ($p in $s.Enabled) {
        if ($s.Readers -contains $p) { continue }
        if ($s.Holders -contains $p) {
            $answer.Reason = 'participant_share_without_read'; $answer.Detail = "participant $p holds a share without Read (never upgraded here)"; return $answer
        }
        if ($s.OwnerUser -and $p -eq $s.OwnerUser) { $grants += $p; continue }
        $answer.Reason = 'participants_not_readable'; $answer.Detail = "participant $p holds no Read share and is not the message's owning user"; return $answer
    }
    $answer.Grants = $grants
    return $answer
}

$plan = New-Object System.Collections.Generic.List[object]
$verifyFailures = New-Object System.Collections.Generic.List[string]
$candidates = @(Select-DirectMessages)
foreach ($m in $candidates) {
    $thread = $threads[(Lc $m._sprk_communicationthread_value)]
    if ($thread.sprk_isdefaultthread -eq $true) { continue }
    if (HasRegarding $thread $ThreadRegardingColumns) { Add-Review $m 'thread_has_regarding' 'the thread is filed under a record'; continue }
    if ($thread.sprk_isdefaultthread -ne $false) { Add-Review $m 'thread_default_flag_unread'; continue }
    if ($secureTeamId -and (NonEmpty $m._owningteam_value) -eq $secureTeamId) { continue }   # Secure-owned: never this script's
    if (-not (ValidPair $m)) { Add-Review $m 'no_valid_pair' "sentby=$(NonEmpty $m._sprk_sentby_value) recipient=$(NonEmpty $m._sprk_directrecipient_value)"; continue }
    $id = Lc $m.sprk_communicationid
    $messageMoves = (NonEmpty $m._owningteam_value) -ne $directTeamId
    # -Verify: a Direct-team-owned message whose enabled participant holds no Read share fails the gate.
    if ($Mode -eq 'Verify' -and -not $messageMoves) {
        $s = Get-ParticipantShares $m
        if ($s.Fault) { Add-Review $m 'read_fault' $s.Fault; $verifyFailures.Add("message $id`: $($s.Fault)") }
        else {
            $without = @($s.Enabled | Where-Object { $s.Readers -notcontains $_ })
            if ($without.Count -gt 0) {
                Add-Review $m 'participant_without_read' "enabled participant(s) without a Read share: $($without -join ', ')"
                $verifyFailures.Add("message $id is on the Direct team and $($without -join ', ') holds no Read share on it")
            }
        }
    }
    $content = @(Get-ContentRows $id | Where-Object { -not $_.Excluded })
    if (-not $messageMoves -and $content.Count -eq 0) { continue }   # done
    $plan.Add([pscustomobject]@{ MessageId = $id; Name = $m.sprk_name; Owner = (OwnerOf $m); MessageMoves = $messageMoves; Content = $content; Message = $m })
}

Write-Host "`nSelection: $($candidates.Count) Direct message(s)."
Write-Host "PLAN: $($plan.Count) message(s) with rows to move to the Direct team."
foreach ($p in $plan) {
    Write-Host "   message $($p.MessageId) '$($p.Name)' owner $($p.Owner)$(if (-not $p.MessageMoves) { ' (already on the Direct team)' })"
    foreach ($c in $p.Content) { Write-Host "      $($c.Table) $($c.Id) owner $($c.Owner)" }
}

if ($Mode -eq 'Verify') {
    Write-Review
    if ($plan.Count -eq 0 -and $verifyFailures.Count -eq 0) { Write-Host "`nVERIFY: PASS (every valid-pair Direct message and its content rows are on the Direct team, and every enabled participant of a Direct-owned message holds Read on it; $($review.Count) on the manual-review list)" -ForegroundColor Green; exit 0 }
    foreach ($p in $plan) { Write-Host "VERIFY FAIL: message $($p.MessageId) has $(@($p.Content).Count + [int]$p.MessageMoves) row(s) not on the Direct team." -ForegroundColor Red }
    foreach ($f in $verifyFailures) { Write-Host "VERIFY FAIL: $f." -ForegroundColor Red }
    exit 1
}
if ($Mode -eq 'DryRun') {
    # The participants' Read, as -Apply would decide it now (read-only): the grants it would make, or why nothing would move.
    foreach ($p in $plan) {
        $access = Get-ParticipantAccess $p.Message
        if ($access.Reason) {
            Add-Review $p.Message $access.Reason $access.Detail
            Write-Host "   would move nothing of message $($p.MessageId): $($access.Reason) ($($access.Detail))"
            continue
        }
        foreach ($g in $access.Grants) { Write-Host "   WOULD grant Read to systemuser $g on message $($p.MessageId) (its owning user), before the assign" }
    }
    Write-Review
    if ($org.sharetopreviousowneronassign -ne $false) { Write-Host 'STOP (for -Apply): organization.sharetopreviousowneronassign is not false.' -ForegroundColor Red }
    Write-Host "`nDRY RUN: nothing was written to Dataverse."
    exit 0
}

function Get-ShareCount([string]$Id) { @(Get-All "principalobjectaccessset?`$select=principalid,accessrightsmask&`$filter=objectid eq $Id" | Where-Object { [int64]$_.accessrightsmask -gt 0 }).Count }
$moved = 0
function Move-Row([string]$Set, [string]$Table, [string]$Id) {
    $o = (Invoke-DvGet 'organizations?$select=name,sharetopreviousowneronassign').value[0]
    if ($o.sharetopreviousowneronassign -ne $false) { Write-Review; Stop-Run "organization.sharetopreviousowneronassign turned on. Stopped before $Table $Id, after $script:moved row(s)." }
    $before = Get-ShareCount $Id
    Invoke-DvPatch "$Set($Id)" @{ 'ownerid@odata.bind' = "/teams($directTeamId)" }
    $back = Invoke-DvGet "$Set($Id)?`$select=_owningteam_value"
    if ((NonEmpty $back._owningteam_value) -ne $directTeamId) { Write-Review; Stop-Run "read-back of $Table ${Id}: owning team is '$($back._owningteam_value)', expected $directTeamId. Stopped after $script:moved row(s)." }
    $after = Get-ShareCount $Id
    if ($after -lt $before) { Write-Review; Stop-Run "share count fell on $Table ${Id}: $before before, $after after. Stopped after $($script:moved + 1) row(s); inspect the row's shares." }
    if ($after -gt $before) { Write-Host "   WARNING: share count rose on $Table ${Id}: $before -> $after (the participant share job may have run)" -ForegroundColor Yellow }
    $script:moved++
    Write-Host "   moved $Table ${Id}: owner verified, shares $before -> $after"
}
try {
    foreach ($p in $plan) {
        # The assign invariant, at write time: a message that is no longer a Direct message with a valid stamped pair moves
        # nothing of it, content rows included.
        $now = Invoke-DvGet "sprk_communications($($p.MessageId))?`$select=$MessageRecheckSelect"
        if (-not (ValidPair $now)) { Add-Review $now 'no_valid_pair' 'changed since selection'; continue }
        if ($secureTeamId -and (NonEmpty $now._owningteam_value) -eq $secureTeamId) { Add-Review $now 'changed_since_selection' 'now owned by the Secure Record team'; continue }
        if (HasRegarding $now $RegardingColumns) { Add-Review $now 'changed_since_selection' 'now carries a typed regarding lookup'; continue }
        $threadNowId = NonEmpty $now._sprk_communicationthread_value
        if (-not $threadNowId) { Add-Review $now 'changed_since_selection' 'now has no thread'; continue }
        $threadChange = Get-ThreadChange $threadNowId
        if ($threadChange) { Add-Review $now 'changed_since_selection' $threadChange; continue }
        # The participants keep Read on the message before anything of it moves (the Direct team has no members and the
        # previous owner gets no share on assign).
        $access = Get-ParticipantAccess $now
        if ($access.Reason) { Add-Review $now $access.Reason $access.Detail; continue }
        $unconfirmed = $null
        foreach ($g in $access.Grants) {
            # Only the owning user, while the message is still user-owned: access the owner already has.
            try {
                Invoke-DvPost 'GrantAccess' @{
                    Target          = @{ '@odata.id' = "sprk_communications($($p.MessageId))" }
                    PrincipalAccess = @{ Principal = @{ '@odata.id' = "systemusers($g)" }; AccessMask = 'ReadAccess' }
                }
            }
            catch { $unconfirmed = "GrantAccess for $g threw: $($_.Exception.Message)"; break }
            try { $back = @(Get-All "principalobjectaccessset?`$select=principalid,principaltypecode,accessrightsmask&`$filter=objectid eq $($p.MessageId)") }
            catch { $unconfirmed = "read-back after the grant to $g threw: $($_.Exception.Message)"; break }
            $confirmed = @($back | Where-Object { $_.principaltypecode -eq 'systemuser' -and (NonEmpty $_.principalid) -eq $g -and (([int64]$_.accessrightsmask) -band $ReadBit) -ne 0 })
            if ($confirmed.Count -eq 0) { $unconfirmed = "read-back after the grant shows no Read share for $g"; break }
            Write-Host "   granted Read to $g on message $($p.MessageId) (its owning user): confirmed by read-back"
        }
        if ($unconfirmed) { Add-Review $now 'participant_grant_unconfirmed' $unconfirmed; continue }
        foreach ($c in $p.Content) { Move-Row $c.Set $c.Table $c.Id }
        if ((NonEmpty $now._owningteam_value) -ne $directTeamId) { Move-Row 'sprk_communications' 'sprk_communication' $p.MessageId }
    }
}
catch { Write-Review; throw }   # a request that throws: the review list gathered so far is kept, then the error goes on
Write-Review
Write-Host "`nAPPLIED: $moved row(s) moved to the Direct team. Re-run without -Apply: the plan must be empty. Then -Verify."
exit 0
