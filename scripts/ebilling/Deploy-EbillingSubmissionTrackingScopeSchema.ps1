<#
.SYNOPSIS
    eBilling R1 task 095c (owner option B, 2026-11-03) - the tracking scope on sprk_invoicesubmission, so a submission with no firm has a
    tracking number the platform keeps unique.

.DESCRIPTION
    Idempotent Dataverse Web API script. Every step is check-then-act; a re-run reports what is already done and continues from the first step
    that is not. -WhatIf is read only (it reports what each step would do). -Verify is read only and checks the final state with a pass / fail
    exit code.

    WHY. The firm key sprk_invoicesubmission_firm_trackingnumber on (sprk_trackingnumber, sprk_vendororg) does not cover a row whose firm is
    null (notes/095c-from-email-firm-optional.md section 2.3), and sprk_vendororg is SystemRequired, so a submission with no firm can be neither
    written nor kept unique today. Option B: a text column sprk_trackingscope (the firm id in D form, or the literal 'none') and a new key on
    (sprk_trackingnumber, sprk_trackingscope), which covers every row once the column is SystemRequired.

    THE ORDER (main-session design 2026-11-03). Uniqueness is guarded at every moment: the firm key stays until the scope key is Active.
      1  Lower sprk_vendororg from SystemRequired to None (nothing writes a null firm yet: the BFF refuses one before phase P2).
      2  Add sprk_trackingscope (text, 36) at None. BFF instances see phase P1 within their 5-minute cache and write the scope on every row.
      3  Wait longer than the BFF's phase cache (-CacheWaitSeconds, at least 301 s), so every instance writes the scope before the backfill.
      4  Backfill: every row whose scope is null gets its firm id in D form, or 'none' when it has no firm.
      5  Raise sprk_trackingscope to SystemRequired (refused by this script while any row still holds a null). From here an instance that
         still holds phase P0 is refused by the platform (0x80040200); it re-reads the phase and retries, or answers a retryable 503.
      6  Re-backfill (rows a stale instance wrote between 4 and 5), then verify: no null scope, every scope equals its row's firm (or 'none'
         with no firm), no two rows share (tracking number, scope).
      7  Add the key sprk_invoicesubmission_scope_trackingnumber on (sprk_trackingnumber, sprk_trackingscope) and wait for it to be Active.
         BFF instances see phase P2 within 5 minutes: counted per scope; a no-firm submission (mode New, from email) is accepted.
      8  Verify: the scope key Active, the column SystemRequired, sprk_vendororg None, no null scope.
      9  Retire the firm key sprk_invoicesubmission_firm_trackingnumber, only after re-reading the scope key as Active. The only
         destructive step; owner OK required (project CLAUDE.md section 5).

    ESCALATION (exit 2, nothing further changed): sprk_trackingscope exists with another type or a length other than 36; a key named
    sprk_invoicesubmission_scope_trackingnumber exists on other columns; a key of any other name exists on (sprk_trackingnumber,
    sprk_trackingscope) (checked before any write: the BFF's phase probe needs the exact name); a row's scope differs from its firm; two rows
    share (tracking number, scope); the scope key index is Failed; at step 9, a row without a scope is found (re-counted next to the Active
    re-read, before the delete).

    NOT RUN BY THE TASK THAT ADDS IT. The main session runs it with the owner's OK and after the notices to uac-r2, ontology-r1 and SPA-r3
    (notes/095c, "Notice for uac-r2"). -WhatIf and -Verify may be run at any time. Publish is entity-scoped (PublishXml for
    sprk_invoicesubmission only) and only when this run wrote metadata. Solution source: Entities/sprk_invoicesubmission/Entity.xml in
    SpaarkeMaster carries the scope column SystemRequired, sprk_vendororg None, and BOTH keys (the firm key stays in source until a later
    release retires it, after every environment with rows has run this script through step 9: a managed upgrade deletes components that are
    missing from source, which on an environment with rows would drop the firm key before the scope key can guard them); a stamp provisioned
    from it has no rows and needs no backfill and no script.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.PARAMETER CacheWaitSeconds
    Step 3: how long to wait after the column exists and before the backfill. At least 301 (the BFF caches the phase for 300 s).

.PARAMETER KeepFirmKey
    Stop after step 8: the firm key is not retired (run again without it, with the owner's OK, to retire it).

.PARAMETER Verify
    Read-only check of the final state (steps 1 to 9 done). Exit 0 = pass, 1 = a gap (each one named). Cannot be combined with -WhatIf.

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingSubmissionTrackingScopeSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingSubmissionTrackingScopeSchema.ps1 -KeepFirmKey
    pwsh scripts/ebilling/Deploy-EbillingSubmissionTrackingScopeSchema.ps1
    pwsh scripts/ebilling/Deploy-EbillingSubmissionTrackingScopeSchema.ps1 -Verify

.NOTES
    Project: ebilling-r1, task 095c-1. Dev only in R1. Pattern: Deploy-EbillingSubmissionSourceEmailSchema.ps1 (structure, escalation,
    entity-scoped publish) and EbillingSchema.psm1 (Add-Column, Set-StringRequiredLevel, Add-AlternateKey, Get-Keys).
    Exit codes: 0 = done / pass, 1 = error / a gap (-Verify), 2 = escalation.
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [ValidateRange(301, 3600)][int]$CacheWaitSeconds = 330,
    [switch]$KeepFirmKey,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
if ($DryRun -and $Verify) { throw '-WhatIf and -Verify are separate modes.' }

$Table = 'sprk_invoicesubmission'
$EntitySet = 'sprk_invoicesubmissions'
$IdColumn = 'sprk_invoicesubmissionid'
$FirmColumn = 'sprk_vendororg'
$TrackingColumn = 'sprk_trackingnumber'
$ScopeColumn = 'sprk_trackingscope'
$ScopeSchema = 'sprk_TrackingScope'
$ScopeLength = 36
$NoFirmScope = 'none'
$ScopeKey = 'sprk_invoicesubmission_scope_trackingnumber'
$FirmKey = 'sprk_invoicesubmission_firm_trackingnumber'
$PageSize = 500

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Table
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Deferred = $State.Deferred
$script:Backfilled = 0

$ScopeDescription = 'The sequence the tracking number belongs to (task 095c): the firm id in D form, or none for a submission with no firm. Part of the alternate key with the tracking number. Written only by the server at create; decides no access.'

function Get-KeyColumns($key) { return (@($key.KeyAttributes | ForEach-Object { $_.ToLowerInvariant() } | Sort-Object) -join ',') }
$ScopeKeyColumns = (@($TrackingColumn, $ScopeColumn) | Sort-Object) -join ','

function Get-RequiredLevel([string]$Column, [string]$Kind) {
    $a = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.${Kind}AttributeMetadata?`$select=RequiredLevel" -Retries 2
    if (-not $a) { return $null }
    return [string]$a.RequiredLevel.Value
}

# By name only: the BFF's phase probe (TrackingScopePhaseProbe) requires this exact key name, so a key of another name on the same columns is
# not the scope key (see Get-MisnamedScopeKey).
function Get-ScopeKey { return Get-Keys $Table | Where-Object { $_.SchemaName -eq $ScopeKey } | Select-Object -First 1 }
# A key on (tracking number, scope) under any other name: the BFF would read P1 forever, and Add-AlternateKey would skip creating the right one.
function Get-MisnamedScopeKey { return Get-Keys $Table | Where-Object { $_.SchemaName -ne $ScopeKey -and (Get-KeyColumns $_) -eq $ScopeKeyColumns } | Select-Object -First 1 }
function Publish-Submission {
    # Entity-scoped (never the tenant-wide publish): the BFF's phase probe reads published metadata.
    Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
    Write-Host "  published $Table" -ForegroundColor Green
}
function Get-FirmKey { return Get-Keys $Table | Where-Object { $_.SchemaName -eq $FirmKey } | Select-Object -First 1 }

# The scope a row must carry: its firm id in D form (lower case, as the BFF writes Guid.ToString("D")), or 'none'.
function Get-ExpectedScope($row) {
    $firm = $row."_$($FirmColumn)_value"
    if ([string]::IsNullOrWhiteSpace([string]$firm)) { return $NoFirmScope }
    return ([guid]$firm).ToString('D')
}

# Rows of the table, every state, $PageSize at a time (follows @odata.nextLink). Each row: id, firm, tracking number, scope.
function Get-AllRows {
    $rows = [System.Collections.Generic.List[object]]::new()
    $next = "${EntitySet}?`$select=$IdColumn,_$($FirmColumn)_value,$TrackingColumn,$ScopeColumn"
    $headers = @{ Prefer = "odata.maxpagesize=$PageSize" }
    while ($next) {
        $page = Invoke-Dv -Path $next -ExtraHeaders $headers
        foreach ($r in @($page.value)) { $rows.Add($r) }
        $next = $page.'@odata.nextLink'
    }
    return $rows
}

# Steps 4 and 6: every row whose scope is null gets its expected scope. Re-queries the null set until it is empty (a filtered page shrinks as
# rows are written, so nextLink paging would skip rows). Bounded: a set that does not shrink is an error.
function Invoke-Backfill([string]$Label) {
    $filter = "${EntitySet}?`$select=$IdColumn,_$($FirmColumn)_value&`$filter=$ScopeColumn eq null&`$top=$PageSize"
    if ($DryRun) {
        if ($script:ScopeColumnPlanned) { Write-Host "  WhatIf: the column does not exist yet; every existing row would be backfilled" -ForegroundColor Cyan; return }
        $n = Get-RowCountExact $EntitySet $IdColumn "$ScopeColumn eq null"
        Write-Host "  WhatIf: $n row(s) would be backfilled ($Label)" -ForegroundColor Cyan
        return
    }
    $written = 0
    for ($round = 1; $round -le 1000; $round++) {
        $page = @((Invoke-Dv -Path $filter).value)
        if ($page.Count -eq 0) {
            Write-Host ("  {0}: {1} row(s) written; no row has a null scope" -f $Label, $written) -ForegroundColor Green
            $script:Backfilled += $written
            return
        }
        foreach ($row in $page) {
            $id = $row.$IdColumn
            # If-Match * : update only, never an upsert that creates a row.
            Invoke-Dv -Path "$EntitySet($id)" -Method PATCH -Body @{ $ScopeColumn = (Get-ExpectedScope $row) } -ExtraHeaders @{ 'If-Match' = '*' } | Out-Null
            $written++
        }
    }
    throw "The backfill did not finish after 1000 rounds of $PageSize rows; rows keep arriving without a scope (is a BFF instance still writing in phase P0?)."
}

# Step 6 and -Verify: every row's scope is its expected scope, and no two rows share (tracking number, scope). Returns the problems.
function Test-ScopeData {
    $problems = [System.Collections.Generic.List[string]]::new()
    $rows = Get-AllRows
    $nulls = @($rows | Where-Object { [string]::IsNullOrEmpty([string]$_.$ScopeColumn) }).Count
    if ($nulls -gt 0) { $problems.Add("$nulls row(s) have no scope") }
    $mismatch = @($rows | Where-Object { -not [string]::IsNullOrEmpty([string]$_.$ScopeColumn) -and ([string]$_.$ScopeColumn).ToLowerInvariant() -ne (Get-ExpectedScope $_) })
    if ($mismatch.Count -gt 0) { $problems.Add("$($mismatch.Count) row(s) carry a scope that is not their firm (first: $(@($mismatch | Select-Object -First 3 | ForEach-Object { $_.$IdColumn }) -join ', '))") }
    $dupes = @($rows | Where-Object { -not [string]::IsNullOrEmpty([string]$_.$ScopeColumn) } |
        Group-Object { "$(([string]$_.$TrackingColumn).ToLowerInvariant())|$(([string]$_.$ScopeColumn).ToLowerInvariant())" } | Where-Object { $_.Count -gt 1 })
    if ($dupes.Count -gt 0) { $problems.Add("$($dupes.Count) (tracking number, scope) pair(s) are held by more than one row; the scope key cannot build") }
    Write-Host ("  checked {0} row(s): {1} without a scope, {2} mismatched, {3} duplicated pair(s)" -f $rows.Count, $nulls, $mismatch.Count, $dupes.Count)
    return $problems
}

# Step 1. EbillingSchema.psm1's Set-ColumnRequiredLevel never lowers a level by design; this is the one deliberate lowering (owner option B).
function Set-FirmOptional {
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$FirmColumn')/Microsoft.Dynamics.CRM.LookupAttributeMetadata"
    $a = Get-DvOrNull $path -Retries 4
    if (-not $a) { throw "$Table.$FirmColumn was not found." }
    $cur = [string]$a.RequiredLevel.Value
    if ($cur -eq 'None') { $script:Skipped.Add("$Table.$FirmColumn required None"); Write-Host "  skipped  $FirmColumn already None" -ForegroundColor Yellow; return }
    if ($DryRun) { $script:Changed.Add("$Table.$FirmColumn required None"); Write-Host "  WOULD SET $FirmColumn required level $cur -> None" -ForegroundColor Cyan; return }
    $a.RequiredLevel.Value = 'None'
    Invoke-WhilePropagating { Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    for ($i = 0; $i -lt 12; $i++) {
        if ((Get-RequiredLevel $FirmColumn 'Lookup') -eq 'None') { break }
        Start-Sleep -Seconds 5
    }
    if ((Get-RequiredLevel $FirmColumn 'Lookup') -ne 'None') { throw "$Table.$FirmColumn required level is still not None after the update." }
    $script:Changed.Add("$Table.$FirmColumn required None"); Write-Host "  CHANGED  $FirmColumn required level $cur -> None" -ForegroundColor Green
}

# Read-only pre-flight, before ANY write: a conflicting column or a conflicting key.
function Test-Preflight {
    $conflict = [System.Collections.Generic.List[string]]::new()
    $col = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$ScopeColumn')/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=LogicalName,AttributeType,MaxLength" -Retries 2
    if (-not $col) {
        $any = Get-Attr $Table $ScopeColumn
        if ($any) { $conflict.Add("$Table.$ScopeColumn exists with type $($any.AttributeType), not String") }
    } elseif ([int]$col.MaxLength -ne $ScopeLength) {
        $conflict.Add("$Table.$ScopeColumn exists with length $($col.MaxLength), not $ScopeLength")
    }
    $named = Get-Keys $Table | Where-Object { $_.SchemaName -eq $ScopeKey } | Select-Object -First 1
    if ($named -and (Get-KeyColumns $named) -ne $ScopeKeyColumns) { $conflict.Add("key $ScopeKey exists on [$(@($named.KeyAttributes) -join ', ')], not ($ScopeKeyColumns)") }
    $misnamed = Get-MisnamedScopeKey
    if ($misnamed) { $conflict.Add("key $($misnamed.SchemaName) already covers ($ScopeKeyColumns) under another name; the BFF reads the phase from the exact name $ScopeKey and would stay in P1") }
    if ($conflict.Count -gt 0) { throw [EscalationException]::new("$Table cannot take the tracking scope as specified: $($conflict -join '; '). Owner decision needed. Nothing was changed.") }
    $fk = Get-FirmKey
    Write-Host ("  firm key {0}: {1}" -f $FirmKey, $(if ($fk) { $fk.EntityKeyIndexStatus } else { 'not present' }))
    $sk = Get-ScopeKey
    Write-Host ("  scope key {0}: {1}" -f $ScopeKey, $(if ($sk) { $sk.EntityKeyIndexStatus } else { 'not present' }))
    Write-Host ("  {0} required level: {1}; {2}: {3}" -f $FirmColumn, (Get-RequiredLevel $FirmColumn 'Lookup'), $ScopeColumn, $(if ($col) { Get-RequiredLevel $ScopeColumn 'String' } else { 'not present' }))
    Write-Host ("  rows: {0} (without a firm: {1})" -f (Get-RowCountExact $EntitySet $IdColumn), (Get-RowCountExact $EntitySet $IdColumn "_$($FirmColumn)_value eq null"))
}

# The phase a BFF instance reads (TrackingScopePhaseProbe): no column P0; the column without an Active scope key P1; the key Active P2.
function Get-Phase {
    if (-not (Get-Attr $Table $ScopeColumn)) { return 'P0' }
    $sk = Get-ScopeKey
    if ($sk -and $sk.EntityKeyIndexStatus -eq 'Active' -and (Get-KeyColumns $sk) -eq $ScopeKeyColumns) { return 'P2' }
    return 'P1'
}

Write-Host ""
Write-Host "eBilling task 095c - $Table tracking scope (option B)" -ForegroundColor Cyan
Write-Host ("Environment: {0}   Solution: {1}   Mode: {2}" -f $EnvironmentUrl, $SolutionUniqueName, $(if ($Verify) { 'VERIFY (read only)' } elseif ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' }))
Write-Host ""

if ($Verify) {
    $gaps = [System.Collections.Generic.List[string]]::new()
    try {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }
        Write-Host "Phase a BFF instance reads now: $(Get-Phase)" -ForegroundColor Cyan
        if ((Get-RequiredLevel $FirmColumn 'Lookup') -ne 'None') { $gaps.Add("step 1: $FirmColumn is not None") }
        $lvl = Get-RequiredLevel $ScopeColumn 'String'
        if (-not $lvl) { $gaps.Add("step 2: $ScopeColumn does not exist") }
        elseif ($lvl -ne 'SystemRequired') { $gaps.Add("step 5: $ScopeColumn is $lvl, not SystemRequired") }
        if ($lvl) { foreach ($p in (Test-ScopeData)) { $gaps.Add("step 6: $p") } }
        $sk = Get-ScopeKey
        $misnamed = Get-MisnamedScopeKey
        if ($misnamed) { $gaps.Add("step 7: key $($misnamed.SchemaName) covers ($ScopeKeyColumns) under another name; the BFF requires $ScopeKey") }
        if (-not $sk) { $gaps.Add("step 7: key $ScopeKey does not exist") }
        elseif ($sk.EntityKeyIndexStatus -ne 'Active') { $gaps.Add("step 7: key $ScopeKey is $($sk.EntityKeyIndexStatus), not Active") }
        if (Get-FirmKey) { $gaps.Add("step 9: the firm key $FirmKey is still present (expected after -KeepFirmKey)") }
    } catch {
        Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    if ($gaps.Count -eq 0) { Write-Host "`nPASS: the tracking scope is complete (phase P2, firm key retired)." -ForegroundColor Green; exit 0 }
    Write-Host "`nGAPS:" -ForegroundColor Red
    foreach ($g in $gaps) { Write-Host "  $g" -ForegroundColor Red }
    exit 1
}

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }

    Write-Host "0. Pre-flight (read only)" -ForegroundColor Cyan
    Test-Preflight
    Write-Host "  phase a BFF instance reads now: $(Get-Phase)"

    Write-Host "1. $FirmColumn required level -> None" -ForegroundColor Cyan
    $changedBefore = $script:Changed.Count
    Set-FirmOptional
    if (-not $DryRun -and $script:Changed.Count -gt $changedBefore) { Publish-Submission }

    Write-Host "2. $ScopeColumn (text, $ScopeLength) at None" -ForegroundColor Cyan
    $script:ScopeColumnPlanned = -not (Get-Attr $Table $ScopeColumn)
    Add-Column -Column $ScopeColumn -ExpectedType 'String' -Definition (New-StringDef -Schema $ScopeSchema -Display 'Tracking Scope' -Description $ScopeDescription -MaxLength $ScopeLength)
    if (-not $DryRun -and $script:Created.Count -gt 0) {
        # Entity-scoped publish now, so the BFF's published-metadata read (phase P1) sees the column before the wait starts.
        Publish-Submission
    }

    $scopeLevel = if ($script:ScopeColumnPlanned -and $DryRun) { 'None' } else { Get-RequiredLevel $ScopeColumn 'String' }
    Write-Host "3. Wait for every BFF instance to read phase P1 (cache 300 s)" -ForegroundColor Cyan
    if ($scopeLevel -eq 'SystemRequired') {
        Write-Host "  skipped  $ScopeColumn is already SystemRequired (step 5 done)" -ForegroundColor Yellow
    } elseif ($DryRun) {
        Write-Host "  WOULD WAIT $CacheWaitSeconds s" -ForegroundColor Cyan
    } else {
        Write-Host "  waiting $CacheWaitSeconds s ..."
        Start-Sleep -Seconds $CacheWaitSeconds
    }

    Write-Host "4. Backfill $ScopeColumn" -ForegroundColor Cyan
    Invoke-Backfill 'backfill'

    Write-Host "5. $ScopeColumn required level -> SystemRequired" -ForegroundColor Cyan
    if ($DryRun -and $script:ScopeColumnPlanned) { Write-Host "  WOULD SET $ScopeColumn required level None -> SystemRequired (after the backfill)" -ForegroundColor Cyan }
    $changedBeforeRaise = $script:Changed.Count
    for ($try = 1; $try -le 3; $try++) {
        Set-StringRequiredLevel -Table $Table -Column $ScopeColumn -Level 'SystemRequired'
        if ($DryRun -or (Get-RequiredLevel $ScopeColumn 'String') -eq 'SystemRequired') { break }
        # Set-ColumnRequiredLevel refuses to raise while a row holds a null (a stale writer since step 4): backfill those and try again.
        if ($try -eq 3) { throw "$Table.$ScopeColumn could not be raised to SystemRequired after 3 backfill rounds: $($script:Deferred -join '; ')" }
        Invoke-Backfill "re-backfill before raise $try"
    }
    # Published at once (entity scope): the platform and the BFF's published-metadata read must see the raised level before step 6 relies on it.
    if (-not $DryRun -and $script:Changed.Count -gt $changedBeforeRaise) { Publish-Submission }

    Write-Host "6. Re-backfill and verify the scope data" -ForegroundColor Cyan
    Invoke-Backfill 're-backfill'
    if (-not $DryRun) {
        $problems = @(Test-ScopeData)
        if ($problems.Count -gt 0) { throw [EscalationException]::new("the scope data is not ready for the key: $($problems -join '; '). Not changed by this script.") }
    }

    Write-Host "7. Scope key $ScopeKey on ($TrackingColumn, $ScopeColumn), wait for Active" -ForegroundColor Cyan
    try {
        Add-AlternateKey -Table $Table -Schema $ScopeKey -Display 'Tracking Scope and Tracking Number' -Attributes @($TrackingColumn, $ScopeColumn)
    } catch {
        if ($_.Exception.Message -match 'FAILED') { throw [EscalationException]::new("$($_.Exception.Message) The firm key is untouched; nothing else was changed.") }
        throw
    }

    Write-Host "8. Verify" -ForegroundColor Cyan
    if ($DryRun) {
        Write-Host "  WhatIf: verification runs after the writes" -ForegroundColor Cyan
    } else {
        $sk = Get-ScopeKey
        $checks = @(
            @{ Ok = ($sk -and $sk.EntityKeyIndexStatus -eq 'Active'); Text = "scope key $ScopeKey Active" },
            @{ Ok = ((Get-RequiredLevel $ScopeColumn 'String') -eq 'SystemRequired'); Text = "$ScopeColumn SystemRequired" },
            @{ Ok = ((Get-RequiredLevel $FirmColumn 'Lookup') -eq 'None'); Text = "$FirmColumn None" },
            @{ Ok = ((Get-RowCountExact $EntitySet $IdColumn "$ScopeColumn eq null") -eq 0); Text = "no row without a scope" }
        )
        foreach ($c in $checks) {
            if (-not $c.Ok) { throw "verification failed: $($c.Text) does not hold. The firm key is untouched." }
            Write-Host "  ok       $($c.Text)" -ForegroundColor Green
        }
        Write-Host "  phase a BFF instance reads now: $(Get-Phase)" -ForegroundColor Green
    }

    Write-Host "9. Retire the firm key $FirmKey" -ForegroundColor Cyan
    $fk = Get-FirmKey
    if (-not $fk) {
        $script:Skipped.Add("key $FirmKey retired"); Write-Host "  skipped  $FirmKey is not present" -ForegroundColor Yellow
    } elseif ($KeepFirmKey) {
        $script:Deferred.Add("key $FirmKey retire (-KeepFirmKey)"); Write-Host "  deferred (-KeepFirmKey): $FirmKey stays; run again without -KeepFirmKey to retire it" -ForegroundColor Yellow
    } elseif ($DryRun) {
        $script:Changed.Add("key $FirmKey retired"); Write-Host "  WOULD DELETE key $FirmKey (only after re-reading $ScopeKey as Active)" -ForegroundColor Cyan
    } else {
        # The order guarantee: re-read the scope key immediately before the delete; anything but Active stops here.
        $sk = Get-ScopeKey
        if (-not $sk -or $sk.EntityKeyIndexStatus -ne 'Active') { throw "the scope key reads $(if ($sk) { $sk.EntityKeyIndexStatus } else { 'absent' }), not Active: the firm key is NOT retired." }
        # And re-count the rows without a scope: the scope key does not cover them, so once the firm key is gone nothing would guard such a row.
        # (A row written by a stale instance after step 8 would be one.) Non-zero stops with exit 2 before the delete.
        $nullScopes = Get-RowCountExact $EntitySet $IdColumn "$ScopeColumn eq null"
        if ($null -eq $nullScopes) { throw "the rows without a scope could not be counted: the firm key is NOT retired." }
        if ($nullScopes -gt 0) { throw [EscalationException]::new("$nullScopes row(s) have no ${ScopeColumn}: the scope key does not cover them, so the firm key is NOT retired. Backfill them (re-run the script) and retry.") }
        Invoke-Dv -Path "EntityDefinitions(LogicalName='$Table')/Keys($($fk.MetadataId))" -Method DELETE -ExtraHeaders $State.SolutionHeader | Out-Null
        for ($i = 0; $i -lt 12 -and (Get-FirmKey); $i++) { Start-Sleep -Seconds 5 }
        if (Get-FirmKey) { throw "the firm key $FirmKey is still readable 60 s after the delete; re-run -Verify." }
        $script:Changed.Add("key $FirmKey retired"); Write-Host "  DELETED  key $FirmKey" -ForegroundColor Green
    }

    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
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
    Write-Host "Steps already done stay done (the script is re-runnable from the first step that is not)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("created    : {0}  {1}" -f $script:Created.Count, ($script:Created -join ', '))
Write-Host ("changed    : {0}  {1}" -f $script:Changed.Count, ($script:Changed -join ', '))
Write-Host ("skipped    : {0}" -f $script:Skipped.Count)
Write-Host ("deferred   : {0}  {1}" -f $script:Deferred.Count, ($script:Deferred -join ', '))
Write-Host ("backfilled : {0} row(s)" -f $script:Backfilled)
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
