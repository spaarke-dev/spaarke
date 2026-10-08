#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Task 101 (unified-access-control-r2, FR-33): creates the two estate-wide "external shares by expiration" public views
    on sprk_externalrecordaccess. DRY RUN by default.

.DESCRIPTION
    THE TWO VIEWS (owner 2026-09-10; design notes/decisions/external-grant-expiry-mandatory.md section 10.3)
      1. "Active External Shares by Expiration" - every row with statecode = 0, soonest-expiring first.
      2. "External Shares Expiring in 30 Days"   - the same rows, narrowed to an expiry ON OR BEFORE today + 30.
    Both carry: the shared record (project / matter / work assignment - the three roots the access evaluator reads),
    the contact, the organization, the access level, the expiry, Granted By and Created By.

    View 2's filter is RELATIVE, so it never goes stale. FetchXML has no relative "on or before", so it is the union of
    three relative operators, whose boundaries were measured on spaarkedev1 on 2026-10-07 (task 101 notes section 3):
        olderthan-x-days 1   ->  expiry <  today - 1
        last-x-days 1        ->  today - 1 <= expiry <= today
        next-x-days 30       ->  today <  expiry <= today + 30      (next-x-days EXCLUDES today)
    Together: every dated expiry on or before today + 30, with no gap. Undated rows match none of them.

    MODES
      (default)  Dry run. Checks every column against live metadata, runs both queries live and checks their rows, and
                 prints what -Apply would create or change. Zero writes.
      -Apply     Creates each missing view in the solution (or rewrites a same-named view whose query or columns differ,
                 after saving its current definition to scripts/logs/), publishes the table, then runs -Verify.
      -Verify    Read-only. Exit 0 when both views exist once each with the right query and columns AND their saved
                 queries return the right rows; exit 1 naming each gap.

    The comparison is SEMANTIC (filter conditions, sort, columns), not textual, so a view built by hand in the maker
    portal (task 101 notes section 6) is verified the same way.

    OPERATOR-RUN ONLY (binding directive 2026-09-04, FAILURE-MODES AP-13). Run order:
      1. pwsh -File scripts/Deploy-ExternalShareExpiryViews.ps1            (dry run)
      2. pwsh -File scripts/Deploy-ExternalShareExpiryViews.ps1 -Apply
      3. pwsh -File scripts/Deploy-ExternalShareExpiryViews.ps1 -Verify

.PARAMETER EnvironmentUrl
    Dataverse environment URL.

.PARAMETER SolutionUniqueName
    The unmanaged solution the views are created in. sprk_externalrecordaccess lives in SpaarkeCore.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code.
#>

[CmdletBinding()]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$Apply,
    [switch]$Verify,
    [string]$SnapshotDir = (Join-Path $PSScriptRoot 'logs')
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; -Apply already verifies after writing.' }

$Table = 'sprk_externalrecordaccess'
$EntitySet = 'sprk_externalrecordaccesses'
$ExpiryColumn = 'sprk_expiresdate'
$WindowDays = 30

# The columns, in display order. Every name is checked against live metadata before anything else runs.
$Columns = [ordered]@{
    'sprk_project'        = 160
    'sprk_matter'         = 160
    'sprk_workassignment' = 160
    'sprk_contact'        = 170
    'sprk_organization'   = 170
    'sprk_accesslevel'    = 110
    'sprk_expiresdate'    = 110
    'sprk_grantedby'      = 150
    'createdby'           = 150
}
$ActiveCondition = '<condition attribute="statecode" operator="eq" value="0" />'
$WindowFilter = '<filter type="or">' +
    "<condition attribute=""$ExpiryColumn"" operator=""olderthan-x-days"" value=""1"" />" +
    "<condition attribute=""$ExpiryColumn"" operator=""last-x-days"" value=""1"" />" +
    "<condition attribute=""$ExpiryColumn"" operator=""next-x-days"" value=""$WindowDays"" />" +
    '</filter>'

$Views = @(
    [pscustomobject]@{
        Key         = 'all'
        Name        = 'Active External Shares by Expiration'
        Description = 'Every active external share (statecode Active), soonest expiry first. Rows at the top with a past or empty expiry confer no access (FR-33, task 107): renew or end them. Task 101.'
        Filter      = $ActiveCondition
    },
    [pscustomobject]@{
        Key         = 'window'
        Name        = "External Shares Expiring in $WindowDays Days"
        Description = "Active external shares whose expiry is on or before today + $WindowDays (relative, never stale), soonest first. Includes shares already past their expiry that are still Active. Task 101."
        Filter      = $ActiveCondition + $WindowFilter
    }
)

# ---------------------------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------------------------

$token = az account get-access-token --resource $BaseUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $BaseUrl. Run 'az login' and retry." }

