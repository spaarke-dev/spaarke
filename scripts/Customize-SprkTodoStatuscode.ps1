<#
.SYNOPSIS
    Customize sprk_todo.statuscode option values to Open / In Progress / Completed / Dismissed
    per smart-todo-decoupling-r3 spec FR-24 + design §4.1.

.DESCRIPTION
    Task 002 created sprk_todo but Dataverse auto-defaulted statuscode to Active(1)/Inactive(2).
    Per FR-24, statuscode must support four Spaarke-side states that map bidirectionally to
    Microsoft Graph todoTask status:
        Open         → notStarted   (Active statecode)
        In Progress  → inProgress   (Active statecode)
        Completed    → completed    (Inactive statecode)
        Dismissed    → deferred     (Inactive statecode)

    Spaarke convention (mirrors sprk_communication.statuscode):
        - Reuse OOB default values 1 and 2 by renaming them (1 → "Open", 2 → "Completed")
        - Add custom values in the 659490001+ range for additional options
            * 659490001 → "In Progress" (Active statecode)
            * 659490002 → "Dismissed"   (Inactive statecode)

    Idempotent: detects existing labels and skips updates that are already applied.

    PORTABILITY: All changes are scoped to SpaarkeCore solution via SolutionUniqueName param
    on InsertStatusValue. Schema exports cleanly via solution; no tenant-specific values.

    DEPLOYMENT APPROACH:
        Web API metadata actions:
        - Microsoft.Dynamics.CRM.UpdateOptionValue (rename existing statuscode value labels)
        - Microsoft.Dynamics.CRM.InsertStatusValue (add new statuscode values)
        - Microsoft.Dynamics.CRM.PublishXml (publish changes)

    PREREQUISITES:
        - Azure CLI logged in:  az login
        - Target env URL via -EnvironmentUrl or DATAVERSE_URL env var
        - sprk_todo entity already exists (created by Deploy-SprkTodoEntity.ps1, task 002)

.PARAMETER EnvironmentUrl
    Target Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com).
    Defaults to $env:DATAVERSE_URL.

.EXAMPLE
    .\Customize-SprkTodoStatuscode.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project:  smart-todo-decoupling-r3
    Task:     009 - Customize sprk_todo.statuscode option values
    Schema:   src/solutions/SpaarkeCore/entities/sprk_todo/entity-schema.md
    Created:  2026-06-07
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = $env:DATAVERSE_URL,

    [string]$SolutionUniqueName = "SpaarkeCore",

    [switch]$SkipVerification
)

$ErrorActionPreference = "Stop"
$StartTime = Get-Date

if (-not $EnvironmentUrl) {
    throw "EnvironmentUrl is required. Pass -EnvironmentUrl or set DATAVERSE_URL env var."
}
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')

# ============================================================================
# HELPERS
# ============================================================================

function Write-Header {
    param([string]$Message)
    Write-Host ""
    Write-Host "======================================================" -ForegroundColor Cyan
    Write-Host "  $Message" -ForegroundColor Cyan
    Write-Host "======================================================" -ForegroundColor Cyan
}

function Get-DataverseToken {
    param([string]$Url)
    $hostname = ([Uri]$Url).Host
    return (az account get-access-token --resource "https://$hostname" --query accessToken -o tsv)
}

function Invoke-DataverseApi {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )
    $uri = "$BaseUrl/$Endpoint"
    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Content-Type"     = "application/json"
        "Accept"           = "application/json"
    }
    $params = @{
        Uri     = $uri
        Headers = $headers
        Method  = $Method
    }
    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress)
    }
    return Invoke-RestMethod @params
}

function New-Label {
    param([string]$Text)
    return @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label"       = $Text
                "LanguageCode" = 1033
            }
        )
    }
}

# ============================================================================
# MAIN
# ============================================================================

