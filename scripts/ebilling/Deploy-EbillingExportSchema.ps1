<#
.SYNOPSIS
    eBilling R1 task 023 - creates the AP export batch table sprk_billingexport (FR-13, FR-41, D-21, D-56) and the
    single-valued batch lookup sprk_invoice.sprk_billingexport that keeps an invoice in at most one batch.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every
    item as skipped and makes no write (no publish either).

    Items (tasks/023-billingexport-schema.poml, spec FR-13 / FR-41):
      1  Table sprk_billingexport (user-owned, "Billing Export"), primary name sprk_name (text 100, the batch id), and the
         columns sprk_invoicecount (whole number, min 0), sprk_totalamount (money), sprk_formats (multi-select local choice
         CSV / JSON / XML; XML is reserved, D-21), sprk_exportstatus (local choice Generating / Completed / Failed),
         sprk_mappingsnapshot (multiline 1048576: the field mapping used, snapshotted per batch, FR-41) and
         sprk_failurereason (text 200). Created by / on are the platform columns.
      2  Lookups (delete = RemoveLink, everything else NoCascade): sprk_csvdocument -> sprk_document,
         sprk_jsondocument -> sprk_document, and on sprk_invoice: sprk_billingexport -> sprk_billingexport ("Export Batch").
         The invoice lookup is single-valued, which is what structurally keeps an invoice in one batch.

    Pre-check (before ANY write, project escalation trigger): if sprk_invoice already has a column named
    sprk_billingexport that is not a lookup to sprk_billingexport, or a lookup to another export / batch table, the script
    STOPS (exit 2) rather than adding a second batch link.

    NEVER deletes or renames anything. An existing item that conflicts (a column of another type, a lookup to another
    table, a choice with other options) stops the script with exit code 2 and an ESCALATION message.

    ADR-002: no plugin, business rule, calculated / rollup column or flow. Invoice count and total are written by the
    batch writer (task 150) on the server; they are plain columns here.

    Eventually consistent metadata reads: a 404 right after a create is not trusted (the item is re-read); a create
    refused with "already exists" counts as skipped after a type check on a settled read; a create issued right after a
    table create is retried while the table propagates (MetadataCache); customization / publish locks and transient
    faults are retried with backoff. The primary name's length AND required level in an entity-create body are ignored by
    Dataverse, so both are set afterwards while the table has no rows. ApplicationRequired is not enforced on API writes;
    nothing here is SystemRequired because a null value corrupts no meaning on any of these columns.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingExportSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingExportSchema.ps1

.NOTES
    Project: ebilling-r1, task 023. Dev only in R1. Helpers copied from Deploy-EbillingApprovalSchema.ps1 (021, the most
    hardened version). Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (conflicting existing item or
    an existing second batch link on sprk_invoice).
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
$ExportTable = 'sprk_billingexport'
$FormatLabels = @('CSV', 'JSON', 'XML')
$StatusLabels = @('Generating', 'Completed', 'Failed')

