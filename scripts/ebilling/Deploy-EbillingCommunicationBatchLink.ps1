<#
.SYNOPSIS
    eBilling R1 task 151c-2 (EB-D-182) - the "Export Batch" reference from sprk_communication to sprk_billingexport.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every step is check-then-act; a second run reports the column as skipped,
    re-verifies the relationship's cascade configuration, makes no write and no publish.

    Item:
      1  sprk_communication.sprk_billingexport - Lookup -> sprk_billingexport, "Export Batch", optional, through the one-to-many
         relationship sprk_communication_billingexport_billingexport. The BFF writes it, in the same create as the row, when "Email to AP"
         sends a batch's files (ServerSendOptions.BillingExportId); no client sets it. It lets the batch form list its AP emails.

    WHAT IT IS NOT. A plain reference, not a filing and not a regarding. It is not a sprk_regarding* column, it is not an ownership parent
    or a lineage edge, and it must stay out of RecordFamilyEdges, SecureChildLineage, the regarding maps and the communication filing
    fields (tasks 151c-1a EB-D-184, 151c-2; guarded by CommunicationBatchLinkNotFilingGuardTests).

    CASCADE (an access requirement, agreed with unified-access-control-r2). The relationship is created with NO cascade on Assign, Share,
    Unshare, Reparent and Merge (and RollupView), and RemoveLink on Delete (Restrict is not wanted: a batch is deletable). A cascading
    assign or share on the batch must never change who owns or can read the email. The script sets all of these explicitly, and verifies
    them on EVERY run, including a re-run: an existing relationship whose configuration differs STOPS the script with exit code 2
    (nothing is changed - the script never alters an existing relationship).

    ESCALATION (exit 2, before any write): sprk_communication.sprk_billingexport exists but is not a lookup to sprk_billingexport; a second
    lookup from sprk_communication to sprk_billingexport exists; the column is carried by a relationship with another SchemaName than
    sprk_communication_billingexport_billingexport; or the existing relationship's cascade differs from the above.

    Publish is entity-scoped (PublishXml for sprk_communication only, never tenant-wide), and only when this run wrote metadata. The
    Billing Export main form's "AP emails" subgrid is Deploy-EbillingFormsViews.ps1's (Spec) and is published with sprk_billingexport
    by that script. This script is NOT run by the task that adds it; the owner's OK is needed to run it on dev. -WhatIf is read-only and
    may be run against dev at any time.

    PRIVILEGES. The BFF writes the reference as its app identity (the sysadmin entity service), so no role change is needed for the send.
    A person needs no right on the new column to read the email.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created component is added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingCommunicationBatchLink.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingCommunicationBatchLink.ps1

.NOTES
    Project: ebilling-r1, task 151c-2 (EB-D-182, EB-D-184). Dev only in R1. Pattern: scripts/ebilling/Deploy-EbillingApExportEmailColumn.ps1 and
    Deploy-EbillingExportSchema.ps1 (the sprk_invoice.sprk_billingexport lookup, Add-Lookup).
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
$Table = 'sprk_communication'
$Target = 'sprk_billingexport'
$Column = 'sprk_billingexport'
$RelSchema = 'sprk_communication_billingexport_billingexport'

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Table
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

$Description = 'The AP export batch this email was sent for (Email to AP, EB-D-182). A plain reference written by the server in the same create as the row; not a regarding or a filing, and it does not decide who owns or can read the email.'

# The cascade the relationship must have, all seven members (Archive, a newer member, is not checked). No cascade anywhere, so a reassign
# or share of the batch can never reach the email; RemoveLink on delete, not Restrict.
$ExpectedCascade = [ordered]@{
    Assign     = 'NoCascade'
    Delete     = 'RemoveLink'
    Merge      = 'NoCascade'
    Reparent   = 'NoCascade'
    RollupView = 'NoCascade'
    Share      = 'NoCascade'
    Unshare    = 'NoCascade'
}

# The members of a relationship's CascadeConfiguration that differ from $ExpectedCascade ("Member is X, expected Y"); empty when exact.
function Get-CascadeMismatches($Actual) {
    $out = @()
    foreach ($k in $ExpectedCascade.Keys) {
        $have = if ($null -ne $Actual) { [string]$Actual.$k } else { '' }
        if ($have -ne $ExpectedCascade[$k]) { $out += "$k is '$have', expected '$($ExpectedCascade[$k])'" }
    }
    return $out
}

