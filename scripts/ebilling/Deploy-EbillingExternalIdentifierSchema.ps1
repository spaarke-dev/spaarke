<#
.SYNOPSIS
    eBilling R1 task 022 - creates the table sprk_externalidentifier (typed identifiers such as the firm invoice number,
    firm client-matter number, law-department matter number and PO as quoted; FR-12) with its ADR-024 regarding fields
    and the owner-decided alternate key D-68: (issuing organization, identifier type, normalized value, core record id).

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every
    item as skipped and makes no write (no publish either). Nothing is ever deleted, retyped or relabelled.

    Items (tasks/022-externalidentifier-schema.poml, spec FR-12 / FR-05, D-12, D-68):
      1  Table sprk_externalidentifier (user-owned, "External Identifier"), primary name sprk_name (200), and the columns
         sprk_identifiertype (local choice, eight values: the four R1 types Firm invoice number, Firm client-matter number,
         Law-department matter number, PO as quoted, then the four reserved AP document number, VAT invoice number,
         E-invoice / clearance ID, Peppol message ID), sprk_identifiervalue (text 200, as quoted), sprk_normalizedvalue
         (text 200, the matching value: upper-case, all whitespace removed; the rule is owned by task 044) and
         sprk_issuingsystem (text 200).
      2  Lookups (delete = RemoveLink, other cascades NoCascade): sprk_issuingorganization -> sprk_organization (the firm),
         sprk_regardinginvoice -> sprk_invoice, and the ADR-024 entity-specific lookups sprk_regardingmatter /
         sprk_regardingproject / sprk_regardingworkassignment plus sprk_regardingrecordtype -> sprk_recordtype_ref;
         resolver text sprk_regardingrecordid (100), sprk_regardingrecordname (200), sprk_regardingrecordnumber (100),
         sprk_regardingrecordurl (500, Url).
      3  Alternate key sprk_externalidentifier_firm_type_value_record on (sprk_issuingorganization, sprk_identifiertype,
         sprk_normalizedvalue, sprk_regardingrecordid) - D-68, decided by the owner 2026-10-15. The script waits for the key
         index to be Active. If Dataverse cannot build a key on these columns the script STOPS (exit 2) and does not
         choose another key.

    Key semantics: a row is covered by the key only when all four key columns hold a value (Dataverse does not index a
    row with an empty key column). The writer (task 044) therefore fills the issuing organization and the resolver id
    whenever it writes a firm-quoted identifier. Lookups by (firm, type, value) are filtered queries; one value on two
    core records is legal under the key and is refused by the resolver in 044.

    sprk_identifiertype and sprk_normalizedvalue are raised to SystemRequired: ApplicationRequired is not enforced on API
    writes (DEF-009), SystemRequired is, and an identifier with no type or no matching value cannot be looked up.

    At most one entity-specific regarding lookup is populated per row, and the resolver fields are filled together: both
    are enforced server-side by task 013, not by schema (ADR-002: no plugin, business rule, calculated / rollup column or
    flow). Forms and views are task 029's.

    If an existing item has the same name but another shape (a column of another type, a lookup to another table, a choice
    with other options, a key on other attributes, a table with another primary name), the script STOPS with exit code 2
    and an ESCALATION message (project CLAUDE.md section 5) and changes nothing further.

    Eventually consistent metadata reads: a 404 right after a create is not trusted (the item is re-read); a create
    refused with "already exists" (or 0x8009050d / 0x80072551) counts as skipped after a type check on a settled read; a
    create issued right after a table create is retried while the table propagates (MetadataCache), as is the key create;
    the primary name's length AND required level in an entity-create body are ignored by Dataverse, so both are set
    afterwards while the table has no rows; customization / publish locks (other schema tasks publish in the same
    environment) and transient faults are retried with backoff. Dataverse returns key attributes alphabetically.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingExternalIdentifierSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingExternalIdentifierSchema.ps1

.NOTES
    Project: ebilling-r1, task 022. Dev only in R1. Patterns: Deploy-EbillingApprovalSchema.ps1 (021, current helpers) and
    Deploy-EbillingSubmissionSchema.ps1 (020, alternate key). Resolver field lengths per
    projects/spaarke-ontology-platform-r1/notes/140-schema-delta.md section 2.
    Exit codes: 0 = done (created / skipped), 1 = error, 2 = escalation (conflicting existing item or key not buildable).
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
$IdTable = 'sprk_externalidentifier'
$KeySchemaName = 'sprk_externalidentifier_firm_type_value_record'
# Closed choice (task 022): the four R1 types first, then the four reserved ones; values sequential from 100000000.
$TypeLabels = @('Firm invoice number', 'Firm client-matter number', 'Law-department matter number', 'PO as quoted',
    'AP document number', 'VAT invoice number', 'E-invoice / clearance ID', 'Peppol message ID')
