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
    Project: ebilling-r1, task 019. Dev only in R1. Pattern: scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1
    (task 018) and Deploy-EbillingInvoiceHeaderSchema.ps1 (task 012, statuscode values); the global option set follows
    scripts/Deploy-AiCatalogSchemaExtensions.ps1.
    Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (destructive or conflicting).
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster'
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$ApiBase = "$EnvironmentUrl/api/data/v9.2"
$RuleSetTable = 'sprk_ruleset'
$PolicyTable = 'sprk_policy'
$VersionTable = 'sprk_policyversion'
$DomainOptionSet = 'sprk_policydomain'

# The table the column helpers currently write to.
$script:Entity = $RuleSetTable
# Tables created by this run (their metadata may lag) and tables a WhatIf run would create.
$script:JustCreated = [System.Collections.Generic.List[string]]::new()
$script:Planned = [System.Collections.Generic.List[string]]::new()
# What the final verification must find: table, column, expected AttributeType.
$script:Expect = [System.Collections.Generic.List[object]]::new()

# Tallies for the final summary.
$script:Created = [System.Collections.Generic.List[string]]::new()
$script:Changed = [System.Collections.Generic.List[string]]::new()
$script:Skipped = [System.Collections.Generic.List[string]]::new()
$script:Token = $null
$script:TokenAt = [datetime]::MinValue

class EscalationException : System.Exception {
    EscalationException([string]$message) : base($message) {}
}

# ---------------------------------------------------------------------------------------------------------------
# Web API plumbing (same behaviour as the task 010 / 012 scripts)
# ---------------------------------------------------------------------------------------------------------------

function Get-DataverseToken {
    if ($script:Token -and ((Get-Date) - $script:TokenAt).TotalMinutes -lt 40) { return $script:Token }
    $t = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>&1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($t)) {
        throw "Could not get a Dataverse token from Azure CLI ($t). Run 'az login'."
    }
    $script:Token = $t.Trim()
    $script:TokenAt = Get-Date
    return $script:Token
}

function Get-ErrorText($err) {
    $text = $err.Exception.Message
    if ($err.ErrorDetails -and $err.ErrorDetails.Message) {
        try {
            $j = $err.ErrorDetails.Message | ConvertFrom-Json -ErrorAction Stop
            if ($j.error.message) { $text = "$($j.error.code): $($j.error.message)" }
        } catch { $text = $err.ErrorDetails.Message }
    }
    return $text
}

function Get-StatusCode($err) {
    try { return [int]$err.Exception.Response.StatusCode } catch { return 0 }
}

# A write that hit a customization / publish lock, throttling or a transient fault is retried with backoff.
function Test-Transient([int]$status, [string]$text) {
    if ($status -in 429, 500, 502, 503, 504) { return $true }
    return ($text -match '(?i)publish.*(in progress|running)|(in progress).*publish|customization.*(lock|in progress)|(lock|locked).*customiz|another .*import|0x80071151|0x80048d19|SqlException.*(timeout|deadlock)|timeout|timed out|unexpected error occurred')
}

function Invoke-Dv {
    param(
        [string]$Path,
        [string]$Method = 'GET',
        $Body = $null,
        [hashtable]$ExtraHeaders = @{},
        [switch]$NoRetry
    )
    $uri = if ($Path -like 'http*') { $Path } else { "$ApiBase/$Path" }
    $attempt = 0
    $max = if ($NoRetry -or $Method -eq 'GET') { 3 } else { 7 }
    while ($true) {
        $attempt++
        $headers = @{
            Authorization      = "Bearer $(Get-DataverseToken)"
            'OData-MaxVersion' = '4.0'
            'OData-Version'    = '4.0'
            Accept             = 'application/json'
        }
        foreach ($k in $ExtraHeaders.Keys) { $headers[$k] = $ExtraHeaders[$k] }
        $p = @{ Uri = $uri; Method = $Method; Headers = $headers }
        if ($null -ne $Body) {
            $p.Body = ($Body | ConvertTo-Json -Depth 30 -Compress)
            $p.ContentType = 'application/json; charset=utf-8'
        }
        try {
            return Invoke-RestMethod @p
        } catch {
            $status = Get-StatusCode $_
            $text = Get-ErrorText $_
            if ($status -eq 401 -and $attempt -eq 1) { $script:Token = $null; continue }
            if ($attempt -lt $max -and (Test-Transient $status $text)) {
                $delay = [Math]::Min(60, 5 * [Math]::Pow(2, $attempt - 1))
                Write-Host "    transient failure (HTTP $status), retry $attempt/$($max - 1) in ${delay}s: $text" -ForegroundColor DarkYellow
                Start-Sleep -Seconds $delay
                continue
            }
            $e = [System.Exception]::new("API error ($Method $Path) HTTP ${status}: $text")
            $e.Data['Status'] = $status
            throw $e
        }
    }
}

