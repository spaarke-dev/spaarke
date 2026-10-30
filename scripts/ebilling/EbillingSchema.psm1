<#
.SYNOPSIS
    Shared helpers for the eBilling Dataverse schema scripts (scripts/ebilling/Deploy-Ebilling*Schema.ps1).

.DESCRIPTION
    ONE copy of the Web API plumbing and the idempotent "check, then create" helpers, so a hardening fix lands once.
    Before this module every schema script carried its own copy and each later task fixed its own copy only
    (DEF-006 .. DEF-010 in projects/ebilling-r1/notes/defer-issues.md).

    What it provides:
      - Web API plumbing: token (Azure CLI), Invoke-Dv with backoff on customization / publish locks, throttling and
        transient faults, Get-DvOrNull with re-reads (metadata reads are eventually consistent).
      - "Already exists" recognition in ONE place (Test-DuplicateRefusal): 0x80047013, 0x80048403, 0x80040216,
        0x8009050d (duplicate table create, DEF-010) and 0x80072551 (NavigationPropertyName not unique, i.e. the
        relationship is already there, DEF-008). A refused duplicate is confirmed on a settled read before it counts.
      - MetadataCache retries (Invoke-WhilePropagating) for calls issued while a table created by this run propagates.
      - Add-Table / Add-Column / Add-Lookup / Add-LocalChoice / Add-AlternateKey / Add-ManyToMany.
      - Set-PrimaryNameLength (exact row count, no false "has rows"; DEF-010), Set-PrimaryNameRequired (the required
        level in an entity-create body is ignored by Dataverse; DEF-006 / DEF-007), Set-LookupRequiredLevel,
        Set-ColumnRequiredLevel (never lowers a level, refuses to raise to SystemRequired while rows hold a null).
      - Column definition builders: New-StringDef, New-MemoDef, New-MoneyDef, New-DateOnlyDef, New-DateTimeDef.

    REQUIRED LEVELS (DEF-009). Dataverse does NOT enforce ApplicationRequired on API writes (a Web API create without the
    value succeeds; only forms refuse). SystemRequired is enforced on every write ("Attribute 'x' cannot be NULL"). Use
    SystemRequired where a null corrupts meaning (a parental or key column); everything else is "required by the BFF
    write path, not enforced on API writes".

    Usage (top of a script, before any other statement):
        using module .\EbillingSchema.psm1
        $State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun
    The returned state carries the tallies (Created / Changed / Skipped / Deferred), Expect (what the final verification
    must find), JustCreated (tables created by this run), Planned (tables a WhatIf run would create) and SolutionHeader.
    The lists are shared by reference, so a script aliases them (`$script:Created = $State.Created`).

.NOTES
    Project: ebilling-r1. Never deletes or renames anything. Output text of the helpers is unchanged from the per-script
    copies (task 021 / 020 versions are the reference). Dev only in R1.
#>

$ErrorActionPreference = 'Stop'

# An existing item that conflicts (a column of another type, a lookup to another table, a choice with other options).
# Scripts catch it and exit 2 with an ESCALATION message.
class EscalationException : System.Exception {
    EscalationException([string]$message) : base($message) {}
}

# ---------------------------------------------------------------------------------------------------------------
# State
# ---------------------------------------------------------------------------------------------------------------

$script:EnvironmentUrl = $null
$script:ApiBase = $null
$script:SolutionHeader = @{}
$script:DryRun = $false
$script:Entity = $null
$script:Unqualified = $false     # 010 / 012 tally bare column names; the others tally "table.column"
$script:Token = $null
$script:TokenAt = [datetime]::MinValue
$script:Created = [System.Collections.Generic.List[string]]::new()
$script:Changed = [System.Collections.Generic.List[string]]::new()
$script:Skipped = [System.Collections.Generic.List[string]]::new()
$script:Deferred = [System.Collections.Generic.List[string]]::new()
$script:JustCreated = [System.Collections.Generic.List[string]]::new()
$script:Planned = [System.Collections.Generic.List[string]]::new()
$script:Expect = [System.Collections.Generic.List[object]]::new()
# Primary-name lengths this run set ("table.column" -> length): a later read-modify-PUT of the same attribute must not start
# from a stale read that still shows the old length (it would silently restore it; DEF-012).
$script:LengthSet = @{}

