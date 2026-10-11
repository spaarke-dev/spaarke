#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Finds the work assignments filed under an INVOICE or EVENT of a secure matter or project (#1630) and brings them into
    isolation through the BFF's own access job. Dry run by default; -Apply triggers the job; -Verify is the gate.

.DESCRIPTION
    #1630 (owner Option A, 2026-10-11): a work assignment whose typed sprk_regardinginvoice / sprk_regardingevent names an
    invoice or event now inherits from the matters and projects THAT record belongs to — the invoice's sprk_matter /
    sprk_project, the event's sprk_regardingmatter / sprk_regardingproject (its core-ancestor stamp columns,
    CoreAncestorResolver.IntermediateRootColumns). Before the fix such a work assignment was never isolated, even under a
    secure matter. Rows written before the fix are found here.

    THIS SCRIPT WRITES NOTHING TO DATAVERSE ITSELF. Securing a record is provisioning's (flag, named owner team, own
    container, creator share, the parents' sharees), and the BFF's secure-root-inheritance job is its one backfill owner
    (DATAVERSE-WRITE-PATH-ARCHITECTURE.md §3: a backfill calls the same invariant owner as the inline path). With the #1630
    BFF deployed, the job's listing (SecureRootInheritance.ListFiledRootsAsync) includes these rows, so its scheduled run
    (every 5 minutes) secures them; -Apply only triggers a run now and re-checks.

    CANDIDATES — every sprk_workassignment whose sprk_regardinginvoice or sprk_regardingevent is set, read with the
    operator's own az identity (read-only):
      SECURE-ME   its invoice / event belongs to a matter or project whose own sprk_issecure is true, and the work
                  assignment is not yet isolated (sprk_issecure true, owned by a team, its own sprk_containerid);
      ISOLATED    the same, already isolated (nothing to do);
      UNREADABLE  the invoice / event it names could not be read or no longer exists — the BFF reads that as
                  unverifiable (an unreadable one) or as no parent (a deleted one); listed for the operator;
      ORDINARY    its invoice / event belongs to no secure matter or project (unchanged by #1630);
      HELD        its event belongs to a work assignment or service request instead (not the parent rule; unchanged).
    KNOWN LIMIT: "secure" is the matter's / project's OWN flag; a project secure only through its own matter is secured by
    the same job first, and its work assignments on the run after.

    -Verify exits 1 while any row is SECURE-ME (UNREADABLE rows are reported but do not fail the gate: the BFF already
    refuses to share from them, and a deleted invoice confers nothing).

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Trigger the BFF's secure-root-inheritance job (POST /api/admin/jobs/secure-root-inheritance/trigger, SystemAdmin), wait
    for the run, then list again. Requires -BffBaseUrl and -ApiScope.

.PARAMETER Verify
    Read-only gate: exit 0 only when no row is SECURE-ME.

.PARAMETER BffBaseUrl
    The BFF's base URL (-Apply only), e.g. https://spe-api-dev-67e2xz.azurewebsites.net

.PARAMETER ApiScope
    The scope of a token for the BFF API (-Apply only), e.g. api://<bff-app-id>/.default; the az login must be a user the
    BFF's SystemAdmin policy admits.

.EXAMPLE
    .\Repair-IntermediateFiledWorkAssignments.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    .\Repair-IntermediateFiledWorkAssignments.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply `
        -BffBaseUrl https://spe-api-dev-67e2xz.azurewebsites.net -ApiScope api://<bff-app-id>/.default

.NOTES
    #1630 (unified-access-control-r2). LIVE ORDER: (1) deploy the BFF with #1630; (2) THIS script (dry run); (3) -Apply, or
    wait one job cycle; (4) -Verify — the gate. Auth: the operator's own az CLI identity. No secrets.
#>

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [switch]$Apply,
    [switch]$Verify,
    [string]$BffBaseUrl,
    [string]$ApiScope
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
if ($Apply -and (-not $BffBaseUrl -or -not $ApiScope)) { throw '-Apply needs -BffBaseUrl and -ApiScope (the job is triggered through the BFF).' }
$Api = "$EnvironmentUrl/api/data/v9.2"
$JobId = 'secure-root-inheritance'   # SecureRootInheritanceJob.JobIdConstant

# ── The rule (the same names as SecureRootInheritance.IntermediateFilingColumns + CoreAncestorResolver.IntermediateRootColumns) ──
$Intermediates = @(
    @{ Column = 'sprk_regardinginvoice'; Table = 'sprk_invoice'; Set = 'sprk_invoices'
       Parents = @{ sprk_matter = 'sprk_matter'; sprk_project = 'sprk_project' }; Other = @() },
    @{ Column = 'sprk_regardingevent'; Table = 'sprk_event'; Set = 'sprk_events'
       Parents = @{ sprk_regardingmatter = 'sprk_matter'; sprk_regardingproject = 'sprk_project' }
       Other = @('sprk_regardingworkassignment', 'sprk_regardingservicerequest') }
)
$ParentSets = @{ sprk_matter = 'sprk_matters'; sprk_project = 'sprk_projects' }

$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
}
function Invoke-DvGet([string]$Path) { Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Get }
function Invoke-DvGetAll([string]$Path) {
    $rows = [System.Collections.Generic.List[object]]::new()
    $h = $headers.Clone(); $h['Prefer'] = 'odata.maxpagesize=500'
    $next = "$Api/$Path"
    while ($next) {
        $page = Invoke-RestMethod -Uri $next -Headers $h -Method Get
        foreach ($r in @($page.value)) { $rows.Add($r) }
        $next = $page.'@odata.nextLink'
    }
    , $rows
}

function Get-Row([string]$Set, [Guid]$Id, [string]$Select) {
    try { return Invoke-DvGet "$Set($Id)?`$select=$Select" }
    catch { if ($_.Exception.Response.StatusCode.value__ -eq 404) { return 'MISSING' }; return 'UNREADABLE' }
}

