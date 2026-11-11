<#
.SYNOPSIS
    eBilling R1 task 019 - extends the shared policy store for billing rules (FR-09, D-26, D-66): domain and rule-set lookup
    on sprk_policy, clause reference / location, consequence and proposal status on sprk_policyversion, and the new table
    sprk_ruleset.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every
    item as skipped and makes no write (no publish either).

    sprk_policy and sprk_policyversion are owned by spaarke-ontology-platform-r1 (Services/Signals). This script only ADDS
    columns to them: it never relabels, deletes or renumbers an existing column or option, and it never writes a policy
    row (no backfill: an empty sprk_domain reads as Ontology).

    Items (tasks/019-policy-store-ruleset.poml, spec FR-09 / FR-27 / FR-28, design.md 5.1):
      1  Global option set sprk_policydomain: Billing 100000000, Ontology 100000001, Docketing 100000002 (reserved, unused
         in R1; D-26).
      2  Table sprk_ruleset (user-owned, like sprk_policy): primary name sprk_name, sprk_domain (sprk_policydomain),
         sprk_sourcedocument (-> sprk_document), sprk_scope (local choice Client default / Firm), sprk_firm
         (-> sprk_organization), sprk_visibletofirms (two-option, default No), sprk_effectivefrom / sprk_effectiveto
         (date only), sprk_notconverted (multiline JSON text). statuscode: Draft and Published (statecode 0), Retired
         (statecode 1). The rule "sprk_firm only when scope is Firm" is enforced by task 076, not by schema.
      3  sprk_policy: sprk_domain (sprk_policydomain) and sprk_ruleset (lookup -> sprk_ruleset, delete = RemoveLink).
      4  sprk_policyversion: sprk_clausereference (multiline, verbatim quote), sprk_clauselocation (text 200),
         sprk_consequence (local choice Reject submission / Flag / Suggest adjustment), sprk_proposalstatus (local choice
         Proposed / Accepted / Rejected; empty on ontology rows).

    If an existing column has the same logical name but a different type (a lookup targets a different table, an existing
    choice has different options, the global option set has other options), the script STOPS with exit code 2 and an
    ESCALATION message (project CLAUDE.md section 5).

    ADR-002: no plugin, business rule, calculated / rollup column or flow. Admin forms and views are task 029's.

    Required levels (DEF-007 / DEF-009): the primary name sprk_ruleset.sprk_name is set to ApplicationRequired after the
    create (the create body's level is ignored). ApplicationRequired is enforced by FORMS only; an API write without a name is
    the BFF write path's to refuse (task 076), not the platform's.

    Eventually consistent metadata reads: a 404 right after a create is not trusted (the item is re-read); a create
    refused with "already exists" counts as skipped after a type check on a settled read; a column create issued right
    after its table was created is retried while the table is still propagating; the primary-name length in an entity
    create body is ignored by Dataverse (850) and is set afterwards while the table is empty. Customization / publish
    locks and transient faults are retried with backoff.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingPolicyStoreSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingPolicyStoreSchema.ps1

.NOTES
    Project: ebilling-r1, task 019. Dev only in R1. The Dataverse helpers live in EbillingSchema.psm1 (shared by the
    eBilling schema scripts). Pattern: scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1
    (task 018) and Deploy-EbillingInvoiceHeaderSchema.ps1 (task 012, statuscode values); the global option set follows
    scripts/Deploy-AiCatalogSchemaExtensions.ps1.
    Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (destructive or conflicting).
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
$RuleSetTable = 'sprk_ruleset'
$PolicyTable = 'sprk_policy'
$VersionTable = 'sprk_policyversion'
$DomainOptionSet = 'sprk_policydomain'

# Shared Dataverse helpers (scripts/ebilling/EbillingSchema.psm1). The state lists are shared by reference.
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $RuleSetTable
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect
$script:Planned = $State.Planned
$script:JustCreated = $State.JustCreated
$SolutionHeader = $State.SolutionHeader

# ---------------------------------------------------------------------------------------------------------------
# Task 019 helpers
# ---------------------------------------------------------------------------------------------------------------

function New-TwoOptionDef([string]$Schema, [string]$Display, [string]$Description, [bool]$Default) {
    return @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.BooleanAttributeMetadata'
        SchemaName     = $Schema
        DisplayName    = New-Label $Display
        Description    = New-Label $Description
        RequiredLevel  = @{ Value = 'None' }
        DefaultValue   = $Default
        OptionSet      = @{
            '@odata.type' = 'Microsoft.Dynamics.CRM.BooleanOptionSetMetadata'
            OptionSetType = 'Boolean'
            TrueOption    = @{ Value = 1; Label = (New-Label 'Yes') }
            FalseOption   = @{ Value = 0; Label = (New-Label 'No') }
        }
    }
}

