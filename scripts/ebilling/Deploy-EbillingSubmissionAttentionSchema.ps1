<#
.SYNOPSIS
    eBilling R1 task 049 (owner decision D-133) - the "Needs Admin Attention" column on sprk_invoicesubmission and the admin view
    "Submissions needing attention".

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-act; a second run reports every item as
    skipped / unchanged and makes no write (no publish either).

    Items:
      1  sprk_invoicesubmission.sprk_adminattention - Text, 500, optional. "Needs Admin Attention": why this submission needs an
         administrator's review (empty when nothing does). The BFF sets it, server-side only, when a billing Signal the submission
         should have could not be created (InvoiceSubmissionPersister for a plan entry the Signal writer would refuse,
         InvoiceSubmissionAfterSteps for a permanent write refusal). Codes only, never an extracted value; the detail stays in
         sprk_findingsjson. Code never clears it: the administrator does, after review.
      2  The public system view "Submissions needing attention" on sprk_invoicesubmission.
           Filter   sprk_adminattention is not empty  OR  sprk_processingstatus = Failed (100000004)
           Columns  sprk_name, sprk_trackingnumber, sprk_vendororg (firm), sprk_processingstatus, sprk_outcomereasoncode,
                    sprk_adminattention, sprk_receivedon, modifiedon
           Sort     modifiedon, descending
         Created in the solution (header MSCRM.SolutionUniqueName), or updated when it exists with another filter or columns.

    Section 11 of the repository rules: nothing overlaps (sprk_outcomereasoncode covers only the terminal Failed / Rejected reason of
    a submission; sprk_backgroundjobrun is unused and for scheduled jobs), the existing submission table is extended by one column
    and one view, and without it an AutoRejected invoice can end with no Signal while the only trace is an Error log the customer
    administrator never sees. A platform-wide process-error table is out of scope (recorded in the decisions log as a future platform
    proposal).

    NEVER deletes or renames anything. A same-name column of another type, or an existing column shorter than 500, STOPS the script
    with exit code 2 and an ESCALATION message. Publish is entity-scoped (PublishXml for sprk_invoicesubmission, never a tenant-wide publish),
    and only when this run wrote metadata. Pending (unpublished) forms / views of sprk_invoicesubmission that this script does not
    edit would be published too; the script lists them and refuses to publish unless -AllowPendingCollateral is given (D-103).

    The column is not placed on the main form here: the form is Deploy-EbillingFormsViews.ps1's (its spec table). The view needs no
    form, and an editable grid or the form lets the administrator clear the value.

    This script is NOT run by the task that adds it. The main session runs it live (needs `az login` to the environment).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.PARAMETER AllowPendingCollateral
    Continue to publish when another pending (unpublished) form / view of sprk_invoicesubmission would be published too.

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingSubmissionAttentionSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingSubmissionAttentionSchema.ps1

.NOTES
    Project: ebilling-r1, task 049 (D-133). Dev only in R1. Pattern: scripts/ebilling/Deploy-EbillingInvoiceTrackingNumberSchema.ps1
    (column) and scripts/ebilling/Deploy-EbillingFormsViews.ps1 (the view).
    Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (conflicting, or pending collateral).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$AllowPendingCollateral
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$Table = 'sprk_invoicesubmission'
$ColumnName = 'sprk_adminattention'
$ViewName = 'Submissions needing attention'
$FailedStatus = 100000004   # sprk_processingstatus: Failed
$ViewColumns = 'sprk_name', 'sprk_trackingnumber', 'sprk_vendororg', 'sprk_processingstatus', 'sprk_outcomereasoncode', 'sprk_adminattention', 'sprk_receivedon', 'modifiedon'
$ViewDescription = 'Submissions an administrator should review: a billing Signal could not be created (Needs Admin Attention is set), or processing failed. Clear Needs Admin Attention after review (D-133).'

$ColumnDescription = 'Why this submission needs an administrator''s review; empty when nothing does. An administrator clears it after review.'

# ---- view XML (pure; the offline check runs these) -------------------------------------------------------------------------
function New-AttentionViewXml([int]$ObjectTypeCode, [string]$PrimaryId, [string[]]$Cols, [int]$Failed) {
    $attrs = ($Cols | ForEach-Object { "<attribute name=""$_"" />" }) -join ''
    $fetch = "<fetch version=""1.0"" mapping=""logical""><entity name=""sprk_invoicesubmission""><attribute name=""$PrimaryId"" />$attrs<order attribute=""modifiedon"" descending=""true"" /><filter type=""or""><condition attribute=""sprk_adminattention"" operator=""not-null"" /><condition attribute=""sprk_processingstatus"" operator=""eq"" value=""$Failed"" /></filter></entity></fetch>"
    $cells = ($Cols | ForEach-Object { $w = if ($_ -in 'sprk_name', 'sprk_adminattention') { 220 } else { 150 }; "<cell name=""$_"" width=""$w"" />" }) -join ''
    $layout = "<grid name=""resultset"" object=""$ObjectTypeCode"" jump=""sprk_name"" select=""1"" icon=""1"" preview=""1""><row name=""result"" id=""$PrimaryId"">$cells</row></grid>"
    return @{ Fetch = $fetch; Layout = $layout }
}
function Get-CellNames([string]$LayoutXml) { return @(([xml]$LayoutXml).SelectNodes('//cell') | ForEach-Object { $_.GetAttribute('name') }) }
function Get-FilterXml([string]$FetchXml) { return ([xml]$FetchXml).SelectSingleNode('//entity/filter').OuterXml }
function Get-OrderXml([string]$FetchXml) { return ([xml]$FetchXml).SelectSingleNode('//entity/order').OuterXml }

. (Join-Path $PSScriptRoot '..' 'lib' 'Publish-SolutionComponents.ps1')
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Table
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

