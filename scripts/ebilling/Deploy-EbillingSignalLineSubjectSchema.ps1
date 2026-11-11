<#
.SYNOPSIS
    eBilling R1 task 026 (FR-15, D-31, D-94) - the invoice line as a Signal / Decision Record subject: the
    sprk_regardinginvoiceline lookup on sprk_signal and sprk_decisionrecord, the firm-visible reason text on sprk_signal and
    the sprk_recordtype_ref catalog row for sprk_billingevent.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every
    item as skipped and makes no write (no publish either).

    Items (tasks/026-signal-dr-line-subject.poml step 1):
      1  sprk_signal.sprk_regardinginvoiceline -> sprk_billingevent (relationship sprk_signal_reginvoiceline).
      2  sprk_decisionrecord.sprk_regardinginvoiceline -> sprk_billingevent (relationship sprk_decisionrecord_reginvoiceline).
         Both copy the shape of sprk_signal_reginvoice / sprk_decisionrecord_reginvoice (SpaarkeMaster
         Other/Relationships/sprk_Invoice.xml): Delete and Archive RemoveLink, every other cascade NoCascade.
      3  sprk_signal.sprk_firmreason: multiline text 2000, the firm-visible reason of a finding (FR-15; task 130 projects
         it to firms).
      4  sprk_recordtype_ref row for sprk_billingevent ("Invoice Line"), created only when no row names the table, so the
         shared regarding builder types the pair of a line subject (D-133).

    NEVER deletes or renames anything. A same-name column of another type, a lookup to another table, or a catalog row for
    sprk_billingevent with different values STOPS the script with exit code 2 and an ESCALATION message. Publish is
    entity-scoped (PublishXml for the two tables), and only when this run wrote metadata.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingSignalLineSubjectSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingSignalLineSubjectSchema.ps1

.NOTES
    Project: ebilling-r1, task 026. Dev only in R1. Pattern: scripts/ebilling/Deploy-EbillingInvoiceHeaderSchema.ps1.
    Ontology prior art: projects/spaarke-ontology-platform-r1/notes/140-schema-delta.md sections 2 and 6 (the BFF's
    MetadataService caches entity metadata for up to 6 h; a live gate test may read the old column set until it expires).
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

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity 'sprk_signal'
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$SolutionHeader = $State.SolutionHeader

$LineTable = 'sprk_billingevent'
$LineColumn = 'sprk_regardinginvoiceline'

# The *_reginvoice shape (sprk_Invoice.xml): Delete and Archive RemoveLink, the rest NoCascade. Add-Lookup in the module
# sets no Archive behaviour, so the subject lookup is created here with the full cascade configuration.
function Add-SubjectLookup([string]$Table, [string]$RelSchema, [string]$Description) {
    Set-EbEntity $Table
    $tally = "$Table.$LineColumn"
    $attr = Get-Attr $Table $LineColumn
    if ($attr) {
        Assert-AttrType $attr $LineColumn 'Lookup'
        $lk = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$LineColumn')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
        if ($lk -and $lk.Targets -and ($lk.Targets -notcontains $LineTable)) {
            throw [EscalationException]::new("$Table.$LineColumn already exists as a lookup to '$($lk.Targets -join ',')' but FR-15 needs '$LineTable'. Owner decision needed.")
        }
        $script:Skipped.Add($tally); Write-Host "  skipped  $tally (exists, Lookup -> $LineTable)" -ForegroundColor Yellow
        return
    }
    if (-not (Test-TableKnown $LineTable)) { throw "Lookup target table '$LineTable' does not exist in $EnvironmentUrl." }
    if ($DryRun) { $script:Created.Add($tally); Write-Host "  WOULD CREATE $tally (Lookup -> $LineTable, relationship $RelSchema)" -ForegroundColor Cyan; return }
    $rel = @{
        '@odata.type'        = 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata'
        SchemaName           = $RelSchema
        ReferencedEntity     = $LineTable
        ReferencingEntity    = $Table
        CascadeConfiguration = @{
            Assign = 'NoCascade'; Delete = 'RemoveLink'; Archive = 'RemoveLink'; Merge = 'NoCascade'; Reparent = 'NoCascade'
            Share = 'NoCascade'; Unshare = 'NoCascade'; RollupView = 'NoCascade'
        }
        Lookup               = @{
            '@odata.type' = 'Microsoft.Dynamics.CRM.LookupAttributeMetadata'
            SchemaName    = 'sprk_RegardingInvoiceLine'
            DisplayName   = New-Label 'Regarding Invoice Line'
            Description   = New-Label $Description
            RequiredLevel = @{ Value = 'None' }
        }
    }
    $r = Invoke-DvCreate -Path 'RelationshipDefinitions' -Body $rel -Column $LineColumn -ExpectedType 'Lookup'
    if ($r -eq 'exists') { $script:Skipped.Add($tally); Write-Host "  skipped  $tally (exists, found via create refusal)" -ForegroundColor Yellow; return }
    Wait-AttrVisible $LineColumn
    $script:Created.Add($tally); Write-Host "  CREATED  $tally (Lookup -> $LineTable, relationship $RelSchema)" -ForegroundColor Green
}