function Initialize-EbillingSchema {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$EnvironmentUrl,
        [Parameter(Mandatory)][string]$SolutionUniqueName,
        [switch]$DryRun,
        # The table the column helpers write to until Set-EbEntity changes it.
        [string]$Entity,
        # Tally bare column names (task 010 / 012 output) instead of "table.column".
        [switch]$UnqualifiedTallies
    )
    $script:EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
    $script:ApiBase = "$($script:EnvironmentUrl)/api/data/v9.2"
    $script:SolutionHeader = @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName }
    $script:DryRun = [bool]$DryRun
    $script:Entity = $Entity
    $script:Unqualified = [bool]$UnqualifiedTallies
    $script:Created = [System.Collections.Generic.List[string]]::new()
    $script:Changed = [System.Collections.Generic.List[string]]::new()
    $script:Skipped = [System.Collections.Generic.List[string]]::new()
    $script:Deferred = [System.Collections.Generic.List[string]]::new()
    $script:JustCreated = [System.Collections.Generic.List[string]]::new()
    $script:Planned = [System.Collections.Generic.List[string]]::new()
    $script:Expect = [System.Collections.Generic.List[object]]::new()
    $script:LengthSet = @{}
    return [pscustomobject]@{
        Created        = $script:Created
        Changed        = $script:Changed
        Skipped        = $script:Skipped
        Deferred       = $script:Deferred
        JustCreated    = $script:JustCreated
        Planned        = $script:Planned
        Expect         = $script:Expect
        SolutionHeader = $script:SolutionHeader
        ApiBase        = $script:ApiBase
    }
}

function Get-EbEntity { return $script:Entity }
function Set-EbEntity([string]$Table) { $script:Entity = $Table }

# "table.column" (or just "column" for the 010 / 012 scripts).
function Format-Tally([string]$Table, [string]$Item) {
    if ($script:Unqualified) { return $Item }
    return "$Table.$Item"
}

# ---------------------------------------------------------------------------------------------------------------
# Web API plumbing
# ---------------------------------------------------------------------------------------------------------------