# The table the column helpers currently write to.
$script:Entity = $ExportTable
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
        $msg = $_.Exception.Message
        # 0x80072551 "NavigationPropertyName ... is not unique within an entity" is what a relationship create returns when
        # the relationship is already there (a re-run whose existence read was stale; DEF-008).
        if ($msg -match '(?i)already exists?|0x80047013|0x80048403|0x8009050d|0x80072551') {
            $attr = $null
            for ($i = 0; $i -lt 12 -and -not $attr; $i++) {
                $attr = Get-Attr $table $Column
                if ($attr) { Assert-AttrType $attr $Column $ExpectedType } else { Start-Sleep -Seconds 5 }
            }
            if (-not $attr) { throw "$table.$Column create was refused as a duplicate ($msg) but the column is not readable after 60 s." }
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

function New-DateTimeDef([string]$Schema, [string]$Display, [string]$Description) {
    return @{
        '@odata.type'    = 'Microsoft.Dynamics.CRM.DateTimeAttributeMetadata'
        SchemaName       = $Schema
        DisplayName      = New-Label $Display
        Description      = New-Label $Description
        RequiredLevel    = @{ Value = 'None' }
        Format           = 'DateAndTime'
        DateTimeBehavior = @{ Value = 'UserLocal' }
    }
}

# A LOCAL choice column. If it exists its option labels must be exactly the expected set (never edited by script).
function Add-LocalChoice {
    param([string]$Column, [string]$Schema, [string]$Display, [string]$Description, [string[]]$Labels, [string]$RequiredLevel = 'None', [int]$DefaultValue = -1)
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
        DefaultFormValue = $(if ($DefaultValue -ge 0) { $DefaultValue } else { -1 })
        OptionSet      = @{
            '@odata.type'  = 'Microsoft.Dynamics.CRM.OptionSetMetadata'
            IsGlobal       = $false
            OptionSetType  = 'Picklist'
            Options        = $options
        }
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
        if ($_.Exception.Message -notmatch '(?i)already exists?|0x80047013|0x80048403|0x80040216|0x8009050d') { throw }
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
    # An exact row count; a read right after the table create can come back empty-handed, so it is repeated until a count appears.
    $cnt = $null
    for ($i = 0; $i -lt 12 -and $null -eq $cnt; $i++) {
        $r = Invoke-Dv "$($set.EntitySetName)?`$select=$($Table)id&`$top=1&`$count=true"
        if ($r -and $null -ne $r.'@odata.count') { $cnt = [int]$r.'@odata.count' } else { Start-Sleep -Seconds 5 }
    }
    if ($null -eq $cnt) { Write-Host "  WARNING: could not count rows of $Table; $Column max length left at $($a.MaxLength) (wanted $Wanted)" -ForegroundColor DarkYellow; return }
    if ($cnt -gt 0) { Write-Host "  WARNING: $Table has $cnt row(s) and $Column max length is $($a.MaxLength) (wanted $Wanted); not shortened" -ForegroundColor DarkYellow; return }
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
function Set-PrimaryNameRequired([string]$Table, [string]$Column, [int]$WantedLength) {
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.StringAttributeMetadata"
    $a = Get-DvOrNull $path -Retries 4
    if (-not $a) { if ($DryRun) { return }; throw "$Table.$Column was not found (string)." }
    # The update below PUTs the whole attribute back. A stale read still carrying the old MaxLength (850) would undo the
    # length change just made, so wait until the read shows the wanted length (it stays as is when the table has rows).
    for ($i = 0; $i -lt 12 -and -not $DryRun -and [int]$a.MaxLength -ne $WantedLength; $i++) {
        Start-Sleep -Seconds 5
        $a = Get-DvOrNull $path -Retries 4
    }
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

# ---------------------------------------------------------------------------------------------------------------
# Task 023 helpers
# ---------------------------------------------------------------------------------------------------------------

function New-WholeNumberDef([string]$Schema, [string]$Display, [string]$Description, [int]$Min, [int]$Max) {
    return @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.IntegerAttributeMetadata'
        SchemaName     = $Schema
        DisplayName    = New-Label $Display
        Description    = New-Label $Description
        RequiredLevel  = @{ Value = 'None' }
        Format         = 'None'
        MinValue       = $Min
        MaxValue       = $Max
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

# A LOCAL multi-select choice column (pattern: scripts/Create-ScopeCapabilityFields.ps1). Its AttributeType reads back as
# 'Virtual'. If it exists its option labels must be exactly the expected set (never edited by script).
function Add-LocalMultiChoice {
    param([string]$Column, [string]$Schema, [string]$Display, [string]$Description, [string[]]$Labels)
    $table = $script:Entity
    $typed = "EntityDefinitions(LogicalName='$table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal)"
    $attr = Get-Attr $table $Column
    if ($attr) {
        Assert-AttrType $attr $Column 'Virtual'
        $script:Expect.Add(@{ Table = $table; Column = $Column; Type = 'Virtual' })
        $have = @()
        for ($i = 0; $i -lt 12 -and $have.Count -eq 0; $i++) {
            $pk = Get-DvOrNull $typed
            $have = @($pk.OptionSet.Options | ForEach-Object { $_.Label.UserLocalizedLabel.Label })
            if ($have.Count -eq 0) { Start-Sleep -Seconds 5 }
        }
        if ($have.Count -ne $Labels.Count -or @($have | Where-Object { $_ -notin $Labels }).Count -gt 0) {
            throw [EscalationException]::new("$table.$Column exists with options [$($have -join ', ')] but the spec needs exactly [$($Labels -join ', ')]. Editing or removing options needs an owner decision.")
        }
        $script:Skipped.Add("$table.$Column"); Write-Host "  skipped  $Column (exists, multi-select choice with $($Labels.Count) options)" -ForegroundColor Yellow
        return
    }
    $options = @(); $v = 100000000
    foreach ($l in $Labels) { $options += @{ Value = $v; Label = New-Label $l }; $v++ }
    $def = @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata'
        SchemaName     = $Schema
        DisplayName    = New-Label $Display
        Description    = New-Label $Description
        RequiredLevel  = @{ Value = 'None' }
        OptionSet      = @{
            '@odata.type'  = 'Microsoft.Dynamics.CRM.OptionSetMetadata'
            IsGlobal       = $false
            OptionSetType  = 'Picklist'
            Options        = $options
        }
    }
    Add-Column -Column $Column -Definition $def -ExpectedType 'Virtual'
}

# Escalation pre-check (read-only, before ANY write): sprk_invoice must not already carry a column named sprk_billingexport
# that is anything but a lookup to sprk_billingexport, and must not already carry a lookup to another export / batch table
# (a second batch link would let an invoice sit in two batches).
function Test-InvoiceBatchLink {
    $conflict = [System.Collections.Generic.List[string]]::new()
    $col = Get-DvOrNull "EntityDefinitions(LogicalName='sprk_invoice')/Attributes(LogicalName='sprk_billingexport')?`$select=LogicalName,AttributeType" -Retries 2
    if ($col) {
        $lk = Get-DvOrNull "EntityDefinitions(LogicalName='sprk_invoice')/Attributes(LogicalName='sprk_billingexport')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets" -Retries 2
        if ($col.AttributeType -ne 'Lookup' -or -not $lk -or @($lk.Targets) -notcontains 'sprk_billingexport') {
            $conflict.Add("sprk_invoice.sprk_billingexport already exists (type $($col.AttributeType), targets $(if ($lk) { @($lk.Targets) -join ',' } else { 'n/a' })) but is not a lookup to sprk_billingexport")
        } else {
            Write-Host "  ok       sprk_invoice.sprk_billingexport is already the lookup to sprk_billingexport (a re-run)"
        }
    }
    $rels = Get-DvOrNull "EntityDefinitions(LogicalName='sprk_invoice')/ManyToOneRelationships?`$select=SchemaName,ReferencedEntity,ReferencingAttribute"
    foreach ($r in @($rels.value)) {
        if ($r.ReferencedEntity -match '(?i)export|batch' -and $r.ReferencedEntity -ne 'sprk_billingexport') {
            $conflict.Add("sprk_invoice.$($r.ReferencingAttribute) is already a lookup to $($r.ReferencedEntity)")
        }
        if ($r.ReferencedEntity -eq 'sprk_billingexport' -and $r.ReferencingAttribute -ne 'sprk_billingexport') {
            $conflict.Add("sprk_invoice.$($r.ReferencingAttribute) is already a second lookup to sprk_billingexport")
        }
    }
    if ($conflict.Count -gt 0) {
        throw [EscalationException]::new("sprk_invoice already has a batch link: $($conflict -join '; '). A second batch link is not added. Nothing was changed.")
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 023 - billing export schema" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    foreach ($ref in 'sprk_invoice', 'sprk_document') {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$ref')?`$select=LogicalName" -Retries 4)) {
            throw "$ref does not exist in $EnvironmentUrl."
        }
    }

    # --- 0. Escalation pre-check (before ANY write) -------------------------------------------------------------------
    Write-Host "0. Pre-check: sprk_invoice has no other batch link" -ForegroundColor Cyan
    Test-InvoiceBatchLink

    # --- 1. Table + columns -------------------------------------------------------------------------------------------
    Write-Host "1. Table $ExportTable" -ForegroundColor Cyan
    Add-Table -Table $ExportTable -Display 'Billing Export' -Plural 'Billing Exports' `
        -Description 'One AP export batch: the approved invoices handed to AP in one run, with the files produced and the mapping used (FR-13, FR-41).' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'The batch id.' -PrimaryLength 100
    Set-PrimaryNameLength $ExportTable 'sprk_name' 100
    Set-PrimaryNameRequired $ExportTable 'sprk_name' 100
    Add-Column 'sprk_invoicecount' (New-WholeNumberDef 'sprk_InvoiceCount' 'Invoice Count' 'Number of invoices in the batch. Written by the batch writer (task 150).' 0 1000000) 'Integer'
    Add-Column 'sprk_totalamount' (New-MoneyDef 'sprk_TotalAmount' 'Total Amount' 'Sum of the approved totals of the invoices in the batch. Written by the batch writer (task 150).') 'Money'
    Add-LocalMultiChoice -Column 'sprk_formats' -Schema 'sprk_Formats' -Display 'Formats' `
        -Description 'The file formats this batch produced: CSV, JSON, XML (XML is reserved; D-21).' -Labels $FormatLabels
    Add-LocalChoice -Column 'sprk_exportstatus' -Schema 'sprk_ExportStatus' -Display 'Export Status' `
        -Description 'Where the batch stands: Generating, Completed or Failed.' -Labels $StatusLabels
    Add-Column 'sprk_mappingsnapshot' (New-MemoDef 'sprk_MappingSnapshot' 'Mapping Snapshot' 'The field mapping used to produce the files, snapshotted per batch so a past batch can be reproduced after a mapping change (FR-41).' 1048576) 'Memo'
    Add-Column 'sprk_failurereason' (New-StringDef 'sprk_FailureReason' 'Failure Reason' 'Why the batch failed (status Failed).' 200) 'String'

    # --- 2. Lookups ---------------------------------------------------------------------------------------------------
    Write-Host "2. Lookups" -ForegroundColor Cyan
    Add-Lookup -Column 'sprk_csvdocument' -SchemaName 'sprk_CsvDocument' -RelSchema 'sprk_billingexport_csvdocument_document' -Target 'sprk_document' `
        -Display 'CSV Document' -Description 'The Document holding the CSV file of this batch (E-Billing Output Files container).'
    Add-Lookup -Column 'sprk_jsondocument' -SchemaName 'sprk_JsonDocument' -RelSchema 'sprk_billingexport_jsondocument_document' -Target 'sprk_document' `
        -Display 'JSON Document' -Description 'The Document holding the JSON file of this batch (E-Billing Output Files container).'
    $script:Entity = 'sprk_invoice'
    Add-Lookup -Column 'sprk_billingexport' -SchemaName 'sprk_BillingExport' -RelSchema 'sprk_invoice_billingexport_billingexport' -Target 'sprk_billingexport' `
        -Display 'Export Batch' -Description 'The AP export batch this invoice was sent in. Single-valued, so an invoice is in at most one batch (FR-41).'
    $script:Entity = $ExportTable

    # --- Publish (only when something changed; entity-scoped, never PublishAllXml) ------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$ExportTable</entity><entity>sprk_invoice</entity><entity>sprk_document</entity></entities></importexportxml>"
        } | Out-Null
        Write-Host "  customizations published" -ForegroundColor Green
    }

    # --- Verification (metadata read of everything this script owns) --------------------------------------------------
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
                'Lookup' {
                    $lk = $null
                    for ($i = 0; $i -lt 12; $i++) {
                        $lk = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
                        if ($lk -and @($lk.Targets).Count -gt 0) { break }
                        Start-Sleep -Seconds 5
                    }
                    if (-not $lk -or $lk.Targets -notcontains $e.Target) { $bad.Add("$($e.Table).$($e.Column) does not target $($e.Target)"); continue }
                    Write-Host ("  {0}.{1}: lookup -> {2}" -f $e.Table, $e.Column, (@($lk.Targets) -join ','))
                }
                'Integer' {
                    $in = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.IntegerAttributeMetadata?`$select=MinValue,MaxValue" -Retries 4
                    Write-Host ("  {0}: whole number, min {1}, max {2}" -f $e.Column, $in.MinValue, $in.MaxValue)
                    if (-not $in -or [int]$in.MinValue -ne 0) { $bad.Add("$($e.Table).$($e.Column) min is $($in.MinValue), expected 0") }
                }
                'Money' {
                    $mn = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.MoneyAttributeMetadata?`$select=Precision,MinValue" -Retries 4
                    Write-Host ("  {0}: currency, precision {1}, min {2}" -f $e.Column, $mn.Precision, $mn.MinValue)
                }
                'Memo' {
                    $mm = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.MemoAttributeMetadata?`$select=MaxLength" -Retries 4
                    Write-Host ("  {0}: multiline, max length {1}" -f $e.Column, $mm.MaxLength)
                    if (-not $mm -or [int]$mm.MaxLength -ne 1048576) { $bad.Add("$($e.Table).$($e.Column) max length is $($mm.MaxLength), expected 1048576") }
                }
                'String' {
                    $st = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength,RequiredLevel" -Retries 4
                    Write-Host ("  {0}: text, max length {1}, required {2}" -f $e.Column, $st.MaxLength, $st.RequiredLevel.Value)
                }
                'Picklist' {
                    $pk = $null
                    for ($i = 0; $i -lt 12; $i++) {
                        $pk = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal)"
                        if ($pk -and @($pk.OptionSet.Options).Count -gt 0) { break }
                        Start-Sleep -Seconds 5
                    }
                    $have = @($pk.OptionSet.Options)
                    Write-Host ("  {0}: choice (local, global={1}) {2}" -f $e.Column, $pk.OptionSet.IsGlobal, (($have | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', '))
                    if ($have.Count -ne $StatusLabels.Count -or @($have | Where-Object { $_.Label.UserLocalizedLabel.Label -notin $StatusLabels }).Count -gt 0) { $bad.Add("$($e.Table).$($e.Column) options differ from Generating / Completed / Failed") }
                }
                'Virtual' {
                    $pk = $null
                    for ($i = 0; $i -lt 12; $i++) {
                        $pk = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal)"
                        if ($pk -and @($pk.OptionSet.Options).Count -gt 0) { break }
                        Start-Sleep -Seconds 5
                    }
                    $have = @($pk.OptionSet.Options)
                    Write-Host ("  {0}: multi-select choice (local, global={1}) {2}" -f $e.Column, $pk.OptionSet.IsGlobal, (($have | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', '))
                    if ($have.Count -ne $FormatLabels.Count -or @($have | Where-Object { $_.Label.UserLocalizedLabel.Label -notin $FormatLabels }).Count -gt 0) { $bad.Add("$($e.Table).$($e.Column) options differ from CSV / JSON / XML") }
                }
            }
        }
        $own = Get-DvOrNull "EntityDefinitions(LogicalName='$ExportTable')?`$select=OwnershipType,PrimaryNameAttribute,EntitySetName" -Retries 4
        if (-not $own) { $bad.Add("$ExportTable not readable") }
        else {
            Write-Host ("  {0}: {1}, primary name {2}, entity set {3}" -f $ExportTable, $own.OwnershipType, $own.PrimaryNameAttribute, $own.EntitySetName)
            if ($own.OwnershipType -ne 'UserOwned') { $bad.Add("$ExportTable ownership is $($own.OwnershipType), expected UserOwned") }
        }
        $pn = Get-DvOrNull "EntityDefinitions(LogicalName='$ExportTable')/Attributes(LogicalName='sprk_name')/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength,RequiredLevel" -Retries 4
        Write-Host ("  sprk_name: text, max length {0}, required {1}" -f $pn.MaxLength, $pn.RequiredLevel.Value)
        if (-not $pn -or [int]$pn.MaxLength -ne 100) { $bad.Add("sprk_name max length is $($pn.MaxLength), expected 100") }
        # Lookup cascade behaviour
        foreach ($c in @(@('sprk_billingexport', 'sprk_csvdocument'), @('sprk_billingexport', 'sprk_jsondocument'), @('sprk_invoice', 'sprk_billingexport'))) {
            $rn = $null
            for ($i = 0; $i -lt 12 -and -not $rn; $i++) {
                $rn = (Get-DvOrNull "EntityDefinitions(LogicalName='$($c[0])')/ManyToOneRelationships?`$select=SchemaName,ReferencingAttribute,CascadeConfiguration&`$filter=ReferencingAttribute eq '$($c[1])'").value
                if (-not $rn) { Start-Sleep -Seconds 5 }
            }
            if (-not $rn) { $bad.Add("no many-to-one relationship for $($c[0]).$($c[1])"); continue }
            $cc = @($rn)[0].CascadeConfiguration
            Write-Host ("  {0}.{1}: {2}  delete {3}, assign {4}, share {5}" -f $c[0], $c[1], @($rn)[0].SchemaName, $cc.Delete, $cc.Assign, $cc.Share)
            if ($cc.Delete -ne 'RemoveLink') { $bad.Add("$($c[1]) delete behaviour is $($cc.Delete), expected RemoveLink") }
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
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