function Get-GlobalOptionSet([string]$Name, [int]$Retries = 0) {
    return Get-DvOrNull "GlobalOptionSetDefinitions(Name='$Name')/Microsoft.Dynamics.CRM.OptionSetMetadata" -Retries $Retries
}

# A GLOBAL option set. If it exists its Value=Label pairs must be exactly the expected set (never edited by script).
function Add-GlobalChoice {
    param([string]$Name, [string]$Display, [string]$Description, [object[]]$Options)
    $want = @($Options | ForEach-Object { "$($_.Value)=$($_.Label)" } | Sort-Object)
    $os = Get-GlobalOptionSet $Name
    if ($os) {
        $have = @($os.Options | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" } | Sort-Object)
        if (($have -join '|') -ne ($want -join '|')) {
            throw [EscalationException]::new("Global option set $Name exists with [$($have -join ', ')] but this task needs [$($want -join ', ')]. Editing or removing options needs an owner decision.")
        }
        $script:Skipped.Add("optionset $Name"); Write-Host "  skipped  global option set $Name (exists, $($want.Count) options)" -ForegroundColor Yellow
        return
    }
    if ($DryRun) { $script:Created.Add("optionset $Name"); Write-Host "  WOULD CREATE global option set $Name" -ForegroundColor Cyan; return }
    $body = @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.OptionSetMetadata'
        Name           = $Name
        DisplayName    = New-Label $Display
        Description    = New-Label $Description
        IsGlobal       = $true
        OptionSetType  = 'Picklist'
        Options        = @($Options | ForEach-Object { @{ Value = $_.Value; Label = (New-Label $_.Label) } })
    }
    try {
        Invoke-Dv -Path 'GlobalOptionSetDefinitions' -Method POST -Body $body -ExtraHeaders $SolutionHeader | Out-Null
    } catch {
        if (-not (Test-DuplicateRefusal $_.Exception.Message -Loose)) { throw }
        if (-not (Get-GlobalOptionSet $Name -Retries 12)) { throw }
        $script:Skipped.Add("optionset $Name"); Write-Host "  skipped  global option set $Name (exists, found via create refusal)" -ForegroundColor Yellow
        return
    }
    if (-not (Get-GlobalOptionSet $Name -Retries 20)) { throw "Global option set $Name was created but is not visible in metadata after 60 s." }
    $script:Created.Add("optionset $Name"); Write-Host "  CREATED  global option set $Name" -ForegroundColor Green
}

# A choice column bound to a global option set. If it exists it must be a picklist bound to that same set.
function Add-GlobalChoiceColumn {
    param([string]$Column, [string]$Schema, [string]$Display, [string]$Description, [string]$OptionSetName)
    $table = Get-EbEntity
    $attr = Get-Attr $table $Column
    if ($attr) {
        Assert-AttrType $attr $Column 'Picklist'
        $pk = Get-DvOrNull "EntityDefinitions(LogicalName='$table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Name,IsGlobal)"
        if (-not $pk.OptionSet.IsGlobal -or $pk.OptionSet.Name -ne $OptionSetName) {
            throw [EscalationException]::new("$table.$Column already exists as a picklist bound to '$($pk.OptionSet.Name)' (global: $($pk.OptionSet.IsGlobal)) but this task needs the global option set '$OptionSetName'. Owner decision needed.")
        }
        $script:Expect.Add(@{ Table = $table; Column = $Column; Type = 'Picklist' })
        $script:Skipped.Add("$table.$Column"); Write-Host "  skipped  $Column (exists, choice bound to $OptionSetName)" -ForegroundColor Yellow
        return
    }
    $bind = '/GlobalOptionSetDefinitions(00000000-0000-0000-0000-000000000000)'
    if (-not $DryRun) {
        $os = Get-GlobalOptionSet $OptionSetName -Retries 12
        if (-not $os) { throw "Global option set $OptionSetName is not readable; cannot create $table.$Column." }
        $bind = "/GlobalOptionSetDefinitions($($os.MetadataId))"
    }
    $def = @{
        '@odata.type'                = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'
        SchemaName                   = $Schema
        DisplayName                  = New-Label $Display
        Description                  = New-Label $Description
        RequiredLevel                = @{ Value = 'None' }
        'GlobalOptionSet@odata.bind' = $bind
    }
    Add-Column -Column $Column -Definition $def -ExpectedType 'Picklist'
}