function Get-DataverseToken {
    if ($script:Token -and ((Get-Date) - $script:TokenAt).TotalMinutes -lt 40) { return $script:Token }
    $t = az account get-access-token --resource $script:EnvironmentUrl --query accessToken -o tsv 2>&1
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

# A write that hit a customization / publish lock, throttling or a transient fault is retried with backoff
# (another task's script may be publishing at the same time).
function Test-Transient([int]$status, [string]$text) {
    if ($status -in 429, 500, 502, 503, 504) { return $true }
    return ($text -match '(?i)publish.*(in progress|running)|(in progress).*publish|customization.*(lock|in progress)|(lock|locked).*customiz|another .*import|0x80071151|0x80048d19|SqlException.*(timeout|deadlock)|timeout|timed out|unexpected error occurred')
}

# A create that Dataverse refused because the item is already there. A re-run whose existence read was stale gets one of
# these instead of a clean 404, so the refusal is the authoritative "it exists" (confirm on a settled read).
#   0x80047013 / 0x80048403 / 0x80040216  object already exists
#   0x8009050d                            "Entities already exist" (duplicate table create)            DEF-010
#   0x80072551                            "NavigationPropertyName ... is not unique" (relationship)     DEF-008
# -Loose adds the option-set / key wording: "duplicate" and 0x8004f009.
function Test-DuplicateRefusal([string]$Message, [switch]$Loose) {
    if ($Message -match '(?i)already exists?|0x80047013|0x80048403|0x80040216|0x8009050d|0x80072551') { return $true }
    if ($Loose -and $Message -match '(?i)duplicate|0x8004f009') { return $true }
    return $false
}

function Invoke-Dv {
    param(
        [string]$Path,
        [string]$Method = 'GET',
        $Body = $null,
        [hashtable]$ExtraHeaders = @{},
        [switch]$NoRetry
    )
    $uri = if ($Path -like 'http*') { $Path } else { "$($script:ApiBase)/$Path" }
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

# A call issued while a table created by THIS run is still propagating can be refused as "entity not found"
# (0x80041102, MetadataCache): retry it. Only when this run created a table is "not found / 404" retried (otherwise it is a
# real error); a MetadataCache refusal is retried briefly either way.
function Invoke-WhilePropagating([scriptblock]$Action) {
    for ($try = 0; $true; $try++) {
        try { return & $Action }
        catch {
            $msg = $_.Exception.Message
            $fresh = $script:JustCreated.Count -gt 0
            if ($fresh -and $try -lt 12 -and $msg -match '(?i)could not find|does not exist|0x80060888|0x80040217|0x80041102|MetadataCache|HTTP 404') {
                Write-Host "    new table still propagating in the metadata cache, retry $($try + 1)/12 in 8s" -ForegroundColor DarkYellow
                Start-Sleep -Seconds 8
                continue
            }
            if (-not $fresh -and $try -lt 3 -and $msg -match '(?i)0x80041102|MetadataCache') {
                Write-Host "    metadata cache not ready, retry $($try + 1)/3 in 8s" -ForegroundColor DarkYellow
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
# create refused as a duplicate is therefore the authoritative "it is there": the item is counted as skipped, after the
# type check on a settled read (and an error if the column never becomes readable).
function Invoke-DvCreate {
    param([string]$Path, $Body, [string]$Column, [string]$ExpectedType)
    $table = $script:Entity
    try {
        Invoke-WhilePropagating { Invoke-Dv -Path $Path -Method POST -Body $Body -ExtraHeaders $script:SolutionHeader | Out-Null } | Out-Null
        return 'created'
    } catch {
        $msg = $_.Exception.Message
        if (Test-DuplicateRefusal $msg) {
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
    $table = $script:Entity
    $tally = Format-Tally $table $Column
    $script:Expect.Add(@{ Table = $table; Column = $Column; Type = $ExpectedType })
    $attr = Get-Attr $table $Column
    if ($attr) {
        Assert-AttrType $attr $Column $ExpectedType
        $script:Skipped.Add($tally); Write-Host "  skipped  $Column (exists, $ExpectedType)" -ForegroundColor Yellow
        return
    }
    if ($script:DryRun) { $script:Created.Add($tally); Write-Host "  WOULD CREATE $Column ($ExpectedType)" -ForegroundColor Cyan; return }
    $r = Invoke-DvCreate -Path "EntityDefinitions(LogicalName='$table')/Attributes" -Body $Definition -Column $Column -ExpectedType $ExpectedType
    if ($r -eq 'exists') { $script:Skipped.Add($tally); Write-Host "  skipped  $Column (exists, found via create refusal)" -ForegroundColor Yellow; return }
    Wait-AttrVisible $Column
    $script:Created.Add($tally); Write-Host "  CREATED  $Column ($ExpectedType)" -ForegroundColor Green
}

# Lookup via a one-to-many relationship. -Parental makes the relationship parental (cascade delete, assign, share,
# unshare, reparent). -RelSchema defaults to sprk_{target}_{table}_{column} (the task 010 / 012 naming).
# -Cascade is an optional full CascadeConfiguration (Assign, Delete, Merge, Reparent, Share, Unshare, RollupView) that replaces
# both built-in configurations (task 080: Delete = Cascade with everything else NoCascade, so a reassign or share of the parent
# never cascades onto the child). Scripts that do not pass it are unchanged.
# -RequiredLevel is what the create body asks for; Dataverse honours None and ApplicationRequired there, so a lookup that
# must be SystemRequired is raised afterwards with Set-LookupRequiredLevel.
# -MenuBehavior (UseCollectionName, UseLabel or DoNotDisplay) sets AssociatedMenuConfiguration, the entry the relationship adds to
# the TARGET table's Related menu. Left empty the body carries none (the platform default: the entry is shown), so scripts that do
# not pass it are unchanged. Task 088 passes DoNotDisplay.
function Add-Lookup {
    param(
        [string]$Column, [string]$SchemaName, [string]$RelSchema, [string]$Target, [string]$Display, [string]$Description,
        [string]$DeleteBehavior = 'RemoveLink', [string]$RequiredLevel = 'None', [switch]$Parental, [hashtable]$Cascade = $null,
        [ValidateSet('', 'UseCollectionName', 'UseLabel', 'DoNotDisplay')][string]$MenuBehavior = ''
    )
    $table = $script:Entity
    $tally = Format-Tally $table $Column
    if (-not $RelSchema) { $RelSchema = "sprk_${Target}_${table}_$Column" }
    $script:Expect.Add(@{ Table = $table; Column = $Column; Type = 'Lookup'; Target = $Target })
    $attr = Get-Attr $table $Column
    if ($attr) {
        Assert-AttrType $attr $Column 'Lookup'
        $lk = Get-DvOrNull "EntityDefinitions(LogicalName='$table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
        if ($lk -and $lk.Targets -and ($lk.Targets -notcontains $Target)) {
            throw [EscalationException]::new("$table.$Column already exists as a lookup to '$($lk.Targets -join ',')' but this task needs '$Target'. Owner decision needed.")
        }
        $script:Skipped.Add($tally); Write-Host "  skipped  $Column (exists, Lookup -> $Target)" -ForegroundColor Yellow
        return
    }
    if (-not (Test-TableKnown $Target)) {
        throw "Lookup target table '$Target' for $Column does not exist in $($script:EnvironmentUrl)."
    }
    $note = if ($Cascade) { ', custom cascade: delete ' + $Cascade.Delete + ', assign/share/unshare/reparent ' + $Cascade.Assign + '/' + $Cascade.Share + '/' + $Cascade.Unshare + '/' + $Cascade.Reparent } elseif ($Parental) { ', parental' } else { '' }
    if ($script:DryRun) { $script:Created.Add($tally); Write-Host "  WOULD CREATE $Column (Lookup -> $Target$note)" -ForegroundColor Cyan; return }
    $cascade = if ($Cascade) {
        $Cascade
    } elseif ($Parental) {
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
    if ($MenuBehavior) { $rel.AssociatedMenuConfiguration = @{ Behavior = $MenuBehavior; Group = 'Details'; Order = 10000 } }
    $r = Invoke-DvCreate -Path 'RelationshipDefinitions' -Body $rel -Column $Column -ExpectedType 'Lookup'
    if ($r -eq 'exists') { $script:Skipped.Add($tally); Write-Host "  skipped  $Column (exists, found via create refusal)" -ForegroundColor Yellow; return }
    Wait-AttrVisible $Column
    $script:Created.Add($tally); Write-Host "  CREATED  $Column (Lookup -> $Target$note)" -ForegroundColor Green
}

# ---------------------------------------------------------------------------------------------------------------
# Column definition builders
# ---------------------------------------------------------------------------------------------------------------

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

# Precision 2 from the attribute, 0 .. 1,000,000,000 (same shape as the task 012 totals).
function New-MoneyDef([string]$Schema, [string]$Display, [string]$Description) {
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

# Same shape as sprk_invoicedate.
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

# ---------------------------------------------------------------------------------------------------------------
# Local choice
# ---------------------------------------------------------------------------------------------------------------

# The option labels of a local choice column. The option list of a column created moments ago can read back empty
# (eventual consistency), so it is re-read until it has options (or the retries run out).
function Get-PicklistLabels([string]$Table, [string]$Column, [int]$Retries = 12) {
    $have = @()
    for ($i = 0; $i -lt $Retries -and $have.Count -eq 0; $i++) {
        $pk = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal)"
        $have = @($pk.OptionSet.Options | ForEach-Object { $_.Label.UserLocalizedLabel.Label })
        if ($have.Count -eq 0) { Start-Sleep -Seconds 5 }
    }
    return $have
}

# A LOCAL choice column. If it exists its option labels must be exactly the expected set (never edited by script).
function Add-LocalChoice {
    param([string]$Column, [string]$Schema, [string]$Display, [string]$Description, [string[]]$Labels, [string]$RequiredLevel = 'None', [int]$DefaultValue = -1)
    $table = $script:Entity
    $attr = Get-Attr $table $Column
    if ($attr) {
        Assert-AttrType $attr $Column 'Picklist'
        $script:Expect.Add(@{ Table = $table; Column = $Column; Type = 'Picklist' })
        $have = Get-PicklistLabels $table $Column
        if ($have.Count -ne $Labels.Count -or @($have | Where-Object { $_ -notin $Labels }).Count -gt 0) {
            throw [EscalationException]::new("$table.$Column exists with options [$($have -join ', ')] but the spec needs exactly [$($Labels -join ', ')]. Editing or removing options needs an owner decision.")
        }
        $script:Skipped.Add((Format-Tally $table $Column)); Write-Host "  skipped  $Column (exists, choice with $($Labels.Count) options)" -ForegroundColor Yellow
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

# ---------------------------------------------------------------------------------------------------------------
# Table, primary name
# ---------------------------------------------------------------------------------------------------------------

# Creates a table with its primary name column (pattern: Deploy-ChartDefinitionEntity.ps1). -Ownership is UserOwned or
# OrganizationOwned. The primary name's MaxLength and RequiredLevel in this body are IGNORED by Dataverse (it comes back
# as 850 / None): call Set-PrimaryNameLength and Set-PrimaryNameRequired afterwards.
# $Table and $PrimaryColumn are LOGICAL names (lowercase). -SchemaName / -PrimarySchema are the SchemaNames the create body carries
# (a table's and a column's SchemaName cannot be changed after the create); they default to the logical names, so scripts that do
# not pass them are unchanged, and each must equal its logical name when lowercased (task 088: sprk_StoredFile, sprk_Name).
function Add-Table {
    param(
        [string]$Table, [string]$Display, [string]$Plural, [string]$Description, [string]$PrimaryColumn, [string]$PrimaryDisplay,
        [string]$PrimaryDescription, [int]$PrimaryLength, [ValidateSet('UserOwned', 'OrganizationOwned')][string]$Ownership = 'OrganizationOwned',
        [string]$SchemaName = '', [string]$PrimarySchema = ''
    )
    if (-not $SchemaName) { $SchemaName = $Table }
    if (-not $PrimarySchema) { $PrimarySchema = $PrimaryColumn }
    if ($SchemaName.ToLowerInvariant() -ne $Table.ToLowerInvariant()) { throw "Add-Table: SchemaName '$SchemaName' does not lowercase to the logical name '$Table'." }
    if ($PrimarySchema.ToLowerInvariant() -ne $PrimaryColumn.ToLowerInvariant()) { throw "Add-Table: PrimarySchema '$PrimarySchema' does not lowercase to the logical name '$PrimaryColumn'." }
    $script:Entity = $Table
    $ownLabel = if ($Ownership -eq 'UserOwned') { 'user-owned' } else { 'organization-owned' }
    $script:Expect.Add(@{ Table = $Table; Column = $PrimaryColumn; Type = 'String' })
    $existing = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName,OwnershipType,PrimaryNameAttribute"
    if ($existing) {
        if ($existing.PrimaryNameAttribute -ne $PrimaryColumn) {
            throw [EscalationException]::new("$Table already exists with primary name '$($existing.PrimaryNameAttribute)', this task needs '$PrimaryColumn'. Owner decision needed.")
        }
        $script:Skipped.Add("table $Table"); Write-Host "  skipped  table $Table (exists, primary name $PrimaryColumn)" -ForegroundColor Yellow
        return
    }
    if ($script:DryRun) { $script:Planned.Add($Table); $script:Created.Add("table $Table"); Write-Host "  WOULD CREATE table $Table ($ownLabel)" -ForegroundColor Cyan; return }
    $def = @{
        '@odata.type'          = 'Microsoft.Dynamics.CRM.EntityMetadata'
        SchemaName             = $SchemaName
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
                SchemaName     = $PrimarySchema
                RequiredLevel  = @{ Value = 'ApplicationRequired' }
                MaxLength      = $PrimaryLength
                DisplayName    = New-Label $PrimaryDisplay
                Description    = New-Label $PrimaryDescription
                IsPrimaryName  = $true
            }
        )
    }
    try {
        Invoke-Dv -Path 'EntityDefinitions' -Method POST -Body $def -ExtraHeaders $script:SolutionHeader | Out-Null
    } catch {
        if (-not (Test-DuplicateRefusal $_.Exception.Message)) { throw }
        # A stale read hid a table that is already there (0x8009050d "Entities already exist" and relatives): the refusal
        # is authoritative; confirm on a settled read.
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 12)) { throw }
        $script:Skipped.Add("table $Table"); Write-Host "  skipped  table $Table (exists, found via create refusal)" -ForegroundColor Yellow
        return
    }
    $script:JustCreated.Add($Table)
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName" -Retries 20)) {
        throw "$Table was created but is not visible in metadata after 60 s."
    }
    $script:Created.Add("table $Table"); Write-Host "  CREATED  table $Table ($ownLabel, primary name $PrimaryColumn)" -ForegroundColor Green
}

# An exact row count. A read right after a table create can come back empty-handed or 404 (entity set not yet known), so
# it is repeated until a count appears; $null when none could be had.
function Get-RowCountExact([string]$EntitySet, [string]$IdColumn, [string]$Filter = '') {
    $q = "${EntitySet}?`$select=$IdColumn&`$top=1&`$count=true"
    if ($Filter) { $q += "&`$filter=$Filter" }
    for ($i = 0; $i -lt 12; $i++) {
        $r = Invoke-WhilePropagating { Invoke-Dv $q }
        if ($r -and $null -ne $r.'@odata.count') { return [int]$r.'@odata.count' }
        Start-Sleep -Seconds 5
    }
    return $null
}

# The create body's MaxLength for the primary name column is ignored by Dataverse (it comes back as 850). Shorten the
# primary name to the wanted length - only while the table has no rows, so no data can be cut.
function Set-PrimaryNameLength([string]$Table, [string]$Column, [int]$Wanted) {
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.StringAttributeMetadata"
    $a = Get-DvOrNull $path -Retries 4
    if (-not $a) { if ($script:DryRun) { return }; throw "$Table.$Column was not found (string)." }
    if ([int]$a.MaxLength -eq $Wanted) { $script:Skipped.Add("$Table.$Column length $Wanted"); Write-Host "  skipped  $Column max length already $Wanted" -ForegroundColor Yellow; return }
    $set = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName" -Retries 4
    if (-not $set) { throw "$Table entity set name could not be read." }
    # An exact count (DEF-010: `@($rows).Count -gt 0` read an empty table as "has rows").
    $cnt = Get-RowCountExact $set.EntitySetName "$($Table)id"
    if ($null -eq $cnt) { Write-Host "  WARNING: could not count rows of $Table; $Column max length left at $($a.MaxLength) (wanted $Wanted)" -ForegroundColor DarkYellow; return }
    if ($cnt -gt 0) { Write-Host "  WARNING: $Table has $cnt row(s) and $Column max length is $($a.MaxLength) (wanted $Wanted); not shortened" -ForegroundColor DarkYellow; return }
    if ($script:DryRun) { $script:Changed.Add("$Table.$Column length $Wanted"); Write-Host "  WOULD SET $Column max length $($a.MaxLength) -> $Wanted" -ForegroundColor Cyan; return }
    $was = [int]$a.MaxLength
    $a.MaxLength = $Wanted
    Invoke-WhilePropagating { Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    $chk = $null
    for ($i = 0; $i -lt 12; $i++) {
        $chk = Get-DvOrNull $path
        if ($chk -and [int]$chk.MaxLength -eq $Wanted) { break }
        Start-Sleep -Seconds 5
    }
    if (-not $chk -or [int]$chk.MaxLength -ne $Wanted) { throw "$Table.$Column max length is still $($chk.MaxLength) after the update." }
    $script:LengthSet["$Table.$Column"] = $Wanted
    $script:Changed.Add("$Table.$Column length $Wanted"); Write-Host "  CHANGED  $Column max length $was -> $Wanted" -ForegroundColor Green
}

# ---------------------------------------------------------------------------------------------------------------
# Required levels (DEF-006 / 007 / 009)
# ---------------------------------------------------------------------------------------------------------------

$script:RequiredRank = @{ None = 0; Recommended = 1; ApplicationRequired = 2; SystemRequired = 3 }

# Sets a column's required level with a read-back. Never LOWERS a level (a column already SystemRequired stays so when an
# older call asks for ApplicationRequired). Raising to SystemRequired needs every existing row to hold a value: the null
# rows are counted first and, when there are any, nothing is changed (a WARNING says so and the column is added to the
# Deferred list for the script to report). -Kind is the attribute cast: String, Lookup, Picklist or Integer.
function Set-ColumnRequiredLevel {
    param(
        [string]$Table, [string]$Column, [string]$Level,
        [ValidateSet('String', 'Lookup', 'Picklist', 'Integer')][string]$Kind,
        [string]$Tag = 'required', [string]$SkipText
    )
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.${Kind}AttributeMetadata"
    $a = Get-DvOrNull $path -Retries 4
    if (-not $a) { if ($script:DryRun) { return }; throw "$Table.$Column was not found ($($Kind.ToLower()))." }
    # The PUT sends the whole attribute back: if Set-PrimaryNameLength changed the length earlier in this run, wait for a read
    # that shows it, otherwise the PUT would restore the old length (DEF-012).
    $lenKey = "$Table.$Column"
    if ($Kind -eq 'String' -and $script:LengthSet.ContainsKey($lenKey) -and -not $script:DryRun) {
        for ($i = 0; $i -lt 12 -and [int]$a.MaxLength -ne $script:LengthSet[$lenKey]; $i++) {
            Start-Sleep -Seconds 5
            $a = Get-DvOrNull $path -Retries 4
        }
        if ([int]$a.MaxLength -ne $script:LengthSet[$lenKey]) { throw "$Table.$Column max length reads $($a.MaxLength) but this run set it to $($script:LengthSet[$lenKey]); not sending a stale copy." }
    }
    $cur = [string]$a.RequiredLevel.Value
    # A metadata read can come from a node that has not seen an earlier change yet (a re-run then reported a change it had
    # already made). Before concluding the level must change, read again a few times: any read at or above the wanted level wins.
    if ($script:RequiredRank[$cur] -lt $script:RequiredRank[$Level]) {
        for ($i = 0; $i -lt 3 -and $script:RequiredRank[$cur] -lt $script:RequiredRank[$Level]; $i++) {
            Start-Sleep -Seconds 2
            $again = Get-DvOrNull $path
            if ($again -and $script:RequiredRank[[string]$again.RequiredLevel.Value] -gt $script:RequiredRank[$cur]) { $a = $again; $cur = [string]$a.RequiredLevel.Value }
        }
    }
    if ($script:RequiredRank[$cur] -ge $script:RequiredRank[$Level]) {
        if (-not $SkipText) { $SkipText = "already $cur" }
        $script:Skipped.Add("$Table.$Column $Tag"); Write-Host "  skipped  $Column $SkipText" -ForegroundColor Yellow
        return
    }
    if ($Level -eq 'SystemRequired') {
        $set = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName" -Retries 4
        $nullFilter = if ($Kind -eq 'Lookup') { "_$($Column)_value eq null" } else { "$Column eq null" }
        $nulls = if ($set) { Get-RowCountExact $set.EntitySetName "$($Table)id" $nullFilter } else { $null }
        if ($null -eq $nulls) {
            Write-Host "  WARNING: could not count null rows of $Table.$Column; left at $cur (wanted SystemRequired)" -ForegroundColor DarkYellow
            $script:Deferred.Add("$Table.$Column required SystemRequired (null rows could not be counted)"); return
        }
        if ($nulls -gt 0) {
            Write-Host "  WARNING: $Table.$Column has $nulls row(s) with no value; left at $cur (wanted SystemRequired). Fill them, then re-run." -ForegroundColor DarkYellow
            $script:Deferred.Add("$Table.$Column required SystemRequired ($nulls null row(s))"); return
        }
    }
    if ($script:DryRun) { $script:Changed.Add("$Table.$Column $Tag"); Write-Host "  WOULD SET $Column required level $cur -> $Level" -ForegroundColor Cyan; return }
    $a.RequiredLevel.Value = $Level
    Invoke-WhilePropagating { Invoke-Dv -Path $path -Method PUT -Body $a -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null }
    $chk = $null
    for ($i = 0; $i -lt 12; $i++) {
        $chk = Get-DvOrNull $path
        if ($chk -and $chk.RequiredLevel.Value -eq $Level) { break }
        Start-Sleep -Seconds 5
    }
    if (-not $chk -or $chk.RequiredLevel.Value -ne $Level) { throw "$Table.$Column required level is still $($chk.RequiredLevel.Value) after the update." }
    if ($Kind -eq 'String' -and $script:LengthSet.ContainsKey($lenKey) -and [int]$chk.MaxLength -ne $script:LengthSet[$lenKey]) {
        throw "$Table.$Column max length is $($chk.MaxLength) after the required-level update but should be $($script:LengthSet[$lenKey])."
    }
    $script:Changed.Add("$Table.$Column $Tag"); Write-Host "  CHANGED  $Column required level $cur -> $Level" -ForegroundColor Green
}

# Like MaxLength, the primary name's RequiredLevel in the entity-create body is ignored (it comes back as None). Set it
# to ApplicationRequired afterwards (a FORM write without a name is refused; an API write is not - ApplicationRequired is
# not enforced on API writes, DEF-009; existing rows are unaffected).
function Set-PrimaryNameRequired([string]$Table, [string]$Column) {
    Set-ColumnRequiredLevel -Table $Table -Column $Column -Level 'ApplicationRequired' -Kind String -Tag 'required' -SkipText 'already application-required'
}

# Dataverse does NOT enforce ApplicationRequired on API writes (a Web API create without the column succeeds; only forms
# enforce it). SystemRequired is enforced by the platform on every create / update ("Attribute 'x' cannot be NULL"), and
# the metadata API accepts it on a custom lookup. Raising the level is additive and applies to an empty table or to rows
# that already hold a value (rows with a null stop the change, see Set-ColumnRequiredLevel).
function Set-LookupRequiredLevel([string]$Table, [string]$Column, [string]$Level) {
    Set-ColumnRequiredLevel -Table $Table -Column $Column -Level $Level -Kind Lookup -Tag "required $Level" -SkipText "already $Level"
}

# SystemRequired for a text column that is a key / parental column (same rules as Set-LookupRequiredLevel).
function Set-StringRequiredLevel([string]$Table, [string]$Column, [string]$Level) {
    Set-ColumnRequiredLevel -Table $Table -Column $Column -Level $Level -Kind String -Tag "required $Level" -SkipText "already $Level"
}

# SystemRequired for a choice column that is a key column (same rules as Set-LookupRequiredLevel).
function Set-PicklistRequiredLevel([string]$Table, [string]$Column, [string]$Level) {
    Set-ColumnRequiredLevel -Table $Table -Column $Column -Level $Level -Kind Picklist -Tag "required $Level" -SkipText "already $Level"
}

# SystemRequired for a whole-number column that is a key column (IntegerAttributeMetadata; same rules as Set-LookupRequiredLevel).
function Set-IntegerRequiredLevel([string]$Table, [string]$Column, [string]$Level) {
    Set-ColumnRequiredLevel -Table $Table -Column $Column -Level $Level -Kind Integer -Tag "required $Level" -SkipText "already $Level"
}

# ---------------------------------------------------------------------------------------------------------------
# Alternate key
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
        throw [EscalationException]::new("$Table key $Schema exists on [$(@($byName.KeyAttributes) -join ', ')] but this task needs [$want]. Owner decision needed.")
    }
    if (-not $key) {
        if ($script:DryRun) { $script:Created.Add("key $Schema"); Write-Host "  WOULD CREATE alternate key $Schema on ($want)" -ForegroundColor Cyan; return }
        # Columns created seconds ago may not yet be visible to the key create (MetadataCache): retry those.
        for ($try = 0; $true; $try++) {
            try {
                Invoke-Dv -Path "EntityDefinitions(LogicalName='$Table')/Keys" -Method POST -ExtraHeaders $script:SolutionHeader -Body @{
                    '@odata.type' = 'Microsoft.Dynamics.CRM.EntityKeyMetadata'
                    SchemaName    = $Schema
                    DisplayName   = New-Label $Display
                    KeyAttributes = $Attributes
                } | Out-Null
                $script:Created.Add("key $Schema"); Write-Host "  CREATED  alternate key $Schema on ($want)" -ForegroundColor Green
                break
            } catch {
                $msg = $_.Exception.Message
                if (Test-DuplicateRefusal $msg -Loose) {
                    $script:Skipped.Add("key $Schema"); Write-Host "  skipped  key $Schema (exists, found via create refusal)" -ForegroundColor Yellow
                    break
                }
                if ($try -lt 12 -and $msg -match '(?i)could not find|does not exist|0x80060888|0x80040217|0x80041102|MetadataCache|HTTP 404') {
                    Write-Host "    metadata still propagating, key create retry $($try + 1)/12 in 8s" -ForegroundColor DarkYellow
                    Start-Sleep -Seconds 8
                    continue
                }
                throw
            }
        }
    } else {
        $script:Skipped.Add("key $Schema"); Write-Host "  skipped  key $($key.SchemaName) (exists on ($want))" -ForegroundColor Yellow
    }
    if ($script:DryRun) { return }
    # The key index builds asynchronously; the key is usable (upsert by key) only when it is Active.
    $status = ''
    for ($i = 1; $i -le 40; $i++) {
        $k = Get-Keys $Table | Where-Object { (@($_.KeyAttributes | Sort-Object) -join ',') -eq $want } | Select-Object -First 1
        if ($k) {
            $status = $k.EntityKeyIndexStatus
            if ($status -eq 'Active') { Write-Host "  key index Active" -ForegroundColor Green; return }
            if ($status -eq 'Failed') { throw "Alternate key $Schema index FAILED (duplicate values in the data? check the system job)." }
        }
        Write-Host "    waiting for the key index ($i/40, status '$status')..."
        Start-Sleep -Seconds 10
    }
    throw "Alternate key $Schema index is not Active after 400 s (last status '$status'). Re-run the script to keep waiting."
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
    if ($script:DryRun) { $script:Created.Add("N:N $Schema"); Write-Host "  WOULD CREATE N:N $Schema ($Entity1 <-> $Entity2)" -ForegroundColor Cyan; return }
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
        Invoke-WhilePropagating { Invoke-Dv -Path 'RelationshipDefinitions' -Method POST -Body $nn -ExtraHeaders $script:SolutionHeader | Out-Null } | Out-Null
    } catch {
        if (-not (Test-DuplicateRefusal $_.Exception.Message)) { throw }
        for ($i = 0; $i -lt 48 -and -not (Get-ManyToMany $Schema); $i++) { Start-Sleep -Seconds 5 }
        if (-not (Get-ManyToMany $Schema)) { throw }
        $script:Skipped.Add("N:N $Schema"); Write-Host "  skipped  N:N $Schema (exists, found via create refusal)" -ForegroundColor Yellow
        return
    }
    for ($i = 0; $i -lt 48 -and -not (Get-ManyToMany $Schema); $i++) { Start-Sleep -Seconds 5 }
    if (-not (Get-ManyToMany $Schema)) { throw "N:N $Schema was created but is not visible in metadata after 240 s." }
    $script:Created.Add("N:N $Schema"); Write-Host "  CREATED  N:N $Schema ($Entity1 <-> $Entity2)" -ForegroundColor Green
}

Export-ModuleMember -Function @(
    'Initialize-EbillingSchema', 'Get-EbEntity', 'Set-EbEntity', 'Format-Tally',
    'Get-DataverseToken', 'Get-ErrorText', 'Get-StatusCode', 'Test-Transient', 'Test-DuplicateRefusal',
    'Invoke-Dv', 'Get-DvOrNull', 'New-Label', 'Invoke-WhilePropagating',
    'Get-Attr', 'Assert-AttrType', 'Test-TableKnown', 'Invoke-DvCreate', 'Wait-AttrVisible',
    'Add-Column', 'Add-Lookup', 'Add-LocalChoice', 'Get-PicklistLabels', 'Add-Table',
    'New-StringDef', 'New-MemoDef', 'New-MoneyDef', 'New-DateOnlyDef', 'New-DateTimeDef',
    'Get-RowCountExact', 'Set-PrimaryNameLength', 'Set-PrimaryNameRequired', 'Set-ColumnRequiredLevel',
    'Set-LookupRequiredLevel', 'Set-StringRequiredLevel', 'Set-PicklistRequiredLevel', 'Set-IntegerRequiredLevel',
    'Get-Keys', 'Add-AlternateKey', 'Get-ManyToMany', 'Add-ManyToMany'
)