# Returns $null when the item does not exist (HTTP 404 / "does not exist"); any other failure propagates.
function Get-DvOrNull([string]$Path, [int]$Retries = 0) {
    for ($i = 0; $true; $i++) {
        try { return Invoke-Dv -Path $Path }
        catch {
            $s = $_.Exception.Data['Status']
            if ($s -eq 404 -or $_.Exception.Message -match '(?i)could not find|does not exist|0x80060888|0x80040217') {
                # Metadata reads are eventually consistent: a must-exist item can read as 404 once. -Retries re-reads it.
                if ($i -lt $Retries) { Start-Sleep -Seconds 3; continue }
                return $null
            }
            throw
        }
    }
}

function New-Label([string]$Text) {
    return @{
        '@odata.type'   = 'Microsoft.Dynamics.CRM.Label'
        LocalizedLabels = @(@{
                '@odata.type' = 'Microsoft.Dynamics.CRM.LocalizedLabel'
                Label         = $Text
                LanguageCode  = 1033
            })
    }
}

$SolutionHeader = @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName }

# ---------------------------------------------------------------------------------------------------------------
# Item helpers
# ---------------------------------------------------------------------------------------------------------------

function Get-Attr([string]$Table, [string]$Column, [string]$Select = 'LogicalName,AttributeType') {
    return Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')?`$select=$Select"
}

function Assert-AttrType($attr, [string]$Column, [string]$ExpectedType) {
    if ($attr.AttributeType -ne $ExpectedType) {
        throw [EscalationException]::new("$($script:Entity).$Column already exists with type '$($attr.AttributeType)' but this task needs '$ExpectedType'. A same-name column of a different type must not be changed by script - owner decision needed.")
    }
}

function Test-TableKnown([string]$Table) {
    return ($script:Planned -contains $Table) -or [bool](Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 2)
}

# Metadata reads are eventually consistent across Dataverse nodes, so an existence check can be a false negative. A
# create refused with "already exists" is therefore the authoritative "it is there": the item is counted as skipped,
# after the type check on a settled read. A create issued on a table created seconds ago can be refused as "entity not
# found" (0x80041102, MetadataCache) while the table propagates: that is retried (only when this run created a table).
function Invoke-DvCreate {
    param([string]$Path, $Body, [string]$Column, [string]$ExpectedType)
    $table = $script:Entity
    for ($try = 0; $true; $try++) {
        try {
            Invoke-Dv -Path $Path -Method POST -Body $Body -ExtraHeaders $SolutionHeader | Out-Null
            return 'created'
        } catch {
            $msg = $_.Exception.Message
            if ($msg -match '(?i)already exists|0x80047013|0x80048403') {
                for ($i = 0; $i -lt 12; $i++) {
                    $attr = Get-Attr $table $Column
                    if ($attr) { Assert-AttrType $attr $Column $ExpectedType; break }
                    Start-Sleep -Seconds 5
                }
                return 'exists'
            }
            if ($script:JustCreated.Count -gt 0 -and $try -lt 12 -and $msg -match '(?i)could not find|does not exist|0x80060888|0x80040217|0x80041102|MetadataCache|HTTP 404') {
                Write-Host "    new table still propagating in the metadata cache, retry $($try + 1)/12 in 8s" -ForegroundColor DarkYellow
                Start-Sleep -Seconds 8
                continue
            }
            throw
        }
    }
}

function Wait-AttrVisible([string]$Column) {
    for ($i = 0; $i -lt 12; $i++) {
        if (Get-Attr $script:Entity $Column) { return }
        Start-Sleep -Seconds 5
    }
    throw "$($script:Entity).$Column was created but is not visible in metadata after 60 s."
}