function Invoke-Dv {
    param([string]$Endpoint, [string]$Method = 'GET', [object]$Body = $null, [hashtable]$ExtraHeaders = @{})
    $headers = @{
        Authorization      = "Bearer $token"
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        Accept             = 'application/json'
        'Content-Type'     = 'application/json; charset=utf-8'
        Prefer             = 'odata.maxpagesize=5000'
    }
    foreach ($k in $ExtraHeaders.Keys) { $headers[$k] = $ExtraHeaders[$k] }
    $uri = if ($Endpoint -like 'https://*') { $Endpoint } else { "$BaseUrl/api/data/v9.2/$Endpoint" }
    $params = @{ Uri = $uri; Method = $Method; Headers = $headers }
    if ($null -ne $Body) { $params.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 10)) }
    try { return Invoke-RestMethod @params }
    catch {
        $detail = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $err = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($err.error.message) { $detail = $err.error.message }
        }
        throw "API error ($Method $Endpoint): $detail"
    }
}

# Every page of a query (a view can exceed one page).
function Get-AllRows([string]$Endpoint) {
    $rows = [Collections.Generic.List[object]]::new()
    $next = $Endpoint
    while ($next) {
        $page = Invoke-Dv $next
        foreach ($r in @($page.value)) { $rows.Add($r) }
        $next = $page.'@odata.nextLink'
    }
    return , $rows
}

function Get-FetchXml([string]$Filter) {
    $attrs = (@('sprk_externalrecordaccessid') + @($Columns.Keys) | ForEach-Object { "<attribute name=""$_"" />" }) -join ''
    return '<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">' +
        "<entity name=""$Table"">$attrs<order attribute=""$ExpiryColumn"" descending=""false"" />" +
        "<filter type=""and"">$Filter</filter></entity></fetch>"
}

function Get-LayoutXml([int]$ObjectTypeCode, [string]$PrimaryName) {
    $cells = ($Columns.Keys | ForEach-Object { "<cell name=""$_"" width=""$($Columns[$_])"" />" }) -join ''
    return "<grid name=""resultset"" object=""$ObjectTypeCode"" jump=""$PrimaryName"" select=""1"" icon=""1"" preview=""1"">" +
        "<row name=""result"" id=""sprk_externalrecordaccessid"">$cells</row></grid>"
}

# The meaning of a view: entity, every condition with the type of the filter it sits in, the sort, the columns it
# needs. Attribute order, whitespace, the savedqueryid the server stamps into fetchxml, and any EXTRA fetch attribute
# (the maker portal adds the primary name) do not change it; a MISSING column does.
function Get-ViewSignature([string]$FetchXml, [string]$LayoutXml) {
    $f = [xml]$FetchXml
    $entity = $f.SelectSingleNode('/fetch/entity')
    $conditions = @($entity.SelectNodes('.//condition') | ForEach-Object {
            $type = $_.ParentNode.GetAttribute('type'); if (-not $type) { $type = 'and' }   # FetchXML's default
            '{0}:{1}|{2}|{3}' -f $type, $_.GetAttribute('attribute'), $_.GetAttribute('operator'), $_.GetAttribute('value')
        } | Sort-Object)
    $orders = @($entity.SelectNodes('order') | ForEach-Object {
            $desc = $_.GetAttribute('descending'); if (-not $desc) { $desc = 'false' }        # FetchXML's default
            '{0}|{1}' -f $_.GetAttribute('attribute'), $desc
        })
    $needed = @('sprk_externalrecordaccessid') + @($Columns.Keys)
    $attrs = @($entity.SelectNodes('attribute') | ForEach-Object { $_.GetAttribute('name') } | Where-Object { $needed -contains $_ } | Sort-Object -Unique)
    $cells = @(([xml]$LayoutXml).SelectNodes('//cell') | ForEach-Object { $_.GetAttribute('name') })
    return "entity=$($entity.GetAttribute('name')); conditions=$($conditions -join ','); order=$($orders -join ','); " +
        "attributes=$($attrs -join ','); cells=$($cells -join ',')"
}

# Rows a view returns must be active, sorted by expiry ascending (empty first, as Dataverse sorts NULL), and - for the
# window view - dated on or before today + 30. One day of slack each side: the relative operators use the VIEWING
# user's time zone, this machine may use another.
# The expiry as a calendar day. The Web API sends a date-only value as midnight UTC ("2026-12-10T00:00:00Z"); taking
# the UTC date keeps it that day whether PowerShell hands it over as a UTC DateTime or as a string.
function Get-ExpiryDay([object]$Row) {
    $v = $Row.$ExpiryColumn
    if ($null -eq $v -or "$v" -eq '') { return $null }
    return ([datetime]$v).ToUniversalTime().Date
}

