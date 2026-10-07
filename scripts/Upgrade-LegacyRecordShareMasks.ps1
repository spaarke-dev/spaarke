<#
.SYNOPSIS
    Upgrades internal user shares written at the pre-2026-09-30 share levels to the current masks (task 139).
    DRY RUN BY DEFAULT — writes nothing unless -Apply is passed.

.DESCRIPTION
    unified-access-control-r2 task 139 (#1062, owner decision C4, 2026-09-30) put the Share right into the
    Collaborate and Full Access share levels, so a person with Write may also use the model-driven app's own
    Share command:

        level         before 2026-09-30            now
        ------------  ---------------------------  ----------------------------------
        View Only     1      (Read)                1      (unchanged)
        Collaborate   23     (Read,Write,Append,   262167 (… + Share 262144)
                              AppendTo)
        Full Access   65559  (Collaborate+Delete)  327703 (Collaborate + Delete)

    The BFF still READS 23 / 65559 as Collaborate / Full Access, so nothing breaks before this runs. But a
    colleague shared before the change holds no ShareAccess, so the model-driven Share command stays unavailable
    to exactly the people the owner said may share. This script upgrades those shares.

    WHAT IT TOUCHES (write scope, deliberately narrow):
      * principalobjectaccess rows on sprk_project, sprk_matter and sprk_workassignment,
      * whose principal is a SYSTEM USER, and
      * whose accessrightsmask is EXACTLY 23 or 65559.
    Each is changed with ModifyAccess (which REPLACES the rights) to 262167 or 327703, and READ BACK; the
    before/after mask is recorded per row.

    WHAT IT LISTS AND NEVER MODIFIES (task 139 escalation trigger 3 — an owner decision):
      * TEAM-principal shares at 23 / 65559 (recommended: upgrade them like user shares — a team shared at
        Collaborate is the same owner-decided level — but only once the owner says so);
      * every share whose mask is neither a current level (1, 262167, 327703) nor a legacy one (23, 65559)
        — for example a creator share that also carries Delete, or an Assign bit — treated as a hand-made share.
    Rows with mask 0 (Dataverse's inherited-access rows) are ignored.

    IDEMPOTENT: a second run finds nothing at 23 / 65559 and writes nothing.

    AUTHENTICATION: the operator's OWN identity via the Azure CLI (`az login`), exactly as
    Deploy-AccessEventEntity.ps1 does. No client secret is read, used or created (ADR-028 A4).
    The operator needs privileges to modify shares on the three tables (System Administrator in dev).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER Apply
    Perform the ModifyAccess writes. Without it the script is a READ-ONLY report (the default).

.PARAMETER ReportPath
    Where to write the JSON report (before/after counts, every row upgraded / listed). Default: a timestamped
    file in the current directory.

.EXAMPLE
    # 1. Always first: read-only report.
    .\scripts\Upgrade-LegacyRecordShareMasks.ps1

    # 2. After reviewing the report (and any escalation it raises):
    .\scripts\Upgrade-LegacyRecordShareMasks.ps1 -Apply

.NOTES
    Project : unified-access-control-r2
    Task    : 139 — grant model: share rights and the grantor cap (GitHub #1062)
    Created : 2026-10-02
    Exit codes: 0 = done (or nothing to do); 1 = a read failed; 2 = at least one upgraded share did not read back
                at its new mask; 3 = listed rows need an owner decision (team shares at a legacy mask, or non-level
                masks). Exit 3 is informational on a dry run and does not mean anything was written.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [switch]$Apply,

    [Parameter(Mandatory = $false)]
    [string]$ReportPath = ("share-mask-upgrade-{0:yyyyMMdd-HHmmss}.json" -f (Get-Date))
)

$ErrorActionPreference = "Stop"

# Dataverse AccessRights values (Microsoft.Crm.Sdk.Proxy): Read 1, Write 2, Append 4, AppendTo 16, Delete 65536,
# Share 262144. The same numbers the BFF's RecordShareLevels uses — re-derived here, not copied from a constant.
$Read = 1; $Write = 2; $Append = 4; $AppendTo = 16; $Delete = 65536; $Share = 262144

$LegacyCollaborate = $Read -bor $Write -bor $Append -bor $AppendTo          # 23
$LegacyFullAccess  = $LegacyCollaborate -bor $Delete                       # 65559
$Collaborate       = $LegacyCollaborate -bor $Share                        # 262167
$FullAccess        = $Collaborate -bor $Delete                             # 327703
$CurrentLevelMasks = @($Read, $Collaborate, $FullAccess)

$Upgrades = @{
    $LegacyCollaborate = @{ Mask = $Collaborate; Csv = "ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess" }
    $LegacyFullAccess  = @{ Mask = $FullAccess;  Csv = "ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess,DeleteAccess" }
}

$Tables = @(
    @{ LogicalName = "sprk_project";        EntitySet = "sprk_projects" }
    @{ LogicalName = "sprk_matter";         EntitySet = "sprk_matters" }
    @{ LogicalName = "sprk_workassignment"; EntitySet = "sprk_workassignments" }
)

# ============================================================================
# Helpers
# ============================================================================

function Get-DataverseToken {
    param([string]$EnvironmentUrl)
    Write-Host "Getting a token for $EnvironmentUrl from the Azure CLI (your own identity)..." -ForegroundColor Cyan
    $tokenResult = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get a token from the Azure CLI: $tokenResult. Run 'az login' first."
    }
    return $tokenResult.Trim()
}

function Invoke-Dataverse {
    param([string]$Token, [string]$Uri, [string]$Method = "GET", [object]$Body = $null)

    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
        "Prefer"           = "odata.maxpagesize=5000"
    }
    $params = @{ Uri = $Uri; Method = $Method; Headers = $headers }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 10) }

    try {
        return Invoke-RestMethod @params
    }
    catch {
        $detail = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $json = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($json.error.message) { $detail = $json.error.message }
        }
        throw "Dataverse $Method $Uri failed: $detail"
    }
}