# ---- statuscode (sprk_ruleset: Draft / Published on statecode 0, Retired on statecode 1) ----------------------------

function Get-StatusOptions([string]$Table) {
    $retries = if ($script:JustCreated -contains $Table) { 12 } else { 4 }
    $a = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='statuscode')/Microsoft.Dynamics.CRM.StatusAttributeMetadata?`$expand=OptionSet(`$select=Options)" -Retries $retries
    if (-not $a) { throw "$Table.statuscode metadata could not be read." }
    return @($a.OptionSet.Options | ForEach-Object {
            [pscustomobject]@{ Value = [int]$_.Value; State = [int]$_.State; Label = $_.Label.UserLocalizedLabel.Label }
        })
}

# The two system values of a new table are relabelled (never deleted): 1 Active -> Draft, 2 Inactive -> Retired.
$StatusRelabel = @(
    @{ Value = 1; State = 0; From = 'Active'; To = 'Draft' }
    @{ Value = 2; State = 1; From = 'Inactive'; To = 'Retired' }
)
$StatusInsert = @(
    @{ Value = 659490000; State = 0; Label = 'Published' }
)

function Set-StatusLabels([string]$Table) {
    if ($DryRun -and $script:Planned -contains $Table) {
        foreach ($r in $StatusRelabel) { $script:Changed.Add("statuscode.$($r.Value) ($($r.To))"); Write-Host "  WOULD RELABEL value $($r.Value) '$($r.From)' -> '$($r.To)'" -ForegroundColor Cyan }
        return
    }
    $opts = Get-StatusOptions $Table
    foreach ($r in $StatusRelabel) {
        $cur = $opts | Where-Object { $_.Value -eq $r.Value }
        $name = "statuscode.$($r.Value)"
        if (-not $cur) { throw [EscalationException]::new("$Table statuscode value $($r.Value) is missing; expected the system value '$($r.From)' / '$($r.To)'. Owner decision needed.") }
        if ($cur.Label -eq $r.To) { $script:Skipped.Add("$name ($($r.To))"); Write-Host "  skipped  value $($r.Value) already '$($r.To)' (statecode $($cur.State))" -ForegroundColor Yellow; continue }
        if ($cur.Label -ne $r.From) { throw [EscalationException]::new("$Table statuscode value $($r.Value) is labelled '$($cur.Label)', expected '$($r.From)' or '$($r.To)'. Owner decision needed.") }
        if ($DryRun) { $script:Changed.Add("$name ($($r.To))"); Write-Host "  WOULD RELABEL value $($r.Value) '$($r.From)' -> '$($r.To)'" -ForegroundColor Cyan; continue }
        Invoke-Dv -Path 'UpdateOptionValue' -Method POST -ExtraHeaders $SolutionHeader -Body @{
            EntityLogicalName = $Table; AttributeLogicalName = 'statuscode'; Value = $r.Value
            Label = (New-Label $r.To); MergeLabels = $true; SolutionUniqueName = $SolutionUniqueName
        } | Out-Null
        $script:Changed.Add("$name ($($r.To))"); Write-Host "  RELABELLED value $($r.Value) '$($r.From)' -> '$($r.To)'" -ForegroundColor Green
    }
}