function Add-Column {
    param([string]$Column, [hashtable]$Definition, [string]$ExpectedType)
    $script:Expect.Add(@{ Table = $script:Entity; Column = $Column; Type = $ExpectedType })
    $attr = Get-Attr $script:Entity $Column
    if ($attr) {
        Assert-AttrType $attr $Column $ExpectedType
        $script:Skipped.Add("$($script:Entity).$Column"); Write-Host "  skipped  $Column (exists, $ExpectedType)" -ForegroundColor Yellow
        return
    }
    if ($DryRun) { $script:Created.Add("$($script:Entity).$Column"); Write-Host "  WOULD CREATE $Column ($ExpectedType)" -ForegroundColor Cyan; return }
    $r = Invoke-DvCreate -Path "EntityDefinitions(LogicalName='$($script:Entity)')/Attributes" -Body $Definition -Column $Column -ExpectedType $ExpectedType
    if ($r -eq 'exists') { $script:Skipped.Add("$($script:Entity).$Column"); Write-Host "  skipped  $Column (exists, found via create refusal)" -ForegroundColor Yellow; return }
    Wait-AttrVisible $Column
    $script:Created.Add("$($script:Entity).$Column"); Write-Host "  CREATED  $Column ($ExpectedType)" -ForegroundColor Green
}

function Add-Lookup {
    param(
        [string]$Column, [string]$SchemaName, [string]$RelSchema, [string]$Target, [string]$Display, [string]$Description,
        [string]$DeleteBehavior = 'RemoveLink', [string]$RequiredLevel = 'None'
    )
    $table = $script:Entity
    $script:Expect.Add(@{ Table = $table; Column = $Column; Type = 'Lookup' })
    $attr = Get-Attr $table $Column
    if ($attr) {
        Assert-AttrType $attr $Column 'Lookup'
        $lk = Get-DvOrNull "EntityDefinitions(LogicalName='$table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
        if ($lk -and $lk.Targets -and ($lk.Targets -notcontains $Target)) {
            throw [EscalationException]::new("$table.$Column already exists as a lookup to '$($lk.Targets -join ',')' but this task needs '$Target'. Owner decision needed.")
        }
        $script:Skipped.Add("$table.$Column"); Write-Host "  skipped  $Column (exists, Lookup -> $Target)" -ForegroundColor Yellow
        return
    }
    if (-not (Test-TableKnown $Target)) {
        throw "Lookup target table '$Target' for $Column does not exist in $EnvironmentUrl."
    }
    if ($DryRun) { $script:Created.Add("$table.$Column"); Write-Host "  WOULD CREATE $Column (Lookup -> $Target)" -ForegroundColor Cyan; return }
    $rel = @{
        '@odata.type'        = 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata'
        SchemaName           = $RelSchema
        ReferencedEntity     = $Target
        ReferencingEntity    = $table
        CascadeConfiguration = @{
            Assign = 'NoCascade'; Delete = $DeleteBehavior; Merge = 'NoCascade'
            Reparent = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'
        }
        Lookup               = @{
            '@odata.type'  = 'Microsoft.Dynamics.CRM.LookupAttributeMetadata'
            SchemaName     = $SchemaName
            DisplayName    = New-Label $Display
            Description    = New-Label $Description
            RequiredLevel  = @{ Value = $RequiredLevel }
        }
    }
    $r = Invoke-DvCreate -Path 'RelationshipDefinitions' -Body $rel -Column $Column -ExpectedType 'Lookup'
    if ($r -eq 'exists') { $script:Skipped.Add("$table.$Column"); Write-Host "  skipped  $Column (exists, found via create refusal)" -ForegroundColor Yellow; return }
    Wait-AttrVisible $Column
    $script:Created.Add("$table.$Column"); Write-Host "  CREATED  $Column (Lookup -> $Target)" -ForegroundColor Green
}

function New-StringDef([string]$Schema, [string]$Display, [string]$Description, [int]$MaxLength, [string]$Format = 'Text', [string]$RequiredLevel = 'None') {
    return @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'
        SchemaName     = $Schema
        DisplayName    = New-Label $Display
        Description    = New-Label $Description
        RequiredLevel  = @{ Value = $RequiredLevel }
        MaxLength      = $MaxLength
        FormatName     = @{ Value = $Format }
    }
}

function New-MemoDef([string]$Schema, [string]$Display, [string]$Description, [int]$MaxLength) {
    return @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.MemoAttributeMetadata'
        SchemaName     = $Schema
        DisplayName    = New-Label $Display
        Description    = New-Label $Description
        RequiredLevel  = @{ Value = 'None' }
        MaxLength      = $MaxLength
        Format         = 'TextArea'
    }
}

