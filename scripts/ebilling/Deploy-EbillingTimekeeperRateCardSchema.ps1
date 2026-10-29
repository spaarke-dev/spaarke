<#
.SYNOPSIS
    eBilling R1 task 017 - extends the sprk_timekeeper stub into the firm timekeeper roster and creates the rate card
    tables sprk_ratecard and sprk_ratecardrate (FR-08).

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every
    item as skipped and makes no write (no publish either).

    Items (tasks/017-timekeeper-ratecard-schema.poml, spec FR-08):
      1  sprk_timekeeper (EXISTING stub, extended - no new timekeeper table): sprk_firm (lookup -> sprk_organization),
         sprk_effectivefrom / sprk_effectiveto (date only), the existing sprk_contact lookup relabelled "Linked contact",
         statuscode: value 1 "Active" -> "Approved" and an inserted "Pending" on statecode 0; value 2 stays "Inactive"
         on statecode 1. Value 1 is relabelled only when no timekeeper row sits at statuscode 1 (otherwise exit 2).
      2  sprk_ratecard (user-owned): sprk_name, sprk_firm, optional core-record scope sprk_matter / sprk_project /
         sprk_workassignment, sprk_sourcedocument (-> sprk_document), sprk_effectivefrom / sprk_effectiveto (date only),
         statuscode: value 1 "Active" -> "Draft" (new table, no rows), inserted "Published" (statecode 0), value 2
         "Inactive" (statecode 1).
      3  sprk_ratecardrate (user-owned): sprk_name, sprk_ratecard (-> sprk_ratecard, parental cascade delete,
         SystemRequired: enforced by the platform on every write),
         sprk_timekeeper (-> sprk_timekeeper, optional), sprk_approvedrate and sprk_rackrate (money),
         sprk_effectivefrom / sprk_effectiveto (date only).
      4  Classification (owner decision 2026-10-15, "no interpretation"): the shared global choice sprk_timekeeperrole is
         revised and extended so it holds the 20 LEDES timekeeper classifications (label = LEDES name, ExternalValue =
         LEDES code); existing integer VALUES never change; an option is relabelled to its LEDES equivalent only where the
         mapping is exact (Partner, Associate, Paralegal, Other - already the LEDES names); options with no exact LEDES
         equivalent are KEPT and reported with their row usage (deleting needs the owner's OK). Then sprk_classification
         (Picklist bound to the global choice) is created on sprk_timekeeper and sprk_ratecardrate.
      5  Obsolete stub lookup sprk_timekeeper.sprk_invoice (owner-approved delete 2026-10-15): removed only with
         -RemoveStubInvoiceLookup, and only while the table holds no row that uses it and nothing else depends on it.

    NEVER deletes or renames anything by logical name (display labels change). If an existing item conflicts (a column of
    another type, a lookup to another table, status values with unexpected labels or states), the script STOPS with exit
    code 2 and an ESCALATION message (project CLAUDE.md section 5).

    ADR-002: no plugin, business rule, calculated / rollup column or flow. The overlap rule for effective-date ranges on one
    card is enforced server-side by the rate import (task 078), not by schema.

    Eventually consistent metadata reads: a 404 right after a create is not trusted (the item is re-read); a create
    refused with "already exists" counts as skipped after a type check on a settled read; a create issued right after a
    table create is retried while the table propagates (MetadataCache); customization / publish locks and transient
    faults are retried with backoff. The primary-name length in an entity-create body is ignored by Dataverse (850), so
    it is shortened afterwards while the table has no rows (the primary name's required level is likewise set afterwards).
    Required levels: ApplicationRequired is enforced by FORMS only, not by API writes; a name is therefore "required by the
    BFF write path, not enforced on API writes". SystemRequired (the platform refuses a null on every write) is used where
    a null corrupts meaning: sprk_ratecardrate.sprk_ratecard.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.PARAMETER RemoveStubInvoiceLookup
    Delete the obsolete stub lookup sprk_timekeeper.sprk_invoice (relationship sprk_Invoice_Timekeeper_1n; its column and
    name companion go with it). Owner-approved 2026-10-15. Refuses (exit 2) if any timekeeper row holds a value in it or a
    form / view / other component depends on it. Idempotent: a second run finds it gone and reports skipped.

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1
    pwsh scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1 -RemoveStubInvoiceLookup

.NOTES
    Project: ebilling-r1, task 017. Dev only in R1. The Dataverse helpers live in EbillingSchema.psm1 (shared by the
    eBilling schema scripts). Patterns: scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1
    (task 018), Deploy-EbillingInvoiceHeaderSchema.ps1 (task 012) for statuscode, Deploy-EbillingInvoiceLineSchema.ps1
    (task 010). Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (destructive or conflicting).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$RemoveStubInvoiceLookup
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$TkTable = 'sprk_timekeeper'
$CardTable = 'sprk_ratecard'
$RateTable = 'sprk_ratecardrate'
$GlobalChoice = 'sprk_timekeeperrole'

# Shared Dataverse helpers (scripts/ebilling/EbillingSchema.psm1). The state lists are shared by reference.
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $TkTable
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect
$script:Planned = $State.Planned
$script:JustCreated = $State.JustCreated
$SolutionHeader = $State.SolutionHeader

# ---------------------------------------------------------------------------------------------------------------
# Item helpers
# ---------------------------------------------------------------------------------------------------------------

# Picklist column bound to an EXISTING global choice (never edits the choice).
function Add-GlobalChoiceColumn {
    param([string]$Column, [string]$Schema, [string]$Display, [string]$Description, [string]$ChoiceName)
    $table = Get-EbEntity
    $attr = Get-Attr $table $Column
    if ($attr) {
        Assert-AttrType $attr $Column 'Picklist'
        $pk = Get-DvOrNull "EntityDefinitions(LogicalName='$table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=GlobalOptionSet(`$select=Name)"
        if ($pk -and $pk.GlobalOptionSet -and $pk.GlobalOptionSet.Name -ne $ChoiceName) {
            throw [EscalationException]::new("$table.$Column uses global choice '$($pk.GlobalOptionSet.Name)' but this task needs '$ChoiceName'. Owner decision needed.")
        }
    }
    # @odata.bind needs the MetadataId GUID key; a Name key is rejected.
    $os = Get-DvOrNull "GlobalOptionSetDefinitions(Name='$ChoiceName')?`$select=MetadataId" -Retries 2
    if (-not $os) { throw "Global choice '$ChoiceName' does not exist in $EnvironmentUrl." }
    $def = @{
        '@odata.type'                = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'
        SchemaName                   = $Schema
        RequiredLevel                = @{ Value = 'None' }
        DisplayName                  = New-Label $Display
        Description                  = New-Label $Description
        'GlobalOptionSet@odata.bind' = "/GlobalOptionSetDefinitions($($os.MetadataId))"
    }
    Add-Column -Column $Column -Definition $def -ExpectedType 'Picklist'
}

# Display-name change of an EXISTING lookup (logical name unchanged). Idempotent: skipped when the label already matches.
function Set-LookupDisplayName([string]$Table, [string]$Column, [string]$From, [string]$To) {
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.LookupAttributeMetadata"
    $a = Get-DvOrNull $path -Retries 4
    if (-not $a) { throw [EscalationException]::new("$Table.$Column was not found; task 017 expects the existing stub lookup. Owner decision needed.") }
    $cur = $a.DisplayName.UserLocalizedLabel.Label
    if ($cur -eq $To) { $script:Skipped.Add("$Table.$Column label '$To'"); Write-Host "  skipped  $Column display name already '$To'" -ForegroundColor Yellow; return }
    if ($cur -ne $From) { throw [EscalationException]::new("$Table.$Column display name is '$cur', expected '$From' or '$To'. Owner decision needed.") }
    if ($DryRun) { $script:Changed.Add("$Table.$Column label '$To'"); Write-Host "  WOULD RELABEL $Column '$From' -> '$To'" -ForegroundColor Cyan; return }
    $a.DisplayName = New-Label $To
    Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
    for ($i = 0; $i -lt 12; $i++) {
        $chk = Get-DvOrNull $path
        if ($chk -and $chk.DisplayName.UserLocalizedLabel.Label -eq $To) { break }
        Start-Sleep -Seconds 5
    }
    if ($chk.DisplayName.UserLocalizedLabel.Label -ne $To) { throw "$Table.$Column display name is still '$($chk.DisplayName.UserLocalizedLabel.Label)' after the update." }
    $script:Changed.Add("$Table.$Column label '$To'"); Write-Host "  RELABELLED $Column display name '$From' -> '$To'" -ForegroundColor Green
}

# ---------------------------------------------------------------------------------------------------------------
# statuscode helpers (pattern: Deploy-EbillingInvoiceHeaderSchema.ps1)
# ---------------------------------------------------------------------------------------------------------------

function Get-StatusOptions([string]$Table) {
    $a = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='statuscode')/Microsoft.Dynamics.CRM.StatusAttributeMetadata?`$expand=OptionSet(`$select=Options)" -Retries 6
    if (-not $a) { throw "$Table.statuscode metadata could not be read." }
    return @($a.OptionSet.Options | ForEach-Object {
            [pscustomobject]@{ Value = [int]$_.Value; State = [int]$_.State; Label = $_.Label.UserLocalizedLabel.Label }
        })
}

