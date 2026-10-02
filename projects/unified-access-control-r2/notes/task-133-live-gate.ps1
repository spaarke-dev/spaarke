<#
.SYNOPSIS
  Task 133 (C11) manual live gate - the platform behaviours secure provisioning relies on, proven in dev.

.DESCRIPTION
  NOT RUN by the task-133 executor (its run was read-only by instruction). The main session runs it AFTER task 144's
  live cutover (the named team 'Secure Record Owners' must exist and hold the Secure Record Owner role) and AFTER the
  BFF carrying task 133 is deployed. Every step without -Apply is read-only.

  Steps (each maps to a lettered item of the task-133 manual live gate; results go in task-133-provision-creator-lockout.md):
    ShareFirstProof   (a)(b)(e)  -Apply. Creates a throwaway secure project owned by -TestUserId, then replays the exact
                                 Web API call sequence the endpoint issues: GrantAccess to the CURRENT owner (a), owner
                                 PATCH to the named team + read-back, POA re-read (b: did the share survive, mask intact?),
                                 then the COMPENSATION sequence: owner PATCH back to the test user + read-back, RevokeAccess
                                 of the share this run issued, POA re-read (must equal the pre-call snapshot), and
                                 sprk_issecure still true (e). No fault-injection switch exists in production code; the
                                 compensation path is proven by replaying its calls, as recorded in the note.
                                 METHOD CAVEAT for (e) (task 133 verifier round 1): this REPLAYS the Web API calls the
                                 endpoint's compensation issues, from this script. It does NOT execute the endpoint's
                                 compensation code (MoveWithCreatorShareAsync -> MoveOwnerAsync back +
                                 RestoreCreatorShareAsync), which no live state reaches without a fault. The code path is
                                 covered by the fixture tests; the replay proves the PLATFORM accepts the calls and
                                 restores the pre-call state. Criterion (e) names "the compensation code path", so the
                                 owner must accept replay as the recorded method, explicitly, when the gate is run.
    StrandForResume   (c)        -Apply. Takes -RecordId (a secure project the TEST USER created in the wizard, so its
                                 createdby is that user) into the stranded state by hand: owner -> named team, the test
                                 user's share revoked, sprk_containerid cleared. Then call provisioning as an
                                 administrator (printed command) and run Inspect.
    Inspect           (b)(c)(d)  Read-only. Owner, container, modifiedon, POA rows, and RetrievePrincipalAccess for
                                 -TestUserId on -RecordId. Run before and after a second provision call for (d).

  The test user must be an EXISTING non-admin user in their current business unit (owner, round 4: do not relocate
  users; ask the owner for a new test user if a specific access level is needed).

.EXAMPLE
  .\task-133-live-gate.ps1 -Step ShareFirstProof -TestUserId <guid> -Apply
  .\task-133-live-gate.ps1 -Step Inspect -TestUserId <guid> -RecordId <guid>
