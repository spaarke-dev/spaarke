<#
.SYNOPSIS
    eBilling R1 task 010 - turns sprk_billingevent into the Invoice Line (D-18, FR-04) and adds the FR-05 core-record fields.

.DESCRIPTION
    Idempotent, additive Dataverse Web API metadata script. Every item is check-then-create; a second run reports
    every item as skipped and makes no write (no publish either).

    Items (project constraint list, tasks/010-invoice-line-columns.poml):
      1  Entity display names "Invoice Line" / "Invoice Lines" (label change only).
      2  Option "Adjustment" (100000002) added to the global choice sprk_costtype (Fee 100000000, Expense 100000001).
      3  sprk_resolvedtimekeeper lookup -> sprk_timekeeper; existing TEXT column sprk_timekeeper relabelled
         "Timekeeper (as billed)" (display name only; logical name unchanged).
      4  sprk_approvedrate, sprk_approvedamount (money), sprk_adjustmentreason (multiline text),
         sprk_reviewedby (lookup -> systemuser), sprk_reviewedon (date-time).
      5  sprk_linestatus local choice: Pending, Clean, Flagged, Accepted, Reduced, Disallowed.
      6  sprk_taskcode / sprk_activitycode / sprk_expensecode lookups -> sprk_billingcode (DEFERRED while
         sprk_billingcode does not exist; task 018 creates it), text sprk_taskcodeasbilled /
         sprk_activitycodeasbilled / sprk_expensecodeasbilled, decimal sprk_codingconfidence.
      7  sprk_extractionconfidence (multiline text, JSON).
      8  sprk_adjustmentmethod local choice Percent / Fixed, decimal sprk_adjustmentvalue.
      9  sprk_adjustmentrule lookup -> sprk_policyversion.
     10  FR-05: sprk_regardingworkassignment lookup -> sprk_workassignment and the five ADR-024 resolver fields
         (sprk_regardingrecordtype lookup -> sprk_recordtype_ref, sprk_regardingrecordid, sprk_regardingrecordname,
         sprk_regardingrecordurl, sprk_regardingrecordnumber). Types and max lengths are read from sprk_invoice
         at run time (documented fallbacks used if sprk_invoice does not carry them yet).

    The typed sprk_matter / sprk_project lookups already on sprk_billingevent are the line's Matter and Project
    lookups (no sprk_regardingmatter / sprk_regardingproject here). Derivation and the one-lookup guard are task 013.

    NEVER deletes or renames anything. If an existing column has the same logical name but a different type (or a
    lookup targets a different table, or an existing choice has different options), the script STOPS with exit code 2
    and an ESCALATION message (project CLAUDE.md section 5).

    ADR-002: no plugin, business rule, calculated / rollup column or flow. Values are written server-side by later tasks.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Unmanaged solution the created components are added to (header MSCRM.SolutionUniqueName). SpaarkeMaster already
    contains sprk_billingevent with all subcomponents; passing it keeps new relationships / columns in the solution
    that task 029 exports.

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingInvoiceLineSchema.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingInvoiceLineSchema.ps1

.NOTES
    Project: ebilling-r1, task 010. Dev only in R1. Exemplar: scripts/Deploy-ChartDefinitionEntity.ps1.
    Exit codes: 0 = done (created / skipped / deferred), 1 = error, 2 = escalation (destructive or conflicting).
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster'
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$Entity = 'sprk_billingevent'
$ApiBase = "$EnvironmentUrl/api/data/v9.2"

# Tallies for the final summary.
$script:Created = [System.Collections.Generic.List[string]]::new()
$script:Changed = [System.Collections.Generic.List[string]]::new()
$script:Skipped = [System.Collections.Generic.List[string]]::new()
$script:Deferred = [System.Collections.Generic.List[string]]::new()
$script:Token = $null
$script:TokenAt = [datetime]::MinValue

class EscalationException : System.Exception {
    EscalationException([string]$message) : base($message) {}
}

# ---------------------------------------------------------------------------------------------------------------
# Web API plumbing
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
        throw [EscalationException]::new("$Entity.$Column already exists with type '$($attr.AttributeType)' but this task needs '$ExpectedType'. A same-name column of a different type must not be changed by script - owner decision needed.")
    }
}

