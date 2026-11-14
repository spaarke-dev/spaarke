<#
.SYNOPSIS
    eBilling R1 task 095g (EB-D-234) - sets sprk_organizationtype_ref.sprk_organizationtypecode = LAWFIRM on the "Law Firm" type row.

.DESCRIPTION
    Idempotent DATA script (no metadata). The firm-assign routes (GET /api/finance/firms, POST /api/finance/core-records/{type}/{id}/firms)
    match the Law Firm organization type by this stable code, never by its id (which differs per environment) or its name. Until exactly one
    active type row carries the code, both routes fail closed with 503 - they never treat every organization as a law firm.

    What it does:
      1  Reads the active type rows that already carry the code. Exactly one: nothing to do (skipped, no write). More than one: ESCALATION.
         None, but an INACTIVE row carries it: ESCALATION (the code is unique across all rows, so it cannot be set on another one).
      2  Otherwise finds the type row to mark: by -TypeId when given, else by its name (-TypeName, default "Law Firm", exact match, active
         rows only). None, or more than one by name: ESCALATION.
      3  A row that already carries a DIFFERENT non-empty code is never overwritten: ESCALATION.
      4  Sets the code on that one row (PATCH of the one column), then reads it back.

    The code is the constant LawFirmOrganizationType.Code in
    src/server/api/Sprk.Bff.Api/Services/Finance/CoreRecordFirmAssignmentService.cs; a test pins the two values equal.

    This script is NOT run by the task that adds it. It is a live data write, so it runs only with the owner's OK, at 095g's deploy.
    -WhatIf is read-only and may be run against dev at any time.

    Setting the code does not change which firms are assigned anywhere. Organizations with no type (or another type) are not offered for a
    NEW add until their type is set; a clear of such a firm still works.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER TypeName
    The display name of the type row to mark when no row carries the code yet. Default "Law Firm".

.PARAMETER TypeId
    The id of the type row to mark, instead of finding it by name.

.EXAMPLE
    pwsh scripts/ebilling/Set-EbillingLawFirmTypeCode.ps1 -WhatIf
    pwsh scripts/ebilling/Set-EbillingLawFirmTypeCode.ps1

.NOTES
    Project: ebilling-r1, task 095g (EB-D-234). Pattern: scripts/ebilling/Deploy-EbillingInvoiceCorrectionNoteColumn.ps1 (check then write,
    escalation on a conflict). Auth: the operator's own az CLI identity. No secrets.
    Exit codes: 0 = done (set / skipped), 1 = error, 2 = escalation (nothing written).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$TypeName = 'Law Firm',
    [guid]$TypeId = [guid]::Empty
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference

# The ONE code the server matches (LawFirmOrganizationType.Code). Keep it equal to the C# constant: a test reads this line.
$LawFirmTypeCode = 'LAWFIRM'

$EntitySet = 'sprk_organizationtype_refs'
$IdColumn = 'sprk_organizationtype_refid'
$CodeColumn = 'sprk_organizationtypecode'
$NameColumn = 'sprk_name'

$null = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName 'SpaarkeMaster' -DryRun:$DryRun

function Get-TypeRows([string]$Filter) {
    $path = "$EntitySet`?`$select=$IdColumn,$NameColumn,$CodeColumn,statecode&`$filter=$Filter"
    return @((Invoke-Dv -Path $path).value)
}

function Format-OData([string]$Text) { return $Text.Replace("'", "''") }

Write-Host ""
Write-Host "eBilling task 095g - Law Firm organization type code (EB-D-234)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Code: $LawFirmTypeCode   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

$result = 'skipped'
try {
    # 1. Already set?
    $coded = Get-TypeRows "$CodeColumn eq '$(Format-OData $LawFirmTypeCode)' and statecode eq 0"
    if ($coded.Count -gt 1) {
        throw [EscalationException]::new("$($coded.Count) active type rows already carry the code $LawFirmTypeCode; exactly one may. Owner decision needed (this script changes none of them).")
    }
    if ($coded.Count -eq 1) {
        Write-Host "  '$($coded[0].$NameColumn)' ($($coded[0].$IdColumn)) already carries $LawFirmTypeCode - nothing to do." -ForegroundColor Yellow
    }
    else {
        # 1b. The code is unique across ALL type rows (an alternate key), inactive ones included, so an inactive row holding it would make
        #     the write below fail on the duplicate key. Stop before writing instead (review 1).
        $inactiveCoded = Get-TypeRows "$CodeColumn eq '$(Format-OData $LawFirmTypeCode)' and statecode ne 0"
        if ($inactiveCoded.Count -gt 0) {
            $names = ($inactiveCoded | ForEach-Object { "'$($_.$NameColumn)' ($($_.$IdColumn))" }) -join ', '
            throw [EscalationException]::new("No active type row carries the code $LawFirmTypeCode, but the inactive type row(s) $names do. The code is unique across all type rows, so it cannot be set on another row. Owner decision needed: reactivate that row, or clear its code, then re-run (this script changes neither).")
        }

        # 2. The row to mark.
        if ($TypeId -ne [guid]::Empty) {
            $candidates = Get-TypeRows "$IdColumn eq $TypeId and statecode eq 0"
            $what = "id $TypeId"
        }
        else {
            $candidates = Get-TypeRows "$NameColumn eq '$(Format-OData $TypeName)' and statecode eq 0"
            $what = "name '$TypeName'"
        }
        if ($candidates.Count -eq 0) {
            throw [EscalationException]::new("No active organization type row has $what. Create or name the Law Firm type first, or pass -TypeId.")
        }
        if ($candidates.Count -gt 1) {
            throw [EscalationException]::new("$($candidates.Count) active organization type rows have $what. Pass -TypeId to choose one.")
        }
        $row = $candidates[0]

        # 3. Never overwrite another code.
        $existing = [string]$row.$CodeColumn
        if (-not [string]::IsNullOrWhiteSpace($existing) -and $existing -ne $LawFirmTypeCode) {
            throw [EscalationException]::new("Type row '$($row.$NameColumn)' ($($row.$IdColumn)) already carries the code '$existing'. Owner decision needed (this script does not overwrite a code).")
        }

        # 4. Set it.
        if ($DryRun) {
            Write-Host "  WhatIf: would set $CodeColumn = $LawFirmTypeCode on '$($row.$NameColumn)' ($($row.$IdColumn))" -ForegroundColor Cyan
            $result = 'planned'
        }
        else {
            Invoke-Dv -Path "$EntitySet($($row.$IdColumn))" -Method PATCH -Body @{ $CodeColumn = $LawFirmTypeCode } -ExtraHeaders @{ 'If-Match' = '*' } | Out-Null
            $check = Get-TypeRows "$IdColumn eq $($row.$IdColumn)"
            if ($check.Count -ne 1 -or [string]$check[0].$CodeColumn -ne $LawFirmTypeCode) {
                throw "The code was written but did not read back on $($row.$IdColumn)."
            }
            Write-Host "  set $CodeColumn = $LawFirmTypeCode on '$($row.$NameColumn)' ($($row.$IdColumn))" -ForegroundColor Green
            $result = 'set'
        }
    }
}
catch [EscalationException] {
    Write-Host ""
    Write-Host "ESCALATION - STOPPED, nothing was written: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "A re-run is safe (it never writes twice), but it stops the same way until the cause above is fixed. A duplicate-key answer means another type row already holds the code." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Result: $result"
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