Write-Header "Customize sprk_todo.statuscode Option Values"
Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Gray
Write-Host "Solution:    $SolutionUniqueName" -ForegroundColor Gray

$Token = Get-DataverseToken -Url $EnvironmentUrl
$BaseUrl = "$EnvironmentUrl/api/data/v9.2"

# ----------------------------------------------------------------------------
# Step 1: Inspect current statuscode option values
# ----------------------------------------------------------------------------
Write-Header "Step 1: Inspect current statuscode option values"

$attrEndpoint = "EntityDefinitions(LogicalName='sprk_todo')/Attributes(LogicalName='statuscode')/Microsoft.Dynamics.CRM.StatusAttributeMetadata?`$expand=OptionSet"
$current = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint $attrEndpoint -Method GET

Write-Host "Current statuscode option values:" -ForegroundColor Gray
$currentMap = @{}
foreach ($opt in $current.OptionSet.Options) {
    $lbl = $opt.Label.LocalizedLabels[0].Label
    $state = $opt.State
    Write-Host "  - Value=$($opt.Value) Label='$lbl' State=$state" -ForegroundColor DarkGray
    $currentMap[[int]$opt.Value] = @{ Label = $lbl; State = [int]$state }
}

# ----------------------------------------------------------------------------
# Step 2: Rename existing values (1 → "Open", 2 → "Completed")
# ----------------------------------------------------------------------------
Write-Header "Step 2: Rename existing statuscode values"

# Value 1 (Active state) → "Open"
if ($currentMap.ContainsKey(1) -and $currentMap[1].Label -eq "Open") {
    Write-Host "  Value 1 already labeled 'Open' — skipping" -ForegroundColor Yellow
} else {
    Write-Host "  Renaming Value 1: '$($currentMap[1].Label)' → 'Open'" -ForegroundColor Green
    $body = @{
        "AttributeLogicalName" = "statuscode"
        "EntityLogicalName"    = "sprk_todo"
        "Value"                = 1
        "Label"                = (New-Label -Text "Open")
        "MergeLabels"          = $false
        "SolutionUniqueName"   = $SolutionUniqueName
    }
    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "UpdateOptionValue" -Method POST -Body $body | Out-Null
}

# Value 2 (Inactive state) → "Completed"
if ($currentMap.ContainsKey(2) -and $currentMap[2].Label -eq "Completed") {
    Write-Host "  Value 2 already labeled 'Completed' — skipping" -ForegroundColor Yellow
} else {
    Write-Host "  Renaming Value 2: '$($currentMap[2].Label)' → 'Completed'" -ForegroundColor Green
    $body = @{
        "AttributeLogicalName" = "statuscode"
        "EntityLogicalName"    = "sprk_todo"
        "Value"                = 2
        "Label"                = (New-Label -Text "Completed")
        "MergeLabels"          = $false
        "SolutionUniqueName"   = $SolutionUniqueName
    }
    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "UpdateOptionValue" -Method POST -Body $body | Out-Null
}

# ----------------------------------------------------------------------------
# Step 3: Insert new statuscode values (In Progress, Dismissed)
# ----------------------------------------------------------------------------
Write-Header "Step 3: Insert new statuscode values"

# 659490001 → "In Progress" under statecode Active (0)
$IN_PROGRESS_VALUE = 659490001
if ($currentMap.ContainsKey($IN_PROGRESS_VALUE)) {
    Write-Host "  Value $IN_PROGRESS_VALUE ('$($currentMap[$IN_PROGRESS_VALUE].Label)') already exists — skipping" -ForegroundColor Yellow
} else {
    Write-Host "  Inserting Value $IN_PROGRESS_VALUE → 'In Progress' (statecode Active=0)" -ForegroundColor Green
    $body = @{
        "AttributeLogicalName" = "statuscode"
        "EntityLogicalName"    = "sprk_todo"
        "Value"                = $IN_PROGRESS_VALUE
        "StateCode"            = 0
        "Label"                = (New-Label -Text "In Progress")
        "SolutionUniqueName"   = $SolutionUniqueName
    }
    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "InsertStatusValue" -Method POST -Body $body | Out-Null
}

