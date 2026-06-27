<#
.SYNOPSIS
    Creates the sprk_backgroundjobrun Dataverse entity (per-run instances for the Spaarke.Scheduling framework).

.DESCRIPTION
    R3 Task 016 — Spaarke Platform Foundations (R3).
    Idempotent: re-running checks every component (entity, columns, option sets, lookup, publish).

    Schema (per spec.md FR-2.5 + design.md Part 2):
      - 1 primary name field (auto-number)
      - 10 custom columns from FR-2.5 (lookup, runid, trigger, correlationid, startedon, completedon,
        status, errormessage, processeditems, resultjson)
      - 1 extra column for task 014 idempotency: sprk_scheduledfireon (DateTime, optional)
      - 2 local OptionSets: sprk_backgroundjobrun_trigger (3 values), sprk_backgroundjobrun_status (4 values)
      - 1 Lookup relationship: sprk_backgroundjob (1) -> sprk_backgroundjobrun (N), Required, Delete=Restrict

    Coordinates with task 015 (sprk_backgroundjob): if parent table does not yet exist, this script
    waits up to 60 seconds for it to appear, then either creates the lookup or warns and exits with
    operator instructions.

.PARAMETER EnvironmentDomain
    The Dataverse environment domain. Default: spaarkedev1.crm.dynamics.com

.EXAMPLE
    .\Create-BackgroundJobRunEntity.ps1
    .\Create-BackgroundJobRunEntity.ps1 -EnvironmentDomain "spaarkedev1.crm.dynamics.com"
#>
param(
    [string]$EnvironmentDomain = "spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = 'Continue'
$EntityLogicalName = "sprk_backgroundjobrun"
$ParentEntityLogicalName = "sprk_backgroundjob"

Write-Host "==============================================================" -ForegroundColor Cyan
Write-Host "Create sprk_backgroundjobrun entity (R3 task 016)" -ForegroundColor Cyan
Write-Host "Target environment: $EnvironmentDomain" -ForegroundColor Cyan
Write-Host "==============================================================" -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# Auth + helpers
# ---------------------------------------------------------------------------
$token = az account get-access-token --resource "https://$EnvironmentDomain" --query accessToken -o tsv
if (-not $token) { Write-Error "Failed to obtain Dataverse access token. Run: az login"; exit 1 }
Write-Host "[auth] Token acquired" -ForegroundColor Green

$BaseUrl = "https://$EnvironmentDomain/api/data/v9.2"
$headers = @{
    "Authorization"     = "Bearer $token"
    "OData-MaxVersion"  = "4.0"
    "OData-Version"     = "4.0"
    "Content-Type"      = "application/json"
    "Accept"            = "application/json"
    "Prefer"            = "return=representation"
}

function New-Label([string]$Text) {
    @{
        "@odata.type"      = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels"  = @(@{
            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
            "Label"        = $Text
            "LanguageCode" = 1033
        })
    }
}

function Invoke-DV([string]$Ep, [string]$Method = "GET", [object]$Body = $null) {
    $p = @{ Uri = "$BaseUrl/$Ep"; Headers = $headers; Method = $Method; UseBasicParsing = $true }
    if ($Body) { $p.Body = $Body | ConvertTo-Json -Depth 25 -Compress }
    try { $r = Invoke-RestMethod @p; @{ Success = $true; Data = $r } }
    catch { @{ Success = $false; Error = $_.Exception.Message; Response = $_.ErrorDetails.Message } }
}

