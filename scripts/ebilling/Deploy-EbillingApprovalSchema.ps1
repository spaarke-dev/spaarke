<#
.SYNOPSIS
    eBilling R1 task 021 - creates the invoice approval table sprk_invoiceapproval (FR-11, FR-05, FR-35) with its status,
    amount, Decision Record link, ADR-024 core-record lookups and the queryable store of eligible approvers.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every
    item as skipped and makes no write (no publish either).

    Items (tasks/021-invoiceapproval-schema.poml, spec FR-11 / FR-05 / FR-35):
      1  Table sprk_invoiceapproval (user-owned, "Invoice Approval"), primary name sprk_name (200), and the columns
         sprk_approvalstatus (local choice Pending / Approved / Rejected / Returned, default Pending),
         sprk_approvedamount (money), sprk_eligibleresolvedon and sprk_decidedon (date and time).
      2  Lookups (delete = RemoveLink, everything else NoCascade): sprk_invoice -> sprk_invoice (SystemRequired: the platform refuses a row without it),
         sprk_decisionrecord -> sprk_decisionrecord, sprk_decidedby -> contact, and the ADR-024 entity-specific lookups
         sprk_regardingmatter / sprk_regardingproject / sprk_regardingworkassignment plus sprk_regardingrecordtype ->
         sprk_recordtype_ref; resolver text sprk_regardingrecordid (100), sprk_regardingrecordname (200),
         sprk_regardingrecordnumber (100), sprk_regardingrecordurl (500, Url).
      3  N:N relationship sprk_invoiceapproval_eligibleapprover_contact between sprk_invoiceapproval and contact: the
         eligible approver persons. Task 103 writes it (server side); the "Invoices to review" view (task 029) filters
         through contact.sprk_systemuser for the current user.

    Pre-check (before ANY write, project escalation trigger / D-33): every assignment lookup that holds approvers on
    sprk_matter, sprk_project and sprk_workassignment must target contact, and contact.sprk_systemuser must target
    systemuser. Otherwise the script STOPS (exit 2) before creating the eligible-approver store.

    NEVER deletes or renames anything. An existing item that conflicts (a column of another type, a lookup to another
    table, a choice with other options) stops the script with exit code 2 and an ESCALATION message.

    ADR-002: no plugin, business rule, calculated / rollup column or flow. Eligible approvers are resolved and enforced by
    task 103 on the server; "one populated core lookup per row" is enforced by task 013; the invoice being required is the
    platform's SystemRequired level on sprk_invoice (ApplicationRequired is not enforced on API writes).

    Eventually consistent metadata reads: a 404 right after a create is not trusted (the item is re-read); a create
    refused with "already exists" counts as skipped after a type check on a settled read; a create issued right after a
    table create is retried while the table propagates (MetadataCache); customization / publish locks and transient
    faults are retried with backoff. The primary name's length AND required level in an entity-create body are ignored by
    Dataverse, so both are set afterwards while the table has no rows.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingApprovalSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingApprovalSchema.ps1

.NOTES
    Project: ebilling-r1, task 021. Dev only in R1. Patterns: Deploy-EbillingTimekeeperRateCardSchema.ps1 (017),
    Deploy-EbillingBillingCodeSchema.ps1 (018), Deploy-EbillingSubmissionSchema.ps1 (020, same ADR-024 shape).
    Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (conflicting existing item or D-33 pre-check).
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
$ApprovalTable = 'sprk_invoiceapproval'
$NnSchema = 'sprk_invoiceapproval_eligibleapprover_contact'
$StatusLabels = @('Pending', 'Approved', 'Rejected', 'Returned')
$StatusDefault = 100000000   # Pending

# The table the column helpers currently write to.
$script:Entity = $ApprovalTable
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

