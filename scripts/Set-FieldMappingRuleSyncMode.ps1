#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Migrates every existing Copy field-mapping rule to sync mode Manual Refresh (sprk_syncmode = 1) — the data step of
    field-mapping-server-write-path-r1 task 005 (FR-05d, decision Q3). Dry-run by default; idempotent; forward-only.

.DESCRIPTION
    /push (task 040) selects the rules it re-applies by sprk_syncmode = Manual Refresh, and only Copy rules (decision Q3).
    New rules default to One-time (0), so every Copy rule authored before this change must be moved to Manual Refresh once.
    This script does exactly that and nothing else.

    WHICH ROWS (the only rows it ever writes):
      sprk_fieldmappingrule rows with sprk_mapping_type = 0 (Copy), ACTIVE (statecode 0), whose sprk_syncmode is NOT 1.
      "Not 1" includes an EMPTY sprk_syncmode: rules created before the column was populated read back null, and null is
      not Manual Refresh (measured on spaarkedev1, 2026-10-21: 20 of 25 Copy rules were null).
    NEVER written: Default (1), Concat (2) and Template (3) rules; any rule already at 1; any other column; no row is
    deleted or created. The PATCH body is exactly { "sprk_syncmode": 1 }, with If-Match '*' so it can only update.

    INACTIVE Copy rules not at 1 are listed (InactiveNotMigrated) but never touched: Dataverse refuses to update an inactive
    row, and reactivating one is a decision for the rule's owner. They fail -Verify until reactivated and re-run.

    REFUSES TO GUESS: a rule whose mapping type is outside 0-3, or whose sprk_syncmode is outside {empty, 0, 1}, stops the
    run before any write (exit 2) so the mapping can be decided by a person (task 005 escalation trigger).

    SAFETY MODEL
      - Dry-run is the DEFAULT: with no switch, nothing is written. -WhatIf forces a dry-run even with -Apply.
      - -Apply writes, then READS BACK every row and checks sprk_syncmode = 1 (Dataverse ignores an unrecognised property;
        "the request succeeded" is not evidence of the write).
      - Idempotent: a migrated rule is at 1, so a second -Apply finds nothing to change (ToMigrate: 0).
      - -Verify is read-only: exits 0 only when EVERY Copy rule (active or not) is at Manual Refresh, else exits 1.
      - A live -Apply needs the owner's explicit OK for the environment (project CLAUDE.md section 5). The token is the
        operator's own az CLI identity; no secret is stored in this script.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com. Required except with -SelfTest.

.PARAMETER Apply
    Write mode. Without it the script always dry-runs.

.PARAMETER WhatIf
    Forces a dry-run even when -Apply is passed.

.PARAMETER Verify
    Read-only report; exits non-zero while any Copy rule is not at Manual Refresh. With -Apply, runs after the writes.

.PARAMETER SelfTest
    Offline: runs the pure row classifier and an in-memory apply twice over inline cases; exits 1 on any mismatch.

.EXAMPLE
    .\Set-FieldMappingRuleSyncMode.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: lists every Copy rule that would move to Manual Refresh, with its current mode, and the totals. Zero writes.

.EXAMPLE
    .\Set-FieldMappingRuleSyncMode.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply -Verify
    Migrate (owner OK required), read each row back, then verify every Copy rule is Manual Refresh.

.EXAMPLE
    .\Set-FieldMappingRuleSyncMode.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify
    Read-only baseline: exit 1 while any Copy rule is not at Manual Refresh.

.EXAMPLE
    .\Set-FieldMappingRuleSyncMode.ps1 -SelfTest
    Offline checks of the classifier and idempotency; needs no environment.
#>

