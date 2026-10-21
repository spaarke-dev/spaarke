<#
.SYNOPSIS
    eBilling R1 task 080 (EB-D-148, procedure-and-file-model.md sections 3, 5.2, 6.3, 7.2, 13) - Procedure schema: the Procedure,
    Procedure Version and Procedure Assignment tables, five global choices, the record scope on the rule set, the Procedure /
    Procedure Version links on the rule set, policy version and rate card, the billing firm / billing responsible columns on every
    core record, and field security on the columns the BFF alone may write.

.DESCRIPTION
    Idempotent, additive Dataverse Web API script (dev only in R1). Every item is check-then-create; a second run reports every item
    as skipped, writes no row and does not publish. RUN -WhatIf FIRST: it reads everything, prints what it would create, change or
    write, and writes nothing. The real run needs ontology-r1's full schema-review PASS, the before-notices to ontology-r1 and
    uac-r2 (and uac-r2's answer on the profile reuse) and the owner's OK (task 080 steps 4-6). Never run by the agent that wrote it.

    This script replaces the never-run Deploy-EbillingRuleSetScopeSchema.ps1 (git mv, rewritten). Carried over from it: the
    pre-flight shape, S1 (record lookups on sprk_ruleset), S2 (the Record / Firm record options of sprk_scope), S7 (field security,
    profile reuse) and S9 (sprk_createdbyperson). Dropped (O-12, model 7.2): S3 (all business units), S4 (business-unit N:N), S6
    (sprk_ratecard.sprk_scope) and its backfill, S8 (the Secure business-unit snapshot).

    THE CORE-TABLE LIST IS NOT TYPED HERE. It is read at run time from src/server/api/Sprk.Bff.Api/Services/Dataverse/
    InvoiceFamilyCoreRecordGuard.cs, the FilingCores initializer, with a regex. The script refuses (exit 2, nothing written) when it
    cannot parse a non-empty list of sprk_ table names from exactly one declaration. Each table's schema name and display name are
    then read from its metadata. A core record added to FilingCores gets its record lookups and billing columns on the next run.

    Items (P1-P10 of the task):
      P1  Five GLOBAL choices, values from 100000000 in the order given: sprk_proceduretype (Guideline, Rate card, Billing
          instructions, Internal Standard, Other); sprk_procedureappliesto (All records, Firm, Assigned records only);
          sprk_procedurestatus (Active, Retired); sprk_procedureversionstatus (Draft, Active, Superseded, Retired);
          sprk_publishstate (Draft, Published). An existing set with other values stops the script (never edited).
      P2  Table sprk_procedure (user-owned, primary sprk_name, audited): sprk_firmfacingname (text 200), sprk_area (global choice
          sprk_policydomain, which must already exist), sprk_proceduretype, sprk_appliesto, sprk_firm (-> sprk_organization,
          Restrict), sprk_issuedby (-> sprk_organization, RemoveLink), sprk_externalcontact and sprk_internalcontact (-> contact,
          RemoveLink), sprk_status (sprk_procedurestatus), sprk_notes (memo), sprk_createdbyperson (-> systemuser, RemoveLink).
      P3  Table sprk_procedureversion (user-owned, primary sprk_name, audited): sprk_procedure (-> sprk_procedure, PARENTAL),
          sprk_versionnumber (whole number), alternate key sprk_procedure_versionnumber on (sprk_procedure, sprk_versionnumber),
          sprk_previousversion (-> sprk_procedureversion, RemoveLink), sprk_effectivefrom / sprk_effectiveto (DateOnly),
          sprk_status (sprk_procedureversionstatus), sprk_notconverted (two options, default No; task 116), sprk_createdbyperson.
      P4  Table sprk_procedureassignment (user-owned, primary sprk_name, audited): one typed lookup per FilingCores table named
          after its table (Cascade), sprk_procedure (-> sprk_procedure, Restrict, optional), sprk_mode (LOCAL choice: Follow
          latest 100000000, Pinned 100000001, Excluded 100000002), sprk_pinnedversion (-> sprk_procedureversion, Restrict),
          sprk_excludedpolicy (-> sprk_policy, Cascade), sprk_firm (-> sprk_organization, Restrict), sprk_reason (memo),
          sprk_createdbyperson.
      P5  sprk_ruleset: one record lookup per FilingCores table (RemoveLink), the sprk_scope options Record 100000002 and Firm
          record 100000003, sprk_procedure (-> sprk_procedure, Restrict), sprk_createdbyperson (S9).
      P6  sprk_policyversion: sprk_procedureversion (-> sprk_procedureversion, Restrict), sprk_publishstate (global
          sprk_publishstate, optional, no default), sprk_basedonversion (-> sprk_policyversion, RemoveLink).
      P7  sprk_ratecard: sprk_procedureversion (-> sprk_procedureversion, Restrict).
      P8  Every FilingCores table: sprk_billingfirm and sprk_billingresponsible (-> sprk_organization, RemoveLink, optional).
      P9  Field security (IsSecured true; Read for every user through the reader profile; Create / Update for the BFF application
          user(s) only through the writer profile) on: sprk_procedure.sprk_appliesto, sprk_firm, sprk_status;
          sprk_procedureversion.sprk_effectivefrom, sprk_effectiveto, sprk_status, sprk_notconverted; sprk_ruleset.sprk_scope,
          sprk_firm, sprk_procedure and the P5 record lookups. sprk_createdbyperson is created UNSECURED (task 081 secures it).
      P10 IsAuditEnabled on sprk_procedure, sprk_procedureversion and sprk_procedureassignment.

    DELETE BEHAVIOUR. Restrict only on lookups to a Procedure, a Procedure Version or a firm, never to a core record (model 5.2).
    The two firm lookups' Restrict (sprk_procedure.sprk_firm, sprk_procedureassignment.sprk_firm) is this task's proposal: a nulled
    firm would widen a firm-only Procedure, or an override, to every firm. An existing lookup with another behaviour is never
    changed by script: the pre-flight stops (exit 2).

    NEVER CREATED: a banner column; sprk_isexceptions; any business-unit column or N:N; sprk_ratecard.sprk_scope; sprk_storedfile
    (task 088). Never deleted or changed: sprk_ruleset.sprk_sourcedocument and the sprk_ratecard core lookups.

    PROFILES. P9 reuses the existing profile pair of unified-access-control-r2 ("Spaarke BFF-Managed Field Readers" on every
    business unit's default team, "Spaarke BFF-Managed Field Writers" with the BFF application users only), as
    Set-EbillingFirmReasonFieldSecurity.ps1 does: it adds permission rows for its columns and NEVER adds or removes a member.
    Preconditions are checked before any write, in every mode (a failure is an escalation, exit 2). An ebilling-owned pair instead
    of uac-r2's is possible: pass other -ReaderProfileName / -WriterProfileName plus -CreateProfiles. The choice is settled with
    uac-r2 in the before-notice (task 080 step 4).

    Securing a column that already holds values (sprk_ruleset.sprk_scope, sprk_firm) hides it from every user without Read until
    the reader grant lands, so each column is secured and granted back to back; a failed grant reverts the column to not secured
    and publishes.

    SOLUTION. Components are created in -SolutionUniqueName (SpaarkeMaster). SpaarkeMaster SOURCE (forms, views, Entity.xml) is
    NOT touched: task 079 syncs it after this run. No form and no view is created or changed here. No plugin, business rule or
    calculated column (ADR-002). No role privilege is added (tasks 081 and 089 own the role rows).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment. Any other URL is refused (R1 is dev only).

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.PARAMETER ReaderProfileName
    Field security profile that reads the P9 columns for every user. Default: uac-r2's "Spaarke BFF-Managed Field Readers".

.PARAMETER WriterProfileName
    Field security profile that creates / updates the P9 columns. Default: uac-r2's "Spaarke BFF-Managed Field Writers".

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF application users that must be members of the writer profile. Default: the two dev ones.

.PARAMETER CreateProfiles
    Create a profile named by -ReaderProfileName / -WriterProfileName when it does not exist (with its members). Without it an
    absent profile stops the script. Existing profiles are never modified.

.PARAMETER GuardSourcePath
    The C# source whose FilingCores initializer lists the core tables. Defaults to the repository copy next to this script.

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingProcedureSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingProcedureSchema.ps1

.NOTES
    Project: ebilling-r1, task 080. Dev only in R1. The Dataverse helpers live in EbillingSchema.psm1. Patterns:
    Deploy-EbillingPolicyStoreSchema.ps1 (global choice and choice column), Deploy-EbillingSettingsSchema.ps1 (table audit),
    Deploy-EbillingSubmissionSchema.ps1 (sprk_createdbyperson, alternate key), Set-EbillingFirmReasonFieldSecurity.ps1
    (profiles, secure + grant). Exit codes: 0 = done (created / skipped), 1 = error or a security finding, 2 = escalation
    (nothing further changed). The parse of FilingCores is tested by tests/scripts/Deploy-EbillingProcedureSchema.Tests.ps1.
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [string]$ReaderProfileName = 'Spaarke BFF-Managed Field Readers',
    [string]$WriterProfileName = 'Spaarke BFF-Managed Field Writers',
    [string[]]$BffApplicationIds = @('5967251e-171c-46fe-a6c2-ef843c90309d', '1e40baad-e065-4aea-a8d4-4b7ab273458c'),
    [switch]$CreateProfiles,
    [string]$GuardSourcePath = (Join-Path $PSScriptRoot '../../src/server/api/Sprk.Bff.Api/Services/Dataverse/InvoiceFamilyCoreRecordGuard.cs')
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$DevUrl = 'https://spaarkedev1.crm.dynamics.com'
# `pwsh -File` passes a comma list as ONE string: split it.
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

# ---------------------------------------------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------------------------------------------

$RuleSet = 'sprk_ruleset'
$Procedure = 'sprk_procedure'
$Version = 'sprk_procedureversion'
$Assignment = 'sprk_procedureassignment'
$PolicyVersion = 'sprk_policyversion'
$RateCard = 'sprk_ratecard'
$ScopeColumn = 'sprk_scope'
$CreatorColumn = 'sprk_createdbyperson'
$DomainChoice = 'sprk_policydomain'

# P5 / S2: the existing sprk_scope options (verified, never changed) and the two inserted.
# (Generic SortedDictionary[int,string], not [ordered]@{}: an OrderedDictionary's integer indexer is POSITIONAL.)
$ScopeKnown = [System.Collections.Generic.SortedDictionary[int, string]]::new()
$ScopeKnown.Add(100000000, 'Client default'); $ScopeKnown.Add(100000001, 'Firm')
$ScopeNew = [System.Collections.Generic.SortedDictionary[int, string]]::new()
$ScopeNew.Add(100000002, 'Record'); $ScopeNew.Add(100000003, 'Firm record')

# P1: the five global choices, in the order of the task. Values are assigned 100000000, 100000001, ... in label order.
$GlobalChoices = @(
    @{ Name = 'sprk_proceduretype'; Display = 'Procedure Type'; Description = 'Kind of document a Procedure holds (ebilling-r1 task 080, model 3.2).'; Labels = @('Guideline', 'Rate card', 'Billing instructions', 'Internal Standard', 'Other') }
    @{ Name = 'sprk_procedureappliesto'; Display = 'Procedure Applies To'; Description = 'Which records a Procedure governs: every core record, one firm (the invoice firm), or only records it is assigned to (model 3.2, 6.1).'; Labels = @('All records', 'Firm', 'Assigned records only') }
    @{ Name = 'sprk_procedurestatus'; Display = 'Procedure Status'; Description = 'Lifecycle of a Procedure. A custom choice, not statecode, so it can be field-secured (model 3.2).'; Labels = @('Active', 'Retired') }
    @{ Name = 'sprk_procedureversionstatus'; Display = 'Procedure Version Status'; Description = 'Lifecycle of one Procedure Version (model 3.2, I-P1, I-P5).'; Labels = @('Draft', 'Active', 'Superseded', 'Retired') }
    @{ Name = 'sprk_publishstate'; Display = 'Publish State'; Description = 'Whether a policy version has been published; only Published versions are loaded by the resolver (model 7.2).'; Labels = @('Draft', 'Published') }
)

# Shared Dataverse helpers (scripts/ebilling/EbillingSchema.psm1). The state lists are shared by reference.
$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun -Entity $RuleSet
$script:Created = $State.Created
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped
$script:Expect = $State.Expect
$script:Planned = $State.Planned
$SolutionHeader = $State.SolutionHeader
$Gaps = [System.Collections.Generic.List[string]]::new()
$script:Wrote = $false
$script:Cores = @()
$script:Lookups = @()
$script:Columns = @()
$script:Secured = @()

# ---------------------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------------------

# The core tables (FilingCores). Reads the C# initializer with a regex; refuses unless exactly ONE declaration yields a non-empty
# list of distinct sprk_ table names. Line comments are removed first so a commented-out declaration cannot match.
# (Tested by tests/scripts/Deploy-EbillingProcedureSchema.Tests.ps1, which extracts this function from the script.)
function Get-FilingCores([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw [EscalationException]::new("The core-table list source '$Path' does not exist. The list is read from the FilingCores initializer and is never typed here. Nothing was changed.")
    }
    $src = (Get-Content -LiteralPath $Path -Raw) -replace '(?m)//.*$', ''
    $decl = @([regex]::Matches($src, '\bFilingCores\s*=(?!=)\s*(?:new\s*(?:string\s*)?\[\s*\]\s*)?[\[{]([^\]}]*)[\]}]'))
    if ($decl.Count -ne 1) {
        throw [EscalationException]::new("Expected exactly one FilingCores initializer in '$Path', found $($decl.Count). Not guessing a core-table list. Nothing was changed.")
    }
    $names = @([regex]::Matches($decl[0].Groups[1].Value, '"([^"]*)"') | ForEach-Object { $_.Groups[1].Value })
    if ($names.Count -eq 0) {
        throw [EscalationException]::new("The FilingCores initializer in '$Path' lists no table names. Not guessing a core-table list. Nothing was changed.")
    }
    foreach ($n in $names) {
        if ($n -notmatch '^sprk_[a-z0-9_]+$') { throw [EscalationException]::new("FilingCores entry '$n' is not a sprk_ table logical name. Nothing was changed.") }
    }
    if (@($names | Select-Object -Unique).Count -ne $names.Count) {
        throw [EscalationException]::new("FilingCores lists a table twice ($($names -join ', ')). Nothing was changed.")
    }
    return $names
}

function New-TwoOptionDef([string]$Schema, [string]$Display, [string]$Description, [bool]$Default) {
    return @{
        '@odata.type' = 'Microsoft.Dynamics.CRM.BooleanAttributeMetadata'
        SchemaName    = $Schema
        DisplayName   = New-Label $Display
        Description   = New-Label $Description
        RequiredLevel = @{ Value = 'None' }
        DefaultValue  = $Default
        OptionSet     = @{
            '@odata.type' = 'Microsoft.Dynamics.CRM.BooleanOptionSetMetadata'
            OptionSetType = 'Boolean'
            TrueOption    = @{ Value = 1; Label = (New-Label 'Yes') }
            FalseOption   = @{ Value = 0; Label = (New-Label 'No') }
        }
    }
}

function New-WholeNumberDef([string]$Schema, [string]$Display, [string]$Description, [int]$Min, [int]$Max) {
    return @{
        '@odata.type' = 'Microsoft.Dynamics.CRM.IntegerAttributeMetadata'
        SchemaName    = $Schema
        DisplayName   = New-Label $Display
        Description   = New-Label $Description
        RequiredLevel = @{ Value = 'None' }
        Format        = 'None'
        MinValue      = $Min
        MaxValue      = $Max
    }
}

# Case and space blind comparison of option labels: "FirmRecord" and "Firm record" are the same meaning.
function ConvertTo-LabelKey([string]$Label) { return (($Label -replace '\s', '').ToLowerInvariant()) }

function Get-ScopeOptions {
    $pk = Get-DvOrNull "EntityDefinitions(LogicalName='$RuleSet')/Attributes(LogicalName='$ScopeColumn')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal,Name)" -Retries 2
    if (-not $pk) { throw [EscalationException]::new("$RuleSet.$ScopeColumn does not exist or is not a choice. Task 019 should have created it. Owner decision needed.") }
    if ($pk.OptionSet.IsGlobal) { throw [EscalationException]::new("$RuleSet.$ScopeColumn is bound to a GLOBAL choice ($($pk.OptionSet.Name)); expected a local choice. Owner decision needed.") }
    return @($pk.OptionSet.Options | Sort-Object Value | ForEach-Object { [pscustomobject]@{ Value = [int]$_.Value; Label = [string]$_.Label.UserLocalizedLabel.Label } })
}

function Format-Options($options) { return (($options | ForEach-Object { "$($_.Value)=$($_.Label)" }) -join ', ') }

# The many-to-one relationship of a lookup column, found by the column (so a lookup made under another relationship name is
# still checked). $null when the column has none.
function Get-LookupRelationship([string]$Table, [string]$Column) {
    $r = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/ManyToOneRelationships?`$select=SchemaName,ReferencedEntity,CascadeConfiguration&`$filter=ReferencingAttribute eq '$Column'" -Retries 2
    return @($r.value) | Select-Object -First 1
}

function Write-Report([string]$StateText, [string]$What) {
    $color = switch ($StateText) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $StateText, $What) -ForegroundColor $color
    if ($StateText -in 'MISSING', 'FAIL') { $Gaps.Add($What) }
    if ($StateText -eq 'DONE') { $script:Wrote = $true }
}

# ---- global choices (copied from Deploy-EbillingPolicyStoreSchema.ps1) -----------------------------------------------

function Get-GlobalOptionSet([string]$Name, [int]$Retries = 0) {
    return Get-DvOrNull "GlobalOptionSetDefinitions(Name='$Name')/Microsoft.Dynamics.CRM.OptionSetMetadata" -Retries $Retries
}

function Get-ChoiceOptions($Labels) {
    $v = 100000000
    return @($Labels | ForEach-Object { $o = [pscustomobject]@{ Value = $v; Label = $_ }; $v++; $o })
}

# $null when the set is absent or has exactly the wanted Value=Label pairs; else the conflict text.
function Get-GlobalChoiceConflict([string]$Name, $Options) {
    $os = Get-GlobalOptionSet $Name
    if (-not $os) { return $null }
    $want = @($Options | ForEach-Object { "$($_.Value)=$($_.Label)" } | Sort-Object)
    $have = @($os.Options | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" } | Sort-Object)
    if (($have -join '|') -ne ($want -join '|')) {
        return "Global option set $Name exists with [$($have -join ', ')] but this task needs [$($want -join ', ')]. Editing or removing options needs an owner decision."
    }
    return $null
}

function Add-GlobalChoice([string]$Name, [string]$Display, [string]$Description, $Options) {
    $conflict = Get-GlobalChoiceConflict $Name $Options
    if ($conflict) { throw [EscalationException]::new($conflict) }
    if (Get-GlobalOptionSet $Name) {
        $script:Skipped.Add("optionset $Name"); Write-Host "  skipped  global option set $Name (exists, $(@($Options).Count) options)" -ForegroundColor Yellow
        return
    }
    if ($DryRun) { $script:Created.Add("optionset $Name"); Write-Host "  WOULD CREATE global option set $Name ($((@($Options) | ForEach-Object { "$($_.Value)=$($_.Label)" }) -join ', '))" -ForegroundColor Cyan; return }
    $body = @{
        '@odata.type' = 'Microsoft.Dynamics.CRM.OptionSetMetadata'
        Name          = $Name
        DisplayName   = New-Label $Display
        Description   = New-Label $Description
        IsGlobal      = $true
        OptionSetType = 'Picklist'
        Options       = @($Options | ForEach-Object { @{ Value = $_.Value; Label = (New-Label $_.Label) } })
    }
    try {
        Invoke-Dv -Path 'GlobalOptionSetDefinitions' -Method POST -Body $body -ExtraHeaders $SolutionHeader | Out-Null
    }
    catch {
        if (-not (Test-DuplicateRefusal $_.Exception.Message -Loose)) { throw }
        if (-not (Get-GlobalOptionSet $Name -Retries 12)) { throw }
        $script:Skipped.Add("optionset $Name"); Write-Host "  skipped  global option set $Name (exists, found via create refusal)" -ForegroundColor Yellow
        return
    }
    if (-not (Get-GlobalOptionSet $Name -Retries 20)) { throw "Global option set $Name was created but is not visible in metadata after 60 s." }
    $script:Created.Add("optionset $Name"); $script:Wrote = $true; Write-Host "  CREATED  global option set $Name" -ForegroundColor Green
}

# A choice column bound to a global option set. If it exists it must be a picklist bound to that same set.
function Add-GlobalChoiceColumn([string]$Column, [string]$Schema, [string]$Display, [string]$Description, [string]$OptionSetName) {
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

# ---- tables ---------------------------------------------------------------------------------------------------------

# Auditing on the table itself. The module's Add-Table does not set it, so it is read and, when off, switched on with a full PUT
# of the table metadata (the same read-modify-PUT Deploy-EbillingSettingsSchema.ps1 uses).
function Set-TableAudit([string]$Table) {
    $path = "EntityDefinitions(LogicalName='$Table')"
    $ent = Get-DvOrNull $path -Retries 4
    if (-not $ent) {
        if ($DryRun) { Write-Host "  WOULD SET auditing on $Table" -ForegroundColor Cyan; return }
        throw "$Table was not found."
    }
    if ($ent.IsAuditEnabled.Value -eq $true) { $script:Skipped.Add("$Table auditing"); Write-Host "  skipped  $Table auditing already on" -ForegroundColor Yellow; return }
    if ($DryRun) { $script:Changed.Add("$Table auditing"); Write-Host "  WOULD SET auditing on $Table" -ForegroundColor Cyan; return }
    $ent.IsAuditEnabled.Value = $true
    Invoke-WhilePropagating { Invoke-Dv -Path "EntityDefinitions($($ent.MetadataId))" -Method PUT -Body $ent -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    $chk = $null
    for ($i = 0; $i -lt 12; $i++) {
        $chk = Get-DvOrNull "$path`?`$select=IsAuditEnabled"
        if ($chk -and $chk.IsAuditEnabled.Value -eq $true) { break }
        Start-Sleep -Seconds 5
    }
    if (-not $chk -or $chk.IsAuditEnabled.Value -ne $true) { throw "$Table auditing is still off after the update." }
    $script:Changed.Add("$Table auditing"); $script:Wrote = $true; Write-Host "  CHANGED  $Table auditing off -> on" -ForegroundColor Green
}

function New-ProcedureTable([string]$Table, [string]$Display, [string]$Plural, [string]$Description, [string]$PrimaryDescription) {
    Add-Table -Table $Table -Display $Display -Plural $Plural -Description $Description `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription $PrimaryDescription -PrimaryLength 200 -Ownership UserOwned
    Set-PrimaryNameLength $Table 'sprk_name' 200
    Set-PrimaryNameRequired $Table 'sprk_name'
    Set-TableAudit $Table
}

# ---- lookups --------------------------------------------------------------------------------------------------------

# One lookup to create. Rel is the relationship schema name. Delete is the delete behaviour the relationship must read.
function New-LookupSpec([string]$Table, [string]$Column, [string]$Schema, [string]$Rel, [string]$Target, [string]$Display, [string]$Desc, [string]$Delete, [switch]$Parental) {
    return [pscustomobject]@{ Table = $Table; Column = $Column; Schema = $Schema; Rel = $Rel; Target = $Target; Display = $Display; Desc = $Desc; Delete = $Delete; Parental = [bool]$Parental }
}

# Relationship name for a lookup on a NEW table: {table}_{column}_{target}, sprk_ prefixes dropped from the column and the target
# (the Deploy-EbillingSubmissionSchema.ps1 shape, e.g. sprk_invoicesubmission_vendororg_organization).
function Get-NewTableRel([string]$Table, [string]$Column, [string]$Target) {
    return ('{0}_{1}_{2}' -f $Table, ($Column -replace '^sprk_', ''), ($Target -replace '^sprk_', ''))
}

function Invoke-LookupSpec($s) {
    Set-EbEntity $s.Table
    $p = @{ Column = $s.Column; SchemaName = $s.Schema; RelSchema = $s.Rel; Target = $s.Target; Display = $s.Display; Description = $s.Desc; DeleteBehavior = $s.Delete }
    if ($s.Parental) { Add-Lookup @p -Parental } else { Add-Lookup @p }
}

# The delete behaviour a created relationship must read.
function Get-ExpectedDelete($s) { if ($s.Parental) { return 'Cascade' } else { return $s.Delete } }

# The creator-person lookup spec (the shape ebilling task 013 gave the invoice family, copied from the old S9).
function New-CreatorSpec([string]$Table) {
    return New-LookupSpec $Table $CreatorColumn 'sprk_CreatedByPerson' "sprk_systemuser_${Table}_createdbyperson" 'systemuser' 'Created By (Person)' `
        'The PERSON who asked for this record. Stamped by the BFF only (field-secured by scripts/Set-ChildRecordCreatorPersonSchema.ps1, which task 081 extends): createdby is the BFF application user for a record the BFF creates app-only, so this column is what records the person. ebilling-r1 task 080 (O-23; D-173 shape).' 'RemoveLink'
}

# Reads each core table's schema name and display name from metadata and builds the full plan (lookups, columns, secured set).
# Nothing here is typed per core table.
function Resolve-Plan([string[]]$CoreTables) {
    $cores = @()
    foreach ($t in $CoreTables) {
        $e = Get-DvOrNull "EntityDefinitions(LogicalName='$t')?`$select=LogicalName,SchemaName,DisplayName" -Retries 4
        if (-not $e) { throw [EscalationException]::new("FilingCores names '$t' but that table does not exist in $EnvironmentUrl. Not renaming or dropping an entry. Nothing was changed.") }
        $display = [string]$e.DisplayName.UserLocalizedLabel.Label
        if (-not $display) { $display = $t }
        $cores += [pscustomobject]@{ Table = $t; Suffix = ($t -replace '^sprk_', ''); Schema = [string]$e.SchemaName; Display = $display }
    }
    $script:Cores = $cores

    $lk = [System.Collections.Generic.List[object]]::new()
    $org = 'sprk_organization'

    # P2 sprk_procedure
    $lk.Add((New-LookupSpec $Procedure 'sprk_firm' 'sprk_Firm' (Get-NewTableRel $Procedure 'sprk_firm' $org) $org 'Firm' 'The firm this Procedure governs. Required when Applies to is Firm and for a Procedure of type Rate card (I-P3); optional otherwise, and when set the firm condition applies from every source (O-20). Field-secured and scope-locked once a version is Active (I-P4). RESTRICT: a firm that is deleted while a Procedure names it is refused, because a nulled firm would widen a firm-only Procedure to every firm (task 080 proposal).' 'Restrict'))
    $lk.Add((New-LookupSpec $Procedure 'sprk_issuedby' 'sprk_IssuedBy' (Get-NewTableRel $Procedure 'sprk_issuedby' $org) $org 'Issued By' 'The organization that issued the document (O-11).' 'RemoveLink'))
    $lk.Add((New-LookupSpec $Procedure 'sprk_externalcontact' 'sprk_ExternalContact' (Get-NewTableRel $Procedure 'sprk_externalcontact' 'contact') 'contact' 'External Contact' 'The external contact for the document (O-11).' 'RemoveLink'))
    $lk.Add((New-LookupSpec $Procedure 'sprk_internalcontact' 'sprk_InternalContact' (Get-NewTableRel $Procedure 'sprk_internalcontact' 'contact') 'contact' 'Internal Contact' 'The internal contact for the document (O-11).' 'RemoveLink'))
    $lk.Add((New-CreatorSpec $Procedure))

    # P3 sprk_procedureversion
    $lk.Add((New-LookupSpec $Version $Procedure 'sprk_Procedure' (Get-NewTableRel $Version $Procedure $Procedure) $Procedure 'Procedure' 'The Procedure this is one issue of. PARENTAL: deleting a Procedure deletes its versions, unless a policy version, rate card or stored file still references one (those links are Restrict).' 'Cascade' -Parental))
    $lk.Add((New-LookupSpec $Version 'sprk_previousversion' 'sprk_PreviousVersion' (Get-NewTableRel $Version 'sprk_previousversion' $Version) $Version 'Previous Version' 'The version this one replaces (model 3.2).' 'RemoveLink'))
    $lk.Add((New-CreatorSpec $Version))

    # P4 sprk_procedureassignment: one typed lookup per core table (Cascade), then the rest
    foreach ($c in $cores) {
        $lk.Add((New-LookupSpec $Assignment $c.Table $c.Schema "$($c.Table)_procedureassignment_$($c.Suffix)" $c.Table $c.Display "The $($c.Display.ToLowerInvariant()) this assignment or override is on. Exactly one core lookup is set on a row (enforced by the BFF). CASCADE: deleting the record deletes its assignment rows (model 5.2). Never Restrict to a core record." 'Cascade'))
    }
    $lk.Add((New-LookupSpec $Assignment $Procedure 'sprk_Procedure' (Get-NewTableRel $Assignment $Procedure $Procedure) $Procedure 'Procedure' 'The Procedure this row assigns, pins or excludes. Optional: a rule-level override names the excluded policy instead. RESTRICT: a Procedure that is assigned cannot be deleted (retire it instead).' 'Restrict'))
    $lk.Add((New-LookupSpec $Assignment 'sprk_pinnedversion' 'sprk_PinnedVersion' (Get-NewTableRel $Assignment 'sprk_pinnedversion' $Version) $Version 'Pinned Version' 'The Procedure Version used whatever is newer (mode Pinned). RESTRICT: only stops deleting a pinned Procedure Version, never a core record.' 'Restrict'))
    $lk.Add((New-LookupSpec $Assignment 'sprk_excludedpolicy' 'sprk_ExcludedPolicy' (Get-NewTableRel $Assignment 'sprk_excludedpolicy' 'sprk_policy') 'sprk_policy' 'Excluded Rule' 'The rule (policy) a rule-level override switches off on this record (O-10). CASCADE: a deleted rule deletes its override, so a nulled rule can never read as a Procedure-level Exclude.' 'Cascade'))
    $lk.Add((New-LookupSpec $Assignment 'sprk_firm' 'sprk_Firm' (Get-NewTableRel $Assignment 'sprk_firm' $org) $org 'Firm' 'Optional firm the override is limited to (6.3). RESTRICT: a nulled firm would widen the override to every firm (task 080 proposal).' 'Restrict'))
    $lk.Add((New-CreatorSpec $Assignment))

    # P5 sprk_ruleset
    foreach ($c in $cores) {
        $lk.Add((New-LookupSpec $RuleSet $c.Table $c.Schema "$($c.Table)_ruleset_$($c.Suffix)" $c.Table $c.Display "Scope: the $($c.Display.ToLowerInvariant()) this rule set applies to (scope Record or Firm record). Exactly one core lookup is set on such a set (enforced by RuleSetService). Written only by the BFF (field-secured). If the record is deleted the link is cleared and the set applies to nothing (D-172, ontology F2-b)." 'RemoveLink'))
    }
    $lk.Add((New-LookupSpec $RuleSet $Procedure 'sprk_Procedure' (Get-NewTableRel $RuleSet $Procedure $Procedure) $Procedure 'Procedure' 'The Procedure this rule set belongs to. RESTRICT: a Procedure with a rule set cannot be deleted (retire it); Signals cite its rules (I-P5).' 'Restrict'))
    $lk.Add((New-CreatorSpec $RuleSet))

    # P6 sprk_policyversion
    $lk.Add((New-LookupSpec $PolicyVersion $Version 'sprk_ProcedureVersion' (Get-NewTableRel $PolicyVersion $Version $Version) $Version 'Procedure Version' 'The Procedure Version this policy version belongs to (model 7.2). RESTRICT: a Procedure Version with policy versions cannot be deleted; Signals cite them (I-P5).' 'Restrict'))
    $lk.Add((New-LookupSpec $PolicyVersion 'sprk_basedonversion' 'sprk_BasedOnVersion' (Get-NewTableRel $PolicyVersion 'sprk_basedonversion' $PolicyVersion) $PolicyVersion 'Based On Version' 'For a record rule: the base rule policy version it was copied from (model 6.3). REMOVELINK, never Restrict (ontology F2-a).' 'RemoveLink'))

    # P7 sprk_ratecard
    $lk.Add((New-LookupSpec $RateCard $Version 'sprk_ProcedureVersion' (Get-NewTableRel $RateCard $Version $Version) $Version 'Procedure Version' 'The Procedure Version whose published rate card this is (model 7.2). RESTRICT: a Procedure Version with a rate card cannot be deleted (I-P5).' 'Restrict'))

    # P8 every core table
    foreach ($c in $cores) {
        $lk.Add((New-LookupSpec $c.Table 'sprk_billingfirm' 'sprk_BillingFirm' (Get-NewTableRel $c.Table 'sprk_billingfirm' $org) $org 'Billing Firm' 'The law firm whose Procedures the record''s list shows by default (model 6.6). Not used for evaluation (the invoice''s firm is) or for submission eligibility (sprk_assignedlawfirm1/2). Confers NO access: never add it to the access registry (model 5.2, K-2).' 'RemoveLink'))
        $lk.Add((New-LookupSpec $c.Table 'sprk_billingresponsible' 'sprk_BillingResponsible' (Get-NewTableRel $c.Table 'sprk_billingresponsible' $org) $org 'Billing Responsible' 'The client''s billing entity. No R1 consumer. Confers NO access: never add it to the access registry (model 5.2, K-2).' 'RemoveLink'))
    }
    $script:Lookups = @($lk)

    # Non-lookup columns (for the pre-flight type check): Table, Column, AttributeType.
    $script:Columns = @(
        @{ Table = $Procedure; Column = 'sprk_name'; Type = 'String' }
        @{ Table = $Procedure; Column = 'sprk_firmfacingname'; Type = 'String' }
        @{ Table = $Procedure; Column = 'sprk_area'; Type = 'Picklist' }
        @{ Table = $Procedure; Column = 'sprk_proceduretype'; Type = 'Picklist' }
        @{ Table = $Procedure; Column = 'sprk_appliesto'; Type = 'Picklist' }
        @{ Table = $Procedure; Column = 'sprk_status'; Type = 'Picklist' }
        @{ Table = $Procedure; Column = 'sprk_notes'; Type = 'Memo' }
        @{ Table = $Version; Column = 'sprk_name'; Type = 'String' }
        @{ Table = $Version; Column = 'sprk_versionnumber'; Type = 'Integer' }
        @{ Table = $Version; Column = 'sprk_effectivefrom'; Type = 'DateTime' }
        @{ Table = $Version; Column = 'sprk_effectiveto'; Type = 'DateTime' }
        @{ Table = $Version; Column = 'sprk_status'; Type = 'Picklist' }
        @{ Table = $Version; Column = 'sprk_notconverted'; Type = 'Boolean' }
        @{ Table = $Assignment; Column = 'sprk_name'; Type = 'String' }
        @{ Table = $Assignment; Column = 'sprk_mode'; Type = 'Picklist' }
        @{ Table = $Assignment; Column = 'sprk_reason'; Type = 'Memo' }
        @{ Table = $PolicyVersion; Column = 'sprk_publishstate'; Type = 'Picklist' }
    )

    # P9: the columns that become field-secured. Reader: Read for every user; writer: Create / Update, BFF application user(s) only.
    $sec = @(
        @{ Table = $Procedure; Column = 'sprk_appliesto'; Kind = 'Picklist' }
        @{ Table = $Procedure; Column = 'sprk_firm'; Kind = 'Lookup' }
        @{ Table = $Procedure; Column = 'sprk_status'; Kind = 'Picklist' }
        @{ Table = $Version; Column = 'sprk_effectivefrom'; Kind = 'DateTime' }
        @{ Table = $Version; Column = 'sprk_effectiveto'; Kind = 'DateTime' }
        @{ Table = $Version; Column = 'sprk_status'; Kind = 'Picklist' }
        @{ Table = $Version; Column = 'sprk_notconverted'; Kind = 'Boolean' }
        @{ Table = $RuleSet; Column = $ScopeColumn; Kind = 'Picklist' }
        @{ Table = $RuleSet; Column = 'sprk_firm'; Kind = 'Lookup' }
        @{ Table = $RuleSet; Column = $Procedure; Kind = 'Lookup' }
    )
    foreach ($c in $cores) { $sec += @{ Table = $RuleSet; Column = $c.Table; Kind = 'Lookup' } }
    $script:Secured = @($sec)
}

# Nothing may change an existing lookup: an existing one must already target the right table with the right delete behaviour.
function Test-LookupSpecPreflight($s, [System.Collections.Generic.List[string]]$Problems) {
    $attr = Get-Attr $s.Table $s.Column
    if ($attr) {
        if ($attr.AttributeType -ne 'Lookup') { $Problems.Add("$($s.Table).$($s.Column) exists as $($attr.AttributeType), expected a lookup"); return }
        $lk = Get-DvOrNull "EntityDefinitions(LogicalName='$($s.Table)')/Attributes(LogicalName='$($s.Column)')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
        if ($lk -and $lk.Targets -and ($lk.Targets -notcontains $s.Target)) { $Problems.Add("$($s.Table).$($s.Column) exists as a lookup to '$($lk.Targets -join ',')', expected '$($s.Target)'") }
        $rel = Get-LookupRelationship $s.Table $s.Column
        $want = Get-ExpectedDelete $s
        if ($rel -and [string]$rel.CascadeConfiguration.Delete -ne $want) {
            $Problems.Add("$($s.Table).$($s.Column) ($($rel.SchemaName)) has delete behaviour '$($rel.CascadeConfiguration.Delete)', expected $want. Changing it needs an owner decision; not changed")
        }
    }
    else {
        $rel = Get-DvOrNull "RelationshipDefinitions(SchemaName='$($s.Rel)')"
        if ($rel) { $Problems.Add("Relationship $($s.Rel) already exists ($($rel.RelationshipType)) but $($s.Table).$($s.Column) does not: the name is taken with another meaning") }
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Field security (P9)
# ---------------------------------------------------------------------------------------------------------------

$script:ReaderId = $null
$script:WriterId = $null
$script:ProfilesToCreate = [System.Collections.Generic.List[string]]::new()

function Get-ProfileByName([string]$Name) {
    $safe = $Name.Replace("'", "''")
    return @((Invoke-Dv "fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$safe'").value)
}

# Preconditions of the profile pair. A failure stops the script (escalation) in every mode: nothing is written.
function Test-ProfilePreconditions {
    $problems = [System.Collections.Generic.List[string]]::new()
    $defaultTeams = @((Invoke-Dv "teams?`$select=teamid,name&`$filter=isdefault eq true&`$expand=businessunitid(`$select=name,isdisabled)").value |
            Where-Object { -not $_.businessunitid.isdisabled })
    if ($defaultTeams.Count -eq 0) { $problems.Add('no business-unit default team was read - the query is wrong') }

    $readers = Get-ProfileByName $ReaderProfileName
    if ($readers.Count -gt 1) { $problems.Add("$($readers.Count) profiles are named '$ReaderProfileName' - exactly one is allowed") }
    elseif ($readers.Count -eq 1) {
        $script:ReaderId = $readers[0].fieldsecurityprofileid
        $onReader = @((Invoke-Dv "fieldsecurityprofiles($($script:ReaderId))/teamprofiles_association?`$select=teamid,name").value | ForEach-Object { $_.teamid.ToString().ToLowerInvariant() })
        foreach ($t in $defaultTeams) {
            if ($onReader -contains $t.teamid.ToString().ToLowerInvariant()) { Write-Report 'OK' "reader profile '$ReaderProfileName' is on default team '$($t.name)' (BU '$($t.businessunitid.name)')" }
            else { $problems.Add("default team '$($t.name)' (BU '$($t.businessunitid.name)') is NOT on '$ReaderProfileName': its users would read the P9 columns EMPTY. Adding a team is a team change (the owner's / uac-r2's); this script never adds a member to an existing profile") }
        }
    }
    elseif ($CreateProfiles) { $script:ProfilesToCreate.Add($ReaderProfileName); Write-Report 'WOULD' "create reader profile '$ReaderProfileName' and add every business unit default team ($($defaultTeams.Count))" }
    else { $problems.Add("reader profile '$ReaderProfileName' does not exist (uac-r2 creates it). Pass -CreateProfiles to create it here") }

    $bffUsers = @()
    foreach ($appId in $BffApplicationIds) {
        $u = @((Invoke-Dv "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $appId").value) | Select-Object -First 1
        if ($u) { $bffUsers += $u } else { $problems.Add("no application user for appId $appId in this environment") }
    }
    if ($BffApplicationIds.Count -eq 0) { $problems.Add('-BffApplicationIds is empty: the writer profile must name the BFF application user(s)') }

    $writers = Get-ProfileByName $WriterProfileName
    if ($writers.Count -gt 1) { $problems.Add("$($writers.Count) profiles are named '$WriterProfileName' - exactly one is allowed") }
    elseif ($writers.Count -eq 1) {
        $script:WriterId = $writers[0].fieldsecurityprofileid
        $members = @((Invoke-Dv "fieldsecurityprofiles($($script:WriterId))/systemuserprofiles_association?`$select=systemuserid,fullname,applicationid").value)
        $allowed = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
        foreach ($u in $bffUsers) {
            if ($members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -eq $u.systemuserid.ToString().ToLowerInvariant() }) { Write-Report 'OK' "writer profile '$WriterProfileName' holds BFF application user '$($u.fullname)'" }
            else { $problems.Add("BFF application user '$($u.fullname)' is NOT a member of '$WriterProfileName': the BFF could not write the P9 columns after they are secured") }
        }
        foreach ($m in $members | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowed }) {
            $kind = if ($m.applicationid) { "an application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
            $problems.Add("'$WriterProfileName' has member '$($m.fullname)', $kind - the profile's membership IS the lock; only the BFF may write these columns")
        }
        $teams = @((Invoke-Dv "fieldsecurityprofiles($($script:WriterId))/teamprofiles_association?`$select=teamid,name").value)
        foreach ($tm in $teams) { $problems.Add("'$WriterProfileName' has TEAM '$($tm.name)' as a member - every member of it could write these columns") }
    }
    elseif ($CreateProfiles) { $script:ProfilesToCreate.Add($WriterProfileName); Write-Report 'WOULD' "create writer profile '$WriterProfileName' with BFF application user(s) $(($bffUsers | ForEach-Object { $_.fullname }) -join ', ')" }
    else { $problems.Add("writer profile '$WriterProfileName' does not exist (uac-r2 creates it). Pass -CreateProfiles to create it here") }

    if ($problems.Count -gt 0) {
        throw [EscalationException]::new("Field security profile preconditions failed (nothing was written):`n    - " + ($problems -join "`n    - "))
    }
    return @{ BffUsers = $bffUsers; DefaultTeams = $defaultTeams }
}

function New-ProfilesIfNeeded($pre) {
    foreach ($name in $script:ProfilesToCreate) {
        if ($DryRun) { continue }
        $isReader = $name -eq $ReaderProfileName
        $desc = if ($isReader) { 'Read on the Procedure schema columns the BFF alone writes (ebilling-r1 task 080). Associated with EVERY business unit default team, so every user keeps reading them: a secured column is hidden from anyone without Read.' }
        else { 'Read/Create/Update on the Procedure schema columns the BFF alone writes (ebilling-r1 task 080). Members: the BFF application user(s) ONLY.' }
        $created = Invoke-Dv -Path 'fieldsecurityprofiles' -Method POST -Body @{ name = $name; description = $desc } -ExtraHeaders (@{ Prefer = 'return=representation' } + $SolutionHeader)
        $id = $created.fieldsecurityprofileid
        if ($isReader) {
            $script:ReaderId = $id
            foreach ($t in $pre.DefaultTeams) { Invoke-Dv -Path "fieldsecurityprofiles($id)/teamprofiles_association/`$ref" -Method POST -Body @{ '@odata.id' = "$($State.ApiBase)/teams($($t.teamid))" } | Out-Null }
        }
        else {
            $script:WriterId = $id
            foreach ($u in $pre.BffUsers) { Invoke-Dv -Path "fieldsecurityprofiles($id)/systemuserprofiles_association/`$ref" -Method POST -Body @{ '@odata.id' = "$($State.ApiBase)/systemusers($($u.systemuserid))" } | Out-Null }
        }
        $script:Created.Add("field security profile '$name'")
        Write-Report 'DONE' "created profile '$name'"
    }
}

function Get-FieldPermission([string]$ProfileId, [string]$Table, [string]$Column) {
    return @((Invoke-Dv "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $ProfileId and entityname eq '$Table' and attributelogicalname eq '$Column'").value) | Select-Object -First 1
}

function Grant-FieldPermission([string]$ProfileId, [string]$Table, [string]$Column, [int]$Write) {
    # Securing propagates asynchronously: a grant right after it can be refused 0x8004f508 "... is NOT secured". Retry that one error.
    for ($attempt = 1; ; $attempt++) {
        try {
            Invoke-Dv -Path 'fieldpermissions' -Method POST -Body @{
                entityname = $Table; attributelogicalname = $Column
                canread = 4; cancreate = $Write; canupdate = $Write
                'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($ProfileId)"
            } | Out-Null
            return
        }
        catch {
            if ("$($_.Exception.Message)" -notmatch '0x8004f508' -or $attempt -ge 12) { throw }
            Write-Host "    $Table.$Column not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
            Start-Sleep -Seconds 5
        }
    }
}

function Set-ColumnSecured([string]$Table, [string]$Column, [string]$Kind, [bool]$Value) {
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.${Kind}AttributeMetadata"
    $a = Get-DvOrNull $path -Retries 4
    if (-not $a) { throw "$Table.$Column was not found ($Kind)." }
    $a.IsSecured = $Value
    try {
        Invoke-WhilePropagating { Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    }
    catch {
        # The typed GET-then-PUT is proven live on lookup and string columns. A choice's definition carries its OptionSet as a
        # navigation property the plain GET omits and the PUT may need it: retry ONCE with the OptionSet expanded. A refused PUT
        # leaves IsSecured unchanged, so nothing is masked. Any other kind, or a second refusal, throws.
        if ($Kind -notin 'Picklist', 'Boolean') { throw }
        Write-Host "    PUT of $Table.$Column refused without its OptionSet ($($_.Exception.Message)); retrying once with `$expand=OptionSet..." -ForegroundColor DarkYellow
        $a = Get-DvOrNull "${path}?`$expand=OptionSet" -Retries 2
        $a.IsSecured = $Value
        Invoke-WhilePropagating { Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    }
}

function Write-MaskedRecovery([string]$Table, [string]$Column, [string]$Cause) {
    Write-Host ''
    Write-Host "  !!! RECOVERY REQUIRED NOW - $Table.$Column may be field-secured WITHOUT its grants !!!" -ForegroundColor Red
    Write-Host "  Cause: $Cause" -ForegroundColor Red
    Write-Host '  Either fix the cause and re-run this script (on an already-secured column it grants only what is missing), or un-secure the' -ForegroundColor Red
    Write-Host "  column: make.powerapps.com > Tables > $Table > Columns > $Column > Advanced options > clear 'Enable column security', save, publish $Table." -ForegroundColor Red
}

function Invoke-ColumnSecurity($spec) {
    $t = $spec.Table; $c = $spec.Column
    $tag = "$t.$c"
    $attr = Get-Attr $t $c 'LogicalName,AttributeType,IsSecured,CanBeSecuredForRead,CanBeSecuredForCreate,CanBeSecuredForUpdate,MetadataId'
    $grants = @(@{ Id = $script:ReaderId; Name = $ReaderProfileName; Write = 0 }, @{ Id = $script:WriterId; Name = $WriterProfileName; Write = 4 })

    if (-not $attr) {
        if (-not $DryRun) { throw "$tag is missing after its create; cannot secure it." }
        $script:Changed.Add("secure $tag"); Write-Host "  WOULD SECURE $tag after it is created, then grant: $(($grants | ForEach-Object { "$($_.Name) (read=4 create=$($_.Write) update=$($_.Write))" }) -join '; ')" -ForegroundColor Cyan
        foreach ($g in $grants) { $script:Created.Add("field permission $($g.Name) on $tag") }
        return
    }
    if (-not ($attr.CanBeSecuredForRead -and $attr.CanBeSecuredForCreate -and $attr.CanBeSecuredForUpdate)) {
        throw [EscalationException]::new("$tag cannot be secured for read, create and update. Owner decision needed.")
    }

    $needSecure = -not $attr.IsSecured
    $missing = @()
    foreach ($g in $grants) {
        $perm = if ($g.Id) { Get-FieldPermission $g.Id $t $c } else { $null }
        if ($perm) {
            if ($perm.canread -eq 4 -and $perm.cancreate -eq $g.Write -and $perm.canupdate -eq $g.Write) { Write-Report 'OK' "$($g.Name) on $tag (read=4 create=$($g.Write) update=$($g.Write))" }
            else { Write-Report 'FAIL' "$($g.Name) on $tag is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($g.Write) update=$($g.Write)" }
        }
        else { $missing += $g }
    }

    if (-not $needSecure -and $missing.Count -eq 0) {
        $script:Skipped.Add("secure $tag"); Write-Host "  skipped  $tag (IsSecured, profiles granted)" -ForegroundColor Yellow
        return
    }
    if ($DryRun) {
        if ($needSecure) { $script:Changed.Add("secure $tag"); Write-Host "  WOULD SECURE $tag" -ForegroundColor Cyan }
        else { $script:Skipped.Add("secure $tag"); Write-Host "  skipped  $tag already IsSecured" -ForegroundColor Yellow }
        foreach ($g in $missing) { $script:Created.Add("field permission $($g.Name) on $tag"); Write-Host "  WOULD GRANT $($g.Name) on $tag (read=4 create=$($g.Write) update=$($g.Write))" -ForegroundColor Cyan }
        return
    }

    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    if ($needSecure) {
        Set-ColumnSecured $t $c $spec.Kind $true
        $script:Changed.Add("secure $tag"); Write-Report 'DONE' "secured $tag"
    }
    try {
        foreach ($g in $missing) {
            Grant-FieldPermission $g.Id $t $c $g.Write
            $script:Created.Add("field permission $($g.Name) on $tag"); Write-Report 'DONE' "granted $($g.Name) on $tag (read=4 create=$($g.Write) update=$($g.Write))"
        }
    }
    catch {
        $grantError = $_
        Write-Host "  FAIL     granting the profiles on $tag failed: $($grantError.Exception.Message)" -ForegroundColor Red
        if ($needSecure) {
            Write-Host "           reverting $tag to NOT field-secured, so no reader is left masked..." -ForegroundColor Yellow
            try {
                Set-ColumnSecured $t $c $spec.Kind $false
                Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = "<importexportxml><entities><entity>$t</entity></entities></importexportxml>" } | Out-Null
                $back = (Get-Attr $t $c 'LogicalName,IsSecured').IsSecured
                if ($back) { throw "the revert was accepted but $tag still reads IsSecured=true" }
                Write-Host "  REVERTED $tag is NOT field-secured again. Fix the cause, then re-run." -ForegroundColor Yellow
            }
            catch { Write-MaskedRecovery $t $c "the grant failed ($($grantError.Exception.Message)) AND the revert failed ($($_.Exception.Message))" }
        }
        throw $grantError
    }
    $clock.Stop()
    if ($needSecure) { Write-Host ("  masked window for {0}: {1:N1}s (secure -> grants)" -f $tag, $clock.Elapsed.TotalSeconds) }
}

# Nobody besides the writer profile and System Administrator may create or update a secured column.
function Test-OtherProfiles {
    foreach ($spec in $script:Secured) {
        $t = $spec.Table; $c = $spec.Column
        if (-not (Get-Attr $t $c 'LogicalName')) { continue }
        $rows = @((Invoke-Dv "fieldpermissions?`$select=canread,cancreate,canupdate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$t' and attributelogicalname eq '$c'").value)
        $otherWriters = @($rows | Where-Object { ($_.canupdate -eq 4 -or $_.cancreate -eq 4) -and $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' })
        foreach ($o in $otherWriters) { Write-Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' can also write $t.$c - only the BFF writer profile may" }
        if ($otherWriters.Count -eq 0) { Write-Report 'OK' "no profile besides '$WriterProfileName' and System Administrator can write $t.$c" }
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 080 - Procedure schema (P1-P10)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host "Profiles   : reader '$ReaderProfileName', writer '$WriterProfileName' (members are never changed on an existing profile)"
Write-Host ""

try {
    if ($EnvironmentUrl -ne $DevUrl) {
        throw [EscalationException]::new("This script runs only against $DevUrl in R1 (project constraint); got $EnvironmentUrl. Nothing was changed.")
    }

    # --- 0. Pre-flight: every refusal that can be known before the first write ----------------------------------------------
    Write-Host "0. Pre-flight (read-only)" -ForegroundColor Cyan

    # 0a. The core-table list: parsed from source, never typed. Refuses (exit 2) when it cannot parse a non-empty list.
    $coreTables = Get-FilingCores $GuardSourcePath
    Write-Host "  FilingCores (parsed from $((Split-Path -Leaf $GuardSourcePath))): $($coreTables -join ', ')"
    foreach ($ref in @($RuleSet, $RateCard, $PolicyVersion, 'sprk_policy', 'sprk_organization', 'contact', 'systemuser')) {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$ref')?`$select=LogicalName" -Retries 4)) { throw [EscalationException]::new("$ref does not exist in $EnvironmentUrl. Nothing was changed.") }
    }
    Resolve-Plan $coreTables
    foreach ($c in $script:Cores) { Write-Host "    core table $($c.Table): schema $($c.Schema), display '$($c.Display)'" }

    # 0b. sprk_area binds to the EXISTING global choice sprk_policydomain; it is not created or changed here.
    $domain = Get-GlobalOptionSet $DomainChoice -Retries 2
    if (-not $domain) { throw [EscalationException]::new("Global choice $DomainChoice does not exist in $EnvironmentUrl (task 019 creates it); sprk_procedure.sprk_area binds to it. Nothing was changed.") }
    Write-Host "  global choice ${DomainChoice}: $(($domain.Options | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', ') (reused, not changed)"

    # 0c. P1: each global choice is absent or already exactly as planned.
    foreach ($g in $GlobalChoices) {
        $conflict = Get-GlobalChoiceConflict $g.Name (Get-ChoiceOptions $g.Labels)
        if ($conflict) { throw [EscalationException]::new($conflict + ' Nothing was changed.') }
        Write-Host "  global choice $($g.Name): $(if (Get-GlobalOptionSet $g.Name) { 'exists, identical' } else { 'absent' })"
    }

    # 0d. The three tables: absent, or user-owned with the right primary name. A column of another type, a lookup to another table,
    #     a relationship name taken with another meaning and a different delete behaviour all stop the script.
    $problems = [System.Collections.Generic.List[string]]::new()
    foreach ($t in @($Procedure, $Version, $Assignment)) {
        $e = Get-DvOrNull "EntityDefinitions(LogicalName='$t')?`$select=LogicalName,OwnershipType,PrimaryNameAttribute"
        if ($e) {
            if ($e.OwnershipType -ne 'UserOwned') { $problems.Add("$t exists as $($e.OwnershipType), expected UserOwned") }
            if ($e.PrimaryNameAttribute -ne 'sprk_name') { $problems.Add("$t exists with primary name '$($e.PrimaryNameAttribute)', expected sprk_name") }
            Write-Host "  NOTE: table $t ALREADY EXISTS - it is verified and skipped, not recreated or changed." -ForegroundColor Yellow
        }
    }
    foreach ($col in $script:Columns) {
        $a = Get-Attr $col.Table $col.Column
        if ($a -and $a.AttributeType -ne $col.Type) { $problems.Add("$($col.Table).$($col.Column) exists as $($a.AttributeType), expected $($col.Type)") }
    }
    foreach ($s in $script:Lookups) { Test-LookupSpecPreflight $s $problems }
    # Never-created items that exist would be a surprise (the old script never ran): report, never touch.
    foreach ($chk in @(@($RateCard, 'sprk_scope'), @($RuleSet, 'sprk_isexceptions'), @($RuleSet, 'sprk_allbusinessunits'))) {
        if (Get-Attr $chk[0] $chk[1]) { Write-Host "  NOTE: $($chk[0]).$($chk[1]) exists. This task never creates, changes or deletes it." -ForegroundColor Yellow }
    }
    if ($problems.Count -gt 0) {
        throw [EscalationException]::new("Existing items conflict with this task (nothing was written, nothing is renamed, renumbered or changed):`n    - " + ($problems -join "`n    - "))
    }

    # 0e. sprk_ruleset.sprk_scope: a local choice with the two known options; the two new values / labels are free or already ours.
    $have = Get-ScopeOptions
    Write-Host "  $RuleSet.$ScopeColumn options now: $(Format-Options $have)"
    foreach ($v in $ScopeKnown.Keys) {
        $cur = $have | Where-Object { $_.Value -eq [int]$v }
        if (-not $cur -or (ConvertTo-LabelKey $cur.Label) -ne (ConvertTo-LabelKey $ScopeKnown[$v])) {
            throw [EscalationException]::new("$RuleSet.$ScopeColumn option $v is '$($cur.Label)', expected '$($ScopeKnown[$v])'. Not touched; owner decision needed.")
        }
    }
    foreach ($v in $ScopeNew.Keys) {
        $key = ConvertTo-LabelKey $ScopeNew[$v]
        $byLabel = @($have | Where-Object { (ConvertTo-LabelKey $_.Label) -eq $key -and $_.Value -ne [int]$v })
        if ($byLabel.Count -gt 0) { throw [EscalationException]::new("Label '$($ScopeNew[$v])' already exists as value $($byLabel[0].Value), expected $v. Owner decision needed; nothing renumbered.") }
        $cur = $have | Where-Object { $_.Value -eq [int]$v }
        if ($cur -and (ConvertTo-LabelKey $cur.Label) -ne $key) { throw [EscalationException]::new("Value $v is labelled '$($cur.Label)', expected '$($ScopeNew[$v])'. Owner decision needed; nothing relabelled.") }
    }
    $other = @($have | Where-Object { $_.Value -notin @($ScopeKnown.Keys) + @($ScopeNew.Keys) })
    if ($other.Count -gt 0) { Write-Host "  note: $RuleSet.$ScopeColumn has options outside the set this task knows: $(Format-Options $other)" -ForegroundColor Yellow }
    Write-Host "  no existing item conflicts" -ForegroundColor Green

    # 0f. The profile pair (P9).
    Write-Host "  field security profiles:"
    $pre = Test-ProfilePreconditions

    # The lookup plan, for the schema review: every lookup with its relationship name and the delete behaviour it must read.
    Write-Host "  lookup plan ($($script:Lookups.Count) lookups): table.column -> target | relationship | delete behaviour" -ForegroundColor Cyan
    foreach ($s in $script:Lookups) {
        Write-Host ("    {0}.{1} -> {2} | {3} | {4}{5}" -f $s.Table, $s.Column, $s.Target, $s.Rel, (Get-ExpectedDelete $s), $(if ($s.Parental) { ' (parental)' } else { '' }))
    }

    # --- 1. P1 global choices ------------------------------------------------------------------------------------------------
    Write-Host "1. P1 global choices (values from 100000000)" -ForegroundColor Cyan
    foreach ($g in $GlobalChoices) { Add-GlobalChoice $g.Name $g.Display $g.Description (Get-ChoiceOptions $g.Labels) }

    # --- 2. P2 sprk_procedure ------------------------------------------------------------------------------------------------
    Write-Host "2. P2 table $Procedure (user-owned, audited)" -ForegroundColor Cyan
    New-ProcedureTable $Procedure 'Procedure' 'Procedures' 'A reusable document that drives ontology policy or rules (guideline, rate card, billing instructions, internal standard): the document as a lasting thing. Versions, assignments and its rule set point to it (ebilling-r1 EB-D-148, model 3.1).' 'Internal name of the Procedure.'
    Set-EbEntity $Procedure
    Add-Column 'sprk_firmfacingname' (New-StringDef 'sprk_FirmFacingName' 'Firm-facing Name' 'What a firm sees where the rule set name is shown (D-66, D-79). Defaults to Name.' 200) 'String'
    Add-GlobalChoiceColumn 'sprk_area' 'sprk_Area' 'Area' 'The ontology area the Procedure belongs to. Billing now (reuses sprk_policydomain).' $DomainChoice
    Add-GlobalChoiceColumn 'sprk_proceduretype' 'sprk_ProcedureType' 'Type' 'Guideline, Rate card, Billing instructions, Internal Standard or Other.' 'sprk_proceduretype'
    Add-GlobalChoiceColumn 'sprk_appliesto' 'sprk_AppliesTo' 'Applies To' 'All records, Firm or Assigned records only. Written only by the BFF (field-secured); locked once a version is Active (I-P4).' 'sprk_procedureappliesto'
    foreach ($s in $script:Lookups | Where-Object { $_.Table -eq $Procedure }) { Invoke-LookupSpec $s }
    Add-GlobalChoiceColumn 'sprk_status' 'sprk_Status' 'Status' 'Active or Retired. A custom choice, not statecode, so it can be field-secured. Written only by the BFF.' 'sprk_procedurestatus'
    Add-Column 'sprk_notes' (New-MemoDef 'sprk_Notes' 'Notes' 'Free-text notes about the Procedure.' 100000) 'Memo'

    # --- 3. P3 sprk_procedureversion -----------------------------------------------------------------------------------------
    Write-Host "3. P3 table $Version (user-owned, audited, parental to $Procedure)" -ForegroundColor Cyan
    New-ProcedureTable $Version 'Procedure Version' 'Procedure Versions' 'One issue of a Procedure: version number, effective dates, status, the policy versions it published and its rate card. Parental to its Procedure (ebilling-r1 EB-D-148, model 3.1).' 'Name of the version (for example "Version 2").'
    Set-EbEntity $Version
    foreach ($s in $script:Lookups | Where-Object { $_.Table -eq $Version -and $_.Column -eq $Procedure }) { Invoke-LookupSpec $s }
    Add-Column 'sprk_versionnumber' (New-WholeNumberDef 'sprk_VersionNumber' 'Version Number' 'Assigned by the BFF. With the Procedure it is the alternate key sprk_procedure_versionnumber.' 1 2147483647) 'Integer'
    Write-Host "   alternate key (sprk_procedure, sprk_versionnumber)" -ForegroundColor Cyan
    Add-AlternateKey $Version 'sprk_procedure_versionnumber' 'Procedure and version number' @($Procedure, 'sprk_versionnumber')
    Set-EbEntity $Version
    foreach ($s in $script:Lookups | Where-Object { $_.Table -eq $Version -and $_.Column -ne $Procedure }) { Invoke-LookupSpec $s }
    Add-Column 'sprk_effectivefrom' (New-DateOnlyDef 'sprk_EffectiveFrom' 'Effective From' 'First work date this version covers. DateOnly, half-open [from, to) (I-P1). Written only by the BFF (field-secured).') 'DateTime'
    Add-Column 'sprk_effectiveto' (New-DateOnlyDef 'sprk_EffectiveTo' 'Effective To' 'Work date this version stops covering (exclusive). DateOnly, half-open [from, to) (I-P1). Written only by the BFF (field-secured).') 'DateTime'
    Add-GlobalChoiceColumn 'sprk_status' 'sprk_Status' 'Status' 'Draft, Active, Superseded or Retired. A custom choice, not statecode, so it can be field-secured. Written only by the BFF.' 'sprk_procedureversionstatus'
    Add-Column 'sprk_notconverted' (New-TwoOptionDef 'sprk_NotConverted' 'Not Converted' 'Yes when drafting could not convert part of this version''s document into rules. Per-version marker for BillingRuleDraftJobHandler (task 116); the rule set keeps its own. Default No. Written only by the BFF (field-secured).' $false) 'Boolean'

    # --- 4. P4 sprk_procedureassignment --------------------------------------------------------------------------------------
    Write-Host "4. P4 table $Assignment (user-owned, audited)" -ForegroundColor Cyan
    New-ProcedureTable $Assignment 'Procedure Assignment' 'Procedure Assignments' 'A Procedure assigned to a core record, with a per-record mode (follow latest, pinned, excluded) or a rule-level override. Secure child of the record it names (ebilling-r1 EB-D-148, model 5.2, 6.2, 6.3).' 'Name of the assignment.'
    Set-EbEntity $Assignment
    foreach ($s in $script:Lookups | Where-Object { $_.Table -eq $Assignment -and $_.Column -in $script:Cores.Table }) { Invoke-LookupSpec $s }
    Set-EbEntity $Assignment
    Add-LocalChoice -Column 'sprk_mode' -Schema 'sprk_Mode' -Display 'Mode' -Description 'Follow latest (the version in force on the work date), Pinned (the Pinned Version whatever is newer) or Excluded (the Procedure or rule does not apply here). Model 6.2.' -Labels @('Follow latest', 'Pinned', 'Excluded')
    foreach ($s in $script:Lookups | Where-Object { $_.Table -eq $Assignment -and $_.Column -notin $script:Cores.Table }) { Invoke-LookupSpec $s }
    Set-EbEntity $Assignment
    Add-Column 'sprk_reason' (New-MemoDef 'sprk_Reason' 'Reason' 'Why the override exists (for example "grandfathered on the 2025 rate card").' 2000) 'Memo'

    # --- 5. P5 sprk_ruleset --------------------------------------------------------------------------------------------------
    Write-Host "5. P5 $RuleSet record lookups, scope options, Procedure link, creator person" -ForegroundColor Cyan
    foreach ($s in $script:Lookups | Where-Object { $_.Table -eq $RuleSet }) { Invoke-LookupSpec $s }
    Write-Host "   $RuleSet.$ScopeColumn options: Record = 100000002, Firm record = 100000003" -ForegroundColor Cyan
    $have = Get-ScopeOptions
    foreach ($v in $ScopeNew.Keys) {
        $label = $ScopeNew[$v]
        $cur = $have | Where-Object { $_.Value -eq [int]$v }
        $name = "$RuleSet.$ScopeColumn=$v"
        if ($cur) { $script:Skipped.Add($name); Write-Host "  skipped  $v $label (already present)" -ForegroundColor Yellow; continue }
        if ($DryRun) { $script:Created.Add($name); Write-Host "  WOULD INSERT $v $label" -ForegroundColor Cyan; continue }
        try {
            Invoke-Dv -Path 'InsertOptionValue' -Method POST -Body @{
                EntityLogicalName = $RuleSet; AttributeLogicalName = $ScopeColumn; Value = [int]$v; Label = (New-Label $label)
                SolutionUniqueName = $SolutionUniqueName
            } | Out-Null
        }
        catch {
            # A stale read hid an option that is already there: the refusal is authoritative, verify on a settled read.
            if (-not (Test-DuplicateRefusal $_.Exception.Message -Loose)) { throw }
            $found = $null
            for ($n = 0; $n -lt 12 -and -not $found; $n++) {
                $found = Get-ScopeOptions | Where-Object { $_.Value -eq [int]$v -and (ConvertTo-LabelKey $_.Label) -eq (ConvertTo-LabelKey $label) }
                if (-not $found) { Start-Sleep -Seconds 5 }
            }
            if (-not $found) { throw }
            $script:Skipped.Add($name); Write-Host "  skipped  $v $label (exists, found via create refusal)" -ForegroundColor Yellow; continue
        }
        $script:Created.Add($name); $script:Wrote = $true; Write-Host "  INSERTED $v $label" -ForegroundColor Green
    }

    # --- 6. P6 sprk_policyversion --------------------------------------------------------------------------------------------
    Write-Host "6. P6 $PolicyVersion : sprk_procedureversion, sprk_publishstate, sprk_basedonversion" -ForegroundColor Cyan
    foreach ($s in $script:Lookups | Where-Object { $_.Table -eq $PolicyVersion }) { Invoke-LookupSpec $s }
    Set-EbEntity $PolicyVersion
    Add-GlobalChoiceColumn 'sprk_publishstate' 'sprk_PublishState' 'Publish State' 'Draft or Published. Only Published policy versions are loaded by the resolver (model 7.2). Optional, no default: existing rows stay empty (dev data is recreated, O-13).' 'sprk_publishstate'

    # --- 7. P7 sprk_ratecard -------------------------------------------------------------------------------------------------
    Write-Host "7. P7 $RateCard : sprk_procedureversion" -ForegroundColor Cyan
    foreach ($s in $script:Lookups | Where-Object { $_.Table -eq $RateCard }) { Invoke-LookupSpec $s }

    # --- 8. P8 billing firm / billing responsible on every core table --------------------------------------------------------
    Write-Host "8. P8 sprk_billingfirm and sprk_billingresponsible on every FilingCores table (RemoveLink, optional, confer no access)" -ForegroundColor Cyan
    foreach ($s in $script:Lookups | Where-Object { $_.Table -in $script:Cores.Table }) { Invoke-LookupSpec $s }

    # --- 9. P9 field security ------------------------------------------------------------------------------------------------
    Write-Host "9. P9 field security (reader: Read for every user; writer: BFF application user(s) only)" -ForegroundColor Cyan
    New-ProfilesIfNeeded $pre
    foreach ($spec in $script:Secured) { Invoke-ColumnSecurity $spec }
    Test-OtherProfiles

    # --- Publish (only when something changed; entity-scoped, never PublishAllXml) -------------------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $metaChanged = $script:Created.Count -gt 0 -or @($script:Changed | Where-Object { $_ -like 'secure *' -or $_ -like '* auditing' }).Count -gt 0
    if ($DryRun) { Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan }
    elseif (-not $metaChanged) { Write-Host "  nothing changed - no publish" -ForegroundColor Yellow }
    else {
        $entities = @($Procedure, $Version, $Assignment, $RuleSet, $PolicyVersion, $RateCard, 'sprk_policy', 'sprk_organization', 'contact', 'systemuser') + @($script:Cores.Table)
        $xml = "<importexportxml><entities>$(($entities | ForEach-Object { "<entity>$_</entity>" }) -join '')</entities><optionsets>$(($GlobalChoices | ForEach-Object { "<optionset>$($_.Name)</optionset>" }) -join '')</optionsets></importexportxml>"
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = $xml } | Out-Null
        Write-Host "  customizations published" -ForegroundColor Green
    }

    # --- Verification (metadata read of everything this script owns) ---------------------------------------------------------------
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

        # Global choices: exactly the listed labels and values; sprk_area bound to sprk_policydomain.
        foreach ($g in $GlobalChoices) {
            $os = Get-GlobalOptionSet $g.Name -Retries 4
            if (-not $os) { $bad.Add("global choice $($g.Name) missing"); continue }
            $conflict = Get-GlobalChoiceConflict $g.Name (Get-ChoiceOptions $g.Labels)
            Write-Host "  $($g.Name): $(($os.Options | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', ')"
            if ($conflict) { $bad.Add($conflict) }
        }
        $pk = Get-DvOrNull "EntityDefinitions(LogicalName='$Procedure')/Attributes(LogicalName='sprk_area')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Name,IsGlobal)"
        if (-not $pk -or -not $pk.OptionSet.IsGlobal -or $pk.OptionSet.Name -ne $DomainChoice) { $bad.Add("$Procedure.sprk_area is not bound to the global choice $DomainChoice") } else { Write-Host "  $Procedure.sprk_area bound to $DomainChoice" }
        foreach ($bind in @(@($Procedure, 'sprk_proceduretype', 'sprk_proceduretype'), @($Procedure, 'sprk_appliesto', 'sprk_procedureappliesto'), @($Procedure, 'sprk_status', 'sprk_procedurestatus'), @($Version, 'sprk_status', 'sprk_procedureversionstatus'), @($PolicyVersion, 'sprk_publishstate', 'sprk_publishstate'))) {
            $p = Get-DvOrNull "EntityDefinitions(LogicalName='$($bind[0])')/Attributes(LogicalName='$($bind[1])')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Name,IsGlobal)"
            if (-not $p -or -not $p.OptionSet.IsGlobal -or $p.OptionSet.Name -ne $bind[2]) { $bad.Add("$($bind[0]).$($bind[1]) is not bound to the global choice $($bind[2])") }
        }

        # Tables: user-owned and audited.
        foreach ($t in @($Procedure, $Version, $Assignment)) {
            $e = Get-DvOrNull "EntityDefinitions(LogicalName='$t')?`$select=OwnershipType,IsAuditEnabled,PrimaryNameAttribute" -Retries 4
            if (-not $e) { $bad.Add("table $t missing"); continue }
            Write-Host "  table ${t}: $($e.OwnershipType), audited = $($e.IsAuditEnabled.Value), primary name $($e.PrimaryNameAttribute)"
            if ($e.OwnershipType -ne 'UserOwned') { $bad.Add("$t is $($e.OwnershipType), expected UserOwned") }
            if ($e.IsAuditEnabled.Value -ne $true) { $bad.Add("$t is not audited") }
        }

        # DateOnly effective dates.
        foreach ($c in 'sprk_effectivefrom', 'sprk_effectiveto') {
            $d = Get-DvOrNull "EntityDefinitions(LogicalName='$Version')/Attributes(LogicalName='$c')/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata?`$select=Format,DateTimeBehavior"
            if (-not $d -or $d.DateTimeBehavior.Value -ne 'DateOnly' -or $d.Format -ne 'DateOnly') { $bad.Add("$Version.$c is not DateOnly") } else { Write-Host "  $Version.${c}: DateOnly" }
        }
        $nc = Get-DvOrNull "EntityDefinitions(LogicalName='$Version')/Attributes(LogicalName='sprk_notconverted')/Microsoft.Dynamics.CRM.BooleanAttributeMetadata?`$select=DefaultValue"
        if (-not $nc -or $nc.DefaultValue -ne $false) { $bad.Add("$Version.sprk_notconverted default is not No") } else { Write-Host "  $Version.sprk_notconverted: two options, default No" }

        # Alternate key Active.
        $key = Get-Keys $Version | Where-Object { $_.SchemaName -eq 'sprk_procedure_versionnumber' } | Select-Object -First 1
        if (-not $key) { $bad.Add("alternate key sprk_procedure_versionnumber missing") }
        else {
            Write-Host "  alternate key $($key.SchemaName) on ($(@($key.KeyAttributes) -join ', ')): $($key.EntityKeyIndexStatus)"
            if ($key.EntityKeyIndexStatus -ne 'Active') { $bad.Add("alternate key sprk_procedure_versionnumber is $($key.EntityKeyIndexStatus), expected Active") }
        }

        # Local choice sprk_mode: exactly three options 100000000-100000002.
        $modeOpts = Get-PicklistLabels $Assignment 'sprk_mode'
        Write-Host "  $Assignment.sprk_mode options: $($modeOpts -join ', ')"
        if (($modeOpts -join '|') -ne 'Follow latest|Pinned|Excluded') { $bad.Add("$Assignment.sprk_mode options are not exactly Follow latest, Pinned, Excluded") }

        # Delete behaviour of every lookup this script owns; no lookup to a core table reads Restrict.
        $coreSet = @($script:Cores.Table)
        foreach ($s in $script:Lookups) {
            $rel = Get-LookupRelationship $s.Table $s.Column
            if (-not $rel) { $bad.Add("no many-to-one relationship for $($s.Table).$($s.Column)"); continue }
            $want = Get-ExpectedDelete $s
            Write-Host ("  {0}.{1} -> {2}: {3}  delete {4}" -f $s.Table, $s.Column, $s.Target, $rel.SchemaName, $rel.CascadeConfiguration.Delete)
            if ($rel.CascadeConfiguration.Delete -ne $want) { $bad.Add("$($s.Table).$($s.Column) delete behaviour is $($rel.CascadeConfiguration.Delete), expected $want") }
            if ($s.Target -in $coreSet -and $rel.CascadeConfiguration.Delete -eq 'Restrict') { $bad.Add("$($s.Table).$($s.Column) targets a core table and reads Restrict") }
            if ($rel.SchemaName -ne $s.Rel) { $bad.Add("$($s.Table).$($s.Column) relationship is named $($rel.SchemaName), expected $($s.Rel)") }
        }
        $par = Get-LookupRelationship $Version $Procedure
        if ($par -and ($par.CascadeConfiguration.Reparent -ne 'Cascade')) { $bad.Add("$Version.$Procedure is not a parental relationship (reparent $($par.CascadeConfiguration.Reparent))") }

        # sprk_scope options exactly 100000000-100000003; one record lookup per FilingCores table.
        $now = Get-ScopeOptions
        Write-Host "  $RuleSet.$ScopeColumn options: $(Format-Options $now)"
        $want = @($ScopeKnown.Keys) + @($ScopeNew.Keys) | ForEach-Object { [int]$_ }
        if (@($now | Where-Object { $_.Value -notin $want }).Count -gt 0 -or @($want | Where-Object { $_ -notin $now.Value }).Count -gt 0) { $bad.Add("$RuleSet.$ScopeColumn options are not exactly 100000000-100000003") }
        foreach ($c in $script:Cores) {
            $n = @((Get-DvOrNull "EntityDefinitions(LogicalName='$RuleSet')/ManyToOneRelationships?`$select=SchemaName&`$filter=ReferencedEntity eq '$($c.Table)'").value).Count
            if ($n -ne 1) { $bad.Add("$RuleSet has $n lookups to $($c.Table), expected exactly 1") }
        }

        # sprk_billingfirm / sprk_billingresponsible: on every FilingCores table and on no other table.
        foreach ($col in 'sprk_billingfirm', 'sprk_billingresponsible') {
            $on = @((Get-DvOrNull "EntityDefinitions(LogicalName='sprk_organization')/OneToManyRelationships?`$select=ReferencingEntity,ReferencingAttribute&`$filter=ReferencingAttribute eq '$col'").value | ForEach-Object { $_.ReferencingEntity } | Sort-Object)
            Write-Host "  $col is on: $($on -join ', ')"
            if (($on -join '|') -ne ((@($coreSet) | Sort-Object) -join '|')) { $bad.Add("$col is on [$($on -join ', ')], expected exactly [$($coreSet -join ', ')]") }
        }

        # Field security: IsSecured plus the two permission rows on every P9 column.
        foreach ($spec in $script:Secured) {
            $a = Get-Attr $spec.Table $spec.Column 'LogicalName,IsSecured'
            if (-not $a -or -not $a.IsSecured) { $bad.Add("$($spec.Table).$($spec.Column) is not IsSecured"); continue }
            $r = Get-FieldPermission $script:ReaderId $spec.Table $spec.Column
            $w = Get-FieldPermission $script:WriterId $spec.Table $spec.Column
            $rOk = $r -and $r.canread -eq 4 -and $r.cancreate -eq 0 -and $r.canupdate -eq 0
            $wOk = $w -and $w.canread -eq 4 -and $w.cancreate -eq 4 -and $w.canupdate -eq 4
            Write-Host "  $($spec.Table).$($spec.Column): IsSecured = true, reader row $(if ($rOk) { 'read-only' } else { 'WRONG' }), writer row $(if ($wOk) { 'read/create/update' } else { 'WRONG' })"
            if (-not $rOk) { $bad.Add("$($spec.Table).$($spec.Column): reader profile row is not Read only") }
            if (-not $wOk) { $bad.Add("$($spec.Table).$($spec.Column): writer profile row is not Read/Create/Update") }
        }

        # Negatives: items this task never creates.
        foreach ($chk in @(@($RateCard, 'sprk_scope'), @($RuleSet, 'sprk_isexceptions'), @($RuleSet, 'sprk_allbusinessunits'))) {
            if (Get-Attr $chk[0] $chk[1]) { $bad.Add("$($chk[0]).$($chk[1]) exists; this task never creates it") }
        }
        foreach ($c in $script:Cores) { if (Get-Attr $c.Table 'sprk_securedfrombusinessunit') { $bad.Add("$($c.Table).sprk_securedfrombusinessunit exists; this task never creates it") } }
        if (Get-DvOrNull "RelationshipDefinitions(SchemaName='sprk_ruleset_businessunit')") { $bad.Add('N:N sprk_ruleset_businessunit exists; this task never creates it') }
        if (Get-DvOrNull "EntityDefinitions(LogicalName='sprk_storedfile')?`$select=LogicalName") { Write-Host "  note: sprk_storedfile exists (task 088's, not this script's)" -ForegroundColor Yellow }

        # External Collaborator holds no privilege on the three new tables (read only; this script adds no role privilege).
        try {
            $roles = @((Invoke-Dv "roles?`$select=roleid,name&`$filter=name eq 'External Collaborator'").value)
            foreach ($role in $roles) {
                $privs = @((Invoke-Dv "roles($($role.roleid))/roleprivileges_association?`$select=name").value | Where-Object { $_.name -match "(?i)sprk_(procedure|procedureversion|procedureassignment)$" })
                if ($privs.Count -gt 0) { $bad.Add("External Collaborator role $($role.roleid) holds privilege(s) on the new tables: $(($privs | ForEach-Object { $_.name }) -join ', ')") }
            }
            if ($roles.Count -eq 0) { Write-Host "  WARNING: no role named 'External Collaborator' exists in this environment (read 2026-10-21: none on dev), so nothing was checked. Confirm by hand which role(s) flagged-external users hold and that none has a privilege on the three new tables (uac-r2 task 191 / K-1)." -ForegroundColor DarkYellow }
            else { Write-Host "  External Collaborator role(s) checked ($($roles.Count)): no privilege on the three new tables" -ForegroundColor Green }
        }
        catch { Write-Host "  WARNING: could not read the External Collaborator role privileges ($($_.Exception.Message)); check by hand." -ForegroundColor DarkYellow }

        if ($bad.Count -gt 0) { throw "Verification failed: $($bad -join '; ')" }
        Write-Host "  all $($script:Expect.Count) expected columns present with the right type and target; every check above passed" -ForegroundColor Green
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
Write-Host ("created : {0}" -f $script:Created.Count)
$script:Created | ForEach-Object { Write-Host "    + $_" }
Write-Host ("changed : {0}" -f $script:Changed.Count)
$script:Changed | ForEach-Object { Write-Host "    ~ $_" }
Write-Host ("skipped : {0}" -f $script:Skipped.Count)
$script:Skipped | ForEach-Object { Write-Host "    = $_" }
if ($State.Deferred.Count -gt 0) { Write-Host ("deferred: {0}  {1}" -f $State.Deferred.Count, ($State.Deferred -join ', ')) -ForegroundColor DarkYellow }
if ($Gaps.Count -gt 0) {
    Write-Host ""
    Write-Host "FINDINGS ($($Gaps.Count)):" -ForegroundColor Red
    $Gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
}
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
elseif ($script:Created.Count -eq 0 -and $script:Changed.Count -eq 0) { Write-Host "Idempotent: nothing was created or changed." -ForegroundColor Green }
if ($Gaps.Count -gt 0) { exit 1 }
exit 0