# The many-to-one relationships of sprk_communication that name sprk_billingexport (read only).
function Get-BatchRelationships {
    $r = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/ManyToOneRelationships?`$select=SchemaName,ReferencedEntity,ReferencingAttribute,CascadeConfiguration" -Retries 2
    return @(@($r.value) | Where-Object { $_.ReferencedEntity -eq $Target })
}

# Read-only pre-flight, before ANY write: a conflicting column, a second lookup, or an existing relationship with another cascade.
function Test-ExistingBatchLink {
    $conflict = [System.Collections.Generic.List[string]]::new()
    $col = Get-Attr $Table $Column
    if ($col) {
        $lk = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets" -Retries 2
        if ($col.AttributeType -ne 'Lookup' -or -not $lk -or @($lk.Targets) -notcontains $Target) {
            $conflict.Add("$Table.$Column already exists (type $($col.AttributeType), targets $(if ($lk) { @($lk.Targets) -join ',' } else { 'n/a' })) but is not a lookup to $Target")
        }
    }
    foreach ($rel in (Get-BatchRelationships)) {
        if ($rel.ReferencingAttribute -ne $Column) {
            $conflict.Add("$Table.$($rel.ReferencingAttribute) is already a second lookup to $Target (relationship $($rel.SchemaName))")
            continue
        }
        if ($rel.SchemaName -ne $RelSchema) {
            # The forms script's subgrid and the solution source name this relationship; a column carried by another one would leave the
            # subgrid pointing at nothing. Never renamed by script.
            $conflict.Add("$Table.$Column exists but its relationship is '$($rel.SchemaName)', not '$RelSchema' (the AP emails subgrid and the solution source use that name)")
            continue
        }
        $diff = @(Get-CascadeMismatches $rel.CascadeConfiguration)
        if ($diff.Count -gt 0) {
            $conflict.Add("relationship $($rel.SchemaName) exists with a different cascade configuration: $($diff -join '; '). A cascading assign or share on the batch could change who owns or can read the email")
        } else {
            Write-Host ("  ok       {0}: delete {1}; assign / share / unshare / reparent / merge / rollup view {2} (as required)" -f $rel.SchemaName, $rel.CascadeConfiguration.Delete, $rel.CascadeConfiguration.Assign)
        }
    }
    if ($conflict.Count -gt 0) {
        throw [EscalationException]::new("$Table cannot take the batch link as specified: $($conflict -join '; '). Owner decision needed (this script never alters an existing column or relationship). Nothing was changed.")
    }
}

Write-Host ""
Write-Host "eBilling task 151c-2 - $Table.$Column -> $Target (EB-D-182)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Target')?`$select=LogicalName" -Retries 4)) { throw "$Target does not exist in $EnvironmentUrl (run Deploy-EbillingExportSchema.ps1 first)." }

    Write-Host "0. Pre-flight (read only)" -ForegroundColor Cyan
    Test-ExistingBatchLink

    Write-Host "1. $Table.$Column" -ForegroundColor Cyan
    Add-Lookup -Column $Column -SchemaName 'sprk_BillingExport' -RelSchema $RelSchema -Target $Target `
        -Display 'Export Batch' -Description $Description -DeleteBehavior 'RemoveLink' `
        -Cascade @{ Assign = 'NoCascade'; Delete = 'RemoveLink'; Merge = 'NoCascade'; Reparent = 'NoCascade'; RollupView = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade' } `
        -MenuBehavior 'DoNotDisplay'

    # Verify what is there now (after a create, or on a re-run): the relationship must exist, with exactly the required cascade.
    Write-Host "2. Verify the cascade" -ForegroundColor Cyan
    if ($DryRun -and ($script:Created.Count -gt 0)) {
        Write-Host "  WhatIf: the relationship does not exist yet; it would be created with the cascade above" -ForegroundColor Cyan
    } else {
        $rels = $null
        for ($i = 0; $i -lt 12 -and -not $rels; $i++) {
            $rels = @(Get-BatchRelationships | Where-Object { $_.ReferencingAttribute -eq $Column })
            if ($rels.Count -eq 0) { $rels = $null; Start-Sleep -Seconds 5 }
        }
        if (-not $rels) { throw "No many-to-one relationship for $Table.$Column -> $Target was readable after the create." }
        $diff = @(Get-CascadeMismatches $rels[0].CascadeConfiguration)
        if ($diff.Count -gt 0) {
            throw [EscalationException]::new("relationship $($rels[0].SchemaName) reads back with a different cascade configuration: $($diff -join '; '). Not changed by this script.")
        }
        Write-Host ("  verified {0}: delete {1}; assign, share, unshare, reparent, merge, rollup view {2}" -f $rels[0].SchemaName, $rels[0].CascadeConfiguration.Delete, $rels[0].CascadeConfiguration.Assign) -ForegroundColor Green
    }

    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        # Entity-scoped: sprk_communication only (never tenant-wide). The batch form is published with sprk_billingexport by
        # Deploy-EbillingFormsViews.ps1.
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