function Add-StatusValues([string]$Table) {
    if ($DryRun -and $script:Planned -contains $Table) {
        foreach ($s in $StatusInsert) { $script:Created.Add("statuscode.$($s.Label)"); Write-Host "  WOULD INSERT '$($s.Label)' = $($s.Value) (statecode $($s.State))" -ForegroundColor Cyan }
        return
    }
    $opts = Get-StatusOptions $Table
    foreach ($s in $StatusInsert) {
        $name = "statuscode.$($s.Label)"
        $byLabel = $opts | Where-Object { $_.Label -eq $s.Label }
        $byValue = $opts | Where-Object { $_.Value -eq $s.Value }
        if ($byLabel) {
            if ($byLabel.State -ne $s.State) { throw [EscalationException]::new("$Table statuscode '$($s.Label)' (value $($byLabel.Value)) exists under statecode $($byLabel.State) but this task needs statecode $($s.State). Owner decision needed.") }
            $script:Skipped.Add($name); Write-Host "  skipped  '$($s.Label)' = $($byLabel.Value) (statecode $($byLabel.State))" -ForegroundColor Yellow
            continue
        }
        if ($byValue) { throw [EscalationException]::new("$Table statuscode value $($s.Value) is already used by '$($byValue.Label)'. Owner decision needed.") }
        if ($DryRun) { $script:Created.Add($name); Write-Host "  WOULD INSERT '$($s.Label)' = $($s.Value) (statecode $($s.State))" -ForegroundColor Cyan; continue }
        try {
            Invoke-Dv -Path 'InsertStatusValue' -Method POST -ExtraHeaders $SolutionHeader -Body @{
                AttributeLogicalName = 'statuscode'; EntityLogicalName = $Table; Value = $s.Value; StateCode = $s.State
                Label = (New-Label $s.Label); SolutionUniqueName = $SolutionUniqueName
            } | Out-Null
        } catch {
            # A stale read hid an option that is already there: the refusal is authoritative, verify on a settled read.
            if (-not (Test-DuplicateRefusal $_.Exception.Message -Loose)) { throw }
            $found = $null
            for ($i = 0; $i -lt 12 -and -not $found; $i++) {
                $found = Get-StatusOptions $Table | Where-Object { $_.Label -eq $s.Label -and $_.State -eq $s.State }
                if (-not $found) { Start-Sleep -Seconds 5 }
            }
            if (-not $found) { throw }
            $script:Skipped.Add($name); Write-Host "  skipped  '$($s.Label)' (exists, found via create refusal)" -ForegroundColor Yellow
            continue
        }
        $script:Created.Add($name); Write-Host "  INSERTED '$($s.Label)' = $($s.Value) (statecode $($s.State))" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 019 - policy store extensions and sprk_ruleset" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    foreach ($t in $PolicyTable, $VersionTable, 'sprk_document', 'sprk_organization') {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$t')?`$select=LogicalName" -Retries 4)) {
            throw "$t does not exist in $EnvironmentUrl."
        }
    }

    # --- 1. Global option set sprk_policydomain -----------------------------------------------------------------
    Write-Host "1. Global option set $DomainOptionSet" -ForegroundColor Cyan
    Add-GlobalChoice -Name $DomainOptionSet -Display 'Policy Domain' `
        -Description 'Which rules system a policy or rule set belongs to. Empty on a policy reads as Ontology. Docketing is reserved and unused in R1 (D-26).' `
        -Options @(
            @{ Value = 100000000; Label = 'Billing' }
            @{ Value = 100000001; Label = 'Ontology' }
            @{ Value = 100000002; Label = 'Docketing' }
        )

    # --- 2. sprk_ruleset ----------------------------------------------------------------------------------------
    Write-Host "2. Table $RuleSetTable" -ForegroundColor Cyan
    Add-Table -Table $RuleSetTable -Display 'Rule Set' -Plural 'Rule Sets' -Ownership 'UserOwned' `
        -Description 'A published set of rules built from one guideline, rate card or procedure document, assigned to a client default or a firm (FR-09, FR-28).' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'Name of the rule set, shown to firms when its visible-to-firms flag is on (D-66).' -PrimaryLength 200
    Set-PrimaryNameLength $RuleSetTable 'sprk_name' 200
    Set-PrimaryNameRequired $RuleSetTable 'sprk_name'
    Add-GlobalChoiceColumn -Column 'sprk_domain' -Schema 'sprk_Domain' -Display 'Domain' -Description 'Rules system the set belongs to (Billing in R1).' -OptionSetName $DomainOptionSet
    Add-Lookup -Column 'sprk_sourcedocument' -SchemaName 'sprk_SourceDocument' -RelSchema 'sprk_document_ruleset_sourcedocument' -Target 'sprk_document' `
        -Display 'Source Document' -Description 'The guideline, rate card or procedure document the rules were drafted from (Procedure Library; internal only, D-63).'
    Add-LocalChoice -Column 'sprk_scope' -Schema 'sprk_Scope' -Display 'Scope' -Description 'Client default (applies to every firm) or Firm (one firm only).' -Labels @('Client default', 'Firm')
    Add-Lookup -Column 'sprk_firm' -SchemaName 'sprk_Firm' -RelSchema 'sprk_organization_ruleset_firm' -Target 'sprk_organization' `
        -Display 'Firm' -Description 'The firm a Firm-scoped rule set applies to. Set only when the scope is Firm (enforced by task 076, not by schema).'
    Add-Column 'sprk_visibletofirms' (New-TwoOptionDef 'sprk_VisibleToFirms' 'Visible to Firms' 'Whether clause quotes and section references from this rule set are shown to firms (D-66, D-79). Defaults to No; the initial value is set from the source kind when the rule set is created.' $false) 'Boolean'
    Add-Column 'sprk_effectivefrom' (New-DateOnlyDef 'sprk_EffectiveFrom' 'Effective From' 'First day the rule set is in force.') 'DateTime'
    Add-Column 'sprk_effectiveto' (New-DateOnlyDef 'sprk_EffectiveTo' 'Effective To' 'Last day the rule set is in force. Empty = open ended.') 'DateTime'
    Add-Column 'sprk_notconverted' (New-MemoDef 'sprk_NotConverted' 'Not Converted' 'JSON list of guideline text the drafting Action could not convert into a supported rule (task 075).' 100000) 'Memo'
    Write-Host "   statuscode: Draft / Published (statecode 0), Retired (statecode 1)" -ForegroundColor Cyan
    Set-StatusLabels $RuleSetTable
    Add-StatusValues $RuleSetTable

    # --- 3. sprk_policy -----------------------------------------------------------------------------------------
    Write-Host "3. $PolicyTable (additive)" -ForegroundColor Cyan
    Set-EbEntity $PolicyTable
    Add-GlobalChoiceColumn -Column 'sprk_domain' -Schema 'sprk_Domain' -Display 'Domain' `
        -Description 'Rules system the policy belongs to. Empty reads as Ontology; billing policies are Billing (D-26).' -OptionSetName $DomainOptionSet
    Add-Lookup -Column 'sprk_ruleset' -SchemaName 'sprk_RuleSet' -RelSchema 'sprk_ruleset_policy_ruleset' -Target $RuleSetTable `
        -Display 'Rule Set' -Description 'The rule set this billing policy belongs to. Stable across versions.'

    # --- 4. sprk_policyversion ----------------------------------------------------------------------------------
    Write-Host "4. $VersionTable (additive)" -ForegroundColor Cyan
    Set-EbEntity $VersionTable
    Add-Column 'sprk_clausereference' (New-MemoDef 'sprk_ClauseReference' 'Clause Reference' 'The verbatim clause of the source guideline this rule was drafted from (D-66, FR-38).' 4000) 'Memo'
    Add-Column 'sprk_clauselocation' (New-StringDef 'sprk_ClauseLocation' 'Clause Location' 'Section and page of the clause in the source document, e.g. Section 4.2, page 7.' 200) 'String'
    Add-LocalChoice -Column 'sprk_consequence' -Schema 'sprk_Consequence' -Display 'Consequence' `
        -Description 'What a failing billing rule does to the invoice: Reject submission, Flag, or Suggest adjustment. Empty on ontology rows (FR-26).' `
        -Labels @('Reject submission', 'Flag', 'Suggest adjustment')
    Add-LocalChoice -Column 'sprk_proposalstatus' -Schema 'sprk_ProposalStatus' -Display 'Proposal Status' `
        -Description 'State of a rule proposed by the drafting Action (FR-27, FR-28): Proposed, Accepted or Rejected. Empty on ontology rows.' `
        -Labels @('Proposed', 'Accepted', 'Rejected')

    # --- Publish (only when something changed) ------------------------------------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$RuleSetTable</entity><entity>$PolicyTable</entity><entity>$VersionTable</entity></entities><optionsets><optionset>$DomainOptionSet</optionset></optionsets></importexportxml>"
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
            $base = "EntityDefinitions(LogicalName='$($e.Table)')/Attributes(LogicalName='$($e.Column)')"
            switch ($e.Type) {
                'Picklist' {
                    # Right after a create the option list can read back empty (eventual consistency): re-read it.
                    $pk = $null
                    for ($i = 0; $i -lt 12; $i++) {
                        $pk = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Name,IsGlobal,Options)"
                        if ($pk -and @($pk.OptionSet.Options).Count -gt 0) { break }
                        Start-Sleep -Seconds 5
                    }
                    if (-not $pk -or @($pk.OptionSet.Options).Count -eq 0) { $bad.Add("$($e.Table).$($e.Column) has no readable options"); continue }
                    Write-Host ("  {0}.{1}: choice ({2}) {3}" -f $e.Table, $e.Column, $(if ($pk.OptionSet.IsGlobal) { "global $($pk.OptionSet.Name)" } else { 'local' }), (($pk.OptionSet.Options | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', '))
                }
                'Lookup' {
                    $lk = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
                    Write-Host ("  {0}.{1}: lookup -> {2}" -f $e.Table, $e.Column, (@($lk.Targets) -join ','))
                }
                'Boolean' {
                    $bo = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.BooleanAttributeMetadata?`$select=DefaultValue&`$expand=OptionSet(`$select=TrueOption,FalseOption)"
                    Write-Host ("  {0}.{1}: two-option default {2} (1={3}, 0={4})" -f $e.Table, $e.Column, $bo.DefaultValue, $bo.OptionSet.TrueOption.Label.UserLocalizedLabel.Label, $bo.OptionSet.FalseOption.Label.UserLocalizedLabel.Label)
                    if ($e.Column -eq 'sprk_visibletofirms' -and $bo.DefaultValue -ne $false) { $bad.Add("sprk_ruleset.sprk_visibletofirms default is $($bo.DefaultValue), expected False") }
                }
                'DateTime' {
                    $dt = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata?`$select=Format,DateTimeBehavior"
                    Write-Host ("  {0}.{1}: datetime format {2}, behavior {3}" -f $e.Table, $e.Column, $dt.Format, $dt.DateTimeBehavior.Value)
                }
                'String' {
                    $st = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength"
                    Write-Host ("  {0}.{1}: text, max length {2}" -f $e.Table, $e.Column, $st.MaxLength)
                }
                'Memo' {
                    $mm = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=MaxLength"
                    Write-Host ("  {0}.{1}: multiline, max length {2}" -f $e.Table, $e.Column, $mm.MaxLength)
                }
            }
        }
        $own = Get-DvOrNull "EntityDefinitions(LogicalName='$RuleSetTable')?`$select=OwnershipType,PrimaryNameAttribute,EntitySetName" -Retries 4
        if (-not $own) { $bad.Add("$RuleSetTable not readable") }
        else {
            Write-Host ("  {0}: {1}, primary name {2}, entity set {3}" -f $RuleSetTable, $own.OwnershipType, $own.PrimaryNameAttribute, $own.EntitySetName)
            if ($own.OwnershipType -ne 'UserOwned') { $bad.Add("$RuleSetTable ownership is $($own.OwnershipType), expected UserOwned") }
        }
        # A read right after InsertStatusValue can return the old list (eventual consistency): wait for all three.
        $expectedStatus = @('Draft', 'Published', 'Retired')
        for ($i = 0; $i -lt 12; $i++) {
            $final = Get-StatusOptions $RuleSetTable | Sort-Object State, Value
            if (@($expectedStatus | Where-Object { $_ -notin $final.Label }).Count -eq 0) { break }
            Start-Sleep -Seconds 5
        }
        $final | ForEach-Object { Write-Host ("  {0} statuscode {1,-10} statecode {2}  {3}" -f $RuleSetTable, $_.Value, $_.State, $_.Label) }
        foreach ($s in @(@{ L = 'Draft'; S = 0 }, @{ L = 'Published'; S = 0 }, @{ L = 'Retired'; S = 1 })) {
            if (-not ($final | Where-Object { $_.Label -eq $s.L -and $_.State -eq $s.S })) { $bad.Add("statuscode '$($s.L)' under statecode $($s.S) missing") }
        }
        $extra = @($final | Where-Object { $_.Label -notin $expectedStatus })
        if ($extra.Count -gt 0) { Write-Host "  WARNING: statuscode values outside the spec: $(($extra | ForEach-Object { "$($_.Value)=$($_.Label)" }) -join ', ')" -ForegroundColor DarkYellow }
        # Existing ontology column untouched: sprk_ruletype keeps exactly Threshold / Switch / Existence.
        $rt = Get-DvOrNull "EntityDefinitions(LogicalName='$VersionTable')/Attributes(LogicalName='sprk_ruletype')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options)"
        $rtText = (($rt.OptionSet.Options | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" } | Sort-Object) -join ', ')
        Write-Host "  $VersionTable.sprk_ruletype (not touched): $rtText"
        if ($bad.Count -gt 0) { throw "Verification failed: $($bad -join '; ')" }
        Write-Host "  all $($script:Expect.Count) expected columns present with the right type" -ForegroundColor Green
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