$parentFlags = @{}
function Test-ParentSecure([string]$Table, [Guid]$Id) {
    $key = "$Table|$Id"
    if (-not $parentFlags.ContainsKey($key)) {
        $row = Get-Row $ParentSets[$Table] $Id 'sprk_issecure'
        $parentFlags[$key] = if ($row -is [string]) { $row } else { [bool]($row.sprk_issecure -eq $true) }
    }
    $parentFlags[$key]
}

function Get-Candidates {
    $select = @('sprk_workassignmentid', 'sprk_name', 'sprk_issecure', '_owningteam_value', 'sprk_containerid') +
        @($Intermediates | ForEach-Object { "_$($_.Column)_value" })
    $filter = (@($Intermediates | ForEach-Object { "_$($_.Column)_value ne null" }) -join ' or ')
    $rows = Invoke-DvGetAll "sprk_workassignments?`$select=$($select -join ',')&`$filter=$filter"
    $found = [System.Collections.Generic.List[object]]::new()
    foreach ($row in $rows) {
        $secureParents = @(); $unreadable = @(); $held = @()
        foreach ($i in $Intermediates) {
            $value = $row."_$($i.Column)_value"
            if (-not $value) { continue }
            $columns = @($i.Parents.Keys) + $i.Other
            $read = Get-Row $i.Set ([Guid]$value) (($columns | ForEach-Object { "_$($_)_value" }) -join ',')
            if ($read -is [string]) { $unreadable += "$($i.Table) $value ($read)"; continue }
            foreach ($column in $i.Parents.Keys) {
                $parentId = $read."_$($column)_value"
                if (-not $parentId) { continue }
                $secure = Test-ParentSecure $i.Parents[$column] ([Guid]$parentId)
                if ($secure -is [string]) { $unreadable += "$($i.Parents[$column]) $parentId ($secure)" }
                elseif ($secure) { $secureParents += "$($i.Parents[$column]) $parentId via $($i.Table) $value" }
            }
            foreach ($column in $i.Other) { if ($read."_$($column)_value") { $held += "$($i.Table) $value -> $column" } }
        }
        $isolated = $row.sprk_issecure -eq $true -and $row._owningteam_value -and $row.sprk_containerid
        $state = if ($secureParents.Count -gt 0) { if ($isolated) { 'ISOLATED' } else { 'SECURE-ME' } }
        elseif ($unreadable.Count -gt 0) { 'UNREADABLE' }
        elseif ($held.Count -gt 0) { 'HELD' }
        else { 'ORDINARY' }
        $found.Add([pscustomobject]@{
                Id = $row.sprk_workassignmentid; Name = $row.sprk_name; State = $state
                Detail = (@($secureParents) + @($unreadable) + @($held)) -join '; '
            })
    }
    , $found
}