[CmdletBinding()]
param(
    [string]$EnvironmentUrl,

    [switch]$Apply,

    [switch]$WhatIf,

    [switch]$Verify,

    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

# sprk_mapping_type option values (Dataverse describe, spaarkedev1 2026-10-21): Copy 0, Default 1, Concat 2, Template 3.
$MappingCopy = 0
$KnownMappingTypes = @(0, 1, 2, 3)
# sprk_syncmode option values: One-time 0, Manual Refresh 1; an empty value is a rule created before the column was set.
$SyncOneTime = 0
$SyncManualRefresh = 1

# PURE: what to do with one rule row. Outcome is one of:
#   Migrate               a Copy rule, active, sprk_syncmode empty or 0  -> PATCH to 1
#   Aligned               a Copy rule already at 1
#   InactiveNotMigrated   a Copy rule, inactive, not at 1 -> reported, never written (Dataverse refuses the update)
#   NotCopy               Default / Concat / Template -> never touched
#   Unknown               mapping type outside 0-3 or sync mode outside {empty, 0, 1} -> the run stops, nothing is written
# Current is the sync mode as a display string: 'empty', 'One-time' or 'Manual Refresh' (or the raw value when unknown).
function Get-RuleDecision {
    param($Row)
    $type = $Row.sprk_mapping_type
    $sync = $Row.sprk_syncmode
    $current = if ($null -eq $sync) { 'empty' } elseif ($sync -eq $SyncOneTime) { 'One-time' } elseif ($sync -eq $SyncManualRefresh) { 'Manual Refresh' } else { "unrecognised ($sync)" }
    if ($null -eq $type -or $KnownMappingTypes -notcontains [int]$type) {
        return @{ Outcome = 'Unknown'; Current = $current; Reason = "sprk_mapping_type '$type' is outside the known values 0-3" }
    }
    if ($null -ne $sync -and $sync -ne $SyncOneTime -and $sync -ne $SyncManualRefresh) {
        return @{ Outcome = 'Unknown'; Current = $current; Reason = "sprk_syncmode '$sync' is outside the known values (empty, 0, 1)" }
    }
    if ([int]$type -ne $MappingCopy) { return @{ Outcome = 'NotCopy'; Current = $current; Reason = $null } }
    if ($sync -eq $SyncManualRefresh) { return @{ Outcome = 'Aligned'; Current = $current; Reason = $null } }
    $active = ($null -eq $Row.statecode) -or ([int]$Row.statecode -eq 0)
    if (-not $active) { return @{ Outcome = 'InactiveNotMigrated'; Current = $current; Reason = 'the rule is inactive; Dataverse refuses to update an inactive row' } }
    return @{ Outcome = 'Migrate'; Current = $current; Reason = $null }
}

# PURE: classify every row and total them. Returns @{ Items; Counts; Unknown }.
function Get-MigrationPlan {
    param([object[]]$Rows)
    $counts = [ordered]@{ Migrate = 0; Aligned = 0; InactiveNotMigrated = 0; NotCopy = 0; Unknown = 0 }
    $byCurrent = [ordered]@{ 'empty' = 0; 'One-time' = 0 }
    $items = [System.Collections.Generic.List[object]]::new()
    foreach ($row in @($Rows)) {
        $d = Get-RuleDecision $row
        $counts[$d.Outcome]++
        if ($d.Outcome -eq 'Migrate') { $byCurrent[$d.Current]++ }
        $items.Add(@{ Row = $row; Decision = $d })
    }
    return @{ Items = $items.ToArray(); Counts = $counts; ByCurrent = $byCurrent; Unknown = @($items | Where-Object { $_.Decision.Outcome -eq 'Unknown' }) }
}

# PURE: -Verify passes only when no Copy rule, active or not, is short of Manual Refresh.
function Test-VerifyPass {
    param([hashtable]$Plan)
    return ($Plan.Counts.Migrate -eq 0) -and ($Plan.Counts.InactiveNotMigrated -eq 0) -and ($Plan.Counts.Unknown -eq 0)
}

# ══ SELF-TEST (offline) ══════════════════════════════════════════════════════════════════════════════════════
if ($SelfTest) {
    $failures = [System.Collections.Generic.List[string]]::new()
    function Assert-Case { param([string]$Name, $Actual, $Expected) if ($Actual -ne $Expected) { $failures.Add("$Name : expected '$Expected', got '$Actual'") } else { Write-Host "  ok   $Name" } }

    $cases = @(
        @{ N = 'Copy, active, empty -> Migrate'; R = @{ sprk_mapping_type = 0; sprk_syncmode = $null; statecode = 0 }; O = 'Migrate' }
        @{ N = 'Copy, active, One-time -> Migrate'; R = @{ sprk_mapping_type = 0; sprk_syncmode = 0; statecode = 0 }; O = 'Migrate' }
        @{ N = 'Copy, active, Manual Refresh -> Aligned'; R = @{ sprk_mapping_type = 0; sprk_syncmode = 1; statecode = 0 }; O = 'Aligned' }
        @{ N = 'Copy, inactive, empty -> InactiveNotMigrated'; R = @{ sprk_mapping_type = 0; sprk_syncmode = $null; statecode = 1 }; O = 'InactiveNotMigrated' }
        @{ N = 'Copy, inactive, Manual Refresh -> Aligned'; R = @{ sprk_mapping_type = 0; sprk_syncmode = 1; statecode = 1 }; O = 'Aligned' }
        @{ N = 'Default, empty -> NotCopy (never written)'; R = @{ sprk_mapping_type = 1; sprk_syncmode = $null; statecode = 0 }; O = 'NotCopy' }
        @{ N = 'Concat, One-time -> NotCopy (never written)'; R = @{ sprk_mapping_type = 2; sprk_syncmode = 0; statecode = 0 }; O = 'NotCopy' }
        @{ N = 'Template, empty -> NotCopy (never written)'; R = @{ sprk_mapping_type = 3; sprk_syncmode = $null; statecode = 0 }; O = 'NotCopy' }
        @{ N = 'unknown mapping type 9 -> Unknown'; R = @{ sprk_mapping_type = 9; sprk_syncmode = 0; statecode = 0 }; O = 'Unknown' }
        @{ N = 'empty mapping type -> Unknown'; R = @{ sprk_mapping_type = $null; sprk_syncmode = 0; statecode = 0 }; O = 'Unknown' }
        @{ N = 'unknown sync mode 7 on a Copy rule -> Unknown'; R = @{ sprk_mapping_type = 0; sprk_syncmode = 7; statecode = 0 }; O = 'Unknown' }
        @{ N = 'unknown sync mode 7 on a Concat rule -> Unknown (not silently skipped)'; R = @{ sprk_mapping_type = 2; sprk_syncmode = 7; statecode = 0 }; O = 'Unknown' }
    )
    foreach ($c in $cases) { Assert-Case $c.N (Get-RuleDecision ([pscustomobject]$c.R)).Outcome $c.O }

    # An in-memory table, applied twice: the second pass must find nothing, and only Copy rows may change.
    $table = @(
        [pscustomobject]@{ id = 'a'; sprk_mapping_type = 0; sprk_syncmode = $null; statecode = 0 }
        [pscustomobject]@{ id = 'b'; sprk_mapping_type = 0; sprk_syncmode = 0; statecode = 0 }
        [pscustomobject]@{ id = 'c'; sprk_mapping_type = 0; sprk_syncmode = 1; statecode = 0 }
        [pscustomobject]@{ id = 'd'; sprk_mapping_type = 1; sprk_syncmode = $null; statecode = 0 }
        [pscustomobject]@{ id = 'e'; sprk_mapping_type = 2; sprk_syncmode = 0; statecode = 0 }
        [pscustomobject]@{ id = 'f'; sprk_mapping_type = 3; sprk_syncmode = $null; statecode = 0 }
        [pscustomobject]@{ id = 'g'; sprk_mapping_type = 0; sprk_syncmode = $null; statecode = 1 }
    )
    $before = @{}; foreach ($r in $table) { $before[$r.id] = "$($r.sprk_mapping_type)|$($r.sprk_syncmode)|$($r.statecode)" }
    for ($pass = 1; $pass -le 2; $pass++) {
        $plan = Get-MigrationPlan $table
        Assert-Case "pass $pass ToMigrate" $plan.Counts.Migrate $(if ($pass -eq 1) { 2 } else { 0 })
        foreach ($i in $plan.Items | Where-Object { $_.Decision.Outcome -eq 'Migrate' }) { $i.Row.sprk_syncmode = 1 }
    }
    $changed = @($table | Where-Object { "$($_.sprk_mapping_type)|$($_.sprk_syncmode)|$($_.statecode)" -ne $before[$_.id] } | ForEach-Object { $_.id }) -join ','
    Assert-Case 'only the active Copy rows below Manual Refresh changed' $changed 'a,b'
    Assert-Case 'Verify fails while an inactive Copy rule is short' (Test-VerifyPass (Get-MigrationPlan $table)) $false
    $table[6].statecode = 0; $table[6].sprk_syncmode = 1
    Assert-Case 'Verify passes when every Copy rule is at 1' (Test-VerifyPass (Get-MigrationPlan $table)) $true

    if ($failures.Count -gt 0) { $failures | ForEach-Object { Write-Host "  FAIL $_" -ForegroundColor Red }; Write-Host "SELF-TEST FAILED ($($failures.Count))" -ForegroundColor Red; exit 1 }
    Write-Host 'SELF-TEST PASSED'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($EnvironmentUrl)) { throw '-EnvironmentUrl is required (except with -SelfTest).' }
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$IsDryRun = (-not $Apply.IsPresent) -or $WhatIf.IsPresent

# ── Auth: the operator's own az CLI identity. No secrets in this script or the repo. ────────────────────────
function Get-DvToken {
    $t = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
    if (-not $t) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
    return $t
}
$script:Token = Get-DvToken
$script:TokenAt = Get-Date
function Get-DvHeaders {
    param([switch]$ForWrite)
    # az tokens last ~60-90 minutes; a long run re-acquires rather than failing half-way through.
    if (((Get-Date) - $script:TokenAt).TotalMinutes -gt 45) { $script:Token = Get-DvToken; $script:TokenAt = Get-Date }
    $h = @{
        Authorization      = "Bearer $script:Token"
        Accept             = 'application/json'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
    }
    if ($ForWrite) {
        $h['Content-Type'] = 'application/json'
        $h['If-Match'] = '*'            # update only; never an upsert that could mint a row
        $h['Prefer'] = 'return=minimal'
    }
    return $h
}

function Invoke-DvGet {
    param([Parameter(Mandatory)][string]$RelativePath)
    $uri = "$EnvironmentUrl/api/data/v9.2/$RelativePath"
    $all = [System.Collections.Generic.List[object]]::new()
    while ($uri) {
        $resp = Invoke-RestMethod -Uri $uri -Headers (Get-DvHeaders) -Method Get
        if ($null -ne $resp.value) { $all.AddRange(@($resp.value)) } else { return , @($resp) }
        $uri = $resp.'@odata.nextLink'
    }
    return , $all.ToArray()
}

# Every rule in every state: the statecode column is what separates an updatable rule from an inactive one.
function Read-Rules {
    return Invoke-DvGet 'sprk_fieldmappingrules?$select=sprk_fieldmappingruleid,sprk_name,_sprk_fieldmappingprofile_value,sprk_mapping_type,sprk_syncmode,statecode&$orderby=sprk_name asc'
}

function Write-Plan {
    param([hashtable]$Plan)
    foreach ($i in $Plan.Items | Where-Object { $_.Decision.Outcome -in 'Migrate', 'InactiveNotMigrated', 'Unknown' }) {
        $r = $i.Row
        Write-Host ("  {0,-20} {1}  [{2}]  current={3}  profile={4}  {5}" -f $i.Decision.Outcome, $r.sprk_fieldmappingruleid, $r.sprk_name, $i.Decision.Current, $r._sprk_fieldmappingprofile_value, $i.Decision.Reason)
    }
    Write-Host ("Totals: {0} rule(s) read. ToMigrate: {1} (empty: {2}, One-time: {3}); Aligned: {4}; InactiveNotMigrated: {5}; NotCopy (never touched): {6}; Unknown: {7}" -f `
            $Plan.Items.Count, $Plan.Counts.Migrate, $Plan.ByCurrent['empty'], $Plan.ByCurrent['One-time'], $Plan.Counts.Aligned, $Plan.Counts.InactiveNotMigrated, $Plan.Counts.NotCopy, $Plan.Counts.Unknown)
}

Write-Host "Set-FieldMappingRuleSyncMode on $EnvironmentUrl ($(if ($IsDryRun) { 'DRY RUN - nothing is written' } else { 'APPLY' }))"
$plan = Get-MigrationPlan (Read-Rules)
Write-Plan $plan

# Escalation: an unrecognised value is never guessed at, and the run stops before any write.
if ($plan.Counts.Unknown -gt 0) {
    Write-Host "STOP: $($plan.Counts.Unknown) rule(s) carry a mapping type or sync mode outside the known option values. Nothing was written; a person must decide the mapping." -ForegroundColor Red
    exit 2
}

if (-not $IsDryRun) {
    $written = 0; $failed = 0
    foreach ($i in $plan.Items | Where-Object { $_.Decision.Outcome -eq 'Migrate' }) {
        $id = $i.Row.sprk_fieldmappingruleid
        try {
            $body = @{ sprk_syncmode = $SyncManualRefresh } | ConvertTo-Json -Compress
            Invoke-RestMethod -Uri "$EnvironmentUrl/api/data/v9.2/sprk_fieldmappingrules($id)" -Headers (Get-DvHeaders -ForWrite) -Method Patch -Body $body | Out-Null
            $reread = (Invoke-DvGet "sprk_fieldmappingrules($id)?`$select=sprk_syncmode")[0]
            if ($reread.sprk_syncmode -ne $SyncManualRefresh) { throw "read-back mismatch: sprk_syncmode=$($reread.sprk_syncmode), expected $SyncManualRefresh" }
            $written++
        } catch {
            $failed++
            Write-Host "  FAILED $id : $($_.Exception.Message)" -ForegroundColor Red
        }
    }
    Write-Host "Applied: $written migrated, $failed failed."
    if ($failed -gt 0) { exit 1 }
}

if ($Verify) {
    $verifyPlan = if ($IsDryRun) { $plan } else { Get-MigrationPlan (Read-Rules) }
    $pass = Test-VerifyPass $verifyPlan
    Write-Host ("VERIFY: {0} (Copy rules short of Manual Refresh: {1} active, {2} inactive)" -f $(if ($pass) { 'PASS' } else { 'NOT MET' }), $verifyPlan.Counts.Migrate, $verifyPlan.Counts.InactiveNotMigrated)
    if (-not $pass) { exit 1 }
}
exit 0