#>
[CmdletBinding()]
param(
  [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
  [Parameter(Mandatory)][ValidateSet('ShareFirstProof', 'StrandForResume', 'Inspect')][string]$Step,
  [Parameter(Mandatory)][Guid]$TestUserId,
  [Guid]$RecordId,
  [string]$OwnerTeamName = 'Secure Record Owners',
  [switch]$Apply
)
$ErrorActionPreference = 'Stop'
$tok = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv
$H = @{ Authorization = "Bearer $tok"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0'; 'Content-Type' = 'application/json' }
$Api = "$EnvironmentUrl/api/data/v9.2"
# ProvisionProjectEndpoint.CreatorAccessRights, DERIVED from the source at run time (task 133 verifier round 1) rather
# than hardcoded, because task 139 owns the value and may change Collaborate. If the constant changes SHAPE (it is no
# longer "RecordShareLevels.CollaborateRights + ',ShareAccess'"), the script stops instead of replaying stale rights.
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$levelsSrc = Get-Content (Join-Path $repoRoot 'src\server\api\Sprk.Bff.Api\Services\Access\RecordShareLevels.cs') -Raw
$endpointSrc = Get-Content (Join-Path $repoRoot 'src\server\api\Sprk.Bff.Api\Api\ExternalAccess\ProvisionProjectEndpoint.cs') -Raw
if ($levelsSrc -notmatch 'internal const string CollaborateRights = "([^"]+)";') { throw 'RecordShareLevels.CollaborateRights not found - update this script.' }
$collaborate = $Matches[1]
if ($endpointSrc -notmatch 'internal const string CreatorAccessRights = RecordShareLevels\.CollaborateRights \+ ",ShareAccess";') {
  throw 'ProvisionProjectEndpoint.CreatorAccessRights changed shape (task 139?) - update this script before running.'
}
$CreatorRights = ((@($collaborate -split ',') + 'ShareAccess') | Select-Object -Unique) -join ','
"CreatorAccessRights (from source) = $CreatorRights"
function S($g) { if ($g) { "$g".Substring(0, 8) } else { '-' } }
function Owner($id) { Invoke-RestMethod "$Api/sprk_projects($id)?`$select=_owningteam_value,_owninguser_value,sprk_containerid,sprk_issecure,modifiedon,_createdby_value" -Headers $H }
function Shares($id) { (Invoke-RestMethod "$Api/principalobjectaccessset?`$filter=objectid eq $id and objecttypecode eq 'sprk_project'&`$select=principalid,principaltypecode,accessrightsmask" -Headers $H).value }
function Show($label, $id) {
  $o = Owner $id
  "$label owner team=$(S $o._owningteam_value) user=$(S $o._owninguser_value) container=[$($o.sprk_containerid)] issecure=$($o.sprk_issecure) modifiedon=$($o.modifiedon) createdby=$(S $o._createdby_value)"
  foreach ($s in Shares $id) { "   POA $(S $s.principalid) type=$($s.principaltypecode) mask=$($s.accessrightsmask)" }
}
function Post($action, $body) { Invoke-RestMethod "$Api/$action" -Method Post -Headers $H -Body ($body | ConvertTo-Json -Depth 5) }
function Grant($id, $user, $rights) { Post 'GrantAccess' @{ Target = @{ '@odata.id' = "sprk_projects($id)" }; PrincipalAccess = @{ Principal = @{ '@odata.id' = "systemusers($user)" }; AccessMask = $rights } } }
function Revoke($id, $user) { Post 'RevokeAccess' @{ Target = @{ '@odata.id' = "sprk_projects($id)" }; Revokee = @{ '@odata.id' = "systemusers($user)" } } }
function Assign($id, $bind) { Invoke-RestMethod "$Api/sprk_projects($id)" -Method Patch -Headers $H -Body (@{ 'ownerid@odata.bind' = $bind } | ConvertTo-Json) }

$team = (Invoke-RestMethod "$Api/teams?`$select=teamid,name&`$filter=name eq '$OwnerTeamName' and isdefault eq false and teamtype eq 0" -Headers $H).value
if (@($team).Count -ne 1) { throw "Named owner team '$OwnerTeamName' resolved $(@($team).Count) times - run task 144's cutover first." }
$teamId = $team[0].teamid
"sharetopreviousowneronassign = $((Invoke-RestMethod "$Api/organizations?`$select=sharetopreviousowneronassign" -Headers $H).value[0].sharetopreviousowneronassign) (must be False)"

switch ($Step) {
  'Inspect' {
    if (-not $RecordId) { throw '-RecordId is required.' }
    Show 'record' $RecordId
    $rpa = Invoke-RestMethod "$Api/systemusers($TestUserId)/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@t)?@t={'@odata.id':'sprk_projects($RecordId)'}" -Headers $H
    "RetrievePrincipalAccess(test user) = $($rpa.AccessRights)"
  }
  'ShareFirstProof' {
    if (-not $Apply) { throw 'ShareFirstProof writes: pass -Apply.' }
    $name = "TASK133-PROBE-$(Get-Date -Format yyyyMMddHHmmss)"
    $resp = Invoke-WebRequest "$Api/sprk_projects" -Method Post -Headers $H -Body (@{ sprk_projectname = $name; sprk_issecure = $true; 'ownerid@odata.bind' = "/systemusers($TestUserId)" } | ConvertTo-Json)
    $id = [Guid]($resp.Headers['OData-EntityId'] -replace '.*\(([^)]+)\).*', '$1')
    "probe $name = $id"
    Show 'pre-call' $id
    try { Grant $id $TestUserId $CreatorRights; '(a) GrantAccess to the CURRENT owner: ACCEPTED' } catch { "(a) GrantAccess to the CURRENT owner: REFUSED - $($_.Exception.Message)" }
    Show 'after share-first' $id
    Assign $id "/teams($teamId)"; Show 'after move (b: is the test user''s POA row still there, same mask?)' $id
    # (e) compensation replay: owner back + read-back, then remove the share this run issued, then re-read.
    Assign $id "/systemusers($TestUserId)"; Show 'after compensating move' $id
    try { Revoke $id $TestUserId } catch { "revoke: $($_.Exception.Message)" }
    Show '(e) after restore - must equal pre-call, issecure True' $id
    "Probe left in place for inspection; delete it with: Invoke-RestMethod '$Api/sprk_projects($id)' -Method Delete -Headers `$H"
  }
  'StrandForResume' {
    if (-not $RecordId) { throw '-RecordId is required.' }
    if (-not $Apply) { Show 'would strand' $RecordId; 'Read-only: pass -Apply to strand it.'; break }
    Show 'before' $RecordId
    Assign $RecordId "/teams($teamId)"
    try { Revoke $RecordId $TestUserId } catch { "revoke: $($_.Exception.Message)" }
    Invoke-RestMethod "$Api/sprk_projects($RecordId)" -Method Patch -Headers $H -Body (@{ sprk_containerid = $null } | ConvertTo-Json)
    Show 'stranded' $RecordId
    "Now, as an administrator: POST <bff>/api/v1/external-access/provision-project  { `"recordType`": `"project`", `"recordId`": `"$RecordId`" }"
    "Then: .\task-133-live-gate.ps1 -Step Inspect -TestUserId $TestUserId -RecordId $RecordId"
  }
}