function Set-StatusLabel([string]$Table, [hashtable]$R) {
    if ($DryRun -and ($script:Planned -contains $Table)) { $script:Changed.Add("$Table.statuscode.$($R.Value) ($($R.To))"); Write-Host "  WOULD RELABEL value $($R.Value) '$($R.From)' -> '$($R.To)' (new table)" -ForegroundColor Cyan; return }
    $opts = Get-StatusOptions $Table
    $cur = $opts | Where-Object { $_.Value -eq $R.Value }
    $name = "$Table.statuscode.$($R.Value)"
    if (-not $cur) { throw [EscalationException]::new("$Table statuscode value $($R.Value) is missing; expected the system value '$($R.From)' / '$($R.To)'. Owner decision needed.") }
    if ($cur.Label -eq $R.To) { $script:Skipped.Add("$name ($($R.To))"); Write-Host "  skipped  value $($R.Value) already '$($R.To)' (statecode $($cur.State))" -ForegroundColor Yellow; return }
    if ($cur.Label -ne $R.From) { throw [EscalationException]::new("$Table statuscode value $($R.Value) is labelled '$($cur.Label)', expected '$($R.From)' or '$($R.To)'. Owner decision needed.") }
    if ($DryRun) { $script:Changed.Add("$name ($($R.To))"); Write-Host "  WOULD RELABEL value $($R.Value) '$($R.From)' -> '$($R.To)'" -ForegroundColor Cyan; return }
    Invoke-WhilePropagating {
        Invoke-Dv -Path 'UpdateOptionValue' -Method POST -ExtraHeaders $SolutionHeader -Body @{
            EntityLogicalName = $Table; AttributeLogicalName = 'statuscode'; Value = $R.Value
            Label = (New-Label $R.To); MergeLabels = $true; SolutionUniqueName = $SolutionUniqueName
        } | Out-Null
    }
    $script:Changed.Add("$name ($($R.To))"); Write-Host "  RELABELLED value $($R.Value) '$($R.From)' -> '$($R.To)'" -ForegroundColor Green
}

