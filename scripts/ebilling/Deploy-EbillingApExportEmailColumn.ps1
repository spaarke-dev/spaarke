<#
.SYNOPSIS
    eBilling R1 task 151c-1a (EB-D-181) - the "AP export email" column on sprk_ebillingsetting.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. The item is check-then-create; a second run reports it as skipped and
    makes no write (no publish either).

    Item:
      1  sprk_ebillingsetting.sprk_apexportemail - Text, Email format, max length 320, optional. The one address "Email to AP" sends a
         batch's CSV and JSON to (EB-D-181). Empty means no recipient is set, and the Email to AP route answers 409
         ebilling.export.ap_email_not_configured. 320 is the longest valid address (64 local part, @, 255 domain).

    An existing column that is not a Text column, or is not Email format, or is shorter than 320, STOPS the script with exit code 2 and an
    ESCALATION message (nothing is altered or renamed). Publish is entity-scoped (PublishXml for sprk_ebillingsetting), and only when
    this run wrote metadata.

    The column is not placed on a form here; the settings main form's "AP export" section is Deploy-EbillingFormsViews.ps1's (Spec), and
    the SpaarkeMaster source form carries it. This script is NOT run by the task that adds it; the owner's OK is needed to run it on dev.
    -WhatIf is read-only and may be run against dev at any time.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created component is added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingApExportEmailColumn.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingApExportEmailColumn.ps1

.NOTES
    Project: ebilling-r1, task 151c-1a (EB-D-181). Dev only in R1. Pattern: scripts/ebilling/Deploy-EbillingInvoiceTrackingNumberSchema.ps1.
    Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (conflicting).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster'
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$Table = 'sprk_ebillingsetting'
$Column = 'sprk_apexportemail'

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Table
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

$Description = 'The address Email to AP sends an export batch''s CSV and JSON files to (EB-D-181). Leave empty until AP has given one; the Email to AP action is refused while it is empty.'

Write-Host ""
Write-Host "eBilling task 151c-1a - $Table.$Column (EB-D-181)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }

    Write-Host "1. $Table.$Column" -ForegroundColor Cyan
    Add-Column $Column (New-StringDef 'sprk_ApExportEmail' 'AP export email' $Description 320 'Email') 'String'

    # An existing column must also be Email format and long enough: Add-Column checks the type only, and a plain text column would let a
    # value the platform did not validate be the recipient, while a shorter one would cut a long address off.
    $a = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength,FormatName" -Retries 4
    if ($a) {
        if ($a.FormatName.Value -ne 'Email') {
            throw [EscalationException]::new("$Table.$Column exists with format '$($a.FormatName.Value)', but this task needs Email. Owner decision needed (this script does not alter an existing column).")
        }
        if ([int]$a.MaxLength -lt 320) {
            throw [EscalationException]::new("$Table.$Column exists with max length $($a.MaxLength), but an address can be 320 characters. Owner decision needed (this script does not shorten or alter an existing column).")
        }
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
    Write-Host "Items already created stay (the script is re-runnable)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("created : {0}  {1}" -f $script:Created.Count, ($script:Created -join ', '))
Write-Host ("changed : {0}  {1}" -f $script:Changed.Count, ($script:Changed -join ', '))
Write-Host ("skipped : {0}" -f $script:Skipped.Count)
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