# Every non-zero POA row on one table, all pages. A read that fails throws — never a partial answer.
function Get-ShareRows {
    param([string]$Token, [string]$ApiBase, [string]$LogicalName)

    $uri = "$ApiBase/principalobjectaccessset?`$filter=objecttypecode eq '$LogicalName' and accessrightsmask ne 0" +
           "&`$select=objectid,principalid,principaltypecode,accessrightsmask"
    $rows = @()
    while ($uri) {
        $page = Invoke-Dataverse -Token $Token -Uri $uri
        $rows += $page.value
        $uri = $page.'@odata.nextLink'
    }
    return $rows
}

function Get-ShareMask {
    param([string]$Token, [string]$ApiBase, [string]$LogicalName, [string]$ObjectId, [string]$PrincipalId)

    $uri = "$ApiBase/principalobjectaccessset?`$filter=objectid eq $ObjectId and objecttypecode eq '$LogicalName'" +
           " and principalid eq $PrincipalId&`$select=accessrightsmask"
    $page = Invoke-Dataverse -Token $Token -Uri $uri
    $mask = 0
    foreach ($row in $page.value) { $mask = $mask -bor [int]$row.accessrightsmask }
    return $mask
}

function Get-Classified {
    param([object[]]$Rows, [string]$LogicalName)

    foreach ($row in $Rows) {
        $mask = [int]$row.accessrightsmask
        # principaltypecode is an EntityName column: the Web API returns the logical name ("systemuser" / "team" —
        # observed live on spaarkedev1, 2026-10-02), while SDK-shaped readers see the object type code (8 / 9). Both
        # spellings are accepted; anything else is never treated as a user.
        $rawType = [string]$row.principaltypecode
        $kind = switch ($rawType) { "8" { "systemuser" } "9" { "team" } default { $rawType.ToLowerInvariant() } }
        $class =
            if ($CurrentLevelMasks -contains $mask) { "current" }
            elseif ($Upgrades.ContainsKey($mask) -and $kind -eq "systemuser") { "upgrade" }
            elseif ($Upgrades.ContainsKey($mask)) { "legacy-other-principal" }
            else { "non-level" }

        [pscustomobject]@{
            Table         = $LogicalName
            RecordId      = [string]$row.objectid
            PrincipalId   = [string]$row.principalid
            PrincipalType = $kind
            Mask          = $mask
            Class         = $class
        }
    }
}

function Get-Counts {
    param([object[]]$Classified)
    $counts = [ordered]@{ current = 0; upgrade = 0; "legacy-other-principal" = 0; "non-level" = 0 }
    foreach ($r in $Classified) { $counts[$r.Class]++ }
    return $counts
}

# ============================================================================
# Run
# ============================================================================

$mode = if ($Apply) { "APPLY" } else { "DRY RUN (read-only)" }
Write-Host "Upgrade-LegacyRecordShareMasks — $mode — $EnvironmentUrl" -ForegroundColor Yellow

$token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
$apiBase = "$($EnvironmentUrl.TrimEnd('/'))/api/data/v9.2"

$before = @()
try {
    foreach ($t in $Tables) {
        $rows = Get-ShareRows -Token $token -ApiBase $apiBase -LogicalName $t.LogicalName
        $before += @(Get-Classified -Rows $rows -LogicalName $t.LogicalName)
    }
}
catch {
    Write-Host "READ FAILED — nothing was written. $_" -ForegroundColor Red
    exit 1
}