# D-68 key columns (Dataverse lists them alphabetically; the script compares sorted).
$KeyAttributes = @('sprk_issuingorganization', 'sprk_identifiertype', 'sprk_normalizedvalue', 'sprk_regardingrecordid')

# The table the column helpers currently write to.
$script:Entity = $IdTable
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
# the metadata API accepts it on a custom column. Used for the identifier type and normalized value so a row that could
# never be looked up is refused for every writer (key D-68). Raising the level is additive and applies to an empty table or to rows that already hold a value.
function Set-RequiredLevel([string]$Table, [string]$Column, [string]$Level, [string]$MetaType) {
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.$MetaType"
    $a = Get-DvOrNull $path -Retries 4
    if (-not $a) { if ($DryRun) { return }; throw "$Table.$Column was not found ($MetaType)." }
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
# Alternate key (pattern: 020 Add-AlternateKey). A refusal that is not "exists" or "still propagating" means Dataverse
# cannot build THIS key: that is an escalation (D-68), never a reason to choose another key.
# ---------------------------------------------------------------------------------------------------------------

function Get-Keys([string]$Table) {
    $r = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Keys?`$select=SchemaName,KeyAttributes,EntityKeyIndexStatus,MetadataId" -Retries 2
    if ($r -and $r.value) { return @($r.value) }
    return @()
}

function Add-AlternateKey([string]$Table, [string]$Schema, [string]$Display, [string[]]$Attributes) {
    $keys = Get-Keys $Table
    # Dataverse returns key attributes in alphabetical order, so compare sorted lists.
    $want = (@($Attributes | Sort-Object) -join ',')
    $byName = $keys | Where-Object { $_.SchemaName -eq $Schema } | Select-Object -First 1
    $byAttrs = $keys | Where-Object { (@($_.KeyAttributes | Sort-Object) -join ',') -eq $want } | Select-Object -First 1
    $key = if ($byName) { $byName } else { $byAttrs }
    if ($byName -and (@($byName.KeyAttributes | Sort-Object) -join ',') -ne $want) {
        throw [EscalationException]::new("$Table key $Schema exists on [$(@($byName.KeyAttributes) -join ', ')] but this task needs [$want] (D-68). Owner decision needed.")
    }
    if (-not $key) {
        if ($DryRun) { $script:Created.Add("key $Schema"); Write-Host "  WOULD CREATE alternate key $Schema on ($want)" -ForegroundColor Cyan; return }
        # Columns created seconds ago may not yet be visible to the key create (MetadataCache): retry those.
        for ($try = 0; $true; $try++) {
            try {
                Invoke-Dv -Path "EntityDefinitions(LogicalName='$Table')/Keys" -Method POST -ExtraHeaders $SolutionHeader -Body @{
                    '@odata.type' = 'Microsoft.Dynamics.CRM.EntityKeyMetadata'
                    SchemaName    = $Schema
                    DisplayName   = New-Label $Display
                    KeyAttributes = $Attributes
                } | Out-Null
                $script:Created.Add("key $Schema"); Write-Host "  CREATED  alternate key $Schema on ($want)" -ForegroundColor Green
                break
            } catch {
                $msg = $_.Exception.Message
                if ($msg -match '(?i)already exists?|0x80047013|0x80048403|0x8009050d|duplicate') {
                    $script:Skipped.Add("key $Schema"); Write-Host "  skipped  key $Schema (exists, found via create refusal)" -ForegroundColor Yellow
                    break
                }
                if ($try -lt 12 -and $msg -match '(?i)could not find|does not exist|0x80060888|0x80040217|0x80041102|MetadataCache|HTTP 404') {
                    Write-Host "    metadata still propagating, key create retry $($try + 1)/12 in 8s" -ForegroundColor DarkYellow
                    Start-Sleep -Seconds 8
                    continue
                }
                if ($msg -match 'HTTP 400') {
                    throw [EscalationException]::new("Dataverse refused to create the D-68 key $Schema on ($want): $msg. The owner decided this key; do not choose another one - report to the owner.")
                }
                throw
            }
        }
    } else {
        $script:Skipped.Add("key $Schema"); Write-Host "  skipped  key $($key.SchemaName) (exists on ($want))" -ForegroundColor Yellow
    }
    if ($DryRun) { return }
    # The key index builds asynchronously; the key is usable (upsert by key, uniqueness) only when it is Active.
    $status = ''
    for ($i = 1; $i -le 40; $i++) {
        $k = Get-Keys $Table | Where-Object { (@($_.KeyAttributes | Sort-Object) -join ',') -eq $want } | Select-Object -First 1
        if ($k) {
            $status = $k.EntityKeyIndexStatus
            if ($status -eq 'Active') { Write-Host "  key index Active" -ForegroundColor Green; return }
            if ($status -eq 'Failed') { throw [EscalationException]::new("D-68 key $Schema index FAILED (duplicate values in the data, or a column Dataverse cannot index; check the system job). Do not choose another key - report to the owner.") }
        }
        Write-Host "    waiting for the key index ($i/40, status '$status')..."
        Start-Sleep -Seconds 10
    }
    throw "Alternate key $Schema index is not Active after 400 s (last status '$status'). Re-run the script to keep waiting."
}

# A typed attribute read can come back empty right after a create (eventual consistency): re-read before judging.
function Get-TypedAttr([string]$Path) {
    $r = $null
    for ($i = 0; $i -lt 12 -and -not $r; $i++) {
        $r = Get-DvOrNull $Path
        if (-not $r) { Start-Sleep -Seconds 5 }
    }
    return $r
}

# Relationships this script owns (referencing column -> target), for the final read-back.
$script:ExpectRel = [System.Collections.Generic.List[object]]::new()
function Add-IdLookup([string]$Column, [string]$SchemaName, [string]$RelSchema, [string]$Target, [string]$Display, [string]$Description) {
    $script:ExpectRel.Add(@{ Column = $Column; Target = $Target })
    Add-Lookup -Column $Column -SchemaName $SchemaName -RelSchema $RelSchema -Target $Target -Display $Display -Description $Description
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 022 - sprk_externalidentifier schema" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

$RegardingNote = 'At most one regarding lookup is set per row (enforced server-side by task 013, not by schema).'

try {
    foreach ($ref in 'sprk_organization', 'sprk_invoice', 'sprk_matter', 'sprk_project', 'sprk_workassignment', 'sprk_recordtype_ref') {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$ref')?`$select=LogicalName" -Retries 4)) {
            throw "$ref does not exist in $EnvironmentUrl."
        }
    }

    # --- 1. Table + columns -------------------------------------------------------------------------------------------
    Write-Host "1. Table $IdTable" -ForegroundColor Cyan
    Add-Table -Table $IdTable -Display 'External Identifier' -Plural 'External Identifiers' `
        -Description 'A typed identifier quoted by an issuer for a record: firm invoice number, firm client-matter number, law-department matter number, PO as quoted, and the reserved AP / VAT / e-invoice / Peppol identifiers (FR-12, D-12).' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'Display name of the identifier, normally type and value.' -PrimaryLength 200
    Set-PrimaryNameLength $IdTable 'sprk_name' 200
    Set-PrimaryNameRequired $IdTable 'sprk_name'
    Add-LocalChoice -Column 'sprk_identifiertype' -Schema 'sprk_IdentifierType' -Display 'Identifier Type' `
        -Description 'What kind of identifier this is. R1 uses the first four; AP document number, VAT invoice number, E-invoice / clearance ID and Peppol message ID are reserved for later channels.' `
        -Labels $TypeLabels
    Add-Column 'sprk_identifiervalue' (New-StringDef 'sprk_IdentifierValue' 'Identifier Value' 'The identifier exactly as quoted by the issuer.' 200) 'String'
    Add-Column 'sprk_normalizedvalue' (New-StringDef 'sprk_NormalizedValue' 'Normalized Value' 'The matching value: the identifier upper-cased with all whitespace removed. Part of the alternate key. The rule is applied by task 044.' 200) 'String'
    Add-Column 'sprk_issuingsystem' (New-StringDef 'sprk_IssuingSystem' 'Issuing System' 'The system or ledger that issued the identifier, when known (for example the firm billing system or the AP system).' 200) 'String'

    # --- 2. Lookups + resolver fields ---------------------------------------------------------------------------------
    Write-Host "2. Lookups and resolver fields" -ForegroundColor Cyan
    Add-IdLookup 'sprk_issuingorganization' 'sprk_IssuingOrganization' 'sprk_externalidentifier_issuingorg_organization' 'sprk_organization' 'Issuing Organization' 'The organization (the firm) that issued the identifier. Part of the alternate key.'
    Add-IdLookup 'sprk_regardinginvoice' 'sprk_RegardingInvoice' 'sprk_externalidentifier_reginvoice' 'sprk_invoice' 'Regarding Invoice' "ADR-024 entity-specific lookup: the invoice this identifier identifies. $RegardingNote"
    Add-IdLookup 'sprk_regardingmatter' 'sprk_RegardingMatter' 'sprk_externalidentifier_regmatter' 'sprk_matter' 'Regarding Matter' "ADR-024 entity-specific lookup. $RegardingNote"
    Add-IdLookup 'sprk_regardingproject' 'sprk_RegardingProject' 'sprk_externalidentifier_regproject' 'sprk_project' 'Regarding Project' "ADR-024 entity-specific lookup. $RegardingNote"
    Add-IdLookup 'sprk_regardingworkassignment' 'sprk_RegardingWorkAssignment' 'sprk_externalidentifier_regworkassignment' 'sprk_workassignment' 'Regarding Work Assignment' "ADR-024 entity-specific lookup. $RegardingNote"
    Add-IdLookup 'sprk_regardingrecordtype' 'sprk_RegardingRecordType' 'sprk_externalidentifier_regrecordtype_recordtype_ref' 'sprk_recordtype_ref' 'Regarding Record Type' 'ADR-024 resolver field: the record type of the record the identifier is regarding.'
    Add-Column 'sprk_regardingrecordid' (New-StringDef 'sprk_RegardingRecordId' 'Regarding Record Id' 'ADR-024 resolver field: id of the record the identifier is regarding. Part of the alternate key.' 100) 'String'
    Add-Column 'sprk_regardingrecordname' (New-StringDef 'sprk_RegardingRecordName' 'Regarding Record Name' 'ADR-024 resolver field: name of that record.' 200) 'String'
    Add-Column 'sprk_regardingrecordnumber' (New-StringDef 'sprk_RegardingRecordNumber' 'Regarding Record Number' 'ADR-024 resolver field: number of that record.' 100) 'String'
    Add-Column 'sprk_regardingrecordurl' (New-StringDef 'sprk_RegardingRecordUrl' 'Regarding Record URL' 'ADR-024 resolver field: URL of that record.' 500 'Url') 'String'

    # --- 3. Required levels (SystemRequired is enforced on API writes; ApplicationRequired is not - DEF-009) ----------
    Write-Host "3. Required levels" -ForegroundColor Cyan
    Set-RequiredLevel $IdTable 'sprk_identifiertype' 'SystemRequired' 'PicklistAttributeMetadata'
    Set-RequiredLevel $IdTable 'sprk_normalizedvalue' 'SystemRequired' 'StringAttributeMetadata'

    # --- 4. Alternate key (D-68) --------------------------------------------------------------------------------------
    Write-Host "4. Alternate key D-68 (issuing organization, identifier type, normalized value, regarding record id)" -ForegroundColor Cyan
    Add-AlternateKey -Table $IdTable -Schema $KeySchemaName -Display 'Firm, Type, Value and Record' -Attributes $KeyAttributes

    # --- Publish (only when something changed; entity-scoped, never PublishAllXml) ------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$IdTable</entity><entity>sprk_organization</entity><entity>sprk_invoice</entity><entity>sprk_matter</entity><entity>sprk_project</entity><entity>sprk_workassignment</entity><entity>sprk_recordtype_ref</entity></entities></importexportxml>"
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
                    if (-not $lk -or @($lk.Targets).Count -eq 0) { $bad.Add("$($e.Table).$($e.Column) lookup targets not readable"); continue }
                    if ($lk.Targets -notcontains $e.Target) { $bad.Add("$($e.Table).$($e.Column) does not target $($e.Target)") }
                    Write-Host ("  {0}: lookup -> {1}" -f $e.Column, (@($lk.Targets) -join ','))
                }
                'String' {
                    $st = Get-TypedAttr "$base/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength,FormatName,RequiredLevel"
                    if (-not $st) { $bad.Add("$($e.Table).$($e.Column) string metadata not readable"); continue }
                    Write-Host ("  {0}: text, max length {1}, format {2}, required {3}" -f $e.Column, $st.MaxLength, $st.FormatName.Value, $st.RequiredLevel.Value)
                    if ($e.Column -eq 'sprk_normalizedvalue' -and $st.RequiredLevel.Value -ne 'SystemRequired') { $bad.Add('sprk_normalizedvalue is not SystemRequired') }
                    if ($e.Column -eq 'sprk_name' -and $st.RequiredLevel.Value -ne 'ApplicationRequired') { $bad.Add('sprk_name is not ApplicationRequired') }
                }
                'Picklist' {
                    $pk = $null; $have = @()
                    for ($i = 0; $i -lt 12 -and $have.Count -eq 0; $i++) {
                        $pk = Get-DvOrNull "$base/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=RequiredLevel&`$expand=OptionSet(`$select=Options,IsGlobal)"
                        $have = @($pk.OptionSet.Options)
                        if ($have.Count -eq 0) { Start-Sleep -Seconds 5 }
                    }
                    if ($have.Count -eq 0) { $bad.Add("$($e.Table).$($e.Column) has no readable options"); continue }
                    Write-Host ("  {0}: choice (local, required {1}) {2}" -f $e.Column, $pk.RequiredLevel.Value, (($have | Sort-Object Value | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)" }) -join ', '))
                    if ($pk.OptionSet.IsGlobal) { $bad.Add("$($e.Column) is a global choice, expected local") }
                    $labels = @($have | Sort-Object Value | ForEach-Object { $_.Label.UserLocalizedLabel.Label })
                    if ($labels.Count -ne $TypeLabels.Count -or (($labels -join '|') -ne ($TypeLabels -join '|'))) { $bad.Add('sprk_identifiertype options differ from the eight agreed values') }
                    if ($pk.RequiredLevel.Value -ne 'SystemRequired') { $bad.Add('sprk_identifiertype is not SystemRequired') }
                }
            }
        }
        $own = Get-DvOrNull "EntityDefinitions(LogicalName='$IdTable')?`$select=OwnershipType,PrimaryNameAttribute,EntitySetName" -Retries 4
        if (-not $own) { $bad.Add("$IdTable not readable") }
        else {
            Write-Host ("  {0}: {1}, primary name {2}, entity set {3}" -f $IdTable, $own.OwnershipType, $own.PrimaryNameAttribute, $own.EntitySetName)
            if ($own.OwnershipType -ne 'UserOwned') { $bad.Add("$IdTable ownership is $($own.OwnershipType), expected UserOwned") }
        }
        # Relationships: ManyToOneRelationships of the table, matched by referencing attribute.
        $rels = $null
        for ($i = 0; $i -lt 12; $i++) {
            $rels = Get-DvOrNull "EntityDefinitions(LogicalName='$IdTable')/ManyToOneRelationships?`$select=SchemaName,ReferencingAttribute,ReferencedEntity,CascadeConfiguration"
            $have = @($rels.value | ForEach-Object { $_.ReferencingAttribute })
            if (@($script:ExpectRel | Where-Object { $_.Column -notin $have }).Count -eq 0) { break }
            Start-Sleep -Seconds 5
        }
        foreach ($x in $script:ExpectRel) {
            $r = $rels.value | Where-Object { $_.ReferencingAttribute -eq $x.Column } | Select-Object -First 1
            if (-not $r) { $bad.Add("relationship for $($x.Column) missing"); continue }
            Write-Host ("  rel {0}: {1} -> {2} (delete {3}, assign {4}, share {5})" -f $r.SchemaName, $r.ReferencingAttribute, $r.ReferencedEntity, $r.CascadeConfiguration.Delete, $r.CascadeConfiguration.Assign, $r.CascadeConfiguration.Share)
            if ($r.ReferencedEntity -ne $x.Target) { $bad.Add("$($x.Column) relationship targets $($r.ReferencedEntity), expected $($x.Target)") }
            if ($r.CascadeConfiguration.Delete -ne 'RemoveLink') { $bad.Add("$($x.Column) delete cascade is $($r.CascadeConfiguration.Delete), expected RemoveLink") }
        }
        $want = (@($KeyAttributes | Sort-Object) -join ',')
        $k = Get-Keys $IdTable | Where-Object { $_.SchemaName -eq $KeySchemaName } | Select-Object -First 1
        if (-not $k) { $bad.Add("key $KeySchemaName missing") }
        elseif ((@($k.KeyAttributes | Sort-Object) -join ',') -ne $want) { $bad.Add("key $KeySchemaName is on ($(@($k.KeyAttributes) -join ', ')), expected ($want)") }
        elseif ($k.EntityKeyIndexStatus -ne 'Active') { $bad.Add("key $KeySchemaName is $($k.EntityKeyIndexStatus), expected Active") }
        else { Write-Host ("  key {0} on ({1}): {2}" -f $k.SchemaName, (@($k.KeyAttributes) -join ', '), $k.EntityKeyIndexStatus) }
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