# The catalog row the shared regarding builder types a line subject's pair from (sprk_recordtype_ref, D-133).
$CatalogRow = [ordered]@{
    sprk_recordlogicalname          = $LineTable
    sprk_recordtypename             = 'Invoice Line'
    sprk_recorddisplayname          = 'Invoice Line'
    sprk_recordtypecode             = 'INVL'
    sprk_regardingfield             = $LineColumn
    sprk_recorddisplaynamefield     = 'sprk_name'
}

function Add-CatalogRow {
    $tally = "sprk_recordtype_ref[$LineTable]"
    $select = ($CatalogRow.Keys + 'statecode') -join ','
    $rows = @((Invoke-Dv "sprk_recordtype_refs?`$select=$select&`$filter=sprk_recordlogicalname eq '$LineTable'").value)
    if ($rows.Count -gt 1) {
        throw [EscalationException]::new("$($rows.Count) sprk_recordtype_ref rows name $LineTable; the builder needs exactly one. Owner decision needed.")
    }
    if ($rows.Count -eq 1) {
        $row = $rows[0]
        $diff = @($CatalogRow.Keys | Where-Object { [string]$row.$_ -ne [string]$CatalogRow[$_] })
        if ($row.statecode -ne 0) { $diff += 'statecode' }
        if ($diff.Count -gt 0) {
            throw [EscalationException]::new("The sprk_recordtype_ref row for $LineTable exists with different values ($($diff -join ', ')). It is not changed by script - owner decision needed.")
        }
        $script:Skipped.Add($tally); Write-Host "  skipped  $tally (exists, active)" -ForegroundColor Yellow
        return
    }
    if ($DryRun) { $script:Created.Add($tally); Write-Host "  WOULD CREATE $tally ('$($CatalogRow.sprk_recorddisplayname)')" -ForegroundColor Cyan; return }
    Invoke-Dv -Path 'sprk_recordtype_refs' -Method POST -Body $CatalogRow | Out-Null
    $script:Created.Add($tally); Write-Host "  CREATED  $tally ('$($CatalogRow.sprk_recorddisplayname)')" -ForegroundColor Green
}

Write-Host ""
Write-Host "eBilling task 026 - invoice line as a Signal / Decision Record subject" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

$metadataWritten = $false
try {
    foreach ($t in 'sprk_signal', 'sprk_decisionrecord', $LineTable) {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$t')?`$select=LogicalName" -Retries 4)) { throw "$t does not exist in $EnvironmentUrl." }
    }

    Write-Host "1. sprk_signal subject lookup" -ForegroundColor Cyan
    Add-SubjectLookup 'sprk_signal' 'sprk_signal_reginvoiceline' 'Subject lookup: the invoice line this Signal is about (FR-15).'

    Write-Host "2. sprk_decisionrecord subject lookup" -ForegroundColor Cyan
    Add-SubjectLookup 'sprk_decisionrecord' 'sprk_decisionrecord_reginvoiceline' 'Subject lookup: the invoice line this Decision Record is about (FR-15, D-75).'

    Write-Host "3. sprk_signal firm-visible reason" -ForegroundColor Cyan
    Set-EbEntity 'sprk_signal'
    Add-Column 'sprk_firmreason' (New-MemoDef 'sprk_FirmReason' 'Firm Reason' 'The reason text shown to the law firm for this finding (FR-15). Written server-side only.' 2000) 'Memo'
    $metadataWritten = ($script:Created.Count -gt 0)

    Write-Host "4. sprk_recordtype_ref row for $LineTable" -ForegroundColor Cyan
    Add-CatalogRow

    Write-Host "Publish" -ForegroundColor Cyan
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $metadataWritten) {
        Write-Host "  no metadata changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = '<importexportxml><entities><entity>sprk_signal</entity><entity>sprk_decisionrecord</entity></entities></importexportxml>'
        } | Out-Null
        Write-Host "  published sprk_signal, sprk_decisionrecord" -ForegroundColor Green
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