$beforeCounts = Get-Counts -Classified $before
Write-Host ""
Write-Host "Before:" -ForegroundColor Cyan
$beforeCounts.GetEnumerator() | ForEach-Object { Write-Host ("  {0,-24} {1}" -f $_.Key, $_.Value) }

$toUpgrade = @($before | Where-Object Class -eq "upgrade")
$needsOwner = @($before | Where-Object { $_.Class -in @("legacy-other-principal", "non-level") })

if ($needsOwner.Count -gt 0) {
    Write-Host ""
    Write-Host "LISTED, NOT MODIFIED — owner decision needed (task 139 escalation 3):" -ForegroundColor Magenta
    $needsOwner | Sort-Object Table, RecordId | Format-Table Table, RecordId, PrincipalType, PrincipalId, Mask, Class -AutoSize | Out-String | Write-Host
}

Write-Host ""
Write-Host ("System-user shares to upgrade: {0}" -f $toUpgrade.Count) -ForegroundColor Cyan
$toUpgrade | Sort-Object Table, RecordId | Format-Table Table, RecordId, PrincipalId, Mask -AutoSize | Out-String | Write-Host

$results = @()
$verifyFailures = 0

if ($Apply -and $toUpgrade.Count -gt 0) {
    $entitySetOf = @{}
    foreach ($t in $Tables) { $entitySetOf[$t.LogicalName] = $t.EntitySet }

    foreach ($row in $toUpgrade) {
        $target = $Upgrades[$row.Mask]
        $body = @{
            Target          = @{ "@odata.id" = "$($entitySetOf[$row.Table])($($row.RecordId))" }
            PrincipalAccess = @{
                Principal  = @{ "@odata.id" = "systemusers($($row.PrincipalId))" }
                AccessMask = $target.Csv
            }
        }

        $stored = $null
        $outcome = "verified"
        try {
            Invoke-Dataverse -Token $token -Uri "$apiBase/ModifyAccess" -Method "POST" -Body $body | Out-Null
            $stored = Get-ShareMask -Token $token -ApiBase $apiBase -LogicalName $row.Table -ObjectId $row.RecordId -PrincipalId $row.PrincipalId
            if ($stored -ne $target.Mask) { $outcome = "not-confirmed"; $verifyFailures++ }
        }
        catch {
            $outcome = "failed: $_"
            $verifyFailures++
        }

        $results += [pscustomobject]@{
            Table = $row.Table; RecordId = $row.RecordId; PrincipalId = $row.PrincipalId
            Before = $row.Mask; Requested = $target.Mask; ReadBack = $stored; Outcome = $outcome
        }
        $color = if ($outcome -eq "verified") { "Green" } else { "Red" }
        Write-Host ("  {0} {1} user {2}: {3} -> {4} (read back {5}) {6}" -f $row.Table, $row.RecordId, $row.PrincipalId, $row.Mask, $target.Mask, $stored, $outcome) -ForegroundColor $color
    }
}

# After-counts: re-read everything (on a dry run this equals "before").
$after = @()
try {
    foreach ($t in $Tables) {
        $rows = Get-ShareRows -Token $token -ApiBase $apiBase -LogicalName $t.LogicalName
        $after += @(Get-Classified -Rows $rows -LogicalName $t.LogicalName)
    }
}
catch {
    Write-Host "The after-read failed: $_" -ForegroundColor Red
    exit 1
}
$afterCounts = Get-Counts -Classified $after

Write-Host ""
Write-Host "After:" -ForegroundColor Cyan
$afterCounts.GetEnumerator() | ForEach-Object { Write-Host ("  {0,-24} {1}" -f $_.Key, $_.Value) }

$report = [ordered]@{
    task           = "unified-access-control-r2 task 139"
    environment    = $EnvironmentUrl
    mode           = $mode
    ranAtUtc       = (Get-Date).ToUniversalTime().ToString("o")
    beforeCounts   = $beforeCounts
    afterCounts    = $afterCounts
    upgraded       = $results
    toUpgrade      = $toUpgrade
    needsOwnerDecision = $needsOwner
}
$report | ConvertTo-Json -Depth 6 | Set-Content -Path $ReportPath -Encoding utf8
Write-Host ""
Write-Host "Report: $ReportPath" -ForegroundColor Cyan

if ($verifyFailures -gt 0) {
    Write-Host "$verifyFailures upgrade(s) did not read back at the new mask." -ForegroundColor Red
    exit 2
}
if ($needsOwner.Count -gt 0) {
    Write-Host "$($needsOwner.Count) share(s) listed for an owner decision; none were modified." -ForegroundColor Magenta
    exit 3
}
exit 0
