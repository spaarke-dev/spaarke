<#
.SYNOPSIS
    eBilling R1 ISS-051 (#3080) - backfill the Invoice Line code lookups (sprk_billingevent.sprk_taskcode / sprk_activitycode /
    sprk_expensecode) from the resolved codes in each line's coding JSON.

.DESCRIPTION
    Until ISS-051 no writer set the three lookups: the resolved UTBMS codes lived only in the "coding" member of the line's
    sprk_extractionconfidence JSON. Since ISS-051 every writer of a resolved code (intake, the re-run after Set firm, a reviewer's code
    correction, Add line) sets the lookup in the same write, and the AP export and the invoice index read the lookup. This script brings
    the lines written before ISS-051 into line with their JSON, from that JSON and from nothing else.

    Only lines of an invoice that is In Review or Approved are included by default. A line of an invoice that is Sent to AP, Paid, or
    inactive (Superseded, Rejected, Returned to Firm, Cancelled) is skipped: AP already received that invoice's as-billed codes (FR-41 / D-56),
    so its lines keep matching what AP got. -IncludeClosed includes them. The report breaks the lines down by invoice status.

    For each included Fee / Expense line with a coding JSON, and each slot (task, activity, expense), the code's set is the line's code set
    (coding.codeSetId), or, for an activity or expense code when that set holds no active code of the kind, the shared "UTBMS Activity" /
    "UTBMS Expense" set (the resolver's own rule, CodeSetResolver.LoadAsync). Then:
      ok          the lookup already names the slot's row (or there is no code and no lookup). Nothing to do.
      set         no lookup, and the slot's code resolves to exactly one sprk_billingcode row:
                    1. the row the JSON names (coding.<slot>.codeId), when it exists AND is that code, of that kind, in that set; else
                    2. the row of that code and kind in that set.
      unresolved  the code matches no row, or more than one. Left empty (FR-22: a code is never stored outside the line's set) and listed.
      conflict    the lookup is set but names another row; or the slot has no code; or the JSON's codeId names a row that is not that code,
                  kind and set. Left as it is and listed for the owner.
    It never guesses, never writes the JSON, never clears a lookup, and never touches any other column.

    REPORT MODE IS THE DEFAULT and is read only: it prints every line that would change and a summary. -Apply writes: one PATCH per line
    that binds only the slots counted as "set", with If-Match set to the row version read, so a line changed since the read (a reviewer's
    correction, a re-run) is refused (412), counted as "changed since read" and never overwritten; re-run the script for it. -Apply is a live
    write and needs the owner's typed OK in the main session; the task that adds this script does not run it. Re-runnable: a line it set reads
    as "ok" the next time.

    A line PATCH does not move sprk_invoice.modifiedon, so the invoice index (which reads the lookups) is not rebuilt by -Apply. The script
    prints the affected invoice ids, the oldest one's modifiedon, and the Invoke-EbillingInvoiceReindexSweep.ps1 command (-Since that date)
    to queue their re-index after -Apply.

    Every Web API call goes through EbillingSchema.psm1 (Invoke-Dv), which sends Consistency: Strong.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER Apply
    Write the lookups counted as "set". Without it the script only reports.

.PARAMETER IncludeClosed
    Also include the lines of an invoice that is Sent to AP, Paid or inactive (skipped by default; see the description).

.PARAMETER Detail
    Also print the "ok" and the skipped lines (report mode prints only lines with something to set, unresolved or in conflict).

.EXAMPLE
    pwsh scripts/ebilling/Backfill-EbillingLineCodeLookups.ps1            # report (read only)
    pwsh scripts/ebilling/Backfill-EbillingLineCodeLookups.ps1 -Apply     # owner-approved write

.NOTES
    Project: ebilling-r1, ISS-051 (#3080). Dev only in R1. Notes: projects/ebilling-r1/notes/iss051-line-code-lookups.md.
    A PATCH moves the line's row version: a reviewer who has the invoice open gets "the line changed, reload" on the next line decision.
    Exit codes: 0 = done (including nothing to do), 1 = error.
#>

using module .\EbillingSchema.psm1

[CmdletBinding()]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [switch]$Apply,
    [switch]$IncludeClosed,
    [switch]$Detail
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$FeeCostType = 100000000       # InvoiceLineCostType.Fee
$ExpenseCostType = 100000001   # InvoiceLineCostType.Expense
$Kinds = [ordered]@{
    # slot = (code type option value, lookup column, shared set of the kind or $null)
    task     = @{ Type = 100000000; Column = 'sprk_taskcode'; Shared = $null }
    activity = @{ Type = 100000001; Column = 'sprk_activitycode'; Shared = 'UTBMS Activity' }
    expense  = @{ Type = 100000002; Column = 'sprk_expensecode'; Shared = 'UTBMS Expense' }
}

$null = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName 'SpaarkeMaster' -DryRun:(-not $Apply) -Entity 'sprk_billingevent'

function Get-AllPages([string]$Path) {
    $rows = [System.Collections.Generic.List[object]]::new()
    $next = $Path
    while ($next) {
        $r = Invoke-Dv -Path $next -ExtraHeaders @{ Prefer = 'odata.maxpagesize=500' }
        foreach ($v in @($r.value)) { $rows.Add($v) }
        $next = $r.'@odata.nextLink'
    }
    return $rows
}

function Format-Text([string]$Text) { return $Text.Replace("'", "''") }

# The navigation property each lookup binds through (read from the relationship, not assumed from the schema name).
function Get-NavigationProperties {
    $rels = Invoke-Dv -Path ("EntityDefinitions(LogicalName='sprk_billingevent')/ManyToOneRelationships" +
        "?`$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity&`$filter=ReferencedEntity eq 'sprk_billingcode'")
    $map = @{}
    foreach ($r in @($rels.value)) { $map[[string]$r.ReferencingAttribute] = [string]$r.ReferencingEntityNavigationPropertyName }
    foreach ($k in $Kinds.Values) {
        if (-not $map.ContainsKey($k.Column)) { throw "sprk_billingevent.$($k.Column) is not a lookup to sprk_billingcode in $EnvironmentUrl." }
    }
    return $map
}

$script:SetByName = @{}
function Get-SetIdByName([string]$Name) {
    if ($script:SetByName.ContainsKey($Name)) { return $script:SetByName[$Name] }
    $rows = @((Invoke-Dv -Path ("sprk_billingcodesets?`$select=sprk_billingcodesetid&`$filter=sprk_name eq '$(Format-Text $Name)' and statecode eq 0&`$orderby=createdon asc")).value)
    $id = if ($rows.Count -gt 0) { ([guid]$rows[0].sprk_billingcodesetid).ToString('D') } else { $null }
    $script:SetByName[$Name] = $id
    return $id
}

$script:HasKind = @{}
function Test-SetHasKind([string]$SetId, [int]$Type) {
    $key = "$SetId|$Type"
    if (-not $script:HasKind.ContainsKey($key)) {
        $rows = @((Invoke-Dv -Path ("sprk_billingcodes?`$select=sprk_billingcodeid&`$top=1&`$filter=_sprk_codeset_value eq $SetId and sprk_codetype eq $Type and statecode eq 0")).value)
        $script:HasKind[$key] = $rows.Count -gt 0
    }
    return $script:HasKind[$key]
}

# The rows of one code of one kind in one set (any state: the code was in the set when the line was coded).
$script:Rows = @{}
function Get-CodeRows([string]$SetId, [int]$Type, [string]$Code) {
    $key = "$SetId|$Type|$($Code.ToUpperInvariant())"
    if (-not $script:Rows.ContainsKey($key)) {
        $rows = @((Invoke-Dv -Path ("sprk_billingcodes?`$select=sprk_billingcodeid,sprk_code&`$filter=_sprk_codeset_value eq $SetId and sprk_codetype eq $Type and sprk_code eq '$(Format-Text $Code)'")).value)
        $script:Rows[$key] = @($rows | ForEach-Object { ([guid]$_.sprk_billingcodeid).ToString('D') })
    }
    return $script:Rows[$key]
}

# One sprk_billingcode row by id, with what identifies it (code, type, set); $null when it does not exist.
$script:CodeRow = @{}
function Get-CodeRowById([string]$Id) {
    if (-not $script:CodeRow.ContainsKey($Id)) {
        $script:CodeRow[$Id] = Get-DvOrNull "sprk_billingcodes($Id)?`$select=sprk_billingcodeid,sprk_code,sprk_codetype,_sprk_codeset_value"
    }
    return $script:CodeRow[$Id]
}

# The row a slot's code resolves to: @{ Id = <guid or $null>; Conflict = <bool>; Why = <text> }.
function Resolve-CodeRow($Coding, [string]$Slot, [string]$Code) {
    $kind = $Kinds[$Slot]
    $parsed = [guid]::Empty

    # The set the code must come from: the line's set, or the shared set of the kind when the line's set has none (CodeSetResolver.LoadAsync).
    $setText = [string]$Coding.codeSetId
    if (-not $setText -or -not [guid]::TryParse($setText, [ref]$parsed)) { return @{ Id = $null; Conflict = $false; Why = 'the coding names no code set' } }
    $setId = $parsed.ToString('D')
    $where = "the line's set $setId"
    if ($kind.Shared -and -not (Test-SetHasKind $setId $kind.Type)) {
        $setId = Get-SetIdByName $kind.Shared
        if (-not $setId) { return @{ Id = $null; Conflict = $false; Why = "the shared set '$($kind.Shared)' was not found" } }
        $where = "the shared set '$($kind.Shared)'"
    }

    # 1. The row the JSON names, only when it is this slot's code, of this kind, in that set. A row that names anything else is a conflict
    #    (the JSON disagrees with itself; never guessed). A row that no longer exists falls through to 2.
    $recorded = [string]$Coding.$Slot.codeId
    if ($recorded -and [guid]::TryParse($recorded, [ref]$parsed)) {
        $r = Get-CodeRowById $parsed.ToString('D')
        if ($null -ne $r) {
            $rowSet = if ($r._sprk_codeset_value) { ([guid]$r._sprk_codeset_value).ToString('D') } else { '' }
            if ([string]::Equals(([string]$r.sprk_code).Trim(), $Code, [System.StringComparison]::OrdinalIgnoreCase) -and
                [int]$r.sprk_codetype -eq $kind.Type -and $rowSet -eq $setId) {
                return @{ Id = $parsed.ToString('D'); Conflict = $false; Why = 'the row the JSON names' }
            }
            return @{ Id = $null; Conflict = $true; Why = "the JSON's codeId $($parsed.ToString('D')) names '$($r.sprk_code)' (type $($r.sprk_codetype), set $rowSet), not $Code of this kind in $where" }
        }
    }

    # 2. The row of that code and kind in that set.
    $rows = @(Get-CodeRows $setId $kind.Type $Code)
    if ($rows.Count -eq 1) { return @{ Id = $rows[0]; Conflict = $false; Why = "$Code in $where" } }
    return @{ Id = $null; Conflict = $false; Why = "$Code matches $($rows.Count) rows in $where" }
}

# The invoice of a line: @{ Status = <label>; Open = <In Review or Approved>; ModifiedOn = <datetime or $null> } (InvoiceStatusCodes).
$StatusLabels = @{ 1 = 'In Review'; 659490000 = 'Approved'; 659490001 = 'Sent to AP'; 659490002 = 'Paid'; 659490003 = 'Rejected'
    659490004 = 'Returned to Firm'; 2 = 'Cancelled'; 659490005 = 'Superseded' }
$OpenStatuses = @(1, 659490000)
$script:Invoice = @{}
function Get-LineInvoice([string]$InvoiceId) {
    if (-not $InvoiceId) { return @{ Status = '(no invoice)'; Open = $false; ModifiedOn = $null } }
    if (-not $script:Invoice.ContainsKey($InvoiceId)) {
        $inv = Get-DvOrNull "sprk_invoices($InvoiceId)?`$select=statuscode,modifiedon"
        $script:Invoice[$InvoiceId] = if ($null -eq $inv) { @{ Status = '(invoice not found)'; Open = $false; ModifiedOn = $null } }
        else {
            $code = [int]$inv.statuscode
            @{ Status = $(if ($StatusLabels.ContainsKey($code)) { $StatusLabels[$code] } else { "status $code" }); Open = ($code -in $OpenStatuses)
                ModifiedOn = ([datetimeoffset]$inv.modifiedon).UtcDateTime }
        }
    }
    return $script:Invoice[$InvoiceId]
}

Write-Host ""
Write-Host "eBilling ISS-051 - backfill the Invoice Line code lookups from the coding JSON" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Mode: $(if ($Apply) { 'APPLY (writes)' } else { 'REPORT (read only)' })   Closed invoices: $(if ($IncludeClosed) { 'INCLUDED' } else { 'skipped' })"
Write-Host ""

$counts = [ordered]@{ lines = 0; noCoding = 0; skipped = 0; linesToSet = 0; linesSet = 0; changed = 0; slotsOk = 0; slotsSet = 0; unresolved = 0; conflict = 0 }
$byStatus = [ordered]@{}   # invoice status label -> lines, lines with a slot to set, slots to set, included
$affected = @{}            # invoice id -> its modifiedon (UTC), for the invoices whose lines are (or would be) written
try {
    $nav = Get-NavigationProperties
    $lines = Get-AllPages ("sprk_billingevents?`$select=sprk_billingeventid,sprk_name,sprk_extractionconfidence,_sprk_invoice_value,_sprk_taskcode_value,_sprk_activitycode_value,_sprk_expensecode_value" +
        "&`$filter=(sprk_costtype eq $FeeCostType or sprk_costtype eq $ExpenseCostType) and sprk_extractionconfidence ne null&`$orderby=createdon asc")
    Write-Host ("Fee / Expense lines with a coding JSON: {0}" -f $lines.Count)
    Write-Host ""

    foreach ($line in $lines) {
        $counts.lines++
        $id = ([guid]$line.sprk_billingeventid).ToString('D')
        $label = "{0} ({1})" -f $line.sprk_name, $id
        $invoiceId = if ($line._sprk_invoice_value) { ([guid]$line._sprk_invoice_value).ToString('D') } else { '' }
        $invoice = Get-LineInvoice $invoiceId
        $included = $invoice.Open -or $IncludeClosed
        if (-not $byStatus.Contains($invoice.Status)) { $byStatus[$invoice.Status] = [ordered]@{ lines = 0; linesToSet = 0; slotsToSet = 0; included = $included } }
        $bucket = $byStatus[$invoice.Status]
        $bucket.lines++

        $coding = $null
        try { $coding = ($line.sprk_extractionconfidence | ConvertFrom-Json -Depth 50).coding } catch { $coding = $null }

        # Every slot is evaluated; its tally reaches the run's counts only when the line is included.
        $body = @{}
        $notes = [System.Collections.Generic.List[string]]::new()
        $tally = @{ ok = 0; set = 0; unresolved = 0; conflict = 0 }
        foreach ($slot in $Kinds.Keys) {
            $kind = $Kinds[$slot]
            $current = [string]$line."_$($kind.Column)_value"
            $code = if ($null -ne $coding -and $null -ne $coding.$slot) { ([string]$coding.$slot.code).Trim() } else { '' }

            if (-not $code) {
                if ($current) { $tally.conflict++; $notes.Add("conflict  $slot - the lookup names $current but the coding has no $slot code; left as it is") }
                else { $tally.ok++ }
                continue
            }

            $row = Resolve-CodeRow $coding $slot $code
            if ($row.Conflict) { $tally.conflict++; $notes.Add("conflict  $slot - $($row.Why); left as it is"); continue }
            if ($current) {
                if ($row.Id -and $current -eq $row.Id) { $tally.ok++ }
                else { $tally.conflict++; $notes.Add("conflict  $slot - the lookup names $current, the coding's $code resolves to $(if ($row.Id) { $row.Id } else { 'no row' }); left as it is") }
                continue
            }

            if (-not $row.Id) { $tally.unresolved++; $notes.Add("unresolved $slot - $($row.Why); left empty"); continue }
            $body["$($nav[$kind.Column])@odata.bind"] = "/sprk_billingcodes($($row.Id))"
            $tally.set++
            $notes.Add("$(if ($Apply) { 'set' } else { 'WOULD SET' }) $slot -> $($row.Id) ($($row.Why))")
        }

        if ($body.Count -gt 0) { $bucket.linesToSet++; $bucket.slotsToSet += $tally.set }
        if (-not $included) {
            # FR-41 / D-56: AP already received this invoice's as-billed codes, so its lines keep matching what AP got.
            $counts.skipped++
            if ($Detail) { Write-Host "  $label  skipped: the invoice is $($invoice.Status)" -ForegroundColor DarkGray }
            continue
        }

        if ($null -eq $coding -or [string]$coding.status -eq 'NotCoded') { $counts.noCoding++ }
        $counts.slotsOk += $tally.ok; $counts.slotsSet += $tally.set; $counts.unresolved += $tally.unresolved; $counts.conflict += $tally.conflict
        if ($body.Count -gt 0) { $counts.linesToSet++ }
        if ($notes.Count -gt 0 -or $Detail) {
            Write-Host "  $label  [$($invoice.Status)]" -ForegroundColor $(if ($tally.conflict -or $tally.unresolved) { 'Yellow' } else { 'Gray' })
            foreach ($n in $notes) { Write-Host "      $n" }
        }
        if ($body.Count -eq 0) { continue }

        if (-not $Apply) {
            if ($invoiceId) { $affected[$invoiceId] = $invoice.ModifiedOn }
            continue
        }
        # If-Match is the row version read above: a line changed since the read (a reviewer's correction, a re-run) is refused with 412 and
        # never overwritten.
        try {
            Invoke-Dv -Path "sprk_billingevents($id)" -Method PATCH -ExtraHeaders @{ 'If-Match' = [string]$line.'@odata.etag' } -Body $body | Out-Null
            $counts.linesSet++
            if ($invoiceId) { $affected[$invoiceId] = $invoice.ModifiedOn }
        }
        catch {
            if ($_.Exception.Data['Status'] -ne 412) { throw }
            $counts.changed++
            Write-Host "      changed since read (412): not written; re-run the script" -ForegroundColor Yellow
        }
    }
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Lines already set stay set (the script is re-runnable: a set lookup reads as ok)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("lines read                 : {0}" -f $counts.lines)
Write-Host ("  skipped (invoice status) : {0}{1}" -f $counts.skipped, $(if ($IncludeClosed) { '' } else { '   (-IncludeClosed includes them)' }))
Write-Host ("  of the rest, not coded   : {0}" -f $counts.noCoding)
Write-Host ("lines with a slot to set   : {0}" -f $counts.linesToSet)
Write-Host ("slots {0}{1}: {2}" -f $(if ($Apply) { 'set' } else { 'that would be set' }), $(if ($Apply) { '                  ' } else { '    ' }), $counts.slotsSet)
Write-Host ("slots already ok           : {0}" -f $counts.slotsOk)
Write-Host ("slots unresolved           : {0}" -f $counts.unresolved)
Write-Host ("slots in conflict          : {0}" -f $counts.conflict)
if ($Apply) {
    Write-Host ("lines written              : {0}" -f $counts.linesSet)
    Write-Host ("lines changed since read   : {0}{1}" -f $counts.changed, $(if ($counts.changed) { '   (not written; re-run the script)' } else { '' }))
}

Write-Host ""
Write-Host "By invoice status (every line read):" -ForegroundColor Cyan
Write-Host ("  {0,-20} {1,6} {2,20} {3,13}  {4}" -f 'status', 'lines', 'with a slot to set', 'slots to set', '')
foreach ($s in $byStatus.Keys) {
    $b = $byStatus[$s]
    Write-Host ("  {0,-20} {1,6} {2,20} {3,13}  {4}" -f $s, $b.lines, $b.linesToSet, $b.slotsToSet, $(if ($b.included) { 'included' } else { 'skipped' }))
}

# The invoice index reads the lookups, but it is rebuilt only when the invoice changes, and a line PATCH does not move
# sprk_invoice.modifiedon. The re-index sweep queues the invoices modified on or after -Since (a UTC date), so the oldest affected
# invoice's modifiedon covers all of them (and possibly more, which is harmless: a re-index rewrites the same search row).
Write-Host ""
if ($affected.Count -gt 0) {
    $oldest = $affected.Values | Where-Object { $_ } | Sort-Object | Select-Object -First 1
    $since = if ($oldest) { $oldest.ToString('yyyy-MM-dd') } else { '<oldest affected invoice modifiedon>' }
    Write-Host ("Invoices whose index must be rebuilt{0}: {1}" -f $(if ($Apply) { '' } else { ' once -Apply has written their lines' }), $affected.Count) -ForegroundColor Cyan
    foreach ($k in ($affected.Keys | Sort-Object)) { Write-Host "  $k" }
    Write-Host "Oldest affected invoice modifiedon (UTC): $(if ($oldest) { $oldest.ToString('yyyy-MM-ddTHH:mm:ssZ') } else { 'unknown' })"
    Write-Host "After -Apply, queue their re-index (dry run first, then -Apply):" -ForegroundColor Cyan
    Write-Host "  pwsh scripts/ebilling/Invoke-EbillingInvoiceReindexSweep.ps1 -DataverseUrl $EnvironmentUrl -Since $since -Change iss051-lookups"
    Write-Host "  pwsh scripts/ebilling/Invoke-EbillingInvoiceReindexSweep.ps1 -DataverseUrl $EnvironmentUrl -Since $since -Change iss051-lookups -Apply"
}
else { Write-Host "No invoice needs a re-index." }

if (-not $Apply) { Write-Host ""; Write-Host "Report mode: nothing was written. -Apply writes the slots counted above (owner's OK first)." -ForegroundColor Cyan }
exit 0