function Test-EntityExists([string]$Logical) {
    try {
        Invoke-RestMethod -Uri "$BaseUrl/EntityDefinitions(LogicalName='$Logical')?`$select=LogicalName" `
            -Headers $headers -Method GET -UseBasicParsing -ErrorAction Stop | Out-Null
        return $true
    } catch { return $false }
}

function Test-AttributeExists([string]$EntityLogical, [string]$AttrLogical) {
    # Retry once on miss — metadata cache propagation can briefly 404 a real attribute.
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        try {
            Invoke-RestMethod -Uri "$BaseUrl/EntityDefinitions(LogicalName='$EntityLogical')/Attributes(LogicalName='$AttrLogical')?`$select=LogicalName" `
                -Headers $headers -Method GET -UseBasicParsing -ErrorAction Stop | Out-Null
            return $true
        } catch {
            if ($attempt -eq 2) { return $false }
            Start-Sleep -Milliseconds 500
        }
    }
}

function Test-RelationshipExists([string]$SchemaName) {
    try {
        Invoke-RestMethod -Uri "$BaseUrl/RelationshipDefinitions(SchemaName='$SchemaName')?`$select=SchemaName" `
            -Headers $headers -Method GET -UseBasicParsing -ErrorAction Stop | Out-Null
        return $true
    } catch { return $false }
}

# ---------------------------------------------------------------------------
# Step 0: Wait for parent table sprk_backgroundjob (created by task 015)
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[0] Checking for parent entity '$ParentEntityLogicalName' (task 015)..." -ForegroundColor Cyan
$parentReady = $false
$maxWaitSeconds = 60
$pollInterval = 5
$elapsed = 0
while ($elapsed -lt $maxWaitSeconds) {
    if (Test-EntityExists $ParentEntityLogicalName) {
        $parentReady = $true
        Write-Host "  Parent entity present" -ForegroundColor Green
        break
    }
    Write-Host "  Parent entity not present yet; waiting ${pollInterval}s (elapsed ${elapsed}s / ${maxWaitSeconds}s)" -ForegroundColor Yellow
    Start-Sleep -Seconds $pollInterval
    $elapsed += $pollInterval
}

# ---------------------------------------------------------------------------
# Step 1: Entity
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[1] Entity '$EntityLogicalName'" -ForegroundColor Cyan
if (Test-EntityExists $EntityLogicalName) {
    Write-Host "  Entity already exists; skipping creation" -ForegroundColor Yellow
} else {
    $r = Invoke-DV -Ep "EntityDefinitions" -Method "POST" -Body @{
        "@odata.type"            = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"             = $EntityLogicalName
        "DisplayName"            = New-Label "Background Job Run"
        "DisplayCollectionName"  = New-Label "Background Job Runs"
        "Description"            = New-Label "Per-run instance of a scheduled background job (Spaarke.Scheduling)."
        "OwnershipType"          = "OrganizationOwned"
        "IsActivity"             = $false
        "HasNotes"               = $false
        "HasActivities"          = $false
        "PrimaryNameAttribute"   = "sprk_name"
        "Attributes"             = @(@{
            "@odata.type"      = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"       = "sprk_name"
            "RequiredLevel"    = @{ "Value" = "ApplicationRequired" }
            "MaxLength"        = 200
            "DisplayName"      = New-Label "Name"
            "Description"      = New-Label "Auto-generated synthetic name (job id + start timestamp)"
            "IsPrimaryName"    = $true
            "AutoNumberFormat" = "RUN-{SEQNUM:8}-{DATETIMEUTC:yyyyMMddHHmmss}"
        })
    }
    if ($r.Success) { Write-Host "  Created entity" -ForegroundColor Green }
    else { Write-Host "  FAIL: $($r.Error) :: $($r.Response)" -ForegroundColor Red; exit 1 }
}

# ---------------------------------------------------------------------------
# Step 2: Plain columns (string, memo, integer, datetime, uniqueidentifier)
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[2] Plain columns" -ForegroundColor Cyan

$plainColumns = @(
    @{
        # Note: Custom Uniqueidentifier columns are NOT creatable via the metadata Web API
        # (only the entity's auto-generated primary id, which here is sprk_backgroundjobrunid).
        # We store the run id as a Text(50) field — sufficient for a GUID string (36 chars).
        Logical = "sprk_runid"
        SchemaName = "sprk_RunId"
        Type = "StringAttributeMetadata"
        Display = "Run Id"
        Description = "Unique identifier for this job run (GUID as string; set by ScheduledJobHost)."
        Required = "ApplicationRequired"
        MaxLength = 50
    },
    @{
        Logical = "sprk_correlationid"
        SchemaName = "sprk_CorrelationId"
        Type = "StringAttributeMetadata"
        Display = "Correlation Id"
        Description = "Distributed trace correlation id; per child playbook per Q1 (R3)."
        Required = "None"
        MaxLength = 100
    },
    @{
        Logical = "sprk_startedon"
        SchemaName = "sprk_StartedOn"
        Type = "DateTimeAttributeMetadata"
        Display = "Started On"
        Description = "UTC timestamp when the job run started."
        Required = "ApplicationRequired"
    },
    @{
        Logical = "sprk_completedon"
        SchemaName = "sprk_CompletedOn"
        Type = "DateTimeAttributeMetadata"
        Display = "Completed On"
        Description = "UTC timestamp when the job run completed (null while Running)."
        Required = "None"
    },
    @{
        Logical = "sprk_errormessage"
        SchemaName = "sprk_ErrorMessage"
        Type = "MemoAttributeMetadata"
        Display = "Error Message"
        Description = "Error detail if Status = Failed."
        Required = "None"
        MaxLength = 4000
    },
    @{
        Logical = "sprk_processeditems"
        SchemaName = "sprk_ProcessedItems"
        Type = "IntegerAttributeMetadata"
        Display = "Processed Items"
        Description = "Number of items processed by the job run (optional)."
        Required = "None"
    },
    @{
        Logical = "sprk_resultjson"
        SchemaName = "sprk_ResultJson"
        Type = "MemoAttributeMetadata"
        Display = "Result JSON"
        Description = "Serialized JobRunResult.Details payload."
        Required = "None"
        MaxLength = 100000
    },
    # Extra (task 014 idempotency) — backs HasRunForScheduledTimeAsync probe
    @{
        Logical = "sprk_scheduledfireon"
        SchemaName = "sprk_ScheduledFireOn"
        Type = "DateTimeAttributeMetadata"
        Display = "Scheduled Fire On"
        Description = "Cron-computed scheduled fire timestamp; used by ScheduledJobHost idempotency probe (R3 task 014)."
        Required = "None"
    }
)

foreach ($c in $plainColumns) {
    if (Test-AttributeExists $EntityLogicalName $c.Logical) {
        Write-Host "  exists: $($c.Logical)" -ForegroundColor Yellow
        continue
    }
    $body = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.$($c.Type)"
        "SchemaName"    = $c.SchemaName
        "RequiredLevel" = @{ "Value" = $c.Required }
        "DisplayName"   = New-Label $c.Display
        "Description"   = New-Label $c.Description
    }
    switch ($c.Type) {
        "StringAttributeMetadata"           { $body.MaxLength = $c.MaxLength; $body.FormatName = @{ "Value" = "Text" } }
        "MemoAttributeMetadata"             { $body.MaxLength = $c.MaxLength }
        "IntegerAttributeMetadata"          { $body.MinValue = 0; $body.MaxValue = 2147483647 }
        "DateTimeAttributeMetadata"         { $body.Format = "DateAndTime"; $body.DateTimeBehavior = @{ "Value" = "UserLocal" } }
        "UniqueidentifierAttributeMetadata" { } # no extra props needed
    }
    $r = Invoke-DV -Ep "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Method "POST" -Body $body
    if ($r.Success) { Write-Host "  + $($c.Logical) ($($c.Type))" -ForegroundColor Green }
    else { Write-Host "  x $($c.Logical) :: $($r.Error) :: $($r.Response)" -ForegroundColor Red }
}

# ---------------------------------------------------------------------------
# Step 3: OptionSet columns (local, per-attribute)
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[3] OptionSet columns (local)" -ForegroundColor Cyan

# sprk_trigger — matches JobRunTrigger enum (Spaarke.Scheduling)
if (Test-AttributeExists $EntityLogicalName "sprk_trigger") {
    Write-Host "  exists: sprk_trigger" -ForegroundColor Yellow
} else {
    $r = Invoke-DV -Ep "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Method "POST" -Body @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"    = "sprk_Trigger"
        "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        "DisplayName"   = New-Label "Trigger"
        "Description"   = New-Label "How the run was triggered (matches JobRunTrigger enum)."
        "OptionSet"     = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
            "Name"          = "sprk_backgroundjobrun_trigger"
            "IsGlobal"      = $false
            "OptionSetType" = "Picklist"
            "Options"       = @(
                @{ "Value" = 1; "Label" = New-Label "Scheduled" },
                @{ "Value" = 2; "Label" = New-Label "ManualAdmin" },
                @{ "Value" = 3; "Label" = New-Label "OnStartup" }
            )
        }
    }
    if ($r.Success) { Write-Host "  + sprk_trigger (3 options)" -ForegroundColor Green }
    else { Write-Host "  x sprk_trigger :: $($r.Error) :: $($r.Response)" -ForegroundColor Red }
}

# sprk_status
if (Test-AttributeExists $EntityLogicalName "sprk_status") {
    Write-Host "  exists: sprk_status" -ForegroundColor Yellow
} else {
    $r = Invoke-DV -Ep "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Method "POST" -Body @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"    = "sprk_Status"
        "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        "DisplayName"   = New-Label "Status"
        "Description"   = New-Label "Run status. Aligned with sprk_backgroundjob.sprk_lastrunstatus."
        "OptionSet"     = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
            "Name"          = "sprk_backgroundjobrun_status"
            "IsGlobal"      = $false
            "OptionSetType" = "Picklist"
            "Options"       = @(
                @{ "Value" = 1; "Label" = New-Label "Running" },
                @{ "Value" = 2; "Label" = New-Label "Success" },
                @{ "Value" = 3; "Label" = New-Label "Failed" },
                @{ "Value" = 4; "Label" = New-Label "Cancelled" }
            )
        }
    }
    if ($r.Success) { Write-Host "  + sprk_status (4 options)" -ForegroundColor Green }
    else { Write-Host "  x sprk_status :: $($r.Error) :: $($r.Response)" -ForegroundColor Red }
}

# ---------------------------------------------------------------------------
# Step 4: Lookup relationship -> sprk_backgroundjob (1:N)
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[4] Lookup relationship sprk_backgroundjob (1) -> sprk_backgroundjobrun (N)" -ForegroundColor Cyan

$relationshipSchema = "sprk_backgroundjob_backgroundjobrun_backgroundjob"
$lookupLogical = "sprk_backgroundjob"
$lookupDeferredNote = $null

if (-not $parentReady) {
    $lookupDeferredNote = "DEFERRED: parent entity '$ParentEntityLogicalName' (from task 015) not found after ${maxWaitSeconds}s wait. Re-run this script after task 015 completes to create the lookup."
    Write-Host "  $lookupDeferredNote" -ForegroundColor Yellow
}
elseif (Test-AttributeExists $EntityLogicalName $lookupLogical) {
    Write-Host "  exists: lookup $lookupLogical" -ForegroundColor Yellow
}
elseif (Test-RelationshipExists $relationshipSchema) {
    Write-Host "  exists (relationship without lookup attribute discoverable): $relationshipSchema" -ForegroundColor Yellow
}
else {
    $r = Invoke-DV -Ep "RelationshipDefinitions" -Method "POST" -Body @{
        "@odata.type"            = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName"             = $relationshipSchema
        "ReferencedEntity"       = $ParentEntityLogicalName
        "ReferencingEntity"      = $EntityLogicalName
        "CascadeConfiguration"   = @{
            "Assign"   = "NoCascade"
            "Delete"   = "Restrict"   # Restrict per task brief — safer for audit (parent cannot delete if runs exist)
            "Merge"    = "NoCascade"
            "Reparent" = "NoCascade"
            "Share"    = "NoCascade"
            "Unshare"  = "NoCascade"
        }
        "Lookup" = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.LookupAttributeMetadata"
            "SchemaName"    = "sprk_BackgroundJob"
            "DisplayName"   = New-Label "Background Job"
            "Description"   = New-Label "Parent job definition (required)."
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        }
    }
    if ($r.Success) { Write-Host "  + lookup sprk_backgroundjob (Delete=Restrict, Required)" -ForegroundColor Green }
    else {
        # If creation failed because parent table doesn't exist after all, downgrade to deferred
        if ($r.Error -match "(?i)not.*found|does not exist|EntityNotFound") {
            $lookupDeferredNote = "DEFERRED: parent entity '$ParentEntityLogicalName' lookup creation failed - $($r.Error). Re-run this script after task 015 completes."
            Write-Host "  $lookupDeferredNote" -ForegroundColor Yellow
        } else {
            Write-Host "  x lookup :: $($r.Error) :: $($r.Response)" -ForegroundColor Red
        }
    }
}

# ---------------------------------------------------------------------------
# Step 5: Publish customizations for this entity
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[5] Publish customizations" -ForegroundColor Cyan
$r = Invoke-DV -Ep "PublishXml" -Method "POST" -Body @{
    "ParameterXml" = "<importexportxml><entities><entity>$EntityLogicalName</entity></entities></importexportxml>"
}
if ($r.Success) { Write-Host "  Published" -ForegroundColor Green }
else { Write-Host "  Publish failed: $($r.Error)" -ForegroundColor Red }

# ---------------------------------------------------------------------------
# Step 6: Verification
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[6] Verification" -ForegroundColor Cyan
$expectedColumns = @(
    "sprk_name", "sprk_runid", "sprk_trigger", "sprk_correlationid",
    "sprk_startedon", "sprk_completedon", "sprk_status", "sprk_errormessage",
    "sprk_processeditems", "sprk_resultjson", "sprk_scheduledfireon"
)
$expectedLookup = "sprk_backgroundjob"
$present = 0
$missing = @()
foreach ($col in $expectedColumns) {
    if (Test-AttributeExists $EntityLogicalName $col) { $present++ } else { $missing += $col }
}
$lookupPresent = Test-AttributeExists $EntityLogicalName $expectedLookup
Write-Host "  Columns present: $present / $($expectedColumns.Count)" -ForegroundColor $(if ($missing.Count -eq 0) { "Green" } else { "Yellow" })
if ($missing.Count -gt 0) { Write-Host "  Missing: $($missing -join ', ')" -ForegroundColor Yellow }
Write-Host "  Lookup '$expectedLookup' present: $lookupPresent" -ForegroundColor $(if ($lookupPresent) { "Green" } else { "Yellow" })

Write-Host ""
Write-Host "==============================================================" -ForegroundColor Cyan
Write-Host "DONE: sprk_backgroundjobrun" -ForegroundColor Green
Write-Host "  Custom data columns: $($expectedColumns.Count - 1) (excluding sprk_name primary)" -ForegroundColor Green
Write-Host "  OptionSets: sprk_backgroundjobrun_trigger (3), sprk_backgroundjobrun_status (4)" -ForegroundColor Green
Write-Host "  Lookup: $expectedLookup (Delete=Restrict, Required) - present=$lookupPresent" -ForegroundColor Green
if ($lookupDeferredNote) {
    Write-Host ""
    Write-Host "OPERATOR-TODO:" -ForegroundColor Yellow
    Write-Host "  $lookupDeferredNote" -ForegroundColor Yellow
}
Write-Host "==============================================================" -ForegroundColor Cyan