# A LOCAL choice column. If it exists its option labels must be exactly the expected set (never edited by script).
function Add-LocalChoice {
    param([string]$Column, [string]$Schema, [string]$Display, [string]$Description, [string[]]$Labels, [string]$RequiredLevel = 'None')
    $table = $script:Entity
    $attr = Get-Attr $table $Column
    if ($attr) {
        Assert-AttrType $attr $Column 'Picklist'
        $script:Expect.Add(@{ Table = $table; Column = $Column; Type = 'Picklist' })
        # The option list of a column created moments ago can read back empty (eventual consistency): re-read before judging.
        $have = @()
        for ($i = 0; $i -lt 12 -and $have.Count -eq 0; $i++) {
            $pk = Get-DvOrNull "EntityDefinitions(LogicalName='$table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal)"
            $have = @($pk.OptionSet.Options | ForEach-Object { $_.Label.UserLocalizedLabel.Label })
            if ($have.Count -eq 0) { Start-Sleep -Seconds 5 }
        }
        if ($have.Count -ne $Labels.Count -or @($have | Where-Object { $_ -notin $Labels }).Count -gt 0) {
            throw [EscalationException]::new("$table.$Column exists with options [$($have -join ', ')] but the spec needs exactly [$($Labels -join ', ')]. Editing or removing options needs an owner decision.")
        }
        $script:Skipped.Add("$table.$Column"); Write-Host "  skipped  $Column (exists, choice with $($Labels.Count) options)" -ForegroundColor Yellow
        return
    }
    $options = @(); $v = 100000000
    foreach ($l in $Labels) { $options += @{ Value = $v; Label = New-Label $l }; $v++ }
    $def = @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'
        SchemaName     = $Schema
        DisplayName    = New-Label $Display
        Description    = New-Label $Description
        RequiredLevel  = @{ Value = $RequiredLevel }
        OptionSet      = @{
            '@odata.type'  = 'Microsoft.Dynamics.CRM.OptionSetMetadata'
            IsGlobal       = $false
            OptionSetType  = 'Picklist'
            Options        = $options
        }
    }
    Add-Column -Column $Column -Definition $def -ExpectedType 'Picklist'
}

# Creates a table with its primary name column (pattern: Deploy-ChartDefinitionEntity.ps1).
function Add-Table {
    param([string]$Table, [string]$Display, [string]$Plural, [string]$Description, [string]$PrimaryColumn, [string]$PrimaryDisplay, [string]$PrimaryDescription, [int]$PrimaryLength, [string]$Ownership = 'OrganizationOwned')
    $script:Entity = $Table
    $script:Expect.Add(@{ Table = $Table; Column = $PrimaryColumn; Type = 'String' })
    $existing = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName,OwnershipType,PrimaryNameAttribute"
    if ($existing) {
        if ($existing.PrimaryNameAttribute -ne $PrimaryColumn) {
            throw [EscalationException]::new("$Table already exists with primary name '$($existing.PrimaryNameAttribute)', this task needs '$PrimaryColumn'. Owner decision needed.")
        }
        $script:Skipped.Add("table $Table"); Write-Host "  skipped  table $Table (exists, primary name $PrimaryColumn)" -ForegroundColor Yellow
        return
    }
    if ($DryRun) { $script:Planned.Add($Table); $script:Created.Add("table $Table"); Write-Host "  WOULD CREATE table $Table" -ForegroundColor Cyan; return }
    $def = @{
        '@odata.type'          = 'Microsoft.Dynamics.CRM.EntityMetadata'
        SchemaName             = $Table
        DisplayName            = New-Label $Display
        DisplayCollectionName  = New-Label $Plural
        Description            = New-Label $Description
        OwnershipType          = $Ownership
        IsActivity             = $false
        HasNotes               = $false
        HasActivities          = $false
        PrimaryNameAttribute   = $PrimaryColumn
        Attributes             = @(
            @{
                '@odata.type'  = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'
                SchemaName     = $PrimaryColumn
                RequiredLevel  = @{ Value = 'ApplicationRequired' }
                MaxLength      = $PrimaryLength
                DisplayName    = New-Label $PrimaryDisplay
                Description    = New-Label $PrimaryDescription
                IsPrimaryName  = $true
            }
        )
    }
    try {
        Invoke-Dv -Path 'EntityDefinitions' -Method POST -Body $def -ExtraHeaders $SolutionHeader | Out-Null
    } catch {
        if ($_.Exception.Message -notmatch '(?i)already exists|0x80047013|0x80048403|0x80040216') { throw }
        # A stale read hid a table that is already there: the refusal is authoritative; confirm on a settled read.
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 12)) { throw }
        $script:Skipped.Add("table $Table"); Write-Host "  skipped  table $Table (exists, found via create refusal)" -ForegroundColor Yellow
        return
    }
    $script:JustCreated.Add($Table)
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 20)) {
        throw "$Table was created but is not visible in metadata after 60 s."
    }
    $script:Created.Add("table $Table"); Write-Host "  CREATED  table $Table ($Ownership, primary name $PrimaryColumn)" -ForegroundColor Green
}