function Add-StatusValue([string]$Table, [hashtable]$S) {
    if ($DryRun -and ($script:Planned -contains $Table)) { $script:Created.Add("$Table.statuscode.$($S.Label)"); Write-Host "  WOULD INSERT '$($S.Label)' = $($S.Value) (statecode $($S.State), new table)" -ForegroundColor Cyan; return }
    $opts = Get-StatusOptions $Table
    $name = "$Table.statuscode.$($S.Label)"
    $byLabel = $opts | Where-Object { $_.Label -eq $S.Label }
    $byValue = $opts | Where-Object { $_.Value -eq $S.Value }
    if ($byLabel) {
        if ($byLabel.State -ne $S.State) { throw [EscalationException]::new("$Table statuscode '$($S.Label)' (value $($byLabel.Value)) exists under statecode $($byLabel.State) but FR-08 needs statecode $($S.State). A status cannot change state by script - owner decision needed.") }
        $script:Skipped.Add($name); Write-Host "  skipped  '$($S.Label)' = $($byLabel.Value) (statecode $($byLabel.State))" -ForegroundColor Yellow
        return
    }
    if ($byValue) { throw [EscalationException]::new("$Table statuscode value $($S.Value) is already used by '$($byValue.Label)'. Owner decision needed.") }
    if ($DryRun) { $script:Created.Add($name); Write-Host "  WOULD INSERT '$($S.Label)' = $($S.Value) (statecode $($S.State))" -ForegroundColor Cyan; return }
    try {
        Invoke-WhilePropagating {
            Invoke-Dv -Path 'InsertStatusValue' -Method POST -ExtraHeaders $SolutionHeader -Body @{
                AttributeLogicalName = 'statuscode'; EntityLogicalName = $Table; Value = $S.Value; StateCode = $S.State
                Label = (New-Label $S.Label); SolutionUniqueName = $SolutionUniqueName
            } | Out-Null
        }
    } catch {
        # A stale read hid an option that is already there: the refusal is authoritative, verify on a settled read.
        if (-not (Test-DuplicateRefusal $_.Exception.Message -Loose)) { throw }
        $found = $null
        for ($i = 0; $i -lt 12 -and -not $found; $i++) {
            $found = Get-StatusOptions $Table | Where-Object { $_.Label -eq $S.Label -and $_.State -eq $S.State }
            if (-not $found) { Start-Sleep -Seconds 5 }
        }
        if (-not $found) { throw }
        $script:Skipped.Add($name); Write-Host "  skipped  '$($S.Label)' (exists, found via create refusal)" -ForegroundColor Yellow
        return
    }
    $script:Created.Add($name); Write-Host "  INSERTED '$($S.Label)' = $($S.Value) (statecode $($S.State))" -ForegroundColor Green
}

# Timekeeper: 1 Active -> Approved, + Pending (state 0); 2 Inactive stays (state 1).
$TkRelabel = @{ Value = 1; State = 0; From = 'Active'; To = 'Approved' }
$TkInsert = @{ Value = 659490000; State = 0; Label = 'Pending' }
# Rate card: 1 Active -> Draft, + Published (state 0); 2 Inactive stays (state 1).
$CardRelabel = @{ Value = 1; State = 0; From = 'Active'; To = 'Draft' }
$CardInsert = @{ Value = 659490000; State = 0; Label = 'Published' }

# ---------------------------------------------------------------------------------------------------------------
# LEDES classification choice (owner decision 2026-10-15: reuse the shared list, make it match LEDES, no interpretation)
# ---------------------------------------------------------------------------------------------------------------

# LOC Revised Timekeeper Classification Codes (August 2014, revised 6/2023; notes/research/ledes-utbms-ocg-reference.md
# section 2, law-firm classifications; source https://ledes.org/loc-revised-timekeeper-classification-codes-august-2014/).
# Label = LEDES name, ExternalValue = LEDES code. Value is the integer stored in data:
#   - an existing sprk_timekeeperrole option keeps its value and is matched ONLY where the meaning is exactly the same
#     (Partner, Associate, Paralegal, Other: the existing labels already ARE the LEDES names);
#   - every other LEDES classification is a new option, numbered after the last existing one (100000014 ...).
$LedesClassifications = @(
    @{ Code = 'PARTNR'; Label = 'Partner'; Value = 100000001 }
    @{ Code = 'OFCOUN'; Label = 'Of Counsel'; Value = 100000014 }
    @{ Code = 'ASSOC'; Label = 'Associate'; Value = 100000003 }
    @{ Code = 'TRANEE'; Label = 'Associate Trainee'; Value = 100000015 }
    @{ Code = 'STFATT'; Label = 'Staff Attorney'; Value = 100000016 }
    @{ Code = 'SECNDE'; Label = 'Secondee'; Value = 100000017 }
    @{ Code = 'LGLINT'; Label = 'Legal Intern'; Value = 100000018 }
    @{ Code = 'PARALG'; Label = 'Paralegal'; Value = 100000004 }
    @{ Code = 'LGLAST'; Label = 'Legal Assistant'; Value = 100000019 }
    @{ Code = 'CLKSEC'; Label = 'Clerk or Secretary'; Value = 100000020 }
    @{ Code = 'NBOTHR'; Label = 'Other'; Value = 100000006 }
    @{ Code = 'LIBRRN'; Label = 'Librarian'; Value = 100000021 }
    @{ Code = 'RSRCHR'; Label = 'Research Specialist/Researcher'; Value = 100000022 }
    @{ Code = 'ANALST'; Label = 'Analyst'; Value = 100000023 }
    @{ Code = 'HLNLPF'; Label = 'High-Level Non-Lawyer Professional'; Value = 100000024 }
    @{ Code = 'LGPRMG'; Label = 'Legal Project Manager'; Value = 100000025 }
    @{ Code = 'IPAGNT'; Label = 'IP Agent'; Value = 100000026 }
    @{ Code = 'CSTCNS'; Label = 'Costs Counsel'; Value = 100000027 }
    @{ Code = 'TEMPAT'; Label = 'Contract Attorney'; Value = 100000028 }
    @{ Code = 'TEMPOS'; Label = 'Contract Other Staff'; Value = 100000029 }
)

