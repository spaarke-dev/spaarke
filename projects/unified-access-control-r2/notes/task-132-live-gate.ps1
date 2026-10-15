<#
.SYNOPSIS
  Task 132 (C12) live gates G-1 (dev deploy) and G-2 (criterion 21): every live write behind -Apply, every result
  checkable with -Verify, everything else read-only.

.DESCRIPTION
  NOT RUN by the task-132 executors (their runs were read-only by instruction). The main session runs it, in this order,
  and records each printed result in task-132-access-cache-faults-and-staleness.md section 12.

    Preflight        Read-only. (1) The deploy-order precondition (notes section 12): systemuser.sprk_primarycontact
                     exists in the target Dataverse — without it a systemuser-row read fails and, by criterion 16's
                     fail-closed rule, Teams/SPA shows internal users nothing. (2) The BFF App Service settings the gate
                     depends on: Redis__Enabled (the real invalidator is registered on it, notes section 6.2),
                     Membership__CacheInvalidator__Enabled, and ExternalAccess__ImpersonatedRootSets__Enabled
                     (criterion 21(c): record it). (3) /healthz and the site's last-modified time.
    Deploy      G-1  Without -Apply: Preflight, then prints the exact deploy command. -Apply: refuses unless the
                     precondition holds, records the start time, runs scripts/Deploy-BffApi.ps1 with the dev defaults
                     (THE LIVE WRITE). -Verify: /healthz is 200 and the site was modified after the recorded start.
    ReassignMatter   Criterion 21(a). Without -Apply: the matter's owner, the target team, and the test user's
                     RetrievePrincipalAccess on the matter (it must include ReadAccess before — they see it through their
                     team). -Apply: records the original owner and the UTC time, then PATCHes ownerid to the target team
                     (THE LIVE WRITE — a record change on an existing TEST matter). The original owner is recorded ONCE
                     per matter: a second -Apply before RestoreMatter keeps the first one (owner round 48 (d) — otherwise
                     it would record the target team as the "original" and the real owner could not be restored); after
                     a verified restore the next -Apply records afresh. Then, AS THE TEST USER, poll the
                     Teams/SPA matter list and record when the matter disappears: it must be within 4 minutes (notes
                     section 9 row 3, owner R3/R4). -Verify: the owner is the target, the minutes since the reassign, and
                     the test user's RetrievePrincipalAccess (Dataverse's own answer: without ReadAccess only a cache can
                     still list the matter).
    RestoreMatter    Criterion 21(a) clean-up. Without -Apply: what it would restore. -Apply: PATCHes the matter back to the
                     recorded original owner (THE LIVE WRITE) and marks the record restored once the owner reads back as
                     the original. -Verify: the owner is the original again.

  State: -StatePath (default %TEMP%\task-132-live-gate.state.json) holds the deploy start and, per matter, the original
  owner. The bookkeeping is tested without any live call: task-132-live-gate.Tests.ps1 (Invoke-Pester).
    VerifyProvision  Criterion 21(b), read-only. First, by hand: a BU colleague (-ColleagueUserId) loads the project list
                     (warm cache); then the project is provisioned from the wizard (or an existing dev secure test project
                     is unsecured and re-secured). The colleague's FIRST request after provisioning returns must not list
                     it, and a direct read must be denied — record both responses. This step then reports the project's
                     owner (the named secure owner team), the colleague's RetrievePrincipalAccess (no ReadAccess), and the
                     BFF's [ACCESS-EVICT] traces for the project from Application Insights (the owner-change eviction
                     that makes the first request correct).

  Test users: EXISTING non-admin users in their current business unit (owner, 2026-09-10 / 2026-09-30: do not relocate
  users; record changes only). The operator's own az login (az account get-access-token) is used for Dataverse and Azure.

