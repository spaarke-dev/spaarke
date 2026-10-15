<#
.SYNOPSIS
    eBilling R1 task 018 - creates the UTBMS reference tables sprk_billingcodeset and sprk_billingcode (FR-08) and the
    default-code-set mapping lookups on sprk_mattertype_ref and sprk_practicearea_ref (FR-22).

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports every
    item as skipped and makes no write (no publish either).

    Items (tasks/018-utbms-code-sets.poml, spec FR-08):
      1  Table sprk_billingcodeset (organization-owned): primary name sprk_name, sprk_body (text, e.g. ABA / LOC),
         sprk_version, sprk_sourceurl (URL). Status is the standard statecode / statuscode (Active / Inactive).
      2  Table sprk_billingcode (organization-owned): primary name sprk_code, sprk_codeset (lookup -> sprk_billingcodeset,
         delete restricted), sprk_codetype local choice Task / Activity / Expense, sprk_phase, sprk_description,
         sprk_guidancetext (multiline; read by the coding Action). Status is statecode / statuscode.
      3  Alternate key sprk_billingcode_codeset_code_key on (sprk_codeset, sprk_code); waits for the index to be Active.
      4  Lookup sprk_defaultbillingcodeset -> sprk_billingcodeset on sprk_mattertype_ref and sprk_practicearea_ref.

    After it runs, re-run scripts/ebilling/Deploy-EbillingInvoiceLineSchema.ps1: task 010 deferred the Task / Activity /
    Expense code lookups on sprk_billingevent until sprk_billingcode exists.

    NEVER deletes or renames anything. If an existing column has the same logical name but a different type (or a lookup
    targets a different table, an existing choice has different options, or an existing key has other attributes), the
    script STOPS with exit code 2 and an ESCALATION message (project CLAUDE.md section 5).

    ADR-002: no plugin, business rule, calculated / rollup column or flow. Rows are loaded by Seed-EbillingBillingCodes.ps1.

    Eventually consistent metadata reads: a 404 right after a create is not trusted (the item is re-read); a create
    refused with "already exists" counts as skipped after a type check on a settled read; a column create issued right
    after its table was created is retried while the table is still propagating. Customization / publish locks and
    transient faults are retried with backoff.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName).

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1

.NOTES
    Project: ebilling-r1, task 018. Dev only in R1. Pattern: scripts/ebilling/Deploy-EbillingInvoiceLineSchema.ps1
    (task 010); table creation follows scripts/Deploy-ChartDefinitionEntity.ps1; the alternate key follows
    scripts/Set-AssignedAccessLedgerSchema.ps1.
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
$CodeSetTable = 'sprk_billingcodeset'
$CodeTable = 'sprk_billingcode'
$KeySchemaName = 'sprk_billingcode_codeset_code_key'