# Dataverse does NOT enforce ApplicationRequired on API writes (a Web API create without the column succeeds; only forms
# enforce it). SystemRequired is enforced by the platform on every create / update ("Attribute 'x' cannot be NULL"), and
# the metadata API accepts it on a custom lookup. Used for sprk_invoice so an approval without an invoice is refused for
# every writer (FR-11). Raising the level is additive and applies to an empty table or to rows that already hold a value.
function Set-LookupRequiredLevel([string]$Table, [string]$Column, [string]$Level) {
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.LookupAttributeMetadata"
    $a = Get-DvOrNull $path -Retries 4
    if (-not $a) { if ($DryRun) { return }; throw "$Table.$Column was not found (lookup)." }
    if ($a.RequiredLevel.Value -eq $Level) { $script:Skipped.Add("$Table.$Column required $Level"); Write-Host "  skipped  $Column already $Level" -ForegroundColor Yellow; return }
    if ($DryRun) { $script:Changed.Add("$Table.$Column required $Level"); Write-Host "  WOULD SET $Column required level $($a.RequiredLevel.Value) -> $Level" -ForegroundColor Cyan; return }
    $was = $a.RequiredLevel.Value
    $a.RequiredLevel.Value = $Level
    Invoke-WhilePropagating { Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    for ($i = 0; $i -lt 12; $i++) {
        $chk = Get-DvOrNull $path
        if ($chk -and $chk.RequiredLevel.Value -eq $Level) { break }
        Start-Sleep -Seconds 5
    }
    if ($chk.RequiredLevel.Value -ne $Level) { throw "$Table.$Column required level is still $($chk.RequiredLevel.Value) after the update." }
    $script:Changed.Add("$Table.$Column required $Level"); Write-Host "  CHANGED  $Column required level $was -> $Level" -ForegroundColor Green
}

# ---------------------------------------------------------------------------------------------------------------
# N:N relationship (pattern: scripts/Deploy-PrecedentEntity.ps1 step 6)
# ---------------------------------------------------------------------------------------------------------------

function Get-ManyToMany([string]$Schema) {
    return Get-DvOrNull "RelationshipDefinitions(SchemaName='$Schema')/Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata?`$select=SchemaName,Entity1LogicalName,Entity2LogicalName,IntersectEntityName"
}

function Add-ManyToMany {
    param([string]$Schema, [string]$Entity1, [string]$Entity2, [string]$Label1, [string]$Label2)
    $existing = Get-ManyToMany $Schema
    if ($existing) {
        if ($existing.Entity1LogicalName -ne $Entity1 -or $existing.Entity2LogicalName -ne $Entity2) {
            throw [EscalationException]::new("N:N $Schema exists between '$($existing.Entity1LogicalName)' and '$($existing.Entity2LogicalName)' but this task needs '$Entity1' and '$Entity2'. Owner decision needed.")
        }
        $script:Skipped.Add("N:N $Schema"); Write-Host "  skipped  N:N $Schema (exists)" -ForegroundColor Yellow
        return
    }
    if ($DryRun) { $script:Created.Add("N:N $Schema"); Write-Host "  WOULD CREATE N:N $Schema ($Entity1 <-> $Entity2)" -ForegroundColor Cyan; return }
    $nn = @{
        '@odata.type'                      = 'Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata'
        SchemaName                         = $Schema
        Entity1LogicalName                 = $Entity1
        Entity2LogicalName                 = $Entity2
        IntersectEntityName                = $Schema
        Entity1AssociatedMenuConfiguration = @{ Behavior = 'UseLabel'; Group = 'Details'; Order = 10000; Label = (New-Label $Label1) }
        Entity2AssociatedMenuConfiguration = @{ Behavior = 'UseLabel'; Group = 'Details'; Order = 10000; Label = (New-Label $Label2) }
    }
    try {
        Invoke-WhilePropagating { Invoke-Dv -Path 'RelationshipDefinitions' -Method POST -Body $nn -ExtraHeaders $SolutionHeader | Out-Null } | Out-Null
    } catch {
        if ($_.Exception.Message -notmatch '(?i)already exists?|0x80047013|0x80048403|0x8009050d') { throw }
        for ($i = 0; $i -lt 48 -and -not (Get-ManyToMany $Schema); $i++) { Start-Sleep -Seconds 5 }
        if (-not (Get-ManyToMany $Schema)) { throw }
        $script:Skipped.Add("N:N $Schema"); Write-Host "  skipped  N:N $Schema (exists, found via create refusal)" -ForegroundColor Yellow
        return
    }
    for ($i = 0; $i -lt 48 -and -not (Get-ManyToMany $Schema); $i++) { Start-Sleep -Seconds 5 }
    if (-not (Get-ManyToMany $Schema)) { throw "N:N $Schema was created but is not visible in metadata after 240 s." }
    $script:Created.Add("N:N $Schema"); Write-Host "  CREATED  N:N $Schema ($Entity1 <-> $Entity2)" -ForegroundColor Green
}

# D-33 pre-check: every assignment lookup that can hold approvers must target contact (the persons the N:N stores), and
# contact.sprk_systemuser must link a person to a user. Read-only; throws EscalationException before any write.
function Test-ApproverFieldTarget {
    $common = 'sprk_assignedattorney1', 'sprk_assignedattorney2', 'sprk_assignedparalegal1', 'sprk_assignedparalegal2', 'sprk_assignedtointernal', 'sprk_assignedtoexternal'
    $fields = [ordered]@{
        'sprk_matter'         = $common
        'sprk_project'        = $common
        'sprk_workassignment' = $common + 'sprk_assignedto'
    }
    $bad = [System.Collections.Generic.List[string]]::new()
    foreach ($t in $fields.Keys) {
        foreach ($f in $fields[$t]) {
            $lk = Get-DvOrNull "EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='$f')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=LogicalName,Targets" -Retries 2
            if (-not $lk) { Write-Host "  note     $t.$f does not exist (not every table carries every assignment field)" -ForegroundColor DarkYellow; continue }
            if (@($lk.Targets).Count -ne 1 -or $lk.Targets[0] -ne 'contact') { $bad.Add("$t.$f -> $($lk.Targets -join ',')"); continue }
            Write-Host "  ok       $t.$f -> contact"
        }
    }
    $su = Get-DvOrNull "EntityDefinitions(LogicalName='contact')/Attributes(LogicalName='sprk_systemuser')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=LogicalName,Targets" -Retries 2
    if (-not $su -or $su.Targets -notcontains 'systemuser') { $bad.Add('contact.sprk_systemuser does not target systemuser (or is missing)') } else { Write-Host "  ok       contact.sprk_systemuser -> systemuser" }
    if ($bad.Count -gt 0) {
        throw [EscalationException]::new("D-33 assignment fields do not all target contact: $($bad -join '; '). The eligible-approver store (N:N to contact) depends on it. Nothing was changed.")
    }
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 021 - invoice approval schema" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    foreach ($ref in 'sprk_invoice', 'sprk_decisionrecord', 'contact', 'sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_recordtype_ref') {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$ref')?`$select=LogicalName" -Retries 4)) {
            throw "$ref does not exist in $EnvironmentUrl."
        }
    }

    # --- 0. D-33 pre-check (before ANY write) -------------------------------------------------------------------------
    Write-Host "0. Pre-check: approver assignment fields target contact (D-33)" -ForegroundColor Cyan
    Test-ApproverFieldTarget

    # --- 1. Table + columns -------------------------------------------------------------------------------------------
    Write-Host "1. Table $ApprovalTable" -ForegroundColor Cyan
    Add-Table -Table $ApprovalTable -Display 'Invoice Approval' -Plural 'Invoice Approvals' `
        -Description 'The approval portion of an invoice: one row per invoice and core record (one portion per invoice in R1), with its decision status and the eligible approvers (FR-11).' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'Label of the approval, composed by the server (invoice number and core record).' -PrimaryLength 200
    Set-PrimaryNameLength $ApprovalTable 'sprk_name' 200
    Set-PrimaryNameRequired $ApprovalTable 'sprk_name'
    Add-LocalChoice -Column 'sprk_approvalstatus' -Schema 'sprk_ApprovalStatus' -Display 'Approval Status' `
        -Description 'Where the approval stands: Pending (awaiting a decision), Approved, Rejected or Returned (FR-11). Default Pending.' `
        -Labels $StatusLabels -DefaultValue $StatusDefault
    Add-Column 'sprk_approvedamount' (New-MoneyDef 'sprk_ApprovedAmount' 'Approved Amount' 'The approved total for this portion (frozen when approved; FR-35).') 'Money'
    Add-Column 'sprk_eligibleresolvedon' (New-DateTimeDef 'sprk_EligibleResolvedOn' 'Eligible Approvers Resolved On' 'When the eligible approvers were last resolved (at acceptance, then on each re-resolution; D-78).') 'DateTime'
    Add-Column 'sprk_decidedon' (New-DateTimeDef 'sprk_DecidedOn' 'Decided On' 'When the decision was made.') 'DateTime'

    # --- 2. Lookups ---------------------------------------------------------------------------------------------------
    Write-Host "2. Lookups" -ForegroundColor Cyan
    $RegardingNote = 'At most one of the three is populated per row (enforced server-side, task 013).'
    Add-Lookup -Column 'sprk_invoice' -SchemaName 'sprk_Invoice' -RelSchema 'sprk_invoiceapproval_invoice_invoice' -Target 'sprk_invoice' `
        -Display 'Invoice' -Description 'The invoice this approval is for. Required.' -RequiredLevel 'ApplicationRequired'
    Set-LookupRequiredLevel $ApprovalTable 'sprk_invoice' 'SystemRequired'
    Add-Lookup -Column 'sprk_decisionrecord' -SchemaName 'sprk_DecisionRecord' -RelSchema 'sprk_invoiceapproval_decisionrecord_decisionrecord' -Target 'sprk_decisionrecord' `
        -Display 'Decision Record' -Description 'The Decision Record written when the approval was decided.'
    Add-Lookup -Column 'sprk_decidedby' -SchemaName 'sprk_DecidedBy' -RelSchema 'sprk_invoiceapproval_decidedby_contact' -Target 'contact' `
        -Display 'Decided By' -Description 'The approver (a person) who decided, so a worklist can show it without reading the Decision Record.'
    Add-Lookup -Column 'sprk_regardingmatter' -SchemaName 'sprk_RegardingMatter' -RelSchema 'sprk_invoiceapproval_regmatter' -Target 'sprk_matter' `
        -Display 'Regarding Matter' -Description "ADR-024 entity-specific lookup. $RegardingNote"
    Add-Lookup -Column 'sprk_regardingproject' -SchemaName 'sprk_RegardingProject' -RelSchema 'sprk_invoiceapproval_regproject' -Target 'sprk_project' `
        -Display 'Regarding Project' -Description "ADR-024 entity-specific lookup. $RegardingNote"
    Add-Lookup -Column 'sprk_regardingworkassignment' -SchemaName 'sprk_RegardingWorkAssignment' -RelSchema 'sprk_invoiceapproval_regworkassignment' -Target 'sprk_workassignment' `
        -Display 'Regarding Work Assignment' -Description "ADR-024 entity-specific lookup. $RegardingNote"
    Add-Lookup -Column 'sprk_regardingrecordtype' -SchemaName 'sprk_RegardingRecordType' -RelSchema 'sprk_invoiceapproval_regrecordtype_recordtype_ref' -Target 'sprk_recordtype_ref' `
        -Display 'Regarding Record Type' -Description 'ADR-024 resolver field: the record type of the core record the approval is regarding.'
    Add-Column 'sprk_regardingrecordid' (New-StringDef 'sprk_RegardingRecordId' 'Regarding Record Id' 'ADR-024 resolver field: id of the core record the approval is regarding.' 100) 'String'
    Add-Column 'sprk_regardingrecordname' (New-StringDef 'sprk_RegardingRecordName' 'Regarding Record Name' 'ADR-024 resolver field: name of that record.' 200) 'String'
    Add-Column 'sprk_regardingrecordnumber' (New-StringDef 'sprk_RegardingRecordNumber' 'Regarding Record Number' 'ADR-024 resolver field: number of that record.' 100) 'String'
    Add-Column 'sprk_regardingrecordurl' (New-StringDef 'sprk_RegardingRecordUrl' 'Regarding Record URL' 'ADR-024 resolver field: URL of that record.' 500 'Url') 'String'

    # --- 3. Eligible approvers (N:N) ----------------------------------------------------------------------------------
    Write-Host "3. Eligible approvers (N:N to contact)" -ForegroundColor Cyan
    Add-ManyToMany -Schema $NnSchema -Entity1 $ApprovalTable -Entity2 'contact' -Label1 'Eligible Approvers' -Label2 'Invoice Approvals (eligible approver)'

    # --- Publish (only when something changed; entity-scoped, never PublishAllXml) ------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$ApprovalTable</entity><entity>contact</entity><entity>sprk_invoice</entity><entity>sprk_decisionrecord</entity><entity>sprk_matter</entity><entity>sprk_project</entity><entity>sprk_workassignment</entity><entity>sprk_recordtype_ref</entity></entities></importexportxml>"
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
            if ($e.Type -eq 'Lookup') {
                $lk = Get-DvOrNull "EntityDefinitions(LogicalName='$($e.Table)')/Attributes(LogicalName='$($e.Column)')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
                if (-not $lk -or $lk.Targets -notcontains $e.Target) { $bad.Add("$($e.Table).$($e.Column) does not target $($e.Target)") }
            }
        }
        $own = Get-DvOrNull "EntityDefinitions(LogicalName='$ApprovalTable')?`$select=OwnershipType,PrimaryNameAttribute,EntitySetName" -Retries 4
        if (-not $own) { $bad.Add("$ApprovalTable not readable") }
        else {
            Write-Host ("  {0}: {1}, primary name {2}, entity set {3}" -f $ApprovalTable, $own.OwnershipType, $own.PrimaryNameAttribute, $own.EntitySetName)
            if ($own.OwnershipType -ne 'UserOwned') { $bad.Add("$ApprovalTable ownership is $($own.OwnershipType), expected UserOwned") }
        }
        # sprk_invoice must be SystemRequired (enforced by the platform on every write; ApplicationRequired is not).
        $inv = $null
        for ($i = 0; $i -lt 12; $i++) {
            $inv = Get-DvOrNull "EntityDefinitions(LogicalName='$ApprovalTable')/Attributes(LogicalName='sprk_invoice')?`$select=RequiredLevel"
            if ($inv -and $inv.RequiredLevel.Value -eq 'SystemRequired') { break }
            Start-Sleep -Seconds 5
        }
        Write-Host "  sprk_invoice required level: $($inv.RequiredLevel.Value)"
        if (-not $inv -or $inv.RequiredLevel.Value -ne 'SystemRequired') { $bad.Add('sprk_invoice is not SystemRequired') }
        # Choice values and default.
        $pk = $null; $have = @()
        for ($i = 0; $i -lt 12 -and $have.Count -eq 0; $i++) {
            $pk = Get-DvOrNull "EntityDefinitions(LogicalName='$ApprovalTable')/Attributes(LogicalName='sprk_approvalstatus')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=DefaultFormValue&`$expand=OptionSet(`$select=Options)"
            $have = @($pk.OptionSet.Options)
            if ($have.Count -eq 0) { Start-Sleep -Seconds 5 }
        }
        Write-Host ("  sprk_approvalstatus: " + (($have | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', ') + "   default: $($pk.DefaultFormValue)")
        if (@($have | Where-Object { $_.Label.UserLocalizedLabel.Label -notin $StatusLabels }).Count -gt 0 -or $have.Count -ne $StatusLabels.Count) { $bad.Add('sprk_approvalstatus options differ from Pending / Approved / Rejected / Returned') }
        if ([int]$pk.DefaultFormValue -ne $StatusDefault) { $bad.Add("sprk_approvalstatus default is $($pk.DefaultFormValue), expected $StatusDefault (Pending)") }
        # N:N
        $nn = Get-ManyToMany $NnSchema
        if (-not $nn) { $bad.Add("N:N $NnSchema missing") }
        else { Write-Host ("  N:N {0}: {1} <-> {2}, intersect {3}" -f $nn.SchemaName, $nn.Entity1LogicalName, $nn.Entity2LogicalName, $nn.IntersectEntityName) }
        # Lookup cascade behaviour
        foreach ($c in 'sprk_invoice', 'sprk_decisionrecord', 'sprk_decidedby', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment', 'sprk_regardingrecordtype') {
            $rn = (Get-DvOrNull "EntityDefinitions(LogicalName='$ApprovalTable')/ManyToOneRelationships?`$select=SchemaName,ReferencingAttribute,CascadeConfiguration&`$filter=ReferencingAttribute eq '$c'").value
            if (-not $rn) { $bad.Add("no many-to-one relationship for $c"); continue }
            $cc = @($rn)[0].CascadeConfiguration
            Write-Host ("  {0}: {1}  delete {2}, assign {3}, share {4}" -f $c, @($rn)[0].SchemaName, $cc.Delete, $cc.Assign, $cc.Share)
            if ($cc.Delete -ne 'RemoveLink') { $bad.Add("$c delete behaviour is $($cc.Delete), expected RemoveLink") }
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