# The create body's MaxLength for the primary name column is ignored by Dataverse (it comes back as 850), and an
# alternate key on (lookup, string) needs the string kept short (index limit 1700 bytes). Shorten the primary name to the
# wanted length - only while the table has no rows, so no data can be cut. A table that already holds rows is left alone
# and reported.
function Set-PrimaryNameLength([string]$Table, [string]$Column, [int]$Wanted) {
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.StringAttributeMetadata"
    $a = Get-DvOrNull $path -Retries 4
    if (-not $a) { if ($DryRun) { return }; throw "$Table.$Column was not found (string)." }
    if ([int]$a.MaxLength -eq $Wanted) { $script:Skipped.Add("$Table.$Column length $Wanted"); Write-Host "  skipped  $Column max length already $Wanted" -ForegroundColor Yellow; return }
    $set = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName" -Retries 2
    $rows = (Invoke-Dv "$($set.EntitySetName)?`$select=$($Table)id&`$top=1").value
    if (@($rows).Count -gt 0) { Write-Host "  WARNING: $Table has rows and $Column max length is $($a.MaxLength) (wanted $Wanted); not shortened" -ForegroundColor DarkYellow; return }
    if ($DryRun) { $script:Changed.Add("$Table.$Column length $Wanted"); Write-Host "  WOULD SET $Column max length $($a.MaxLength) -> $Wanted" -ForegroundColor Cyan; return }
    $was = [int]$a.MaxLength
    $a.MaxLength = $Wanted
    Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
    for ($i = 0; $i -lt 12; $i++) {
        $chk = Get-DvOrNull $path
        if ($chk -and [int]$chk.MaxLength -eq $Wanted) { break }
        Start-Sleep -Seconds 5
    }
    if ([int]$chk.MaxLength -ne $Wanted) { throw "$Table.$Column max length is still $($chk.MaxLength) after the update." }
    $script:Changed.Add("$Table.$Column length $Wanted"); Write-Host "  CHANGED  $Column max length $was -> $Wanted" -ForegroundColor Green
}

# ---------------------------------------------------------------------------------------------------------------
# Task 019 helpers
# ---------------------------------------------------------------------------------------------------------------

function New-DateOnlyDef([string]$Schema, [string]$Display, [string]$Description) {
    return @{
        '@odata.type'    = 'Microsoft.Dynamics.CRM.DateTimeAttributeMetadata'
        SchemaName       = $Schema
        DisplayName      = New-Label $Display
        Description      = New-Label $Description
        RequiredLevel    = @{ Value = 'None' }
        Format           = 'DateOnly'
        DateTimeBehavior = @{ Value = 'DateOnly' }
    }
}

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
        if ($_.Exception.Message -notmatch '(?i)already exists|duplicate|0x80047013|0x80048403|0x8004f009') { throw }
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
    $table = $script:Entity
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
            if ($_.Exception.Message -notmatch '(?i)already exists|duplicate|0x80047013|0x80048403|0x8004f009') { throw }
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
    $script:Entity = $PolicyTable
    Add-GlobalChoiceColumn -Column 'sprk_domain' -Schema 'sprk_Domain' -Display 'Domain' `
        -Description 'Rules system the policy belongs to. Empty reads as Ontology; billing policies are Billing (D-26).' -OptionSetName $DomainOptionSet
    Add-Lookup -Column 'sprk_ruleset' -SchemaName 'sprk_RuleSet' -RelSchema 'sprk_ruleset_policy_ruleset' -Target $RuleSetTable `
        -Display 'Rule Set' -Description 'The rule set this billing policy belongs to. Stable across versions.'

    # --- 4. sprk_policyversion ----------------------------------------------------------------------------------
    Write-Host "4. $VersionTable (additive)" -ForegroundColor Cyan
    $script:Entity = $VersionTable
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