Write-Host ""
Write-Host "eBilling task 049 - $Table.$ColumnName and view '$ViewName' (D-133)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    $meta = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName,ObjectTypeCode,PrimaryIdAttribute" -Retries 4
    if (-not $meta) { throw "$Table does not exist in $EnvironmentUrl." }

    # ---- 1. the column ------------------------------------------------------------------------------------------------------
    Write-Host "1. $Table.$ColumnName" -ForegroundColor Cyan
    Add-Column $ColumnName (New-StringDef 'sprk_AdminAttention' 'Needs Admin Attention' $ColumnDescription 500) 'String'

    # An existing column must also be long enough: the BFF caps its text at 500; a shorter column would fault the submission update.
    $a = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$ColumnName')/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength" -Retries 4
    if ($a -and [int]$a.MaxLength -lt 500) {
        throw [EscalationException]::new("$Table.$ColumnName exists with max length $($a.MaxLength), but the BFF writes up to 500 characters. Owner decision needed (this script does not alter an existing column).")
    }

    # ---- 2. the view --------------------------------------------------------------------------------------------------------
    Write-Host "2. view '$ViewName' on $Table" -ForegroundColor Cyan
    foreach ($c in $ViewColumns) {
        # In a WhatIf run the column of this script does not exist yet.
        if ($DryRun -and $c -eq $ColumnName) { continue }
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$c')?`$select=LogicalName" -Retries 2)) {
            throw "$Table.$c does not exist in $EnvironmentUrl (a column of the view)."
        }
    }
    $want = New-AttentionViewXml ([int]$meta.ObjectTypeCode) $meta.PrimaryIdAttribute $ViewColumns $FailedStatus
    $rows = @((Invoke-Dv -Path "savedqueries?`$select=savedqueryid,fetchxml,layoutxml,description&`$filter=returnedtypecode eq '$Table' and name eq '$ViewName' and querytype eq 0").value)
    $viewIds = [System.Collections.Generic.List[string]]::new()
    if ($rows.Count -eq 0) {
        if ($DryRun) {
            $script:Created.Add("view $ViewName"); Write-Host "  WOULD CREATE view '$ViewName'" -ForegroundColor Cyan
        } else {
            $body = @{ name = $ViewName; description = $ViewDescription; returnedtypecode = $Table; querytype = 0; isdefault = $false; fetchxml = $want.Fetch; layoutxml = $want.Layout }
            Invoke-Dv -Path 'savedqueries' -Method POST -Body $body -ExtraHeaders $State.SolutionHeader | Out-Null
            $new = @((Invoke-Dv -Path "savedqueries?`$select=savedqueryid&`$filter=returnedtypecode eq '$Table' and name eq '$ViewName' and querytype eq 0").value)[0]
            $viewIds.Add($new.savedqueryid)
            $script:Created.Add("view $ViewName"); Write-Host "  CREATED  view '$ViewName' $($new.savedqueryid)" -ForegroundColor Green
        }
    } else {
        $cur = $rows[0]
        $viewIds.Add($cur.savedqueryid)
        $same = ((Get-FilterXml $cur.fetchxml) -eq (Get-FilterXml $want.Fetch)) -and
                ((Get-OrderXml $cur.fetchxml) -eq (Get-OrderXml $want.Fetch)) -and
                (((Get-CellNames $cur.layoutxml) -join ',') -eq ((Get-CellNames $want.Layout) -join ','))
        if ($same) {
            $script:Skipped.Add("view $ViewName"); Write-Host "  skipped  view '$ViewName' (filter, sort and columns as wanted)" -ForegroundColor Yellow
        } elseif ($DryRun) {
            $script:Changed.Add("view $ViewName"); Write-Host "  WOULD UPDATE view '$ViewName' ($($cur.savedqueryid))" -ForegroundColor Cyan
        } else {
            Invoke-Dv -Path "savedqueries($($cur.savedqueryid))" -Method PATCH -Body @{ fetchxml = $want.Fetch; layoutxml = $want.Layout; description = $ViewDescription } | Out-Null
            $script:Changed.Add("view $ViewName"); Write-Host "  CHANGED  view '$ViewName' ($($cur.savedqueryid))" -ForegroundColor Green
        }
    }

    # ---- publish ------------------------------------------------------------------------------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } else {
        # PublishXml of an entity publishes every pending form / view of it (it has no per-view element): list those this script did
        # not write and refuse unless the owner agrees (D-103). The token is read here, not for a WhatIf run.
        $Ctx = Get-DataverseApiContext -EnvironmentUrl $EnvironmentUrl
        $unpublished = @(Get-EntityPublishCollateral -Context $Ctx -Entity $Table)
        $others = @(Get-EntityPublishCollateral -Context $Ctx -Entity $Table -ExcludeIds $viewIds)
        $own = @($unpublished | Where-Object { $others -notcontains $_ })
        # A re-run after a refused publish finds the view already written: it is still unpublished, so it still needs the publish.
        if (-not $wrote -and $own.Count -eq 0) {
            Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
        } else {
            if ($others.Count -gt 0 -and -not $AllowPendingCollateral) {
                Write-Host "  REFUSING to publish: pending forms / views this script does not edit would go live too:" -ForegroundColor Red
                $others | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
                Write-Host "  Re-run with -AllowPendingCollateral after the owner of those changes agrees (the column and the view are already written)." -ForegroundColor Red
                exit 2
            }
            if ($others.Count -gt 0) { Write-Host "  -AllowPendingCollateral: publishing $($others.Count) other pending item(s) of $Table" -ForegroundColor Yellow }
            Invoke-PublishXml -Context $Ctx -ParameterXml "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>"
            Write-Host "  published $Table" -ForegroundColor Green
        }
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