# The table the column helpers currently write to.
$script:Entity = $CodeSetTable
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
        $pk = Get-DvOrNull "EntityDefinitions(LogicalName='$table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal)"
        $have = @($pk.OptionSet.Options | ForEach-Object { $_.Label.UserLocalizedLabel.Label })
        if ((Compare-Object $have $Labels) -or $have.Count -ne $Labels.Count) {
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

# Creates an organization-owned table with its primary name column (pattern: Deploy-ChartDefinitionEntity.ps1).
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
    if ($DryRun) { $script:Planned.Add($Table); $script:Created.Add("table $Table"); Write-Host "  WOULD CREATE table $Table" -ForegroundColor Cyan; return }
    $def = @{
        '@odata.type'          = 'Microsoft.Dynamics.CRM.EntityMetadata'
        SchemaName             = $Table
        DisplayName            = New-Label $Display
        DisplayCollectionName  = New-Label $Plural
        Description            = New-Label $Description
        OwnershipType          = 'OrganizationOwned'
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
    $script:Created.Add("table $Table"); Write-Host "  CREATED  table $Table (organization-owned, primary name $PrimaryColumn)" -ForegroundColor Green
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
        if ($DryRun) { $script:Created.Add("key $Schema"); Write-Host "  WOULD CREATE alternate key $Schema on ($want)" -ForegroundColor Cyan; return }
        try {
            Invoke-Dv -Path "EntityDefinitions(LogicalName='$Table')/Keys" -Method POST -ExtraHeaders $SolutionHeader -Body @{
                '@odata.type' = 'Microsoft.Dynamics.CRM.EntityKeyMetadata'
                SchemaName    = $Schema
                DisplayName   = New-Label $Display
                KeyAttributes = $Attributes
            } | Out-Null
            $script:Created.Add("key $Schema"); Write-Host "  CREATED  alternate key $Schema on ($want)" -ForegroundColor Green
        } catch {
            if ($_.Exception.Message -notmatch '(?i)already exists|0x80047013|0x80048403|duplicate') { throw }
            $script:Skipped.Add("key $Schema"); Write-Host "  skipped  key $Schema (exists, found via create refusal)" -ForegroundColor Yellow
        }
    } else {
        $script:Skipped.Add("key $Schema"); Write-Host "  skipped  key $($key.SchemaName) (exists on ($want))" -ForegroundColor Yellow
    }
    if ($DryRun) { return }
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
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 018 - UTBMS code set schema" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    foreach ($ref in 'sprk_mattertype_ref', 'sprk_practicearea_ref') {
        if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$ref')?`$select=LogicalName" -Retries 4)) {
            throw "$ref does not exist in $EnvironmentUrl."
        }
    }

    # --- 1. sprk_billingcodeset ---------------------------------------------------------------------------------
    Write-Host "1. Table $CodeSetTable" -ForegroundColor Cyan
    Add-Table -Table $CodeSetTable -Display 'Billing Code Set' -Plural 'Billing Code Sets' `
        -Description 'A versioned UTBMS code set (litigation, counseling, project, ...) that billing codes belong to.' `
        -PrimaryColumn 'sprk_name' -PrimaryDisplay 'Name' -PrimaryDescription 'Name of the code set.' -PrimaryLength 200
    Set-PrimaryNameLength $CodeSetTable 'sprk_name' 200
    Add-Column 'sprk_body' (New-StringDef 'sprk_Body' 'Body' 'Standards body that publishes the set, e.g. ABA or LOC.' 100) 'String'
    Add-Column 'sprk_version' (New-StringDef 'sprk_Version' 'Version' 'Published version of the set, e.g. 1997 or 2023-04.' 50) 'String'
    Add-Column 'sprk_sourceurl' (New-StringDef 'sprk_SourceURL' 'Source URL' 'Where the set was downloaded from (utbms.com).' 500 'Url') 'String'

    # --- 2. sprk_billingcode ------------------------------------------------------------------------------------
    Write-Host "2. Table $CodeTable" -ForegroundColor Cyan
    Add-Table -Table $CodeTable -Display 'Billing Code' -Plural 'Billing Codes' `
        -Description 'A UTBMS task, activity or expense code, with the guidance text the coding Action reads.' `
        -PrimaryColumn 'sprk_code' -PrimaryDisplay 'Code' -PrimaryDescription 'The code as billed, e.g. L110 or E101. Unique within its code set.' -PrimaryLength 100
    Set-PrimaryNameLength $CodeTable 'sprk_code' 100
    Add-Lookup -Column 'sprk_codeset' -SchemaName 'sprk_CodeSet' -RelSchema 'sprk_billingcodeset_billingcode_codeset' -Target $CodeSetTable `
        -Display 'Code Set' -Description 'The code set this code belongs to.' -DeleteBehavior 'Restrict' -RequiredLevel 'ApplicationRequired'
    Add-LocalChoice -Column 'sprk_codetype' -Schema 'sprk_CodeType' -Display 'Code Type' -Description 'Task, Activity or Expense.' `
        -Labels @('Task', 'Activity', 'Expense') -RequiredLevel 'ApplicationRequired'
    Add-Column 'sprk_phase' (New-StringDef 'sprk_Phase' 'Phase' 'Phase code the task rolls up to (e.g. L100). Empty for activity and expense codes.' 20) 'String'
    Add-Column 'sprk_description' (New-StringDef 'sprk_Description' 'Description' 'Short name of the code, e.g. Fact Investigation/Development.' 500) 'String'
    Add-Column 'sprk_guidancetext' (New-MemoDef 'sprk_GuidanceText' 'Guidance Text' 'Definition and usage guidance sent to the coding Action with the code. An administrator edit is never overwritten by the seed.' 10000) 'Memo'

    # --- 3. Alternate key ---------------------------------------------------------------------------------------
    Write-Host "3. Alternate key (sprk_codeset, sprk_code)" -ForegroundColor Cyan
    $script:Entity = $CodeTable
    Add-AlternateKey -Table $CodeTable -Schema $KeySchemaName -Display 'Code Set and Code' -Attributes @('sprk_codeset', 'sprk_code')

    # --- 4. Default code set on the mapping tables --------------------------------------------------------------
    Write-Host "4. sprk_defaultbillingcodeset on the matter type and practice area tables" -ForegroundColor Cyan
    $script:Entity = 'sprk_mattertype_ref'
    Add-Lookup -Column 'sprk_defaultbillingcodeset' -SchemaName 'sprk_DefaultBillingCodeSet' -RelSchema 'sprk_billingcodeset_mattertype_ref_defaultcodeset' -Target $CodeSetTable `
        -Display 'Default Billing Code Set' -Description 'UTBMS code set used to code invoice lines for matters of this type (FR-22).'
    $script:Entity = 'sprk_practicearea_ref'
    Add-Lookup -Column 'sprk_defaultbillingcodeset' -SchemaName 'sprk_DefaultBillingCodeSet' -RelSchema 'sprk_billingcodeset_practicearea_ref_defaultcodeset' -Target $CodeSetTable `
        -Display 'Default Billing Code Set' -Description 'UTBMS code set used to code invoice lines for matters in this practice area (FR-22).'

    # --- Publish (only when something changed) ------------------------------------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$CodeSetTable</entity><entity>$CodeTable</entity><entity>sprk_mattertype_ref</entity><entity>sprk_practicearea_ref</entity></entities></importexportxml>"
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
            if (-not $a) { $bad.Add("$($e.Table).$($e.Column) missing") }
            elseif ($a.AttributeType -ne $e.Type) { $bad.Add("$($e.Table).$($e.Column) is $($a.AttributeType), expected $($e.Type)") }
        }
        foreach ($t in $CodeSetTable, $CodeTable) {
            $own = Get-DvOrNull "EntityDefinitions(LogicalName='$t')?`$select=OwnershipType,PrimaryNameAttribute,EntitySetName" -Retries 4
            if (-not $own) { $bad.Add("$t not readable"); continue }
            Write-Host ("  {0}: {1}, primary name {2}, entity set {3}" -f $t, $own.OwnershipType, $own.PrimaryNameAttribute, $own.EntitySetName)
            $sc = Get-DvOrNull "EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='statuscode')/Microsoft.Dynamics.CRM.StatusAttributeMetadata?`$expand=OptionSet(`$select=Options)" -Retries 4
            if ($sc) { Write-Host ("    statuscode: " + (($sc.OptionSet.Options | ForEach-Object { "$($_.Value)=$($_.Label.UserLocalizedLabel.Label)/state$($_.State)" }) -join ', ')) }
        }
        $k = Get-Keys $CodeTable | Where-Object { $_.SchemaName -eq $KeySchemaName } | Select-Object -First 1
        if (-not $k) { $bad.Add("key $KeySchemaName missing") }
        elseif ($k.EntityKeyIndexStatus -ne 'Active') { $bad.Add("key $KeySchemaName is $($k.EntityKeyIndexStatus), expected Active") }
        else { Write-Host ("  key {0} on ({1}): {2}" -f $k.SchemaName, (@($k.KeyAttributes) -join ', '), $k.EntityKeyIndexStatus) }
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
