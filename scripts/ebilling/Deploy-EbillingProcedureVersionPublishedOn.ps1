<#
.SYNOPSIS
    eBilling R1 DEF-061 (owner-approved schema change, 2026-11-09) - sprk_procedureversion.sprk_publishedon, the server's publish stamp.
    -WhatIf is a dry run, a plain run creates the column, -Verify checks, -Backfill stamps the versions published before the column existed.

.DESCRIPTION
    WHY. Whether a Retired Procedure Version ever opened a window (ontology-r1 D-203 (5a)/(9)) was inferred from a Published policy version
    under it, which misses a version that governed only rate cards. ProcedureService.PublishVersionAsync now stamps this column with the
    server's UTC now in the write that makes the version Active; "opened a window" is "has an effective-from AND this stamp". The resolver
    (ProcedureResolver, ProcedureVersionRow.OpenedAWindow) falls back to the old policy-version evidence only for a RETIRED version whose stamp
    is empty.

    THE COLUMN. sprk_procedureversion.sprk_publishedon, Date and Time, user local (an instant: stored UTC and shown in each viewer's zone; a
    time-zone-independent column would store a wall-clock literal, which is wrong for a moment in time), optional, no default, NOT
    field-secured by this script. Empty on a Draft. Written only by the BFF; the generic writers are refused it
    (RecordTableRule.WriterOnlyColumns on sprk_procedureversion).

    MODES (exactly one; a plain run is the writing one).
      -WhatIf             read only: reports what a plain run (or, with -Backfill, a backfill) would do. Safe to run against dev at any time.
      (no switch)         creates the column if absent (skips it if present) and publishes sprk_procedureversion (entity-scoped, only when this
                          run wrote metadata). A second run writes nothing, publishes nothing.
      -Verify             read only, exit 0 = pass, 1 = a gap (each named): the column exists, is Date and Time, user local, optional, AND no
                          Active or Superseded version is left without a stamp (the deploy gate for the BFF build that reads it).
      -Backfill           stamps every Active and Superseded version whose stamp is empty. Retired versions are NEVER touched (see below);
                          Drafts have nothing to stamp. Combine with -WhatIf for the counts.

    BACKFILL RULE (the stamp's VALUE is informational; only "not empty" decides anything). For each Active or Superseded version with an
    empty stamp, in order of evidence:
      1. publish evidence: the earliest audit row of that version that set sprk_status to Active (the publish write). Used only when Dataverse
         audit holds one; the audit read is best effort (a table or column that was never audited, or an audit read the operator may not
         make, simply leaves the version on rule 2 and the run says so).
      2. otherwise its effective-from date at 00:00 UTC. The version has been in force from that date, so the stamp never claims a publish
         LATER than the version's own window start; it may be later than the true publish (a version published before its start date), which
         changes nothing the resolver reads.
    A version with no effective-from cannot have opened a window and is reported, not stamped. A row whose status reads empty is a masked read
    (the operator lacks the field-reader profile for the secured status column): the run STOPS with exit 1 and writes nothing, because it
    could not tell which versions to stamp.
    WHY Retired versions are left alone: a Retired row cannot tell a version that was published (and then withdrawn) from a Draft that was
    discarded unpublished. Stamping both would make every discarded Draft hold work as a gap (D-203 (9)). The resolver keeps the policy-version
    evidence for them.

    ORDER (binding): column (plain run) -> -Backfill -> -Verify exits 0 -> then deploy the BFF build that carries the resolver change. Until
    the backfill, an Active or Superseded version with an empty stamp did not open a window to the new resolver.

    A same-name column of another type STOPS the script with exit code 2 and an ESCALATION message (nothing is altered). Auth: the operator's
    own az CLI identity. Publish is entity-scoped (PublishXml for sprk_procedureversion), never the tenant-wide publish.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created column is added to (header MSCRM.SolutionUniqueName). Default SpaarkeMaster.

.PARAMETER Verify
    Read-only check with a pass / fail exit code. Cannot be combined with -WhatIf, -Backfill.

.PARAMETER Backfill
    Stamp the Active and Superseded versions with an empty stamp (see BACKFILL RULE). With -WhatIf: counts only, nothing written.

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingProcedureVersionPublishedOn.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingProcedureVersionPublishedOn.ps1
    pwsh scripts/ebilling/Deploy-EbillingProcedureVersionPublishedOn.ps1 -Backfill -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingProcedureVersionPublishedOn.ps1 -Backfill
    pwsh scripts/ebilling/Deploy-EbillingProcedureVersionPublishedOn.ps1 -Verify

.NOTES
    Project: ebilling-r1, DEF-061. Dev only in R1. Pattern: scripts/ebilling/Deploy-EbillingSubmissionPreviousSequenceColumn.ps1 and
    Deploy-EbillingSentToApDateColumn.ps1 (one column, escalation, entity-scoped publish only on a write). Pinned against the BFF's column
    name and the SpaarkeMaster source by ProcedureVersionPublishedOnAgreementTests. The Dataverse helpers live in EbillingSchema.psm1.
    Exit codes: 0 = pass / done, 1 = a gap or an error, 2 = escalation (a conflicting column).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$Verify,
    [switch]$Backfill
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
if ($Verify -and ($DryRun -or $Backfill)) { throw '-Verify is its own mode: it cannot be combined with -WhatIf or -Backfill.' }

# ── Constants (pinned by ProcedureVersionPublishedOnAgreementTests) ─────────────────────────────────────────────
$Table = 'sprk_procedureversion'
$EntitySet = 'sprk_procedureversions'
$Column = 'sprk_publishedon'
$ColumnSchemaName = 'sprk_PublishedOn'
$StatusActive = 100000001
$StatusSuperseded = 100000002
$StatusRetired = 100000003

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Table
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

$Description = 'When the BFF published this version (UTC instant). Written only by the BFF in the publish write and never moved; empty on a Draft and on a version published before this column existed (the backfill stamps Active and Superseded ones). Decides whether the version opened a window (D-203). DEF-061.'

function Get-ColumnShape {
    # Read only. $null when the column does not exist.
    $attr = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')?`$select=LogicalName,AttributeType,IsSecured" -Retries 2
    if (-not $attr) { return $null }
    $detail = $null
    if ($attr.AttributeType -eq 'DateTime') {
        $detail = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata?`$select=Format,DateTimeBehavior,RequiredLevel" -Retries 4
    }
    return [pscustomobject]@{ Type = $attr.AttributeType; Secured = ($attr.IsSecured -eq $true); Detail = $detail }
}

# Every way the column differs from the specification above; empty when it is as specified. A column that does not exist is one gap.
function Get-ColumnGaps($shape) {
    $gaps = [System.Collections.Generic.List[string]]::new()
    if (-not $shape) { $gaps.Add("$Table.$Column does not exist"); return , $gaps }
    if ($shape.Type -ne 'DateTime') { $gaps.Add("$Table.$Column is '$($shape.Type)', not Date and Time"); return , $gaps }
    if (-not $shape.Detail) { $gaps.Add("$Table.$Column format could not be read"); return , $gaps }
    if ($shape.Detail.Format -ne 'DateAndTime') { $gaps.Add("$Table.$Column format is '$($shape.Detail.Format)', expected DateAndTime") }
    if ($shape.Detail.DateTimeBehavior.Value -ne 'UserLocal') { $gaps.Add("$Table.$Column behaviour is '$($shape.Detail.DateTimeBehavior.Value)', expected UserLocal (an instant)") }
    if ($shape.Detail.RequiredLevel.Value -ne 'None') { $gaps.Add("$Table.$Column is required ('$($shape.Detail.RequiredLevel.Value)'); it must be optional (a Draft has no stamp)") }
    return , $gaps
}

# All versions with the fields the backfill and the verify read. $HasColumn false: the column is not there yet, so every stamp is empty.
function Get-VersionRows([bool]$HasColumn) {
    $select = "sprk_procedureversionid,sprk_status,sprk_effectivefrom" + $(if ($HasColumn) { ",$Column" } else { '' })
    $rows = [System.Collections.Generic.List[object]]::new()
    $next = "$EntitySet`?`$select=$select"
    while ($next) {
        $page = Invoke-Dv -Path $next
        foreach ($r in $page.value) { $rows.Add($r) }
        $next = $page.'@odata.nextLink'
    }
    return , $rows
}

# The publish evidence of one version: the earliest audit row that set sprk_status to Active, or $null. Best effort (see the BACKFILL RULE).
$script:AuditUsable = $true
function Get-PublishAuditTime([string]$Id) {
    if (-not $script:AuditUsable) { return $null }
    try {
        $page = Invoke-Dv -Path "audits?`$select=createdon,changedata&`$filter=_objectid_value eq $Id and operation eq 2&`$orderby=createdon asc"
    } catch {
        $script:AuditUsable = $false
        Write-Host "  audit history is not readable here ($($_.Exception.Message)); every version falls back to its effective-from" -ForegroundColor DarkYellow
        return $null
    }
    foreach ($a in $page.value) {
        if (-not $a.changedata) { continue }
        try { $change = $a.changedata | ConvertFrom-Json } catch { continue }
        foreach ($c in @($change.changedAttributes)) {
            if ($c.logicalName -eq 'sprk_status' -and ("$($c.newValue)" -match "(^|\D)$StatusActive(\D|$)")) { return [datetime]::Parse($a.createdon).ToUniversalTime() }
        }
    }
    return $null
}

function Get-StampPlan($rows, [bool]$HasColumn) {
    $plan = [pscustomobject]@{
        Total = $rows.Count; Active = 0; Superseded = 0; RetiredLeft = 0; Draft = 0; AlreadyStamped = 0; Masked = 0; NoFrom = 0
        ToStamp = [System.Collections.Generic.List[object]]::new()
    }
    foreach ($r in $rows) {
        if ($null -eq $r.sprk_status) { $plan.Masked++; continue }
        $status = [int]$r.sprk_status
        if ($status -eq $StatusRetired) { $plan.RetiredLeft++; continue }
        if ($status -ne $StatusActive -and $status -ne $StatusSuperseded) { $plan.Draft++; continue }
        if ($status -eq $StatusActive) { $plan.Active++ } else { $plan.Superseded++ }
        if ($HasColumn -and $r.$Column) { $plan.AlreadyStamped++; continue }
        if (-not $r.sprk_effectivefrom) { $plan.NoFrom++; continue }
        $plan.ToStamp.Add($r)
    }
    return $plan
}

Write-Host ""
Write-Host "eBilling DEF-061 - $Table.$Column" -ForegroundColor Cyan
Write-Host ("Environment: {0}   Solution: {1}   Mode: {2}" -f $EnvironmentUrl, $SolutionUniqueName,
    $(if ($Verify) { 'VERIFY (read only)' } elseif ($Backfill -and $DryRun) { 'BACKFILL WHATIF (no writes)' } elseif ($Backfill) { 'BACKFILL' } elseif ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' }))
Write-Host ""

if ($Verify) {
    try {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }
        $shape = Get-ColumnShape
        $gaps = Get-ColumnGaps $shape
        if ($gaps.Count -eq 0) {
            $plan = Get-StampPlan (Get-VersionRows $true) $true
            if ($plan.Masked -gt 0) { $gaps.Add("$($plan.Masked) version(s) read with an empty status (masked by field security for this operator); the backfill state cannot be told") }
            if ($plan.ToStamp.Count -gt 0) { $gaps.Add("$($plan.ToStamp.Count) Active or Superseded version(s) have no $Column; run -Backfill before the BFF build that reads it is deployed") }
            if ($plan.NoFrom -gt 0) { Write-Host "  note: $($plan.NoFrom) Active or Superseded version(s) have no effective-from (they never opened a window; not a gap)" -ForegroundColor Yellow }
            if ($shape.Secured) { Write-Host "  note: $Column is field-secured here; the BFF application user must read it through a profile, or a masked read reads as 'not published'" -ForegroundColor Yellow }
        }
    } catch {
        Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    if ($gaps.Count -eq 0) {
        Write-Host "PASS: $Table.$Column exists as an optional Date and Time (user local) column, and every Active or Superseded version is stamped." -ForegroundColor Green
        exit 0
    }
    Write-Host "GAPS:" -ForegroundColor Red
    foreach ($g in $gaps) { Write-Host "  $g" -ForegroundColor Red }
    exit 1
}

if ($Backfill) {
    try {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }
        $shape = Get-ColumnShape
        $hasColumn = [bool]$shape
        if (-not $hasColumn) {
            if (-not $DryRun) { throw "$Table.$Column does not exist; run this script without -Backfill first to create it." }
            Write-Host "  $Column does not exist yet (run without -Backfill to create it): counting every Active and Superseded version as unstamped." -ForegroundColor Yellow
        } else {
            $gaps = Get-ColumnGaps $shape
            if ($gaps.Count -gt 0) { throw [EscalationException]::new("$Table.$Column is not as specified: $($gaps -join '; '). Owner decision needed (this script does not alter an existing column).") }
        }

        $plan = Get-StampPlan (Get-VersionRows $hasColumn) $hasColumn
        if ($plan.Masked -gt 0) {
            throw "$($plan.Masked) of $($plan.Total) version(s) read with an empty status: this operator cannot read the secured status column, so which versions to stamp cannot be told. Nothing was written."
        }

        $byAudit = 0; $byFrom = 0; $written = 0
        foreach ($r in $plan.ToStamp) {
            $evidence = 'effective-from'
            $stamp = Get-PublishAuditTime $r.sprk_procedureversionid
            if ($stamp) { $evidence = 'audit (status -> Active)'; $byAudit++ }
            else { $stamp = [datetime]::Parse("$($r.sprk_effectivefrom)T00:00:00Z", [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal); $byFrom++ }
            $iso = $stamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture)
            $label = if ([int]$r.sprk_status -eq $StatusActive) { 'Active' } else { 'Superseded' }
            if ($DryRun) {
                Write-Host ("  WOULD STAMP {0} ({1}) {2}  [{3}]" -f $r.sprk_procedureversionid, $label, $iso, $evidence) -ForegroundColor Cyan
            } else {
                # Only a row that is still empty is stamped (a stamp written meanwhile by a publish is never moved).
                $fresh = Invoke-Dv -Path "$EntitySet($($r.sprk_procedureversionid))?`$select=$Column"
                if ($fresh.$Column) { Write-Host "  skipped  $($r.sprk_procedureversionid) (stamped meanwhile)" -ForegroundColor Yellow; continue }
                Invoke-Dv -Path "$EntitySet($($r.sprk_procedureversionid))" -Method PATCH -Body @{ $Column = $iso } -ExtraHeaders @{ 'If-Match' = '*' } | Out-Null
                $written++
                Write-Host ("  STAMPED  {0} ({1}) {2}  [{3}]" -f $r.sprk_procedureversionid, $label, $iso, $evidence) -ForegroundColor Green
            }
        }

        Write-Host ""
        Write-Host "================ Backfill summary ================" -ForegroundColor Cyan
        Write-Host ("versions read                      : {0}" -f $plan.Total)
        Write-Host ("  Active                           : {0}" -f $plan.Active)
        Write-Host ("  Superseded                       : {0}" -f $plan.Superseded)
        Write-Host ("  Retired (left alone)             : {0}" -f $plan.RetiredLeft)
        Write-Host ("  Draft (nothing to stamp)         : {0}" -f $plan.Draft)
        Write-Host ("Active/Superseded already stamped  : {0}" -f $plan.AlreadyStamped)
        Write-Host ("Active/Superseded with no from     : {0} (reported, not stamped)" -f $plan.NoFrom)
        Write-Host ("{0} : {1}  (audit evidence {2}, effective-from {3})" -f $(if ($DryRun) { 'would stamp                        ' } else { 'stamped                            ' }), $(if ($DryRun) { $plan.ToStamp.Count } else { $written }), $byAudit, $byFrom)
        if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
        exit 0
    }
    catch [EscalationException] {
        Write-Host ""
        Write-Host "ESCALATION - STOPPED, nothing further was changed: $($_.Exception.Message)" -ForegroundColor Red
        exit 2
    }
    catch {
        Write-Host ""
        Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Versions already stamped stay (the backfill is re-runnable: it only stamps an empty stamp)." -ForegroundColor Red
        exit 1
    }
}

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }

    Write-Host "1. $Table.$Column (Date and Time, user local, optional, no default)" -ForegroundColor Cyan
    Add-Column $Column (New-DateTimeDef $ColumnSchemaName 'Published On' $Description) 'DateTime'

    # Add-Column checks the type only (Date Only and Date and Time are both 'DateTime'). An existing column of another shape is not this
    # column: never altered here. (A column this run created has the specified shape by construction, so only an existing one is checked.)
    if ($script:Skipped.Count -gt 0) {
        $gaps = Get-ColumnGaps (Get-ColumnShape)
        if ($gaps.Count -gt 0) {
            throw [EscalationException]::new("$Table.$Column is not as specified: $($gaps -join '; '). Owner decision needed (this script does not alter an existing column).")
        }
    }

    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        # Entity-scoped (never the tenant-wide publish).
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>"
        } | Out-Null
        Write-Host "  published $Table" -ForegroundColor Green
    }
}
catch [EscalationException] {
    Write-Host ""
    Write-Host "ESCALATION - STOPPED, nothing further was changed: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "The column, if created, stays (the script is re-runnable)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("created : {0}  {1}" -f $script:Created.Count, ($script:Created -join ', '))
Write-Host ("changed : {0}  {1}" -f $script:Changed.Count, ($script:Changed -join ', '))
Write-Host ("skipped : {0}" -f $script:Skipped.Count)
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