# Metadata reads are eventually consistent across Dataverse nodes (a column created minutes ago can read as 404 once),
# so an existence check can be a false negative. A create refused with "already exists" (0x80047013 and relatives) is
# therefore the authoritative "it is there": the item is counted as skipped, after the type check on a settled read.
function Invoke-DvCreate {
    param([string]$Path, $Body, [string]$Column, [string]$ExpectedType)
    try {
        Invoke-Dv -Path $Path -Method POST -Body $Body -ExtraHeaders $SolutionHeader | Out-Null
        return 'created'
    } catch {
        if ($_.Exception.Message -notmatch '(?i)already exists|0x80047013|0x80048403') { throw }
        for ($i = 0; $i -lt 12; $i++) {
            $attr = Get-Attr $Entity $Column
            if ($attr) { Assert-AttrType $attr $Column $ExpectedType; break }
            Start-Sleep -Seconds 5
        }
        return 'exists'
    }
}

function Wait-AttrVisible([string]$Column) {
    for ($i = 0; $i -lt 12; $i++) {
        if (Get-Attr $Entity $Column) { return }
        Start-Sleep -Seconds 5
    }
    throw "$Entity.$Column was created but is not visible in metadata after 60 s."
}

function Add-Column {
    param([string]$Column, [hashtable]$Definition, [string]$ExpectedType)
    $attr = Get-Attr $Entity $Column
    if ($attr) {
        Assert-AttrType $attr $Column $ExpectedType
        $script:Skipped.Add($Column); Write-Host "  skipped  $Column (exists, $ExpectedType)" -ForegroundColor Yellow
        return
    }
    if ($DryRun) { $script:Created.Add($Column); Write-Host "  WOULD CREATE $Column ($ExpectedType)" -ForegroundColor Cyan; return }
    $r = Invoke-DvCreate -Path "EntityDefinitions(LogicalName='$Entity')/Attributes" -Body $Definition -Column $Column -ExpectedType $ExpectedType
    if ($r -eq 'exists') { $script:Skipped.Add($Column); Write-Host "  skipped  $Column (exists, found via create refusal)" -ForegroundColor Yellow; return }
    Wait-AttrVisible $Column
    $script:Created.Add($Column); Write-Host "  CREATED  $Column ($ExpectedType)" -ForegroundColor Green
}

function Add-Lookup {
    param([string]$Column, [string]$SchemaName, [string]$Target, [string]$Display, [string]$Description, [string]$DeleteBehavior = 'RemoveLink')
    $attr = Get-Attr $Entity $Column
    if ($attr) {
        Assert-AttrType $attr $Column 'Lookup'
        $lk = Get-DvOrNull "EntityDefinitions(LogicalName='$Entity')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=Targets"
        if ($lk -and $lk.Targets -and ($lk.Targets -notcontains $Target)) {
            throw [EscalationException]::new("$Entity.$Column already exists as a lookup to '$($lk.Targets -join ',')' but this task needs '$Target'. Owner decision needed.")
        }
        $script:Skipped.Add($Column); Write-Host "  skipped  $Column (exists, Lookup -> $Target)" -ForegroundColor Yellow
        return
    }
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Target')?`$select=LogicalName")) {
        throw "Lookup target table '$Target' for $Column does not exist in $EnvironmentUrl."
    }
    if ($DryRun) { $script:Created.Add($Column); Write-Host "  WOULD CREATE $Column (Lookup -> $Target)" -ForegroundColor Cyan; return }
    $rel = @{
        '@odata.type'        = 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata'
        SchemaName           = "sprk_${Target}_${Entity}_$Column"
        ReferencedEntity     = $Target
        ReferencingEntity    = $Entity
        CascadeConfiguration = @{
            Assign = 'NoCascade'; Delete = $DeleteBehavior; Merge = 'NoCascade'
            Reparent = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'
        }
        Lookup               = @{
            '@odata.type'  = 'Microsoft.Dynamics.CRM.LookupAttributeMetadata'
            SchemaName     = $SchemaName
            DisplayName    = New-Label $Display
            Description    = New-Label $Description
            RequiredLevel  = @{ Value = 'None' }
        }
    }
    $r = Invoke-DvCreate -Path 'RelationshipDefinitions' -Body $rel -Column $Column -ExpectedType 'Lookup'
    if ($r -eq 'exists') { $script:Skipped.Add($Column); Write-Host "  skipped  $Column (exists, found via create refusal)" -ForegroundColor Yellow; return }
    Wait-AttrVisible $Column
    $script:Created.Add($Column); Write-Host "  CREATED  $Column (Lookup -> $Target)" -ForegroundColor Green
}

