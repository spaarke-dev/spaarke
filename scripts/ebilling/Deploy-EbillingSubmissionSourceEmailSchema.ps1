<#
.SYNOPSIS
    eBilling R1 task 095a (owner choice B, 2026-11-03) - the "Source Email Document" reference from sprk_invoicesubmission to sprk_document.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every step is check-then-act; a second run reports the column as skipped,
    re-verifies the relationship's cascade configuration, makes no write and no publish. The column ALREADY EXISTS on dev (created by hand by
    the owner with the names below), so on dev the first run is a verification: -WhatIf reports "skipped", and the cascade check runs.

    Item:
      1  sprk_invoicesubmission.sprk_sourceemaildocument - Lookup -> sprk_document, "Source Email Document", optional, through the one-to-many
         relationship sprk_invoicesubmission_SourceEmailDocument_sprk_document (schema name sprk_SourceEmailDocument). The BFF writes it, in
         the same create as the submission row, for every from-email attempt (SubmitInvoiceFromEmailOperation: the .eml the attempt was read
         from, the same id the Stored Files' sprk_sourcedocument is committed with). GET /api/finance/invoice-submissions?internetMessageId=
         finds the .eml by its Message-ID and then the submissions by this column. No client sets it.

    WHAT IT IS NOT. A citation, not a filing. It is not a sprk_regarding* column, not an ownership parent or a lineage edge, and it must stay
    out of RecordFamilyEdges, ParentLineage, SecureChildLineage, SecureRootInheritance, CoreAncestorResolver, the regarding maps and the
    filing fields. RecordOwnershipResolver registers it as a citation (CitationLookups) so a lookup to sprk_document is not read as a
    filing parent. It never reaches an external response (kept out of InvoiceSubmissionView). Guarded by
    SubmissionSourceEmailLinkNotFilingGuardTests.

    CASCADE (an access requirement, uac-r2). The relationship is created with RemoveLink on Delete and on Archive, and NO cascade on Assign,
    Share, Unshare, Reparent and Merge (and RollupView). A cascading assign, share or delete on the .eml must never change who owns or can
    read the submission. The script sets all of these explicitly and verifies them on EVERY run, including a re-run: an existing
    relationship whose configuration differs STOPS the script with exit code 2 (nothing is changed - the script never alters an existing
    relationship). Unlike the 151c-2 script, Archive is compared too. (On dev the hand-made relationship had Archive = Cascade when this
    script was written; it must be corrected in the environment before this script reports clean.)

    ESCALATION (exit 2, before any write): sprk_invoicesubmission.sprk_sourceemaildocument exists but is not a lookup to sprk_document; a
    second lookup from sprk_invoicesubmission to sprk_document exists under another column (sprk_document is the submission's own filing
    lookup and is excluded from this check); the column is carried by a relationship with another SchemaName than
    sprk_invoicesubmission_SourceEmailDocument_sprk_document; or the existing relationship's cascade differs from the above.

    Publish is entity-scoped (PublishXml for sprk_invoicesubmission only, never tenant-wide), and only when this run wrote metadata. This
    script is NOT run by the task that adds it; the owner's OK is needed to run it on dev. -WhatIf is read-only and may be run against dev
    at any time. Rows written before the column existed carry no link until Backfill-EbillingSubmissionSourceEmail.ps1 is run.

    PRIVILEGES. The BFF writes the reference as its app identity (the sysadmin entity service), so no role change is needed. The column is not
    field-secured (IsSecured 0): nobody reads it but the BFF.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created component is added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingSubmissionSourceEmailSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingSubmissionSourceEmailSchema.ps1

.NOTES
    Project: ebilling-r1, task 095a. Dev only in R1. Pattern: scripts/ebilling/Deploy-EbillingCommunicationBatchLink.ps1 (the lookup + cascade
    verification) and Deploy-EbillingInvoiceTrackingNumberSchema.ps1. Solution source: Entities/sprk_invoicesubmission/Entity.xml and
    Other/Relationships/sprk_Document.xml in SpaarkeMaster.
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
$Table = 'sprk_invoicesubmission'
$Target = 'sprk_document'
$Column = 'sprk_sourceemaildocument'
$RelSchema = 'sprk_invoicesubmission_SourceEmailDocument_sprk_document'
$FilingColumn = 'sprk_document'

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $Table
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

$Description = 'The source .eml document this invoice submission was read from (submission by email, task 095a). A citation written by the server in the same create as the row; not a regarding or a filing, and it does not decide who owns or can read the submission.'

# The cascade the relationship must have, all eight members (Archive included). No cascade anywhere, so a reassign or share of the .eml can
# never reach the submission; RemoveLink on delete and archive, not Restrict.
$ExpectedCascade = [ordered]@{
    Archive    = 'RemoveLink'
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

# The many-to-one relationships of sprk_invoicesubmission that name sprk_document (read only).
function Get-LinkRelationships {
    $r = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/ManyToOneRelationships?`$select=SchemaName,ReferencedEntity,ReferencingAttribute,CascadeConfiguration" -Retries 2
    return @(@($r.value) | Where-Object { $_.ReferencedEntity -eq $Target })
}

# Read-only pre-flight, before ANY write: a conflicting column, a second lookup, or an existing relationship with another cascade.
function Test-ExistingSourceEmailLink {
    $conflict = [System.Collections.Generic.List[string]]::new()
    $col = Get-Attr $Table $Column
    if ($col) {
        $lk = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets" -Retries 2
        if ($col.AttributeType -ne 'Lookup' -or -not $lk -or @($lk.Targets) -notcontains $Target) {
            $conflict.Add("$Table.$Column already exists (type $($col.AttributeType), targets $(if ($lk) { @($lk.Targets) -join ',' } else { 'n/a' })) but is not a lookup to $Target")
        }
    }
    foreach ($rel in (Get-LinkRelationships)) {
        # The submission's own filing lookup to sprk_document is not a second link.
        if ($rel.ReferencingAttribute -eq $FilingColumn) { continue }
        if ($rel.ReferencingAttribute -ne $Column) {
            $conflict.Add("$Table.$($rel.ReferencingAttribute) is already a second lookup to $Target (relationship $($rel.SchemaName))")
            continue
        }
        if ($rel.SchemaName -ne $RelSchema) {
            # The solution source names this relationship; a column carried by another one would drift from it. Never renamed by script.
            $conflict.Add("$Table.$Column exists but its relationship is '$($rel.SchemaName)', not '$RelSchema' (the solution source uses that name)")
            continue
        }
        $diff = @(Get-CascadeMismatches $rel.CascadeConfiguration)
        if ($diff.Count -gt 0) {
            $conflict.Add("relationship $($rel.SchemaName) exists with a different cascade configuration: $($diff -join '; '). A cascading assign, share or delete on the .eml could change who owns or can read the submission")
        } else {
            Write-Host ("  ok       {0}: delete {1}; archive {2}; assign / share / unshare / reparent / merge / rollup view {3} (as required)" -f $rel.SchemaName, $rel.CascadeConfiguration.Delete, $rel.CascadeConfiguration.Archive, $rel.CascadeConfiguration.Assign)
        }
    }
    if ($conflict.Count -gt 0) {
        throw [EscalationException]::new("$Table cannot take the source-email link as specified: $($conflict -join '; '). Owner decision needed (this script never alters an existing column or relationship). Nothing was changed.")
    }
}

Write-Host ""
Write-Host "eBilling task 095a - $Table.$Column -> $Target (submission by email, owner choice B)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 4)) { throw "$Table does not exist in $EnvironmentUrl." }
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Target')?`$select=LogicalName" -Retries 4)) { throw "$Target does not exist in $EnvironmentUrl." }

    Write-Host "0. Pre-flight (read only)" -ForegroundColor Cyan
    Test-ExistingSourceEmailLink

    Write-Host "1. $Table.$Column" -ForegroundColor Cyan
    Add-Lookup -Column $Column -SchemaName 'sprk_SourceEmailDocument' -RelSchema $RelSchema -Target $Target `
        -Display 'Source Email Document' -Description $Description -DeleteBehavior 'RemoveLink' `
        -Cascade @{ Archive = 'RemoveLink'; Assign = 'NoCascade'; Delete = 'RemoveLink'; Merge = 'NoCascade'; Reparent = 'NoCascade'; RollupView = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade' } `
        -MenuBehavior 'DoNotDisplay'

    # Verify what is there now (after a create, or on a re-run): the relationship must exist, with exactly the required cascade.
    Write-Host "2. Verify the cascade" -ForegroundColor Cyan
    if ($DryRun -and ($script:Created.Count -gt 0)) {
        Write-Host "  WhatIf: the relationship does not exist yet; it would be created with the cascade above" -ForegroundColor Cyan
    } else {
        $rels = $null
        for ($i = 0; $i -lt 12 -and -not $rels; $i++) {
            $rels = @(Get-LinkRelationships | Where-Object { $_.ReferencingAttribute -eq $Column })
            if ($rels.Count -eq 0) { $rels = $null; Start-Sleep -Seconds 5 }
        }
        if (-not $rels) { throw "No many-to-one relationship for $Table.$Column -> $Target was readable after the create." }
        $diff = @(Get-CascadeMismatches $rels[0].CascadeConfiguration)
        if ($diff.Count -gt 0) {
            throw [EscalationException]::new("relationship $($rels[0].SchemaName) reads back with a different cascade configuration: $($diff -join '; '). Not changed by this script.")
        }
        Write-Host ("  verified {0}: delete {1}; archive {2}; assign, share, unshare, reparent, merge, rollup view {3}" -f $rels[0].SchemaName, $rels[0].CascadeConfiguration.Delete, $rels[0].CascadeConfiguration.Archive, $rels[0].CascadeConfiguration.Assign) -ForegroundColor Green
    }

    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        # Entity-scoped: sprk_invoicesubmission only (never tenant-wide).
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