function Test-ViewRows([string]$Key, [object[]]$Rows, [object[]]$ActiveRows) {
    $gaps = [Collections.Generic.List[string]]::new()
    $activeIds = @{}
    foreach ($a in $ActiveRows) { $activeIds[$a.sprk_externalrecordaccessid] = $a }
    $dates = @($Rows | ForEach-Object { $d = Get-ExpiryDay $_; if ($d) { $d } else { [datetime]::MinValue } })
    for ($i = 1; $i -lt $dates.Count; $i++) {
        if ($dates[$i] -lt $dates[$i - 1]) { $gaps.Add("row $i is not in expiry order ($($dates[$i - 1].ToString('yyyy-MM-dd')) then $($dates[$i].ToString('yyyy-MM-dd')))"); break }
    }
    $notActive = @($Rows | Where-Object { -not $activeIds.ContainsKey($_.sprk_externalrecordaccessid) })
    if ($notActive.Count) { $gaps.Add("$($notActive.Count) row(s) are not Active") }
    $today = (Get-Date).Date
    if ($Key -eq 'all') {
        if ($Rows.Count -ne $ActiveRows.Count) { $gaps.Add("returns $($Rows.Count) rows; $($ActiveRows.Count) rows are Active") }
    } else {
        $late = @($Rows | Where-Object { $d = Get-ExpiryDay $_; -not $d -or $d -gt $today.AddDays($WindowDays + 1) })
        if ($late.Count) { $gaps.Add("$($late.Count) row(s) are undated or expire after today + $WindowDays") }
        $ids = @{}
        foreach ($r in $Rows) { $ids[$r.sprk_externalrecordaccessid] = $true }
        $missed = @($ActiveRows | Where-Object { $d = Get-ExpiryDay $_; $d -and $d -le $today.AddDays($WindowDays - 1) -and -not $ids.ContainsKey($_.sprk_externalrecordaccessid) })
        if ($missed.Count) { $gaps.Add("$($missed.Count) Active row(s) dated on or before today + $($WindowDays - 1) are missing") }
    }
    return , $gaps
}

function Write-Gap([string]$Text) { Write-Host "  GAP  $Text" -ForegroundColor Red; $script:gapCount++ }

# ---------------------------------------------------------------------------------------------------------------------
# Metadata: every name the views use must exist on the table, and the expiry must be a date-only column
# ---------------------------------------------------------------------------------------------------------------------

$orgName = (Invoke-Dv 'organizations?$select=name').value[0].name
Write-Host "Environment : $BaseUrl (org '$orgName')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($Apply) { "APPLY (solution $SolutionUniqueName)" } else { 'DRY RUN (no writes)' }))

$meta = Invoke-Dv "EntityDefinitions(LogicalName='$Table')?`$select=ObjectTypeCode,PrimaryNameAttribute,PrimaryIdAttribute"
$liveAttrs = @{}
foreach ($a in (Invoke-Dv "EntityDefinitions(LogicalName='$Table')/Attributes?`$select=LogicalName,AttributeType").value) { $liveAttrs[$a.LogicalName] = $a.AttributeType }
$missingAttrs = @(@($Columns.Keys) + @('statecode', $meta.PrimaryIdAttribute) | Where-Object { -not $liveAttrs.ContainsKey($_) })
if ($missingAttrs.Count) { throw "Not on $Table in this environment: $($missingAttrs -join ', '). Nothing was written." }
$expiryMeta = Invoke-Dv "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$ExpiryColumn')/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata?`$select=Format"
if ($expiryMeta.Format -ne 'DateOnly') { throw "$ExpiryColumn has format '$($expiryMeta.Format)', expected DateOnly; the window boundaries were measured on a date-only column. Nothing was written." }
Write-Host "Metadata    : $($Columns.Count) columns + statecode present; $ExpiryColumn is DateOnly; object type code $($meta.ObjectTypeCode)"

$layout = Get-LayoutXml $meta.ObjectTypeCode $meta.PrimaryNameAttribute
$activeRows = Get-AllRows "$EntitySet`?`$select=sprk_externalrecordaccessid,$ExpiryColumn&`$filter=statecode eq 0"
Write-Host "Active rows : $($activeRows.Count) (undated: $(@($activeRows | Where-Object { -not $_.$ExpiryColumn }).Count))"

# ---------------------------------------------------------------------------------------------------------------------
# Plan
# ---------------------------------------------------------------------------------------------------------------------