.EXAMPLE
  .\task-132-live-gate.ps1 -Step Preflight
  .\task-132-live-gate.ps1 -Step Deploy                 # dry run: preflight + the exact command
  .\task-132-live-gate.ps1 -Step Deploy -Apply
  .\task-132-live-gate.ps1 -Step Deploy -Verify
  .\task-132-live-gate.ps1 -Step ReassignMatter -MatterId <guid> -ToTeamId <guid> -TestUserId <guid>
  .\task-132-live-gate.ps1 -Step ReassignMatter -MatterId <guid> -ToTeamId <guid> -TestUserId <guid> -Apply
  .\task-132-live-gate.ps1 -Step ReassignMatter -MatterId <guid> -TestUserId <guid> -Verify
  .\task-132-live-gate.ps1 -Step RestoreMatter -MatterId <guid> -Apply
  .\task-132-live-gate.ps1 -Step RestoreMatter -MatterId <guid> -Verify
  .\task-132-live-gate.ps1 -Step VerifyProvision -ProjectId <guid> -ColleagueUserId <guid>
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][ValidateSet('Preflight', 'Deploy', 'ReassignMatter', 'RestoreMatter', 'VerifyProvision')][string]$Step,
  [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
  [string]$ResourceGroupName = 'rg-spaarke-dev',
  [string]$AppServiceName = 'spaarke-bff-dev',
  [string]$AppInsightsName,
  [Guid]$MatterId,
  [Guid]$ToTeamId,
  [Guid]$TestUserId,
  [Guid]$ProjectId,
  [Guid]$ColleagueUserId,
  [int]$LookbackMinutes = 60,
  [string]$StatePath = (Join-Path ([IO.Path]::GetTempPath()) 'task-132-live-gate.state.json'),
  [switch]$Apply,
  [switch]$Verify
)
$ErrorActionPreference = 'Stop'
if ($Apply -and $Verify) { throw 'Pass -Apply or -Verify, not both.' }

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$statePath = $StatePath
$Api = "$EnvironmentUrl/api/data/v9.2"
$BffUrl = "https://$AppServiceName.azurewebsites.net"