function Write-Report($Candidates) {
    foreach ($c in $Candidates) {
        $colour = switch ($c.State) { 'SECURE-ME' { 'Yellow' } 'UNREADABLE' { 'Red' } 'ISOLATED' { 'Green' } default { 'Gray' } }
        Write-Host ("  {0,-10} {1} '{2}' {3}" -f $c.State, $c.Id, $c.Name, $c.Detail) -ForegroundColor $colour
    }
    $count = { param($s) @($Candidates | Where-Object State -eq $s).Count }
    Write-Host ("`n{0} work assignment(s) filed under an invoice or event: SECURE-ME {1}, ISOLATED {2}, UNREADABLE {3}, ORDINARY {4}, HELD {5}." -f
        $Candidates.Count, (& $count 'SECURE-ME'), (& $count 'ISOLATED'), (& $count 'UNREADABLE'), (& $count 'ORDINARY'), (& $count 'HELD'))
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($Apply) { 'APPLY (triggers the BFF job)' } else { 'DRY RUN (read-only)' }))
Write-Host "`nCandidates"
$candidates = Get-Candidates
Write-Report $candidates
$toSecure = @($candidates | Where-Object State -eq 'SECURE-ME')

if ($Verify) {
    if ($toSecure.Count -eq 0) { Write-Host "`nPASS: every work assignment filed under an invoice or event of a secure record is isolated." -ForegroundColor Green; exit 0 }
    Write-Host "`nFAIL: $($toSecure.Count) work assignment(s) still to secure (-Apply, or wait one job cycle)." -ForegroundColor Red; exit 1
}
if (-not $Apply) { Write-Host "`nDRY RUN complete — nothing was written." -ForegroundColor Cyan; exit 0 }
if ($toSecure.Count -eq 0) { Write-Host "`nNothing to secure." -ForegroundColor Green; exit 0 }
if (-not $PSCmdlet.ShouldProcess("$($toSecure.Count) work assignment(s)", "trigger the BFF's $JobId job")) { exit 0 }

$base = $BffBaseUrl.TrimEnd('/')
function Invoke-Bff([string]$Method, [string]$Path) {
    $bffToken = az account get-access-token --scope $ApiScope --query accessToken -o tsv
    if ($LASTEXITCODE -ne 0 -or -not $bffToken) { throw "Could not get a token for $ApiScope (az login as a SystemAdmin user)." }
    Invoke-RestMethod -Method $Method -Uri "$base$Path" -Headers @{ Authorization = "Bearer $bffToken" }
}
$runId = (Invoke-Bff 'Post' "/api/admin/jobs/$JobId/trigger").runId
Write-Host "`nTriggered $JobId run $runId; waiting for it to finish..."
$run = $null
for ($i = 0; $i -lt 120 -and -not $run; $i++) {
    Start-Sleep -Seconds 5
    $run = @(Invoke-Bff 'Get' "/api/admin/jobs/$JobId/history?limit=20") | Where-Object { $_.runId -eq $runId -and $_.status -ne 'InProgress' } | Select-Object -First 1
}
if (-not $run) { throw "Run $runId did not finish within 10 minutes; check /api/admin/jobs/$JobId/status, then -Verify." }
Write-Host "Run $runId ended '$($run.status)'. $($run.errorMessage)"
Write-Host "`nAfter the run"
$after = Get-Candidates
Write-Report $after
$left = @($after | Where-Object State -eq 'SECURE-ME').Count
if ($left -gt 0) { Write-Host "`n$left still to secure — see the run's report above; re-run -Apply or wait one job cycle, then -Verify." -ForegroundColor Yellow; exit 1 }
Write-Host "`nAPPLIED. Run -Verify." -ForegroundColor Green
