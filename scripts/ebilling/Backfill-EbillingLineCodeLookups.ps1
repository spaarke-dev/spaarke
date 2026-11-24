<#
.SYNOPSIS
    eBilling R1 ISS-051 (#3080) - backfill the Invoice Line code lookups (sprk_billingevent.sprk_taskcode / sprk_activitycode /
    sprk_expensecode) from the resolved codes in each line's coding JSON.

.DESCRIPTION
    Until ISS-051 no writer set the three lookups: the resolved UTBMS codes lived only in the "coding" member of the line's
    sprk_extractionconfidence JSON. Since ISS-051 every writer of a resolved code (intake, the re-run after Set firm, a reviewer's code
    correction, Add line) sets the lookup in the same write, and the AP export and the invoice index read the lookup. This script brings
    the lines written before ISS-051 into line with their JSON, from that JSON and from nothing else.

    For each Fee / Expense line with a coding JSON, and each slot (task, activity, expense):
      ok          the lookup already names the slot's row (or there is no code and no lookup). Nothing to do.
      set         no lookup, and the slot's code resolves to exactly one sprk_billingcode row:
                    1. the row the JSON names (coding.<slot>.codeId), when it exists; else
                    2. the row of that code and kind in the code set the line was coded with (coding.codeSetId); else, for an activity or
                       expense code when that set holds no active code of the kind, the row in the shared "UTBMS Activity" / "UTBMS Expense"
                       set (the resolver's own rule, CodeSetResolver.LoadAsync).
      unresolved  the code matches no row, or more than one. Left empty (FR-22: a code is never stored outside the line's set) and listed.
      conflict    the lookup is set but names another row, or the slot has no code. Left as it is and listed for the owner.
    It never guesses, never writes the JSON, never clears a lookup, and never touches any other column.

    REPORT MODE IS THE DEFAULT and is read only: it prints every line that would change and a summary. -Apply writes: one PATCH per line
    (If-Match '*', so it never creates a row) that binds only the slots counted as "set". -Apply is a live write and needs the owner's typed OK
    in the main session; the task that adds this script does not run it. Re-runnable: a line it set reads as "ok" the next time.

    Every Web API call goes through EbillingSchema.psm1 (Invoke-Dv), which sends Consistency: Strong.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER Apply
    Write the lookups counted as "set". Without it the script only reports.

.PARAMETER Detail
    Also print the "ok" lines (report mode prints only lines with something to set, unresolved or in conflict).

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

$script:Exists = @{}
function Test-CodeRowExists([string]$Id) {
    if (-not $script:Exists.ContainsKey($Id)) {
        $script:Exists[$Id] = $null -ne (Get-DvOrNull "sprk_billingcodes($Id)?`$select=sprk_billingcodeid")
    }
    return $script:Exists[$Id]
}

# The row a slot's code resolves to: @{ Id = <guid or $null>; Why = <text> }.
function Resolve-CodeRow($Coding, [string]$Slot, [string]$Code) {
    $kind = $Kinds[$Slot]
    $recorded = [string]$Coding.$Slot.codeId
    $parsed = [guid]::Empty
    if ($recorded -and [guid]::TryParse($recorded, [ref]$parsed) -and (Test-CodeRowExists $parsed.ToString('D'))) {
        return @{ Id = $parsed.ToString('D'); Why = 'the row the JSON names' }
    }

    $setText = [string]$Coding.codeSetId
    if (-not $setText -or -not [guid]::TryParse($setText, [ref]$parsed)) { return @{ Id = $null; Why = 'the coding names no code set' } }
    $setId = $parsed.ToString('D')
    $where = "the line's set $setId"
    if ($kind.Shared -and -not (Test-SetHasKind $setId $kind.Type)) {
        $setId = Get-SetIdByName $kind.Shared
        if (-not $setId) { return @{ Id = $null; Why = "the shared set '$($kind.Shared)' was not found" } }
        $where = "the shared set '$($kind.Shared)'"
    }

    $rows = @(Get-CodeRows $setId $kind.Type $Code)
    if ($rows.Count -eq 1) { return @{ Id = $rows[0]; Why = "$Code in $where" } }
    return @{ Id = $null; Why = "$Code matches $($rows.Count) rows in $where" }
}

Write-Host ""
Write-Host "eBilling ISS-051 - backfill the Invoice Line code lookups from the coding JSON" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Mode: $(if ($Apply) { 'APPLY (writes)' } else { 'REPORT (read only)' })"
Write-Host ""

$counts = [ordered]@{ lines = 0; noCoding = 0; linesToSet = 0; linesSet = 0; slotsOk = 0; slotsSet = 0; unresolved = 0; conflict = 0 }
try {
    $nav = Get-NavigationProperties
    $lines = Get-AllPages ("sprk_billingevents?`$select=sprk_billingeventid,sprk_name,sprk_extractionconfidence,_sprk_taskcode_value,_sprk_activitycode_value,_sprk_expensecode_value" +
        "&`$filter=(sprk_costtype eq $FeeCostType or sprk_costtype eq $ExpenseCostType) and sprk_extractionconfidence ne null&`$orderby=createdon asc")
    Write-Host ("Fee / Expense lines with a coding JSON: {0}" -f $lines.Count)
    Write-Host ""

    foreach ($line in $lines) {
        $counts.lines++
        $id = ([guid]$line.sprk_billingeventid).ToString('D')
        $label = "{0} ({1})" -f $line.sprk_name, $id
        $coding = $null
        try { $coding = ($line.sprk_extractionconfidence | ConvertFrom-Json -Depth 50).coding } catch { $coding = $null }
        if ($null -eq $coding -or [string]$coding.status -eq 'NotCoded') { $counts.noCoding++ }

        $body = @{}
        $notes = [System.Collections.Generic.List[string]]::new()
        foreach ($slot in $Kinds.Keys) {
            $kind = $Kinds[$slot]
            $current = [string]$line."_$($kind.Column)_value"
            $code = if ($null -ne $coding -and $null -ne $coding.$slot) { ([string]$coding.$slot.code).Trim() } else { '' }

            if (-not $code) {
                if ($current) { $counts.conflict++; $notes.Add("conflict  $slot - the lookup names $current but the coding has no $slot code; left as it is") }
                else { $counts.slotsOk++ }
                continue
            }

            $row = Resolve-CodeRow $coding $slot $code
            if ($current) {
                if ($row.Id -and $current -eq $row.Id) { $counts.slotsOk++ }
                else { $counts.conflict++; $notes.Add("conflict  $slot - the lookup names $current, the coding's $code resolves to $(if ($row.Id) { $row.Id } else { 'no row' }); left as it is") }
                continue
            }

            if (-not $row.Id) { $counts.unresolved++; $notes.Add("unresolved $slot - $($row.Why); left empty"); continue }
            $body["$($nav[$kind.Column])@odata.bind"] = "/sprk_billingcodes($($row.Id))"
            $counts.slotsSet++
            $notes.Add("$(if ($Apply) { 'set' } else { 'WOULD SET' }) $slot -> $($row.Id) ($($row.Why))")
        }

        if ($body.Count -gt 0) { $counts.linesToSet++ }
        if ($notes.Count -gt 0 -or $Detail) {
            Write-Host "  $label" -ForegroundColor $(if ($notes | Where-Object { $_ -like 'conflict*' -or $_ -like 'unresolved*' }) { 'Yellow' } else { 'Gray' })
            foreach ($n in $notes) { Write-Host "      $n" }
        }

        if ($Apply -and $body.Count -gt 0) {
            Invoke-Dv -Path "sprk_billingevents($id)" -Method PATCH -ExtraHeaders @{ 'If-Match' = '*' } -Body $body | Out-Null
            $counts.linesSet++
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
Write-Host ("  of which not coded       : {0}" -f $counts.noCoding)
Write-Host ("lines with a slot to set   : {0}" -f $counts.linesToSet)
Write-Host ("slots {0}{1}: {2}" -f $(if ($Apply) { 'set' } else { 'that would be set' }), $(if ($Apply) { '                  ' } else { '    ' }), $counts.slotsSet)
Write-Host ("slots already ok           : {0}" -f $counts.slotsOk)
Write-Host ("slots unresolved           : {0}" -f $counts.unresolved)
Write-Host ("slots in conflict          : {0}" -f $counts.conflict)
if ($Apply) { Write-Host ("lines written              : {0}" -f $counts.linesSet) }
else { Write-Host "Report mode: nothing was written. -Apply writes the slots counted above (owner's OK first)." -ForegroundColor Cyan }
exit 0
