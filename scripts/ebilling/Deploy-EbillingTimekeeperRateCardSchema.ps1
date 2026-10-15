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
      3  sprk_ratecardrate (user-owned): sprk_name, sprk_ratecard (-> sprk_ratecard, parental cascade delete),
         sprk_timekeeper (-> sprk_timekeeper, optional), sprk_approvedrate and sprk_rackrate (money),
         sprk_effectivefrom / sprk_effectiveto (date only).
      4  Classification (OPT-IN, held by default): sprk_classification on sprk_timekeeper and sprk_ratecardrate, using the
         EXISTING global choice sprk_timekeeperrole. Task 017 escalation: more than two LEDES classifications have no
         sprk_timekeeperrole equivalent, so the owner chooses reuse vs a new choice first. Until then the script skips
         item 4 and says so. After the owner chooses "reuse", re-run with -ApplyClassification.

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

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.PARAMETER ApplyClassification
    Create sprk_classification (global choice sprk_timekeeperrole) on sprk_timekeeper and sprk_ratecardrate. Use only
    after the owner confirmed "reuse sprk_timekeeperrole" (see notes/017-timekeeper-ratecard-schema.md).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1
    pwsh scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1 -ApplyClassification

.NOTES
    Project: ebilling-r1, task 017. Dev only in R1. Patterns: scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1
    (task 018), Deploy-EbillingInvoiceHeaderSchema.ps1 (task 012) for statuscode, Deploy-EbillingInvoiceLineSchema.ps1
    (task 010). Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (destructive or conflicting).
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$ApplyClassification
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$ApiBase = "$EnvironmentUrl/api/data/v9.2"
$TkTable = 'sprk_timekeeper'
$CardTable = 'sprk_ratecard'
$RateTable = 'sprk_ratecardrate'
$GlobalChoice = 'sprk_timekeeperrole'

# The table the column helpers currently write to.
$script:Entity = $TkTable
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
# Web API plumbing (same behaviour as the task 010 / 012 / 018 scripts)
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