$script:gapCount = 0
$plan = @()
foreach ($v in $Views) {
    Write-Host "`n[$($v.Name)]"
    $fetch = Get-FetchXml $v.Filter
    $want = Get-ViewSignature $fetch $layout
    $escaped = $v.Name -replace "'", "''"
    $existing = @((Invoke-Dv "savedqueries?`$select=savedqueryid,name,fetchxml,layoutxml,querytype&`$filter=returnedtypecode eq '$Table' and querytype eq 0 and name eq '$escaped'").value)

    if ($existing.Count -gt 1) {
        if ($Verify) { Write-Gap "$($existing.Count) public views carry this name"; continue }
        throw "$($existing.Count) public views on $Table are named '$($v.Name)'. Remove the duplicates by hand; nothing was written."
    }
    if ($existing.Count -eq 0) {
        if ($Verify) { Write-Gap 'view does not exist'; continue }
        $encoded = [uri]::EscapeDataString($fetch)
        $preview = Get-AllRows "$EntitySet`?fetchXml=$encoded"
        $rowGaps = Test-ViewRows $v.Key $preview $activeRows
        if ($rowGaps.Count) { throw "The query for '$($v.Name)' returns the wrong rows: $($rowGaps -join '; '). Nothing was written." }
        Write-Host "  PLAN create (query runs: $($preview.Count) rows, all checks pass)" -ForegroundColor Cyan
        $plan += [pscustomobject]@{ View = $v; Fetch = $fetch; Id = $null; Current = $null }
        continue
    }

    $current = $existing[0]
    $have = Get-ViewSignature $current.fetchxml $current.layoutxml
    if ($have -ne $want) {
        if ($Verify) { Write-Gap "definition differs`n         have: $have`n         want: $want"; continue }
        Write-Host "  PLAN rewrite $($current.savedqueryid)`n         have: $have`n         want: $want" -ForegroundColor Cyan
        $plan += [pscustomobject]@{ View = $v; Fetch = $fetch; Id = $current.savedqueryid; Current = $current }
        continue
    }
    $rows = Get-AllRows "$EntitySet`?savedQuery=$($current.savedqueryid)"
    $rowGaps = Test-ViewRows $v.Key $rows $activeRows
    if ($rowGaps.Count) { foreach ($g in $rowGaps) { Write-Gap $g } }
    else { Write-Host "  OK   $($current.savedqueryid): definition matches; saved query returns $($rows.Count) rows, all checks pass" -ForegroundColor Green }
}

if ($Verify) {
    if ($script:gapCount -eq 0) { Write-Host "`nVERIFY PASS: both views exist on $Table and return the right rows." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL: $($script:gapCount) gap(s) above." -ForegroundColor Red
    exit 1
}
if ($script:gapCount -gt 0) { Write-Host "`n$($script:gapCount) gap(s) above in a view whose definition already matches. Nothing was written." -ForegroundColor Red; exit 1 }
if ($plan.Count -eq 0) { Write-Host "`nNothing to change." -ForegroundColor Green; exit 0 }
if (-not $Apply) { Write-Host "`nDRY RUN: $($plan.Count) view(s) to write. Re-run with -Apply." -ForegroundColor Cyan; exit 0 }

# ---------------------------------------------------------------------------------------------------------------------
# Apply
# ---------------------------------------------------------------------------------------------------------------------

$solution = @((Invoke-Dv "solutions?`$select=solutionid,ismanaged&`$filter=uniquename eq '$SolutionUniqueName'").value)
if ($solution.Count -ne 1 -or $solution[0].ismanaged) { throw "Solution '$SolutionUniqueName' is missing or managed. Nothing was written." }

$toRewrite = @($plan | Where-Object Id)
if ($toRewrite.Count) {
    New-Item -ItemType Directory -Force -Path $SnapshotDir | Out-Null
    $snapPath = Join-Path $SnapshotDir ("external-share-expiry-views-{0:yyyyMMdd-HHmmss}.json" -f (Get-Date))
    $toRewrite | ForEach-Object { $_.Current | Select-Object savedqueryid, name, fetchxml, layoutxml } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $snapPath -Encoding UTF8
    Write-Host "Snapshot    : $snapPath"
}

foreach ($p in $plan) {
    $body = @{ name = $p.View.Name; description = $p.View.Description; fetchxml = $p.Fetch; layoutxml = $layout }
    if ($p.Id) {
        Invoke-Dv "savedqueries($($p.Id))" 'PATCH' $body | Out-Null
        Invoke-Dv 'AddSolutionComponent' 'POST' @{ ComponentId = $p.Id; ComponentType = 26; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false } | Out-Null
        Write-Host "  rewrote '$($p.View.Name)' ($($p.Id))" -ForegroundColor Green
    } else {
        $body.returnedtypecode = $Table
        $body.querytype = 0
        $body.isdefault = $false
        $body.isquickfindquery = $false
        $created = Invoke-Dv 'savedqueries' 'POST' $body @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName; Prefer = 'return=representation' }
        Write-Host "  created '$($p.View.Name)' ($($created.savedqueryid)) in $SolutionUniqueName" -ForegroundColor Green
    }
}
Invoke-Dv 'PublishXml' 'POST' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
Write-Host "Published $Table. Verifying..."

& $PSCommandPath -EnvironmentUrl $BaseUrl -Verify
exit $LASTEXITCODE