# 659490002 → "Dismissed" under statecode Inactive (1)
$DISMISSED_VALUE = 659490002
if ($currentMap.ContainsKey($DISMISSED_VALUE)) {
    Write-Host "  Value $DISMISSED_VALUE ('$($currentMap[$DISMISSED_VALUE].Label)') already exists — skipping" -ForegroundColor Yellow
} else {
    Write-Host "  Inserting Value $DISMISSED_VALUE → 'Dismissed' (statecode Inactive=1)" -ForegroundColor Green
    $body = @{
        "AttributeLogicalName" = "statuscode"
        "EntityLogicalName"    = "sprk_todo"
        "Value"                = $DISMISSED_VALUE
        "StateCode"            = 1
        "Label"                = (New-Label -Text "Dismissed")
        "SolutionUniqueName"   = $SolutionUniqueName
    }
    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "InsertStatusValue" -Method POST -Body $body | Out-Null
}

# ----------------------------------------------------------------------------
# Step 4: Publish customizations
# ----------------------------------------------------------------------------
Write-Header "Step 4: Publish customizations"

$publishBody = @{
    "ParameterXml" = "<importexportxml><entities><entity>sprk_todo</entity></entities></importexportxml>"
}
Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "PublishXml" -Method POST -Body $publishBody | Out-Null
Write-Host "  Published sprk_todo customizations" -ForegroundColor Green

# ----------------------------------------------------------------------------
# Step 5: Verify
# ----------------------------------------------------------------------------
if (-not $SkipVerification) {
    Write-Header "Step 5: Verify final statuscode option values"

    $verify = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint $attrEndpoint -Method GET

    $finalOptions = $verify.OptionSet.Options | Sort-Object -Property { [int]$_.State }, { [int]$_.Value }

    Write-Host "Final statuscode option values:" -ForegroundColor Gray
    foreach ($opt in $finalOptions) {
        $lbl = $opt.Label.LocalizedLabels[0].Label
        $state = $opt.State
        $stateName = if ($state -eq 0) { "Active" } else { "Inactive" }
        Write-Host ("  Value={0,-12} Label='{1,-12}' State={2} ({3})" -f $opt.Value, $lbl, $state, $stateName) -ForegroundColor Cyan
    }

    # Acceptance checks
    $expected = @(
        @{ Value = 1;          Label = "Open";        State = 0 },
        @{ Value = 659490001;  Label = "In Progress"; State = 0 },
        @{ Value = 2;          Label = "Completed";   State = 1 },
        @{ Value = 659490002;  Label = "Dismissed";   State = 1 }
    )

    $errors = @()
    foreach ($e in $expected) {
        $match = $finalOptions | Where-Object {
            [int]$_.Value -eq $e.Value -and
            $_.Label.LocalizedLabels[0].Label -eq $e.Label -and
            [int]$_.State -eq $e.State
        }
        if (-not $match) {
            $errors += "MISSING/MISMATCH: Value=$($e.Value) Label='$($e.Label)' State=$($e.State)"
        }
    }
    if ($finalOptions.Count -ne 4) {
        $errors += "Expected 4 options, found $($finalOptions.Count)"
    }

    if ($errors.Count -gt 0) {
        Write-Host ""
        Write-Host "VERIFICATION FAILED:" -ForegroundColor Red
        $errors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        throw "Verification failed."
    }

    Write-Host ""
    Write-Host "VERIFICATION PASSED: 4 statuscode options match acceptance criteria." -ForegroundColor Green
}

$elapsed = (Get-Date) - $StartTime
Write-Host ""
Write-Host "Done in $([math]::Round($elapsed.TotalSeconds, 1))s" -ForegroundColor Green
