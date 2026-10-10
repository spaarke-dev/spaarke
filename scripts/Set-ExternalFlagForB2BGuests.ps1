#!/usr/bin/env pwsh
<#
.SYNOPSIS
    One-off data step per environment: every B2B guest system user (#EXT# in its user name) gets sprk_isexternal = Yes
    (true) - unified-access-control-r2 task 114, owner round 67 (2026-10-06). Dry run by default; -Apply writes;
    -Verify checks. Idempotent. Changes nothing else.

.DESCRIPTION
    WHY. Since task 114 a BLANK sprk_isexternal means NOT external, everywhere ("sprk_isexternal means they're
    external"): Manage Access "+ User" and the Assigned-To rule share with such a user, internal-only messages reach
    them, and a Restricted record keeps their share. A B2B guest from another organization is external, but the column
    only says so where someone set it. This step sets it, once, for every guest the directory marks as one: Entra gives
    a B2B guest a user principal name containing "#EXT#", which Dataverse stores in systemuser.domainname.

    What it does:
      (a) LIST   every systemuser whose domainname contains "#EXT#" and whose sprk_isexternal is not Yes (blank or No),
                 every page past Dataverse's 5,000-row page, with name, user name, current value, enabled state.
      (b) APPLY  (-Apply only) PATCHes each listed row's sprk_isexternal to true - If-Match: * (never an upsert) - and
                 nothing else (modifiedon / modifiedby move, as for any update). Then re-lists.
      (c) REPORT writes a JSON report (-ReportPath): mode, timestamps, every user listed before, and the count after.
    Users WITHOUT #EXT# are never read for writing and never changed - an internal user's blank flag stays blank, which
    is now internal. A guest already set to Yes is left alone.

    ⚠️ EFFECT OF -Apply (state it to the owner before running it on an environment with Restricted records): within 5
    minutes the BFF's Assigned-To reconciliation job removes each newly flagged guest's direct share on every Restricted
    project, matter and work assignment (task 114's RestrictedExternalShareRemover), the record's next save does it at
    once, and those guests stop receiving internal-only messages (the resolver's cache lapses within 10 minutes).

    Run order per environment (docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md §12.2 Phase 7b, task 114):
      1. dry run - review the list with the owner;  2. -Apply;  3. -Verify (exit 0).
    In an EXISTING environment run all three BEFORE deploying the BFF that carries task 114. That BFF reads a BLANK flag
    as internal, so a guest still blank when it starts is shared with on Restricted records and receives internal-only
    messages. The BFF before task 114 already treats a blank flag as external, so running this first changes nothing
    for it - it only marks explicitly what that BFF assumed. If the BFF was deployed first anyway: run this at once, and
    allow 10 minutes after -Apply (the identity resolver's sprk_isexternal cache, which also decides a licensed user's
    Restricted-record access on the SPA/Teams plane) before relying on the guests being treated as external.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER ReportPath
    Where the JSON report is written. Default: .\b2b-guest-external-flag-<yyyyMMdd-HHmmss>.json

.PARAMETER Apply
    Write mode. Without it the script never writes (except the report file).

.PARAMETER Verify
    Read-only check with a pass/fail exit code: no #EXT# system user without sprk_isexternal = Yes.

.EXAMPLE
    ./Set-ExternalFlagForB2BGuests.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: every B2B guest whose flag is not Yes; zero writes.

.EXAMPLE
    ./Set-ExternalFlagForB2BGuests.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply

.EXAMPLE
    ./Set-ExternalFlagForB2BGuests.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify

.NOTES
    unified-access-control-r2 task 114 (#1003, owner round 67). Code: InternalShareEndpoints.ClassifyEligibility,
    SystemUserIdentityResolver.IsExternalAsync, RestrictedExternalShareRemover. Auth: the operator's own az CLI identity
    (needs Write on systemuser). No secrets.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [string]$ReportPath = (Join-Path (Get-Location) ("b2b-guest-external-flag-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))),
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$Api = "$EnvironmentUrl/api/data/v9.2"

$Column = 'sprk_isexternal'
# "#EXT#" URL-encoded: an unencoded '#' would end the URL at a fragment and silently drop the filter.
$GuestFilter = "contains(domainname,'%23EXT%23')"
$NotYesFilter = "($Column eq null or $Column eq false)"

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
function Get-DvAll([string]$Path) {
    $h = $headers.Clone(); $h['Prefer'] = 'odata.maxpagesize=5000'
    $rows = [System.Collections.Generic.List[object]]::new(); $next = "$Api/$Path"
    while ($next) {
        $page = Invoke-RestMethod -Uri $next -Headers $h -Method Get
        foreach ($r in @($page.value)) { $rows.Add($r) }
        $next = $page.'@odata.nextLink'
    }
    , $rows   # ONE object: count the List itself, never @(Get-DvAll ...) (Repair-SecureFlagNulls.ps1's G-0 lesson)
}

$Select = "systemuserid,fullname,domainname,$Column,isdisabled"
function Get-UnflaggedGuests { Get-DvAll "systemusers?`$select=$Select&`$filter=$GuestFilter and $NotYesFilter&`$orderby=domainname" }

$IsDryRun = -not $Apply.IsPresent
$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))

$guests = Get-UnflaggedGuests
$alreadyYes = (Get-DvAll "systemusers?`$select=systemuserid&`$filter=$GuestFilter and $Column eq true").Count
Write-Host ("B2B guests (#EXT#): {0} already Yes; {1} blank or No" -f $alreadyYes, $guests.Count)
foreach ($g in $guests) {
    $value = if ($null -eq $g.$Column) { 'blank' } else { "$($g.$Column)" }
    Write-Host ("  {0}  {1,-40} {2}  [{3}{4}]" -f $g.systemuserid, $g.fullname, $g.domainname, $value,
        $(if ($g.isdisabled) { ', disabled' } else { '' }))
}

$report = [ordered]@{
    environment  = $EnvironmentUrl
    organization = $org
    mode         = $(if ($Verify) { 'verify' } elseif ($IsDryRun) { 'dry-run' } else { 'apply' })
    startedUtc   = (Get-Date).ToUniversalTime().ToString('o')
    column       = $Column
    alreadyYes   = $alreadyYes
    before       = @($guests | ForEach-Object {
            [ordered]@{ systemuserid = $_.systemuserid; fullname = $_.fullname; domainname = $_.domainname; value = $_.$Column; isdisabled = $_.isdisabled }
        })
}

$failures = [System.Collections.Generic.List[string]]::new()
if ($Apply -and $guests.Count -gt 0) {
    $h = $headers.Clone(); $h['If-Match'] = '*'   # never an upsert: a wrong id is a 404, not a new row
    $body = @{ $Column = $true } | ConvertTo-Json -Compress
    foreach ($g in $guests) {
        try {
            Invoke-RestMethod -Uri "$Api/systemusers($($g.systemuserid))" -Headers $h -Method Patch -Body $body | Out-Null
        }
        catch {
            $failures.Add("could not set $($g.systemuserid) ($($g.domainname)): $($_.Exception.Message)")
        }
    }
    Write-Host ("Set {0} of {1} guest(s) to Yes." -f ($guests.Count - $failures.Count), $guests.Count) -ForegroundColor Green
}

$after = if ($Apply) { (Get-UnflaggedGuests).Count } else { $guests.Count }
$report.after = $after
$report.failures = @($failures)
$report.finishedUtc = (Get-Date).ToUniversalTime().ToString('o')
$report | ConvertTo-Json -Depth 6 | Set-Content -Path $ReportPath -Encoding utf8
Write-Host "Report: $ReportPath"

if ($Verify -or $Apply) {
    if ($after -eq 0 -and $failures.Count -eq 0) {
        Write-Host 'PASS: every B2B guest system user has sprk_isexternal = Yes.' -ForegroundColor Green
        exit 0
    }
    Write-Host ("FAIL: {0} B2B guest(s) still blank or No." -f $after) -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host 'DRY RUN complete - nothing was written. Review the list, then re-run with -Apply.' -ForegroundColor Cyan