function New-StringDef([string]$Schema, [string]$Display, [string]$Description, [int]$MaxLength, [string]$Format = 'Text') {
    return @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'
        SchemaName     = $Schema
        DisplayName    = New-Label $Display
        Description    = New-Label $Description
        RequiredLevel  = @{ Value = 'None' }
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

function New-MoneyDef([string]$Schema, [string]$Display, [string]$Description) {
    # Same shape as sprk_amount / sprk_rate on this table: precision 2 from the attribute, 0 .. 1,000,000,000.
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

function New-DecimalDef([string]$Schema, [string]$Display, [string]$Description, [int]$Precision, [double]$Min, [double]$Max) {
    return @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.DecimalAttributeMetadata'
        SchemaName     = $Schema
        DisplayName    = New-Label $Display
        Description    = New-Label $Description
        RequiredLevel  = @{ Value = 'None' }
        Precision      = $Precision
        MinValue       = $Min
        MaxValue       = $Max
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
    param([string]$Column, [string]$Schema, [string]$Display, [string]$Description, [string[]]$Labels)
    $attr = Get-Attr $Entity $Column
    if ($attr) {
        Assert-AttrType $attr $Column 'Picklist'
        $pk = Get-DvOrNull "EntityDefinitions(LogicalName='$Entity')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options,IsGlobal)"
        $have = @($pk.OptionSet.Options | ForEach-Object { $_.Label.UserLocalizedLabel.Label })
        if ((Compare-Object $have $Labels) -or $have.Count -ne $Labels.Count) {
            throw [EscalationException]::new("$Entity.$Column exists with options [$($have -join ', ')] but the spec needs exactly [$($Labels -join ', ')]. Editing or removing options needs an owner decision.")
        }
        $script:Skipped.Add($Column); Write-Host "  skipped  $Column (exists, choice with $($Labels.Count) options)" -ForegroundColor Yellow
        return
    }
    $options = @(); $v = 100000000
    foreach ($l in $Labels) { $options += @{ Value = $v; Label = New-Label $l }; $v++ }
    $def = @{
        '@odata.type'  = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'
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
    Add-Column -Column $Column -Definition $def -ExpectedType 'Picklist'
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling task 010 - Invoice Line schema on $Entity" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Solution: $SolutionUniqueName   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    if (-not (Get-DvOrNull "EntityDefinitions(LogicalName='$Entity')?`$select=LogicalName" -Retries 4)) {
        throw "$Entity does not exist in $EnvironmentUrl."
    }

    # --- 1. Entity display names ------------------------------------------------------------------------------
    Write-Host "1. Entity display names" -ForegroundColor Cyan
    $ent = Invoke-Dv "EntityDefinitions(LogicalName='$Entity')"
    $curName = $ent.DisplayName.UserLocalizedLabel.Label
    $curPlural = $ent.DisplayCollectionName.UserLocalizedLabel.Label
    if ($curName -eq 'Invoice Line' -and $curPlural -eq 'Invoice Lines') {
        $script:Skipped.Add('entity display names'); Write-Host "  skipped  display names already 'Invoice Line' / 'Invoice Lines'" -ForegroundColor Yellow
    } elseif ($DryRun) {
        $script:Changed.Add('entity display names'); Write-Host "  WOULD RELABEL '$curName' / '$curPlural' -> 'Invoice Line' / 'Invoice Lines'" -ForegroundColor Cyan
    } else {
        $ent.DisplayName = New-Label 'Invoice Line'
        $ent.DisplayCollectionName = New-Label 'Invoice Lines'
        Invoke-Dv -Path "EntityDefinitions($($ent.MetadataId))" -Method PUT -Body $ent -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        $script:Changed.Add('entity display names'); Write-Host "  RELABELLED '$curName' / '$curPlural' -> 'Invoice Line' / 'Invoice Lines'" -ForegroundColor Green
    }

    # --- 2. Line type: add Adjustment to sprk_costtype ---------------------------------------------------------
    Write-Host "2. sprk_costtype option 'Adjustment'" -ForegroundColor Cyan
    $cost = Get-DvOrNull "GlobalOptionSetDefinitions(Name='sprk_costtype')" -Retries 4
    if (-not $cost) { throw "Global choice sprk_costtype does not exist in $EnvironmentUrl." }
    $byLabel = $cost.Options | Where-Object { $_.Label.UserLocalizedLabel.Label -eq 'Adjustment' }
    $byValue = $cost.Options | Where-Object { $_.Value -eq 100000002 }
    if ($byLabel) {
        if ($byLabel.Value -ne 100000002) { throw [EscalationException]::new("sprk_costtype already has 'Adjustment' with value $($byLabel.Value), not 100000002. Owner decision needed.") }
        $script:Skipped.Add('sprk_costtype.Adjustment'); Write-Host "  skipped  option Adjustment = 100000002 exists" -ForegroundColor Yellow
    } elseif ($byValue) {
        throw [EscalationException]::new("sprk_costtype value 100000002 is already used by '$($byValue.Label.UserLocalizedLabel.Label)'. Owner decision needed.")
    } elseif ($DryRun) {
        $script:Created.Add('sprk_costtype.Adjustment'); Write-Host "  WOULD CREATE option Adjustment = 100000002" -ForegroundColor Cyan
    } else {
        Invoke-Dv -Path 'InsertOptionValue' -Method POST -Body @{
            OptionSetName = 'sprk_costtype'; Value = 100000002; Label = (New-Label 'Adjustment')
        } | Out-Null
        $script:Created.Add('sprk_costtype.Adjustment'); Write-Host "  CREATED  option Adjustment = 100000002" -ForegroundColor Green
    }

    # --- 3. Timekeeper: roster lookup + relabel of the existing text column ------------------------------------
    Write-Host "3. Timekeeper" -ForegroundColor Cyan
    Add-Lookup -Column 'sprk_resolvedtimekeeper' -SchemaName 'sprk_ResolvedTimekeeper' -Target 'sprk_timekeeper' `
        -Display 'Timekeeper' -Description 'Resolved roster timekeeper (sprk_timekeeper table); the billed name stays in Timekeeper (as billed).'
    $tk = Get-DvOrNull "EntityDefinitions(LogicalName='$Entity')/Attributes(LogicalName='sprk_timekeeper')/Microsoft.Dynamics.CRM.StringAttributeMetadata" -Retries 4
    if (-not $tk) { throw "Expected the existing text column $Entity.sprk_timekeeper was not found (or is not a string)." }
    if ($tk.DisplayName.UserLocalizedLabel.Label -eq 'Timekeeper (as billed)') {
        $script:Skipped.Add('sprk_timekeeper label'); Write-Host "  skipped  sprk_timekeeper already labelled 'Timekeeper (as billed)'" -ForegroundColor Yellow
    } elseif ($DryRun) {
        $script:Changed.Add('sprk_timekeeper label'); Write-Host "  WOULD RELABEL sprk_timekeeper '$($tk.DisplayName.UserLocalizedLabel.Label)' -> 'Timekeeper (as billed)'" -ForegroundColor Cyan
    } else {
        $tk.DisplayName = New-Label 'Timekeeper (as billed)'
        Invoke-Dv -Path "EntityDefinitions(LogicalName='$Entity')/Attributes(LogicalName='sprk_timekeeper')/Microsoft.Dynamics.CRM.StringAttributeMetadata" -Method PUT -Body $tk -ExtraHeaders @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        $script:Changed.Add('sprk_timekeeper label'); Write-Host "  RELABELLED sprk_timekeeper -> 'Timekeeper (as billed)'" -ForegroundColor Green
    }

    # --- 4. Review outcome columns ------------------------------------------------------------------------------
    Write-Host "4. Review outcome columns" -ForegroundColor Cyan
    Add-Column 'sprk_approvedrate' (New-MoneyDef 'sprk_ApprovedRate' 'Approved Rate' 'Hourly rate approved in review.') 'Money'
    Add-Column 'sprk_approvedamount' (New-MoneyDef 'sprk_ApprovedAmount' 'Approved Amount' 'Line amount approved in review.') 'Money'
    Add-Column 'sprk_adjustmentreason' (New-MemoDef 'sprk_AdjustmentReason' 'Adjustment Reason' 'Reason recorded when a line is reduced or disallowed.' 2000) 'Memo'
    Add-Lookup -Column 'sprk_reviewedby' -SchemaName 'sprk_ReviewedBy' -Target 'systemuser' -Display 'Reviewed By' -Description 'User who decided this line.'
    Add-Column 'sprk_reviewedon' (New-DateTimeDef 'sprk_ReviewedOn' 'Reviewed On' 'When this line was decided.') 'DateTime'

    # --- 5. Line status ------------------------------------------------------------------------------------------
    Write-Host "5. Line status" -ForegroundColor Cyan
    Add-LocalChoice -Column 'sprk_linestatus' -Schema 'sprk_LineStatus' -Display 'Line Status' `
        -Description 'Review state of the invoice line. Pending is shown as Processing in the review UI.' `
        -Labels @('Pending', 'Clean', 'Flagged', 'Accepted', 'Reduced', 'Disallowed')

    # --- 6. UTBMS coding -----------------------------------------------------------------------------------------
    Write-Host "6. UTBMS coding" -ForegroundColor Cyan
    $billingCodeExists = [bool](Get-DvOrNull "EntityDefinitions(LogicalName='sprk_billingcode')?`$select=LogicalName")
    foreach ($c in @(
            @{ Col = 'sprk_taskcode'; Schema = 'sprk_TaskCode'; Display = 'Task Code'; Desc = 'UTBMS task code (sprk_billingcode).' },
            @{ Col = 'sprk_activitycode'; Schema = 'sprk_ActivityCode'; Display = 'Activity Code'; Desc = 'UTBMS activity code (sprk_billingcode).' },
            @{ Col = 'sprk_expensecode'; Schema = 'sprk_ExpenseCode'; Display = 'Expense Code'; Desc = 'UTBMS expense code (sprk_billingcode).' })) {
        if ($billingCodeExists) {
            Add-Lookup -Column $c.Col -SchemaName $c.Schema -Target 'sprk_billingcode' -Display $c.Display -Description $c.Desc
        } else {
            $script:Deferred.Add($c.Col); Write-Host "  deferred: sprk_billingcode missing - $($c.Col) not created (task 018 creates the table; re-run afterwards)" -ForegroundColor DarkYellow
        }
    }
    Add-Column 'sprk_taskcodeasbilled' (New-StringDef 'sprk_TaskCodeAsBilled' 'Task Code (as billed)' 'Task code exactly as it appears on the invoice.' 100) 'String'
    Add-Column 'sprk_activitycodeasbilled' (New-StringDef 'sprk_ActivityCodeAsBilled' 'Activity Code (as billed)' 'Activity code exactly as it appears on the invoice.' 100) 'String'
    Add-Column 'sprk_expensecodeasbilled' (New-StringDef 'sprk_ExpenseCodeAsBilled' 'Expense Code (as billed)' 'Expense code exactly as it appears on the invoice.' 100) 'String'
    Add-Column 'sprk_codingconfidence' (New-DecimalDef 'sprk_CodingConfidence' 'Coding Confidence' 'Combined confidence (0 to 1) of the AI-assigned UTBMS codes.' 4 0 1) 'Decimal'

    # --- 7. Extraction confidence --------------------------------------------------------------------------------
    Write-Host "7. Extraction confidence" -ForegroundColor Cyan
    Add-Column 'sprk_extractionconfidence' (New-MemoDef 'sprk_ExtractionConfidence' 'Extraction Confidence' 'JSON: per-field confidence with source page and bounding box (FR-04 / FR-21).' 100000) 'Memo'

    # --- 8. Adjustment lines -------------------------------------------------------------------------------------
    Write-Host "8. Adjustment method and value" -ForegroundColor Cyan
    Add-LocalChoice -Column 'sprk_adjustmentmethod' -Schema 'sprk_AdjustmentMethod' -Display 'Adjustment Method' `
        -Description 'How an Adjustment line is applied: percent of fees or a fixed amount.' -Labels @('Percent', 'Fixed')
    Add-Column 'sprk_adjustmentvalue' (New-DecimalDef 'sprk_AdjustmentValue' 'Adjustment Value' 'Percent or fixed amount for an Adjustment line, per Adjustment Method.' 4 0 1000000000) 'Decimal'

    # --- 9. Adjustment rule --------------------------------------------------------------------------------------
    Write-Host "9. Adjustment rule" -ForegroundColor Cyan
    Add-Lookup -Column 'sprk_adjustmentrule' -SchemaName 'sprk_AdjustmentRule' -Target 'sprk_policyversion' `
        -Display 'Adjustment Rule' -Description 'Optional published policy version the adjustment was made under (FR-32).'

    # --- 10. FR-05 core-record fields ----------------------------------------------------------------------------
    Write-Host "10. FR-05 core record (work assignment lookup + ADR-024 resolver fields)" -ForegroundColor Cyan
    Add-Lookup -Column 'sprk_regardingworkassignment' -SchemaName 'sprk_RegardingWorkAssignment' -Target 'sprk_workassignment' `
        -Display 'Regarding Work Assignment' -Description 'Core record when the line is filed under a work assignment (typed sprk_matter / sprk_project cover Matter and Project).'
    # The five resolver fields mirror sprk_invoice (read live); fallbacks are the values read on 2026-10-15.
    $inv = @{}
    foreach ($n in 'sprk_regardingrecordid', 'sprk_regardingrecordname', 'sprk_regardingrecordnumber', 'sprk_regardingrecordurl') {
        $a = Get-DvOrNull "EntityDefinitions(LogicalName='sprk_invoice')/Attributes(LogicalName='$n')/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength,FormatName"
        if ($a) { $inv[$n] = $a }
    }
    function Get-InvLen([string]$n, [int]$fallback) { if ($inv[$n]) { [int]$inv[$n].MaxLength } else { $fallback } }
    Add-Lookup -Column 'sprk_regardingrecordtype' -SchemaName 'sprk_RegardingRecordType' -Target 'sprk_recordtype_ref' `
        -Display 'Regarding Record Type' -Description 'ADR-024 resolver field: the record type of the core record.'
    Add-Column 'sprk_regardingrecordid' (New-StringDef 'sprk_RegardingRecordId' 'Regarding Record Id' 'ADR-024 resolver field: id of the core record.' (Get-InvLen 'sprk_regardingrecordid' 100)) 'String'
    Add-Column 'sprk_regardingrecordname' (New-StringDef 'sprk_RegardingRecordName' 'Regarding Record Name' 'ADR-024 resolver field: name of the core record.' (Get-InvLen 'sprk_regardingrecordname' 100)) 'String'
    Add-Column 'sprk_regardingrecordurl' (New-StringDef 'sprk_RegardingRecordURL' 'Regarding Record URL' 'ADR-024 resolver field: link to the core record.' (Get-InvLen 'sprk_regardingrecordurl' 250) 'Url') 'String'
    Add-Column 'sprk_regardingrecordnumber' (New-StringDef 'sprk_RegardingRecordNumber' 'Regarding Record Number' 'ADR-024 resolver field: business number of the core record.' (Get-InvLen 'sprk_regardingrecordnumber' 1000)) 'String'

    # --- Publish (only when something changed) ------------------------------------------------------------------
    Write-Host "Publish" -ForegroundColor Cyan
    $wrote = ($script:Created.Count + $script:Changed.Count) -gt 0
    if ($DryRun) {
        Write-Host "  WhatIf: publish skipped" -ForegroundColor Cyan
    } elseif (-not $wrote) {
        Write-Host "  nothing changed - no publish" -ForegroundColor Yellow
    } else {
        Invoke-Dv -Path 'PublishXml' -Method POST -Body @{
            ParameterXml = "<importexportxml><entities><entity>$Entity</entity></entities><optionsets><optionset>sprk_costtype</optionset></optionsets></importexportxml>"
        } | Out-Null
        Write-Host "  customizations published" -ForegroundColor Green
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
Write-Host ("deferred: {0}  {1}" -f $script:Deferred.Count, ($script:Deferred -join ', '))
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