function Get-ChoiceOptions([string]$Name) {
    $o = Get-DvOrNull "GlobalOptionSetDefinitions(Name='$Name')" -Retries 4
    if (-not $o) { throw "Global choice '$Name' does not exist in $EnvironmentUrl." }
    return @($o.Options | ForEach-Object {
            [pscustomobject]@{ Value = [int]$_.Value; Label = $_.Label.UserLocalizedLabel.Label; External = [string]$_.ExternalValue }
        })
}

# The ExternalValue of an option is set by UpdateOptionValue (the same call carries the label). Read back on a settled read.
function Set-ChoiceOptionExternal([string]$Name, [int]$Value, [string]$Label, [string]$External) {
    Invoke-WhilePropagating {
        Invoke-Dv -Path 'UpdateOptionValue' -Method POST -ExtraHeaders $SolutionHeader -Body @{
            OptionSetName = $Name; Value = $Value; Label = (New-Label $Label); MergeLabels = $true
            ExternalValue = $External; SolutionUniqueName = $SolutionUniqueName
        } | Out-Null
    } | Out-Null
    $got = $null
    for ($i = 0; $i -lt 18; $i++) {
        $got = Get-ChoiceOptions $Name | Where-Object { $_.Value -eq $Value }
        if ($got -and $got.External -eq $External -and $got.Label -eq $Label) { return }
        Start-Sleep -Seconds 5
    }
    throw "$Name option $Value reads '$($got.Label)' / '$($got.External)' after the update, expected '$Label' / '$External'."
}

# Brings the global choice to the LEDES classification set. Never deletes, never changes an integer value, never relabels a
# non-LEDES option. Returns the options that are NOT LEDES (kept, reported).
function Sync-LedesChoice([string]$Name) {
    $have = Get-ChoiceOptions $Name
    $ledesValues = @($LedesClassifications | ForEach-Object { $_.Value })
    foreach ($l in $LedesClassifications) {
        $tag = "$Name.$($l.Code)"
        $cur = $have | Where-Object { $_.Value -eq $l.Value }
        $byLabel = $have | Where-Object { $_.Label -eq $l.Label -and $_.Value -ne $l.Value }
        if ($byLabel) { throw [EscalationException]::new("$Name already has '$($l.Label)' as value $($byLabel.Value) but the LEDES mapping uses $($l.Value). Owner decision needed.") }
        $byCode = $have | Where-Object { $_.External -eq $l.Code -and $_.Value -ne $l.Value }
        if ($byCode) { throw [EscalationException]::new("$Name option $($byCode.Value) '$($byCode.Label)' already carries ExternalValue '$($l.Code)' but the LEDES mapping uses value $($l.Value). Owner decision needed.") }
        if ($cur) {
            if ($cur.Label -ne $l.Label) { throw [EscalationException]::new("$Name value $($l.Value) is labelled '$($cur.Label)' but the exact LEDES match is '$($l.Label)' ($($l.Code)). Owner decision needed.") }
            if ($cur.External -eq $l.Code) { $script:Skipped.Add($tag); Write-Host "  skipped  $($l.Value) $($l.Label) = $($l.Code)" -ForegroundColor Yellow; continue }
            if ($cur.External) { throw [EscalationException]::new("$Name option $($l.Value) '$($cur.Label)' has ExternalValue '$($cur.External)', expected empty or '$($l.Code)'. Owner decision needed.") }
            if ($DryRun) { $script:Changed.Add("$tag external"); Write-Host "  WOULD SET $($l.Value) $($l.Label): ExternalValue '' -> $($l.Code)" -ForegroundColor Cyan; continue }
            Set-ChoiceOptionExternal $Name $l.Value $l.Label $l.Code
            $script:Changed.Add("$tag external"); Write-Host "  CHANGED  $($l.Value) $($l.Label): ExternalValue -> $($l.Code)" -ForegroundColor Green
            continue
        }
        if ($DryRun) { $script:Created.Add($tag); Write-Host "  WOULD INSERT $($l.Value) $($l.Label) = $($l.Code)" -ForegroundColor Cyan; continue }
        try {
            Invoke-WhilePropagating {
                Invoke-Dv -Path 'InsertOptionValue' -Method POST -ExtraHeaders $SolutionHeader -Body @{
                    OptionSetName = $Name; Value = $l.Value; Label = (New-Label $l.Label); SolutionUniqueName = $SolutionUniqueName
                } | Out-Null
            } | Out-Null
        } catch {
            # A stale read hid an option that is already there: the refusal is authoritative, verify on a settled read.
            if (-not (Test-DuplicateRefusal $_.Exception.Message -Loose)) { throw }
            $found = $null
            for ($i = 0; $i -lt 12 -and -not $found; $i++) {
                $found = Get-ChoiceOptions $Name | Where-Object { $_.Value -eq $l.Value -and $_.Label -eq $l.Label }
                if (-not $found) { Start-Sleep -Seconds 5 }
            }
            if (-not $found) { throw }
        }
        Set-ChoiceOptionExternal $Name $l.Value $l.Label $l.Code
        $script:Created.Add($tag); Write-Host "  INSERTED $($l.Value) $($l.Label) = $($l.Code)" -ForegroundColor Green
    }
    return @($have | Where-Object { $_.Value -notin $ledesValues })
}