# A call issued while a table created by THIS run is still propagating can be refused as "entity not found"
# (0x80041102, MetadataCache): retry it (only when this run created a table).
function Invoke-WhilePropagating([scriptblock]$Action) {
    for ($try = 0; $true; $try++) {
        try { return & $Action }
        catch {
            $msg = $_.Exception.Message
            if ($script:JustCreated.Count -gt 0 -and $try -lt 12 -and $msg -match '(?i)could not find|does not exist|0x80060888|0x80040217|0x80041102|MetadataCache|HTTP 404') {
                Write-Host "    new table still propagating in the metadata cache, retry $($try + 1)/12 in 8s" -ForegroundColor DarkYellow
                Start-Sleep -Seconds 8
                continue
            }
            throw
        }
    }
}

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
# after the type check on a settled read.
function Invoke-DvCreate {
    param([string]$Path, $Body, [string]$Column, [string]$ExpectedType)
    $table = $script:Entity
    try {
        Invoke-WhilePropagating { Invoke-Dv -Path $Path -Method POST -Body $Body -ExtraHeaders $SolutionHeader | Out-Null } | Out-Null
        return 'created'
    } catch {
        if ($_.Exception.Message -match '(?i)already exists|0x80047013|0x80048403') {
            for ($i = 0; $i -lt 12; $i++) {
                $attr = Get-Attr $table $Column
                if ($attr) { Assert-AttrType $attr $Column $ExpectedType; break }
                Start-Sleep -Seconds 5
            }
            return 'exists'
        }
        throw
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

# Lookup via a one-to-many relationship. -Parental makes the relationship parental (cascade delete, assign, share,
# unshare, reparent) - used for sprk_ratecardrate -> sprk_ratecard only.
function Add-Lookup {
    param(
        [string]$Column, [string]$SchemaName, [string]$RelSchema, [string]$Target, [string]$Display, [string]$Description,
        [string]$DeleteBehavior = 'RemoveLink', [string]$RequiredLevel = 'None', [switch]$Parental
    )
    $table = $script:Entity
    $script:Expect.Add(@{ Table = $table; Column = $Column; Type = 'Lookup'; Target = $Target })
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
    if ($DryRun) { $script:Created.Add("$table.$Column"); Write-Host "  WOULD CREATE $Column (Lookup -> $Target$(if ($Parental) { ', parental' }))" -ForegroundColor Cyan; return }
    $cascade = if ($Parental) {
        @{ Assign = 'Cascade'; Delete = 'Cascade'; Merge = 'NoCascade'; Reparent = 'Cascade'; Share = 'Cascade'; Unshare = 'Cascade' }
    } else {
        @{ Assign = 'NoCascade'; Delete = $DeleteBehavior; Merge = 'NoCascade'; Reparent = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade' }
    }
    $rel = @{
        '@odata.type'        = 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata'
        SchemaName           = $RelSchema
        ReferencedEntity     = $Target
        ReferencingEntity    = $table
        CascadeConfiguration = $cascade
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
    $script:Created.Add("$table.$Column"); Write-Host "  CREATED  $Column (Lookup -> $Target$(if ($Parental) { ', parental' }))" -ForegroundColor Green
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

function New-MoneyDef([string]$Schema, [string]$Display, [string]$Description) {
    # Same shape as the task 012 totals: precision 2 from the attribute, 0 .. 1,000,000,000.
    return @{
        '@odata.type'   = 'Microsoft.Dynamics.CRM.MoneyAttributeMetadata'
        SchemaName      = $Schema
        DisplayName     = New-Label $Display
        Description     = New-Label $Description
        RequiredLevel   = @{ Value = 'None' }
        PrecisionSource = 0
        Precision       = 2
        MinValue        = 0
        MaxValue        = 1000000000
    }
}

function New-DateOnlyDef([string]$Schema, [string]$Display, [string]$Description) {
    # Same shape as sprk_invoicedate / the task 012 billing period.
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

# Picklist column bound to an EXISTING global choice (never edits the choice).
function Add-GlobalChoiceColumn {
    param([string]$Column, [string]$Schema, [string]$Display, [string]$Description, [string]$ChoiceName)
    $table = $script:Entity
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

# Creates a USER-owned table with its primary name column (pattern: Deploy-ChartDefinitionEntity.ps1, 018 Add-Table).
function Add-Table {
    param([string]$Table, [string]$Display, [string]$Plural, [string]$Description, [string]$PrimaryColumn, [string]$PrimaryDisplay, [string]$PrimaryDescription, [int]$PrimaryLength)
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
    if ($DryRun) { $script:Planned.Add($Table); $script:Created.Add("table $Table"); Write-Host "  WOULD CREATE table $Table (user-owned)" -ForegroundColor Cyan; return }
    $def = @{
        '@odata.type'          = 'Microsoft.Dynamics.CRM.EntityMetadata'
        SchemaName             = $Table
        DisplayName            = New-Label $Display
        DisplayCollectionName  = New-Label $Plural
        Description            = New-Label $Description
        OwnershipType          = 'UserOwned'
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
    $script:Created.Add("table $Table"); Write-Host "  CREATED  table $Table (user-owned, primary name $PrimaryColumn)" -ForegroundColor Green
}

# The create body's MaxLength for the primary name column is ignored by Dataverse (it comes back as 850). Shorten the
# primary name to the wanted length - only while the table has no rows, so no data can be cut.
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
    Invoke-WhilePropagating { Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    for ($i = 0; $i -lt 12; $i++) {
        $chk = Get-DvOrNull $path
        if ($chk -and [int]$chk.MaxLength -eq $Wanted) { break }
        Start-Sleep -Seconds 5
    }
    if ([int]$chk.MaxLength -ne $Wanted) { throw "$Table.$Column max length is still $($chk.MaxLength) after the update." }
    $script:Changed.Add("$Table.$Column length $Wanted"); Write-Host "  CHANGED  $Column max length $was -> $Wanted" -ForegroundColor Green
}

# Like MaxLength, the primary name's RequiredLevel in the entity-create body is ignored (it comes back as None). Set it
# to ApplicationRequired afterwards (a form / API write without a name is refused; existing rows are unaffected).
function Set-PrimaryNameRequired([string]$Table, [string]$Column) {
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.StringAttributeMetadata"
    $a = Get-DvOrNull $path -Retries 4
    if (-not $a) { if ($DryRun) { return }; throw "$Table.$Column was not found (string)." }
    if ($a.RequiredLevel.Value -eq 'ApplicationRequired') { $script:Skipped.Add("$Table.$Column required"); Write-Host "  skipped  $Column already application-required" -ForegroundColor Yellow; return }
    if ($DryRun) { $script:Changed.Add("$Table.$Column required"); Write-Host "  WOULD SET $Column required level $($a.RequiredLevel.Value) -> ApplicationRequired" -ForegroundColor Cyan; return }
    $was = $a.RequiredLevel.Value
    $a.RequiredLevel.Value = 'ApplicationRequired'
    Invoke-WhilePropagating { Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    for ($i = 0; $i -lt 12; $i++) {
        $chk = Get-DvOrNull $path
        if ($chk -and $chk.RequiredLevel.Value -eq 'ApplicationRequired') { break }
        Start-Sleep -Seconds 5
    }
    if ($chk.RequiredLevel.Value -ne 'ApplicationRequired') { throw "$Table.$Column required level is still $($chk.RequiredLevel.Value) after the update." }
    $script:Changed.Add("$Table.$Column required"); Write-Host "  CHANGED  $Column required level $was -> ApplicationRequired" -ForegroundColor Green
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
        if ($_.Exception.Message -notmatch '(?i)already exists|duplicate|0x80047013|0x80048403|0x8004f009') { throw }
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
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })   Classification: $(if ($ApplyClassification) { 'APPLY (reuse sprk_timekeeperrole)' } else { 'HELD (owner decision pending)' })"
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
    $script:Entity = $TkTable
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
    Add-Table -Table $CardTable -Display 'Rate Card' -Plural 'Rate Cards' `
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
    Add-Table -Table $RateTable -Display 'Rate Card Rate' -Plural 'Rate Card Rates' `
        -Description 'One approved rate (and the firm rack rate) for a timekeeper or classification, within an effective period, on a rate card.' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'Label of the rate, composed by the rate import (e.g. timekeeper or classification and period).' -PrimaryLength 200
    Set-PrimaryNameLength $RateTable 'sprk_name' 200
    Set-PrimaryNameRequired $RateTable 'sprk_name'
    Add-Lookup -Column 'sprk_ratecard' -SchemaName 'sprk_RateCard' -RelSchema 'sprk_ratecard_ratecardrate_ratecard' -Target $CardTable `
        -Display 'Rate Card' -Description 'The card this rate belongs to. Deleting the card deletes its rates.' -RequiredLevel 'ApplicationRequired' -Parental
    Add-Lookup -Column 'sprk_timekeeper' -SchemaName 'sprk_Timekeeper' -RelSchema 'sprk_timekeeper_ratecardrate_timekeeper' -Target $TkTable `
        -Display 'Timekeeper' -Description 'Optional: the rate applies to this timekeeper. Empty = the rate is by classification.' -DeleteBehavior 'Restrict'
    Add-Column 'sprk_approvedrate' (New-MoneyDef 'sprk_ApprovedRate' 'Approved Rate' 'The hourly rate the client approved.') 'Money'
    Add-Column 'sprk_rackrate' (New-MoneyDef 'sprk_RackRate' 'Rack Rate' 'The firm standard (undiscounted) hourly rate.') 'Money'
    Add-Column 'sprk_effectivefrom' (New-DateOnlyDef 'sprk_EffectiveFrom' 'Effective From' 'First day the rate applies.') 'DateTime'
    Add-Column 'sprk_effectiveto' (New-DateOnlyDef 'sprk_EffectiveTo' 'Effective To' 'Last day the rate applies. Empty = open ended.') 'DateTime'

    # --- 4. Classification (held until the owner decides reuse vs new choice) ----------------------------------------
    Write-Host "4. Classification (global choice $GlobalChoice)" -ForegroundColor Cyan
    if (-not $ApplyClassification) {
        Write-Host "  HELD - not created: more than two LEDES classifications have no $GlobalChoice equivalent, so the owner chooses reuse vs a new choice first (task 017 escalation; notes/017-timekeeper-ratecard-schema.md). Re-run with -ApplyClassification after the owner confirms 'reuse'." -ForegroundColor Magenta
    } else {
        $script:Entity = $TkTable
        Add-GlobalChoiceColumn -Column 'sprk_classification' -Schema 'sprk_Classification' -Display 'Classification' `
            -Description 'Timekeeper classification (Partner, Associate, Paralegal, ...). Shares the global choice sprk_timekeeperrole with sprk_billingevent.' -ChoiceName $GlobalChoice
        $script:Entity = $RateTable
        Add-GlobalChoiceColumn -Column 'sprk_classification' -Schema 'sprk_Classification' -Display 'Classification' `
            -Description 'Optional: the rate applies to every timekeeper of this classification. Empty = the rate is for the named timekeeper.' -ChoiceName $GlobalChoice
    }

    # --- Publish (only when something changed) ------------------------------------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$TkTable</entity><entity>$CardTable</entity><entity>$RateTable</entity><entity>sprk_organization</entity><entity>sprk_matter</entity><entity>sprk_project</entity><entity>sprk_workassignment</entity><entity>sprk_document</entity></entities></importexportxml>"
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
if (-not $ApplyClassification) { Write-Host "held    : sprk_classification on sprk_timekeeper and sprk_ratecardrate (owner decision pending)" -ForegroundColor Magenta }
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