# PowerShell 7's ConvertFrom-Json turns the stored ISO 'o' strings into DateTime (Kind Utc); [DateTime]::Parse of that
# value re-reads its culture string as LOCAL time (negative 'minutes since'). Normalise either shape to UTC once, here.
function ConvertTo-UtcTime($v) {
  if ($v -is [DateTime]) { if ($v.Kind -eq [DateTimeKind]::Unspecified) { [DateTime]::SpecifyKind($v, [DateTimeKind]::Utc) } else { $v.ToUniversalTime() } }
  else { [DateTimeOffset]::Parse([string]$v, [Globalization.CultureInfo]::InvariantCulture).UtcDateTime }
}
function Get-State { if (Test-Path $statePath) { Get-Content $statePath -Raw | ConvertFrom-Json -AsHashtable } else { @{} } }
function Set-State($state) { $state | ConvertTo-Json -Depth 5 | Set-Content $statePath; "state recorded in $statePath" }
# The per-matter record: { OriginalOwner, ReassignedUtc, Restored, RestoredUtc }. A state file written before the per-matter
# shape (one top-level MatterId / OriginalOwner) is read as that one matter's record.
function Get-MatterRecord($state, $id) {
  if (-not $state.Matters) { $state.Matters = @{} }
  if ($state.MatterId -and $state.OriginalOwner -and -not $state.Matters.ContainsKey($state.MatterId)) {
    $state.Matters[$state.MatterId] = @{ OriginalOwner = $state.OriginalOwner; ReassignedUtc = $state.ReassignedUtc; Restored = $false }
  }
  $state.Remove('MatterId'); $state.Remove('OriginalOwner'); $state.Remove('ReassignedUtc')
  $state.Matters["$id"]
}
function Get-DvHeaders {
  $tok = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv
  if (-not $tok) { throw "No Dataverse token for $EnvironmentUrl - run az login first." }
  @{ Authorization = "Bearer $tok"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0'; 'Content-Type' = 'application/json' }
}
function Get-Owner($set, $id) {
  Invoke-RestMethod "$Api/$set($id)?`$select=_ownerid_value,_owningteam_value,_owninguser_value,modifiedon" -Headers (Get-DvHeaders)
}
function Get-OwnerBind($set, $id) {
  $o = Get-Owner $set $id
  if ($o._owningteam_value) { "/teams($($o._owningteam_value))" } else { "/systemusers($($o._owninguser_value))" }
}
function Get-Rights($userId, $set, $id) {
  (Invoke-RestMethod "$Api/systemusers($userId)/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@t)?@t={'@odata.id':'$set($id)'}" -Headers (Get-DvHeaders)).AccessRights
}
function Get-TeamName($id) { (Invoke-RestMethod "$Api/teams($id)?`$select=name" -Headers (Get-DvHeaders)).name }
function Test-PrimaryContactColumn {
  try {
    Invoke-RestMethod "$Api/EntityDefinitions(LogicalName='systemuser')/Attributes(LogicalName='sprk_primarycontact')?`$select=LogicalName" -Headers (Get-DvHeaders) | Out-Null
    $true
  } catch { $false }
}
function Show-Owner($label, $set, $id) {
  $o = Get-Owner $set $id
  $team = if ($o._owningteam_value) { " team '$(Get-TeamName $o._owningteam_value)' ($($o._owningteam_value))" } else { " user $($o._owninguser_value)" }
  "$label $set($id): owner$team, modifiedon $($o.modifiedon)"
}
function Invoke-Preflight {
  $hasColumn = Test-PrimaryContactColumn
  Write-Host "(1) systemuser.sprk_primarycontact in $EnvironmentUrl : $(if ($hasColumn) { 'PRESENT' } else { 'MISSING - deploy task 141 schema first; do NOT deploy this BFF' })"
  $names = "[?name=='Redis__Enabled' || name=='Membership__CacheInvalidator__Enabled' || name=='ExternalAccess__ImpersonatedRootSets__Enabled'].{name:name, value:value}"
  $settings = az webapp config appsettings list -g $ResourceGroupName -n $AppServiceName --query $names -o json | ConvertFrom-Json
  foreach ($n in 'Redis__Enabled', 'Membership__CacheInvalidator__Enabled', 'ExternalAccess__ImpersonatedRootSets__Enabled') {
    $s = @($settings | Where-Object name -EQ $n)
    Write-Host "(2) $AppServiceName $n = $(if ($s.Count) { $s[0].value } else { '(not set: the appsettings default applies)' })"
  }
  try { $code = (Invoke-WebRequest "$BffUrl/healthz" -UseBasicParsing -TimeoutSec 30).StatusCode } catch { $code = "error: $($_.Exception.Message)" }
  Write-Host "(3) $BffUrl/healthz = $code; site last modified (UTC) = $(az webapp show -g $ResourceGroupName -n $AppServiceName --query lastModifiedTimeUtc -o tsv)"
  $hasColumn
}
function Get-AppInsightsName {
  if ($AppInsightsName) { return $AppInsightsName }
  $found = @(az resource list -g $ResourceGroupName --resource-type 'Microsoft.Insights/components' --query '[].name' -o tsv)
  if ($found.Count -ne 1) { throw "Application Insights components in $ResourceGroupName : $($found -join ', ') - pass -AppInsightsName." }
  $found[0]
}

switch ($Step) {
  'Preflight' { Invoke-Preflight | Out-Null }

  'Deploy' {
    $deployScript = Join-Path $repoRoot 'scripts\Deploy-BffApi.ps1'
    $deploy = "& '$deployScript' -Environment dev -ResourceGroupName '$ResourceGroupName' -AppServiceName '$AppServiceName'"
    if ($Verify) {
      $state = Get-State
      if (-not $state.DeployStartedUtc) { throw 'No recorded deploy start - run -Step Deploy -Apply first.' }
      try { $code = (Invoke-WebRequest "$BffUrl/healthz" -UseBasicParsing -TimeoutSec 30).StatusCode } catch { $code = "error: $($_.Exception.Message)" }
      $modified = [DateTime]::Parse((az webapp show -g $ResourceGroupName -n $AppServiceName --query lastModifiedTimeUtc -o tsv)).ToUniversalTime()
      "healthz = $code (must be 200); site modified $($modified.ToString('o')) vs deploy start $($state.DeployStartedUtc) -> $(if ($modified -ge (ConvertTo-UtcTime $state.DeployStartedUtc)) { 'DEPLOYED' } else { 'NOT REDEPLOYED' })"
      break
    }
    $ok = Invoke-Preflight
    if (-not $Apply) { "Dry run. The deploy (G-1), from the branch to be verified:"; "  $deploy"; 'Re-run with -Apply to deploy.'; break }
    if (-not $ok) { throw 'Deploy-order precondition failed (systemuser.sprk_primarycontact missing) - not deploying.' }
    $state = Get-State; $state.DeployStartedUtc = [DateTime]::UtcNow.ToString('o'); Set-State $state
    & $deployScript -Environment dev -ResourceGroupName $ResourceGroupName -AppServiceName $AppServiceName
  }

  'ReassignMatter' {
    if (-not $MatterId -or -not $TestUserId) { throw '-MatterId and -TestUserId are required.' }
    if ($Verify) {
      $record = Get-MatterRecord (Get-State) $MatterId
      if (-not $record -or -not $record.ReassignedUtc) { throw "No recorded reassign of $MatterId - run -Apply first." }
      Show-Owner 'now' 'sprk_matters' $MatterId
      $minutes = ([DateTime]::UtcNow - (ConvertTo-UtcTime $record.ReassignedUtc)).TotalMinutes
      "minutes since the reassign = $([Math]::Round($minutes, 1)) (bound: the matter must have left the test user's Teams/SPA list within 4)"
      "test user's RetrievePrincipalAccess = $(Get-Rights $TestUserId 'sprk_matters' $MatterId) (without ReadAccess, only a cache can still list it)"
      break
    }
    if (-not $ToTeamId) { throw '-ToTeamId is required.' }
    Show-Owner 'before' 'sprk_matters' $MatterId
    "target team: '$(Get-TeamName $ToTeamId)' ($ToTeamId)"
    "test user's RetrievePrincipalAccess = $(Get-Rights $TestUserId 'sprk_matters' $MatterId) (must include ReadAccess before the reassign)"
    if (-not $Apply) { 'Dry run. Re-run with -Apply to reassign (a record change on this TEST matter).'; break }
    $state = Get-State
    $record = Get-MatterRecord $state $MatterId
    if ($record -and $record.OriginalOwner -and -not $record.Restored) {
      # Owner round 48 (d): never overwrite a recorded original. Re-read now, the owner is the target of the FIRST reassign,
      # and recording it would leave RestoreMatter unable to put the real owner back.
      "original owner already recorded for $MatterId : $($record.OriginalOwner) - KEPT (not re-read; restore with -Step RestoreMatter)"
    } else {
      $record = @{ OriginalOwner = Get-OwnerBind 'sprk_matters' $MatterId }
      "original owner recorded for $MatterId : $($record.OriginalOwner)"
    }
    $record.ReassignedUtc = [DateTime]::UtcNow.ToString('o'); $record.Restored = $false
    $state.Matters["$MatterId"] = $record
    Set-State $state
    Invoke-RestMethod "$Api/sprk_matters($MatterId)" -Method Patch -Headers (Get-DvHeaders) -Body (@{ 'ownerid@odata.bind' = "/teams($ToTeamId)" } | ConvertTo-Json) | Out-Null
    Show-Owner 'after' 'sprk_matters' $MatterId
    "Reassigned at $($record.ReassignedUtc). Now poll the Teams/SPA matter list AS THE TEST USER; record when the matter disappears."
  }

  'RestoreMatter' {
    if (-not $MatterId) { throw '-MatterId is required.' }
    $state = Get-State
    $record = Get-MatterRecord $state $MatterId
    if (-not $record -or -not $record.OriginalOwner) { throw "No recorded original owner for $MatterId." }
    Show-Owner 'now' 'sprk_matters' $MatterId
    if ($Verify) { "restored: $(if ((Get-OwnerBind 'sprk_matters' $MatterId) -eq $record.OriginalOwner) { 'YES' } else { "NO (expected $($record.OriginalOwner))" })"; break }
    if (-not $Apply) { "Dry run. Would set ownerid to $($record.OriginalOwner). Re-run with -Apply."; break }
    Invoke-RestMethod "$Api/sprk_matters($MatterId)" -Method Patch -Headers (Get-DvHeaders) -Body (@{ 'ownerid@odata.bind' = $record.OriginalOwner } | ConvertTo-Json) | Out-Null
    Show-Owner 'restored' 'sprk_matters' $MatterId
    if ((Get-OwnerBind 'sprk_matters' $MatterId) -eq $record.OriginalOwner) {
      $record.Restored = $true; $record.RestoredUtc = [DateTime]::UtcNow.ToString('o'); $state.Matters["$MatterId"] = $record; Set-State $state
    } else {
      "WARNING: the owner did not read back as $($record.OriginalOwner); the record is kept so the restore can be retried."
    }
  }

  'VerifyProvision' {
    if (-not $ProjectId -or -not $ColleagueUserId) { throw '-ProjectId and -ColleagueUserId are required.' }
    Show-Owner 'project' 'sprk_projects' $ProjectId
    "colleague's RetrievePrincipalAccess = $(Get-Rights $ColleagueUserId 'sprk_projects' $ProjectId) (must not include ReadAccess)"
    $app = Get-AppInsightsName
    $kql = "traces | where message has '[ACCESS-EVICT]' and message has '$ProjectId' | project timestamp, message | order by timestamp asc"
    $rows = (az monitor app-insights query --app $app -g $ResourceGroupName --analytics-query $kql --offset "$($LookbackMinutes)m" -o json | ConvertFrom-Json).tables[0].rows
    "[ACCESS-EVICT] traces for the project in the last $LookbackMinutes min ($app):"
    if (-not $rows) { '  none - the owner-change eviction did not run (or Redis is not enabled): criterion 21(b) FAILS' }
    foreach ($r in $rows) { "  $($r[0])  $($r[1])" }
    "Record the colleague's FIRST list response after provisioning returned, and the direct-read response, in notes section 12."
  }
}