# Row usage of every column bound to the global choice (read-only; counts include inactive rows).
$ChoiceUsers = @(
    @{ Set = 'sprk_billingevents'; Table = 'sprk_billingevent'; Column = 'sprk_timekeeperrole' }
    @{ Set = 'sprk_contactorganizations'; Table = 'sprk_contactorganization'; Column = 'sprk_role' }
    @{ Set = 'sprk_userprofiles'; Table = 'sprk_userprofile'; Column = 'sprk_primaryrole' }
    @{ Set = 'sprk_timekeepers'; Table = 'sprk_timekeeper'; Column = 'sprk_classification' }
    @{ Set = 'sprk_ratecardrates'; Table = 'sprk_ratecardrate'; Column = 'sprk_classification' }
)

function Get-ChoiceUsage([int]$Value) {
    $out = [ordered]@{}
    foreach ($u in $ChoiceUsers) {
        $key = "$($u.Table).$($u.Column)"
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$($u.Table)')/Attributes(LogicalName='$($u.Column)')?`$select=LogicalName")) { $out[$key] = 'n/a'; continue }
        $out[$key] = Get-RowCountExact $u.Set "$($u.Table)id" "$($u.Column) eq $Value"
    }
    return $out
}

# ---------------------------------------------------------------------------------------------------------------
# Obsolete stub lookup sprk_timekeeper.sprk_invoice (owner-approved delete 2026-10-15)
# ---------------------------------------------------------------------------------------------------------------

function Remove-StubInvoiceLookup {
    $col = 'sprk_invoice'
    $attr = Get-Attr $TkTable $col 'LogicalName,AttributeType,MetadataId'
    if (-not $attr) { $script:Skipped.Add("$TkTable.$col removed"); Write-Host "  skipped  $col (already removed)" -ForegroundColor Yellow; return }
    if (-not $RemoveStubInvoiceLookup) { Write-Host "  kept     $col (obsolete stub lookup; pass -RemoveStubInvoiceLookup to delete it - owner-approved 2026-10-15)" -ForegroundColor Yellow; return }
    # 1. no data lost: no row holds a value in it (exact count, includes inactive rows)
    $used = Get-RowCountExact 'sprk_timekeepers' 'sprk_timekeeperid' '_sprk_invoice_value ne null'
    if ($null -eq $used) { throw "Could not count $TkTable rows that use $col; not deleting." }
    $all = Get-RowCountExact 'sprk_timekeepers' 'sprk_timekeeperid'
    Write-Host "  $TkTable rows: $all   rows holding $col`: $used"
    if ($used -gt 0) { throw [EscalationException]::new("$used $TkTable row(s) hold a value in $col; deleting it would lose data. Nothing was deleted.") }
    # 2. nothing depends on the column or its relationship (forms, views, workflows ... )
    $rel = Get-DvOrNull "EntityDefinitions(LogicalName='$TkTable')/ManyToOneRelationships?`$select=SchemaName,MetadataId&`$filter=ReferencingAttribute eq '$col'"
    $relRow = if ($rel -and $rel.value) { @($rel.value)[0] } else { $null }
    if (-not $relRow) { throw "The relationship that owns $TkTable.$col was not found; not deleting." }
    $depA = Invoke-Dv "RetrieveDependenciesForDelete(ObjectId=$($attr.MetadataId),ComponentType=2)"
    $depR = Invoke-Dv "RetrieveDependenciesForDelete(ObjectId=$($relRow.MetadataId),ComponentType=10)"
    $nDep = @($depA.value).Count + @($depR.value).Count
    Write-Host "  dependencies: column $(@($depA.value).Count), relationship $($relRow.SchemaName) $(@($depR.value).Count)"
    if ($nDep -gt 0) { throw [EscalationException]::new("$nDep component(s) depend on $TkTable.$col / $($relRow.SchemaName); remove them first. Nothing was deleted.") }
    if ($DryRun) { $script:Changed.Add("$TkTable.$col removed"); Write-Host "  WOULD DELETE relationship $($relRow.SchemaName) (column $col and its name companions go with it)" -ForegroundColor Cyan; return }
    Invoke-WhilePropagating { Invoke-Dv -Path "RelationshipDefinitions($($relRow.MetadataId))" -Method DELETE | Out-Null } | Out-Null
    $gone = $false
    for ($i = 0; $i -lt 18 -and -not $gone; $i++) {
        $gone = -not (Get-Attr $TkTable $col)
        if (-not $gone) { Start-Sleep -Seconds 5 }
    }
    if (-not $gone) { throw "$TkTable.$col is still readable after the relationship delete." }
    $script:Changed.Add("$TkTable.$col removed"); Write-Host "  DELETED  relationship $($relRow.SchemaName) (column $col removed)" -ForegroundColor Green
}

