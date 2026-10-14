#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Per-environment data step: B2B guest system users (#EXT# in the user name) who are NOT the customer's own staff get
    sprk_isexternal = Yes (true). unified-access-control-r2 task 114 (owner round 67, 2026-10-06); Model 1 rule added by
    spaarkeai-word-add-in-r1 task 126 (owner 2026-10-13, ADR-028 A8/A9). Dry run by default; -Apply writes; -Verify
    checks. Idempotent. Changes nothing else.

.DESCRIPTION
    WHY. Since task 114 a BLANK sprk_isexternal means NOT external, everywhere ("sprk_isexternal means they're
    external"): Manage Access "+ User" and the Assigned-To rule share with such a user, internal-only messages reach
    them, and a Restricted record keeps their share. Entra gives a B2B guest a user principal name containing "#EXT#",
    which Dataverse stores in systemuser.domainname.

    TWO MODES - the operator must choose one (no default, so the wrong one is never run by omission):

    -CustomerTenantIds <guid[]>   MODEL 1 stamp (Dataverse in the Spaarke tenant). The customer's licensed core users
        are themselves B2B guests here (ADR-028 A8), so "#EXT#" alone does not mean external. Each guest's company
        directory is found from its email domain (Microsoft Graph findTenantInformationByDomainName; the address is
        systemuser.internalemailaddress, else the one encoded in the #EXT# user name) and compared with the customer's
        home-directory ids - the stamp's WorkforceIdentity:CustomerTenantIds:
          customer    - home directory listed, or the user is in -InternalUserIds -> NEVER written. One already set to
                        Yes is reported for review (customer staff are internal on Model 1; set it back to No).
          other       - resolves to any other directory  -> set to Yes unless it already is Yes.
          unresolved  - no address, unknown domain, lookup failure, or the two addresses disagree -> NEVER written.
                        Already Yes: kept. Otherwise reported for review: the operator either sets Yes on the user
                        (an outsider) or adds the user to -InternalUserIds (customer staff).
        Only a stored Yes is anyone's decision: No is the column's default (AppDefaultValue 0), so a new guest row
        carries No without anyone choosing it.
        Guards: a listed id that is the stamp's own directory (the Dataverse token's tid) or the empty GUID is refused,
        and -Apply is refused when it has guests to set but no guest resolves to the customer's directories. These
        catch only an id that matches no guest: another real directory that has guests here passes them. Check the
        ids against the stamp BFF's WorkforceIdentity__CustomerTenantIds__N settings before -Apply.
    -AllGuestsExternal            NOT Model 1 (Dataverse in the customer's own tenant, where a guest is an outsider):
        the task 114 behaviour - every #EXT# user whose flag is blank or No is set to Yes.

    What it does: (a) LIST the guests and the planned change; (b) APPLY (-Apply only) PATCHes sprk_isexternal = true on
    each planned row - If-Match: <the row's ETag> (never an upsert; a row changed since it was listed is a 412,
    reported, and picked up by the next run) - and nothing else, then re-lists; (c) REPORT a JSON file
    (-ReportPath). Users WITHOUT #EXT# are never read for writing and never changed.

    ⚠️ EFFECT OF -Apply (state it to the owner before running it on an environment with Restricted records): within 5
    minutes the BFF's Assigned-To reconciliation job removes each newly flagged guest's direct share on every Restricted
    project, matter and work assignment (task 114's RestrictedExternalShareRemover), the record's next save does it at
    once, and those guests stop receiving internal-only messages (the resolver's cache lapses within 10 minutes).

    Run order per environment (docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md §12.2 Phase 7b):
      1. dry run - review the list with the owner;  2. -Apply;  3. -Verify (exit 0).

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER CustomerTenantIds
    Model 1: the customer's home-directory (Entra tenant) ids, as configured in the stamp BFF's
    WorkforceIdentity:CustomerTenantIds. Needs a Graph token from the operator's az login (no extra permission
    was needed with the az CLI in the Spaarke tenant, 2026-10-13).

.PARAMETER InternalUserIds
    Model 1 only: systemuserids the operator has confirmed are the customer's staff although their address does not
    resolve to a listed directory (a review row, or a subsidiary's domain). They are treated as 'customer'. Pass the
    same list on every run; the report lists them.

.PARAMETER AllGuestsExternal
    Not Model 1: every #EXT# guest is external (task 114 behaviour).

.PARAMETER ReportPath
    Where the JSON report is written. Default: .\b2b-guest-external-flag-<yyyyMMdd-HHmmss>.json

.PARAMETER Apply
    Write mode. Without it the script never writes (except the report file).

.PARAMETER Verify
    Read-only check with a pass/fail exit code. Model 1: no 'other' or 'unresolved' guest without Yes, and no
    customer staffer with Yes. Otherwise: no #EXT# system user without sprk_isexternal = Yes.

.EXAMPLE
    ./Set-ExternalFlagForB2BGuests.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -CustomerTenantIds bc3aa7f4-3ca3-47e6-84e7-fea35f5c245b
    Model 1 dry run: classifies every guest; zero writes.

.EXAMPLE
    ./Set-ExternalFlagForB2BGuests.ps1 -EnvironmentUrl https://contoso.crm.dynamics.com -AllGuestsExternal -Apply

.NOTES
    Code: InternalShareEndpoints.ClassifyEligibility, SystemUserIdentityResolver.IsExternalAsync,
    RestrictedExternalShareRemover. Auth: the operator's own az CLI identity (Write on systemuser; Graph read for the
    Model 1 lookup). No secrets.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [guid[]]$CustomerTenantIds,
    [guid[]]$InternalUserIds,
    [switch]$AllGuestsExternal,
    [string]$ReportPath = (Join-Path (Get-Location) ("b2b-guest-external-flag-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))),
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$Model1 = $null -ne $CustomerTenantIds -and $CustomerTenantIds.Count -gt 0
if ($Model1 -eq $AllGuestsExternal.IsPresent) {
    throw ('Choose exactly one mode: -CustomerTenantIds <customer home-directory ids> on a Model 1 stamp ' +
        '(customer staff are B2B guests there and must stay internal), or -AllGuestsExternal where every guest is an outsider.')
}
if ($InternalUserIds -and -not $Model1) { throw '-InternalUserIds applies to a Model 1 run (-CustomerTenantIds) only.' }
$Api = "$EnvironmentUrl/api/data/v9.2"

$Column = 'sprk_isexternal'
# "#EXT#" URL-encoded: an unencoded '#' would end the URL at a fragment and silently drop the filter.
$GuestFilter = "contains(domainname,'%23EXT%23')"
$NotYesFilter = "($Column eq null or $Column eq false)"

$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
if ($Model1) {
    # The stamp's own directory is never a customer's home directory: a guest's home is elsewhere by definition, and a
    # Model 1 stamp's directory is the Spaarke tenant. Listing it (or the empty GUID) makes every customer staffer 'other'.
    try {
        $p = $token.Split('.')[1].Replace('-', '+').Replace('_', '/'); $p += '=' * ((4 - $p.Length % 4) % 4)
        $stampTenant = "$(([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p)) | ConvertFrom-Json).tid)".ToLowerInvariant()
    }
    catch { throw "Could not read the directory id (tid) from the Dataverse token: $($_.Exception.Message)" }
    if (-not $stampTenant) { throw 'The Dataverse token carries no directory id (tid); cannot check -CustomerTenantIds.' }
    foreach ($id in $CustomerTenantIds) {
        if ($id -eq [guid]::Empty) { throw '-CustomerTenantIds holds the empty GUID.' }
        if ("$id" -eq $stampTenant) {
            throw ("-CustomerTenantIds holds $id, the stamp's own directory. List the customer's home directory " +
                '(the stamp BFF''s WorkforceIdentity__CustomerTenantIds__N), not the Spaarke tenant.')
        }
    }
}
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

$Select = "systemuserid,fullname,domainname,internalemailaddress,$Column,isdisabled"

# --- Model 1: which company directory a guest belongs to --------------------------------------------------------------
# The address Entra encoded in the guest UPN: "name_domain.com#EXT#@host.onmicrosoft.com" -> "name@domain.com".
# The LAST '_' before #EXT# separates local part and domain (a DNS domain has no '_'; a local part may).
function Get-UpnEmail([string]$DomainName) {
    $i = $DomainName.IndexOf('#EXT#', [StringComparison]::OrdinalIgnoreCase)
    if ($i -le 0) { return $null }
    $encoded = $DomainName.Substring(0, $i); $u = $encoded.LastIndexOf('_')
    if ($u -le 0 -or $u -eq $encoded.Length - 1) { return $null }
    return $encoded.Substring(0, $u) + '@' + $encoded.Substring($u + 1)
}
function Get-EmailDomain([string]$Email) {
    if ([string]::IsNullOrWhiteSpace($Email) -or $Email -match '#EXT#') { return $null }
    $at = $Email.LastIndexOf('@')
    if ($at -le 0 -or $at -eq $Email.Length - 1) { return $null }
    return $Email.Substring($at + 1).Trim().ToLowerInvariant()
}
$script:TenantByDomain = @{}
$script:GraphHeaders = $null
function Resolve-DomainTenant([string]$Domain) {
    if ($script:TenantByDomain.ContainsKey($Domain)) { return $script:TenantByDomain[$Domain] }
    if (-not $script:GraphHeaders) {
        $g = az account get-access-token --resource-type ms-graph --query accessToken -o tsv 2>$null
        if (-not $g) { throw "No Microsoft Graph token. Run 'az login' and retry." }
        $script:GraphHeaders = @{ Authorization = "Bearer $g"; Accept = 'application/json' }
    }
    $escaped = $Domain.Replace("'", "''")
    try {
        $r = Invoke-RestMethod -Method Get -Headers $script:GraphHeaders `
            -Uri "https://graph.microsoft.com/v1.0/tenantRelationships/findTenantInformationByDomainName(domainName='$escaped')"
        $result = @{ tenantId = "$($r.tenantId)".ToLowerInvariant(); error = $null }
    }
    catch {
        $result = @{ tenantId = $null; error = "lookup of '$Domain' failed: $($_.Exception.Message)" }
    }
    $script:TenantByDomain[$Domain] = $result
    return $result
}
function Get-GuestClass($Guest, [string[]]$CustomerIds) {
    $domains = @(@((Get-EmailDomain $Guest.internalemailaddress), (Get-EmailDomain (Get-UpnEmail $Guest.domainname))) |
        Where-Object { $_ } | Select-Object -Unique)
    if ($domains.Count -eq 0) { return @{ class = 'unresolved'; tenantId = $null; reason = 'no usable email address' } }
    $tenants = @()
    foreach ($d in $domains) {
        $t = Resolve-DomainTenant $d
        if (-not $t.tenantId) { return @{ class = 'unresolved'; tenantId = $null; reason = $t.error } }
        $tenants += $t.tenantId
    }
    $tenants = @($tenants | Select-Object -Unique)
    if ($tenants.Count -gt 1) {
        return @{ class = 'unresolved'; tenantId = $null; reason = "addresses resolve to different directories: $($tenants -join ', ')" }
    }
    $class = if ($CustomerIds -contains $tenants[0]) { 'customer' } else { 'other' }
    return @{ class = $class; tenantId = $tenants[0]; reason = "domain $($domains -join ', ')" }
}

# --- List -------------------------------------------------------------------------------------------------------------
$IsDryRun = -not $Apply.IsPresent
$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host ("Mode        : {0}, {1}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }),
    $(if ($Model1) { "Model 1 (customer directories: $($CustomerTenantIds -join ', '))" } else { 'all guests external' }))

function Get-Plan {
    if (-not $Model1) {
        $rows = Get-DvAll "systemusers?`$select=$Select&`$filter=$GuestFilter and $NotYesFilter&`$orderby=domainname"
        return @{ toSet = @($rows); rows = @($rows | ForEach-Object { @{ guest = $_; class = 'guest'; tenantId = $null; reason = '#EXT#'; action = 'set Yes' } }) }
    }
    $customerIds = @($CustomerTenantIds | ForEach-Object { "$_".ToLowerInvariant() })
    $confirmed = @($InternalUserIds | ForEach-Object { "$_".ToLowerInvariant() })
    $all = Get-DvAll "systemusers?`$select=$Select&`$filter=$GuestFilter&`$orderby=domainname"
    $rows = foreach ($g in $all) {
        $c = if ($confirmed -contains "$($g.systemuserid)".ToLowerInvariant()) {
            @{ class = 'customer'; tenantId = $null; reason = 'confirmed customer staff (-InternalUserIds)' }
        }
        else { Get-GuestClass $g $customerIds }
        $yes = $g.$Column -eq $true   # No is the column default, not anyone's decision: only a stored Yes counts
        $action = switch ($c.class) {
            'customer' { if ($yes) { 'review: customer staff flagged external (set it back to No)' } else { 'none' } }
            'other' { if ($yes) { 'none (already Yes)' } else { 'set Yes' } }
            default { if ($yes) { 'none (already Yes)' } else { 'review: set Yes (outsider) or pass the id in -InternalUserIds (customer staff)' } }
        }
        @{ guest = $g; class = $c.class; tenantId = $c.tenantId; reason = $c.reason; action = $action }
    }
    $rows = @($rows)
    return @{ toSet = @($rows | Where-Object { $_.action -eq 'set Yes' } | ForEach-Object { $_.guest }); rows = $rows }
}

$plan = Get-Plan
foreach ($r in $plan.rows) {
    $g = $r.guest
    $value = if ($null -eq $g.$Column) { 'blank' } else { "$($g.$Column)" }
    Write-Host ("  {0}  {1,-32} {2}  [{3}{4}]  {5}: {6}" -f $g.systemuserid, $g.fullname, $g.domainname, $value,
        $(if ($g.isdisabled) { ', disabled' } else { '' }), $r.class, $r.action)
}
$review = @($plan.rows | Where-Object { $_.action -like 'review*' })
Write-Host ("Guests: {0}; to set Yes: {1}; for review: {2}" -f $plan.rows.Count, $plan.toSet.Count, $review.Count)
$noCustomerMatch = $Model1 -and @($plan.rows | Where-Object { $_.class -eq 'customer' }).Count -eq 0 -and
    @($plan.rows | Where-Object { $_.class -eq 'other' }).Count -gt 0
if ($noCustomerMatch) {
    Write-Host ('WARNING: no guest belongs to the listed customer directories, yet some belong to other directories. ' +
        'Check -CustomerTenantIds; -Apply is refused in this state.') -ForegroundColor Yellow
}

$report = [ordered]@{
    environment       = $EnvironmentUrl
    organization      = $org
    mode              = $(if ($Verify) { 'verify' } elseif ($IsDryRun) { 'dry-run' } else { 'apply' })
    model1            = $Model1
    # Where-Object first: piping $null into ForEach-Object runs it once and would report [""].
    customerTenantIds = @($CustomerTenantIds | Where-Object { $_ } | ForEach-Object { "$_" })
    internalUserIds   = @($InternalUserIds | Where-Object { $_ } | ForEach-Object { "$_" })
    noCustomerMatch   = [bool]$noCustomerMatch
    startedUtc        = (Get-Date).ToUniversalTime().ToString('o')
    column            = $Column
    guests            = @($plan.rows | ForEach-Object {
            [ordered]@{ systemuserid = $_.guest.systemuserid; fullname = $_.guest.fullname; domainname = $_.guest.domainname
                email = $_.guest.internalemailaddress; value = $_.guest.$Column; isdisabled = $_.guest.isdisabled
                class = $_.class; tenantId = $_.tenantId; reason = $_.reason; action = $_.action }
        })
}

# --- Apply ------------------------------------------------------------------------------------------------------------
$failures = [System.Collections.Generic.List[string]]::new()
if ($Apply -and $noCustomerMatch -and $plan.toSet.Count -gt 0) {   # nothing to write = nothing to protect (idempotent re-run)
    $failures.Add('refused: no guest belongs to the listed customer directories (check -CustomerTenantIds); nothing was written')
}
elseif ($Apply -and $plan.toSet.Count -gt 0) {
    $body = @{ $Column = $true } | ConvertTo-Json -Compress
    foreach ($g in $plan.toSet) {
        # The row's ETag, never '*': still never an upsert (a wrong id is a 404), and a change an administrator made
        # after the list was read fails with 412 instead of being overwritten.
        $etag = $g.'@odata.etag'
        if (-not $etag) { $failures.Add("could not set $($g.systemuserid) ($($g.domainname)): no ETag on the listed row"); continue }
        $h = $headers.Clone(); $h['If-Match'] = $etag
        try {
            Invoke-RestMethod -Uri "$Api/systemusers($($g.systemuserid))" -Headers $h -Method Patch -Body $body | Out-Null
        }
        catch {
            $failures.Add("could not set $($g.systemuserid) ($($g.domainname)): $($_.Exception.Message)")
        }
    }
    Write-Host ("Set {0} of {1} guest(s) to Yes." -f ($plan.toSet.Count - $failures.Count), $plan.toSet.Count) -ForegroundColor Green
}

$afterPlan = if ($Apply) { Get-Plan } else { $plan }
$after = $afterPlan.toSet.Count
$afterReview = @($afterPlan.rows | Where-Object { $_.action -like 'review*' }).Count
$report.after = $after
$report.reviewAfter = $afterReview
$report.failures = @($failures)
$report.finishedUtc = (Get-Date).ToUniversalTime().ToString('o')
$report | ConvertTo-Json -Depth 6 | Set-Content -Path $ReportPath -Encoding utf8
Write-Host "Report: $ReportPath"

if ($Verify -or $Apply) {
    if ($after -eq 0 -and $afterReview -eq 0 -and $failures.Count -eq 0) {
        Write-Host 'PASS: every guest that is not the customer''s staff has sprk_isexternal = Yes, and none is left for review.' -ForegroundColor Green
        exit 0
    }
    Write-Host ("FAIL: {0} guest(s) still to set; {1} for review (see each row's action)." -f $after, $afterReview) -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host 'DRY RUN complete - nothing was written. Review the list, then re-run with -Apply.' -ForegroundColor Cyan