function Get-RowCount([string]$Table, [string]$Filter) {
    # Counts rows only (no row data); @odata.count is exact for the filter. Includes inactive rows.
    $set = (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName" -Retries 2).EntitySetName
    $r = Invoke-Dv "$set`?`$select=$($Table)id&`$filter=$Filter&`$top=1&`$count=true"
    return [int]$r.'@odata.count'
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 017 - timekeeper roster and rate card schema" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })   Stub invoice lookup: $(if ($RemoveStubInvoiceLookup) { 'REMOVE (owner-approved)' } else { 'kept' })"
Write-Host ""

try {
    foreach ($ref in $TkTable, 'sprk_organization', 'sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_document') {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$ref')?`$select=LogicalName" -Retries 4)) {
            throw "$ref does not exist in $EnvironmentUrl."
        }
    }

    # --- 0. Pre-check (before ANY write): timekeeper rows at statuscode 1 block the relabel to Approved --------------
    Write-Host "0. Pre-check: timekeeper rows" -ForegroundColor Cyan
    $tkOpts0 = Get-StatusOptions $TkTable
    $tkAll = Get-RowCount $TkTable 'statecode eq 0 or statecode eq 1'
    $tkAt1 = Get-RowCount $TkTable 'statuscode eq 1'
    Write-Host "  $TkTable rows: $tkAll   at statuscode 1: $tkAt1   (statuscode options: $(($tkOpts0 | ForEach-Object { "$($_.Value)=$($_.Label)/s$($_.State)" }) -join ', '))"
    $v1 = $tkOpts0 | Where-Object { $_.Value -eq 1 }
    if ($v1 -and $v1.Label -eq 'Active' -and $tkAt1 -gt 0) {
        throw [EscalationException]::new("$tkAt1 timekeeper row(s) are at statuscode 1 'Active'. Relabelling value 1 to 'Approved' would silently re-label them. Their meaning is not known to be 'approved'. How should they be migrated (relabel anyway, or keep 1 as Active and add Approved as a new value)? Nothing was changed.")
    }

    # --- 1. sprk_timekeeper (existing stub) ----------------------------------------------------------------------
    Write-Host "1. Table $TkTable (existing stub, extended)" -ForegroundColor Cyan
    Set-EbEntity $TkTable
    Add-Lookup -Column 'sprk_firm' -SchemaName 'sprk_Firm' -RelSchema 'sprk_organization_timekeeper_firm' -Target 'sprk_organization' `
        -Display 'Firm' -Description 'The law firm this timekeeper bills for.' -DeleteBehavior 'Restrict'
    Add-Column 'sprk_effectivefrom' (New-DateOnlyDef 'sprk_EffectiveFrom' 'Effective From' 'First day the timekeeper is approved to bill for the firm.') 'DateTime'
    Add-Column 'sprk_effectiveto' (New-DateOnlyDef 'sprk_EffectiveTo' 'Effective To' 'Last day the timekeeper is approved to bill for the firm. Empty = open ended.') 'DateTime'
    Set-LookupDisplayName $TkTable 'sprk_contact' 'Person' 'Linked contact'
    Write-Host "   statuscode: value 1 -> Approved, insert Pending" -ForegroundColor Cyan
    Set-StatusLabel $TkTable $TkRelabel
    Add-StatusValue $TkTable $TkInsert

    # --- 2. sprk_ratecard -----------------------------------------------------------------------------------------
    Write-Host "2. Table $CardTable" -ForegroundColor Cyan
    Add-Table -Table $CardTable -Display 'Rate Card' -Plural 'Rate Cards' -Ownership 'UserOwned' `
        -Description 'A firm rate card: provenance and effective period for the approved rates in its rows.' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'Name of the rate card, e.g. Acme LLP 2026.' -PrimaryLength 200
    Set-PrimaryNameLength $CardTable 'sprk_name' 200
    Set-PrimaryNameRequired $CardTable 'sprk_name'
    Add-Lookup -Column 'sprk_firm' -SchemaName 'sprk_Firm' -RelSchema 'sprk_organization_ratecard_firm' -Target 'sprk_organization' `
        -Display 'Firm' -Description 'The law firm the rates apply to.' -DeleteBehavior 'Restrict'
    Add-Lookup -Column 'sprk_matter' -SchemaName 'sprk_Matter' -RelSchema 'sprk_matter_ratecard_matter' -Target 'sprk_matter' `
        -Display 'Matter' -Description 'Optional scope: the card applies to this matter only. At most one scope lookup is set (enforced by the rate import, task 078).'
    Add-Lookup -Column 'sprk_project' -SchemaName 'sprk_Project' -RelSchema 'sprk_project_ratecard_project' -Target 'sprk_project' `
        -Display 'Project' -Description 'Optional scope: the card applies to this project only. At most one scope lookup is set (enforced by the rate import, task 078).'
    Add-Lookup -Column 'sprk_workassignment' -SchemaName 'sprk_WorkAssignment' -RelSchema 'sprk_workassignment_ratecard_workassignment' -Target 'sprk_workassignment' `
        -Display 'Work Assignment' -Description 'Optional scope: the card applies to this work assignment only. At most one scope lookup is set (enforced by the rate import, task 078).'
    Add-Lookup -Column 'sprk_sourcedocument' -SchemaName 'sprk_SourceDocument' -RelSchema 'sprk_document_ratecard_sourcedocument' -Target 'sprk_document' `
        -Display 'Source Document' -Description 'The rate card document (Procedure Library) the rates were imported from.'
    Add-Column 'sprk_effectivefrom' (New-DateOnlyDef 'sprk_EffectiveFrom' 'Effective From' 'First day the card is in force.') 'DateTime'
    Add-Column 'sprk_effectiveto' (New-DateOnlyDef 'sprk_EffectiveTo' 'Effective To' 'Last day the card is in force. Empty = open ended.') 'DateTime'
    Write-Host "   statuscode: value 1 -> Draft, insert Published" -ForegroundColor Cyan
    Set-StatusLabel $CardTable $CardRelabel
    Add-StatusValue $CardTable $CardInsert

    # --- 3. sprk_ratecardrate -------------------------------------------------------------------------------------
    Write-Host "3. Table $RateTable" -ForegroundColor Cyan
    Add-Table -Table $RateTable -Display 'Rate Card Rate' -Plural 'Rate Card Rates' -Ownership 'UserOwned' `
        -Description 'One approved rate (and the firm rack rate) for a timekeeper or classification, within an effective period, on a rate card.' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'Label of the rate, composed by the rate import (e.g. timekeeper or classification and period).' -PrimaryLength 200
    Set-PrimaryNameLength $RateTable 'sprk_name' 200
    Set-PrimaryNameRequired $RateTable 'sprk_name'
    Add-Lookup -Column 'sprk_ratecard' -SchemaName 'sprk_RateCard' -RelSchema 'sprk_ratecard_ratecardrate_ratecard' -Target $CardTable `
        -Display 'Rate Card' -Description 'The card this rate belongs to. Deleting the card deletes its rates.' -RequiredLevel 'ApplicationRequired' -Parental
    # A rate without its card is meaningless and the parental cascade needs the parent: SystemRequired (the platform refuses a
    # null on every write; ApplicationRequired is enforced on forms only, DEF-009).
    Set-LookupRequiredLevel $RateTable 'sprk_ratecard' 'SystemRequired'
    Add-Lookup -Column 'sprk_timekeeper' -SchemaName 'sprk_Timekeeper' -RelSchema 'sprk_timekeeper_ratecardrate_timekeeper' -Target $TkTable `
        -Display 'Timekeeper' -Description 'Optional: the rate applies to this timekeeper. Empty = the rate is by classification.' -DeleteBehavior 'Restrict'
    Add-Column 'sprk_approvedrate' (New-MoneyDef 'sprk_ApprovedRate' 'Approved Rate' 'The hourly rate the client approved.') 'Money'
    Add-Column 'sprk_rackrate' (New-MoneyDef 'sprk_RackRate' 'Rack Rate' 'The firm standard (undiscounted) hourly rate.') 'Money'
    Add-Column 'sprk_effectivefrom' (New-DateOnlyDef 'sprk_EffectiveFrom' 'Effective From' 'First day the rate applies.') 'DateTime'
    Add-Column 'sprk_effectiveto' (New-DateOnlyDef 'sprk_EffectiveTo' 'Effective To' 'Last day the rate applies. Empty = open ended.') 'DateTime'

    # --- 4. Classification: the shared global choice becomes the LEDES list; then the two columns -----------------
    Write-Host "4. Classification (global choice $GlobalChoice = LEDES timekeeper classifications)" -ForegroundColor Cyan
    $leftover = @(Sync-LedesChoice $GlobalChoice)
    Set-EbEntity $TkTable
    Add-GlobalChoiceColumn -Column 'sprk_classification' -Schema 'sprk_Classification' -Display 'Classification' `
        -Description 'Timekeeper classification, the LEDES list (Partner, Associate, Paralegal, ...). Shares the global choice sprk_timekeeperrole with sprk_billingevent.' -ChoiceName $GlobalChoice
    Set-EbEntity $RateTable
    Add-GlobalChoiceColumn -Column 'sprk_classification' -Schema 'sprk_Classification' -Display 'Classification' `
        -Description 'Optional: the rate applies to every timekeeper of this classification (LEDES list). Empty = the rate is for the named timekeeper.' -ChoiceName $GlobalChoice

    # --- 5. Obsolete stub lookup (owner-approved delete) -----------------------------------------------------------------
    Write-Host "5. Obsolete stub lookup $TkTable.sprk_invoice" -ForegroundColor Cyan
    Remove-StubInvoiceLookup

    # --- Publish (only when something changed) ------------------------------------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$TkTable</entity><entity>$CardTable</entity><entity>$RateTable</entity><entity>sprk_organization</entity><entity>sprk_matter</entity><entity>sprk_project</entity><entity>sprk_workassignment</entity><entity>sprk_document</entity></entities><optionsets><optionset>$GlobalChoice</optionset></optionsets></importexportxml>"
        } | Out-Null
        Write-Host "  customizations published" -ForegroundColor Green
    }

    # --- Verification (metadata read of everything this script owns) --------------------------------------------
    if (-not $DryRun) {
        Write-Host "Verify" -ForegroundColor Cyan
        $bad = [System.Collections.Generic.List[string]]::new()
        foreach ($e in $script:Expect) {
            $a = $null
            for ($i = 0; $i -lt 12 -and -not $a; $i++) {
                $a = Get-Attr $e.Table $e.Column
                if (-not $a) { Start-Sleep -Seconds 5 }
            }
            if (-not $a) { $bad.Add("$($e.Table).$($e.Column) missing"); continue }
            if ($a.AttributeType -ne $e.Type) { $bad.Add("$($e.Table).$($e.Column) is $($a.AttributeType), expected $($e.Type)"); continue }
            if ($e.Type -eq 'Lookup') {
                $lk = Get-DvOrNull "EntityDefinitions(LogicalName='$($e.Table)')/Attributes(LogicalName='$($e.Column)')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
                if (-not $lk -or $lk.Targets -notcontains $e.Target) { $bad.Add("$($e.Table).$($e.Column) does not target $($e.Target)") }
            }
        }
        foreach ($t in $TkTable, $CardTable, $RateTable) {
            $own = Get-DvOrNull "EntityDefinitions(LogicalName='$t')?`$select=OwnershipType,PrimaryNameAttribute,EntitySetName" -Retries 4
            if (-not $own) { $bad.Add("$t not readable"); continue }
            Write-Host ("  {0}: {1}, primary name {2}, entity set {3}" -f $t, $own.OwnershipType, $own.PrimaryNameAttribute, $own.EntitySetName)
            # A read right after InsertStatusValue can return the old list: wait for the expected labels.
            $want = switch ($t) { $TkTable { 'Approved', 'Pending', 'Inactive' } $CardTable { 'Draft', 'Published', 'Inactive' } default { $null } }
            if ($want) {
                for ($i = 0; $i -lt 12; $i++) {
                    $so = Get-StatusOptions $t | Sort-Object State, Value
                    if (@($want | Where-Object { $_ -notin $so.Label }).Count -eq 0) { break }
                    Start-Sleep -Seconds 5
                }
                Write-Host ("    statuscode: " + (($so | ForEach-Object { "$($_.Value)=$($_.Label)/state$($_.State)" }) -join ', '))
                if (@($want | Where-Object { $_ -notin $so.Label }).Count -gt 0) { $bad.Add("$t statuscode missing: $((@($want | Where-Object { $_ -notin $so.Label })) -join ', ')") }
                $extra = @($so | Where-Object { $_.Label -notin $want })
                if ($extra.Count -gt 0) { Write-Host "  WARNING: $t statuscode values outside FR-08: $(($extra | ForEach-Object { "$($_.Value)=$($_.Label)" }) -join ', ')" -ForegroundColor DarkYellow }
            }
        }
        $rel = Get-DvOrNull "RelationshipDefinitions(SchemaName='sprk_ratecard_ratecardrate_ratecard')/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?`$select=SchemaName,CascadeConfiguration" -Retries 2
        if (-not $rel) { $bad.Add('relationship sprk_ratecard_ratecardrate_ratecard missing') }
        else {
            Write-Host ("  sprk_ratecard_ratecardrate_ratecard cascade: delete {0}, assign {1}, reparent {2}" -f $rel.CascadeConfiguration.Delete, $rel.CascadeConfiguration.Assign, $rel.CascadeConfiguration.Reparent)
            if ($rel.CascadeConfiguration.Delete -ne 'Cascade') { $bad.Add("sprk_ratecard_ratecardrate_ratecard delete behaviour is $($rel.CascadeConfiguration.Delete), expected Cascade") }
        }
        # The global choice holds every LEDES classification with its code (settled read: option changes lag a few seconds).
        $opts = @()
        for ($i = 0; $i -lt 12; $i++) {
            $opts = Get-ChoiceOptions $GlobalChoice
            $miss = @($LedesClassifications | Where-Object { $l = $_; -not ($opts | Where-Object { $_.Value -eq $l.Value -and $_.Label -eq $l.Label -and $_.External -eq $l.Code }) })
            if ($miss.Count -eq 0) { break }
            Start-Sleep -Seconds 5
        }
        if ($miss.Count -gt 0) { $bad.Add("$GlobalChoice is missing LEDES option(s): $(($miss | ForEach-Object { $_.Code }) -join ', ')") }
        else { Write-Host "  $GlobalChoice holds all $($LedesClassifications.Count) LEDES classifications ($($opts.Count) options in total, $($opts.Count - $LedesClassifications.Count) non-LEDES kept)" -ForegroundColor Green }
        if ($bad.Count -gt 0) { throw "Verification failed: $($bad -join '; ')" }
        Write-Host "  all $($script:Expect.Count) expected columns present with the right type and target" -ForegroundColor Green
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
if ($State.Deferred.Count -gt 0) { Write-Host ("deferred: {0}  {1}" -f $State.Deferred.Count, ($State.Deferred -join ', ')) -ForegroundColor DarkYellow }
if ($leftover.Count -gt 0) {
    Write-Host ""
    Write-Host "Options of $GlobalChoice with NO exact LEDES equivalent (kept, not deleted - deleting needs the owner's OK); row usage by column:" -ForegroundColor Magenta
    foreach ($o in ($leftover | Sort-Object Value)) {
        $use = ''
        if (-not $DryRun) {
            $pairs = @((Get-ChoiceUsage $o.Value).GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" })
            $use = '  ->  ' + ($pairs -join ', ')
        }
        Write-Host ("  {0}  {1}{2}" -f $o.Value, $o.Label, $use) -ForegroundColor Magenta
    }
}
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
