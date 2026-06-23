<#
.SYNOPSIS
    Deploys the sprk_backgroundjob Dataverse entity (R3 Part 2 — background-job framework).

.DESCRIPTION
    Creates the sprk_backgroundjob entity (job DEFINITION table) for the Spaarke.Scheduling
    background-job framework introduced in R3 Part 2. This entity holds the catalog of
    scheduled jobs that ScheduledJobHost (BackgroundService) reads on startup and refreshes
    hourly. Per-run audit history is kept in the sibling sprk_backgroundjobrun entity
    (created by task 016).

    NAMING-COLLISION NOTE (per spec.md FR-2.4 + design.md alternatives table):
      sprk_processingjob already exists and is scoped to Office document operations
      (DocumentSave / EmailSave / ShareLinks / QuickCreate / ProfileSummary / Indexing /
      DeepAnalysis). DO NOT extend that entity. sprk_backgroundjob is a parallel,
      distinct entity for the scheduled-job framework (junction recon, playbook scheduler,
      cache warming, etc.). Both coexist.

    Schema fields (11 columns + primary key + standard audit fields):
      - sprk_jobid                : Text(100)   REQUIRED, UNIQUE KEY — stable string id (e.g., "membership-reconciliation")
      - sprk_displayname          : Text(200)   PRIMARY NAME — human-readable
      - sprk_description          : Memo(2000)  what the job does
      - sprk_handlertype          : Text(500)   fully-qualified C# class name; resolved at startup
      - sprk_enabled              : Boolean     default true — master enable/disable
      - sprk_cronschedule         : Text(100)   standard cron (e.g., "0 2 * * *")
      - sprk_configjson           : Memo(100000) handler-specific config
      - sprk_lastrunstartedon     : DateTime    (denormalized from latest run)
      - sprk_lastruncompletedon   : DateTime    (denormalized from latest run)
      - sprk_lastrunstatus        : Picklist    Success=1 / Failed=2 / Running=3 / Cancelled=4
      - sprk_lastrunerror         : Memo(2000)  last error message
      Standard audit fields (createdon/createdby/modifiedon/modifiedby/ownerid/statecode/
        statuscode/versionnumber) are auto-added by Dataverse — no explicit declaration.

    Alternate key (unique constraint) on sprk_jobid:
      - Key SchemaName: sprk_jobid_key
      - Key attributes: sprk_jobid

    The script is IDEMPOTENT — safe to re-run:
      - If the entity does not exist, creates it with sprk_jobid + sprk_displayname (primary)
      - If the entity exists, adds only the missing attributes
      - If the alternate key does not exist, creates it (after sprk_jobid is present)
      - Publishes customizations at the end

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (default: spaarkedev1).

.PARAMETER DryRun
    Preview-only mode — checks what would be created without modifying Dataverse.

.EXAMPLE
    # Preview without modifying (recommended first run)
    .\Create-BackgroundJobEntity.ps1 -DryRun

.EXAMPLE
    # Deploy to spaarkedev1 (idempotent — safe to re-run)
    .\Create-BackgroundJobEntity.ps1

.EXAMPLE
    # Deploy to a different environment
    .\Create-BackgroundJobEntity.ps1 -EnvironmentUrl "https://spaarketest.crm.dynamics.com"

.NOTES
    Project: spaarke-platform-foundations-r3 (Part 2 — Background-job infrastructure)
    Task:    015 (R3-015) — Create sprk_backgroundjob Dataverse Entity
    Spec FR: FR-2.4
    Blocks:  016 (sprk_backgroundjobrun lookup target), 017 (ADR-036), 020/021/022 (admin endpoints),
             023 (PlaybookScheduler migration)
    Created: 2026-06-21
    Pattern source: scripts/Create-AiPersonaEntity.ps1 (R6 canonical exemplar)
    ADR Compliance:
      - ADR-027 (unmanaged solution; sprk_ prefix)
      - ADR-029 (BFF size N/A — 0 MB delta; no BFF code change in this task)
      - ADR-002 (late-bound; no early-bound code generation needed downstream)
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = ($env:DATAVERSE_URL ?? "https://spaarkedev1.crm.dynamics.com"),

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# -----------------------------------------------------------------------------
# Authentication
# -----------------------------------------------------------------------------

function Get-DataverseToken {
    param([string]$EnvironmentUrl)

    Write-Host "Getting authentication token from Azure CLI..." -ForegroundColor Cyan
    $tokenResult = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get token from Azure CLI. Error: $tokenResult. Run 'az login' first."
    }

    return $tokenResult.Trim()
}

# -----------------------------------------------------------------------------
# Web API Helpers
# -----------------------------------------------------------------------------

function Invoke-DataverseApi {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )

    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
        "Prefer"           = "odata.include-annotations=*"
    }

    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"

    $params = @{
        Uri     = $uri
        Method  = $Method
        Headers = $headers
    }

    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20)
    }

    try {
        return Invoke-RestMethod @params
    }
    catch {
        $errorDetails = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errorJson.error.message) {
                $errorDetails = $errorJson.error.message
            }
        }
        throw "API Error ($Method $Endpoint): $errorDetails"
    }
}

function New-Label {
    param([string]$Text)
    return @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type"  = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label"        = $Text
                "LanguageCode" = 1033
            }
        )
    }
}

# -----------------------------------------------------------------------------
# Idempotency Checks
# -----------------------------------------------------------------------------

function Test-EntityExists {
    param([string]$Token, [string]$BaseUrl, [string]$LogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$LogicalName')" -Method "GET" | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Test-AttributeExists {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$EntityLogicalName,
        [string]$AttributeLogicalName
    )
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')" `
            -Method "GET" | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Test-EntityKeyExists {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$EntityLogicalName,
        [string]$KeySchemaName
    )
    try {
        $result = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Keys?`$filter=SchemaName eq '$KeySchemaName'" `
            -Method "GET"
        return ($result.value.Count -gt 0)
    }
    catch {
        return $false
    }
}

# -----------------------------------------------------------------------------
# Entity + Attribute Creation
# -----------------------------------------------------------------------------

function New-BackgroundJobEntity {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "Creating sprk_backgroundjob entity..." -ForegroundColor Yellow

    $entityDef = @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"            = "sprk_backgroundjob"
        "DisplayName"           = New-Label -Text "Background Job"
        "DisplayCollectionName" = New-Label -Text "Background Jobs"
        "Description"           = New-Label -Text "Job definition for the Spaarke.Scheduling background-job framework (R3 Part 2). Read by ScheduledJobHost on startup + hourly refresh. Per-run audit history lives in sprk_backgroundjobrun. Distinct from sprk_processingjob, which is Office-scoped — do NOT overload that entity."
        "OwnershipType"         = "OrganizationOwned"
        "IsActivity"            = $false
        "HasNotes"              = $false
        "HasActivities"         = $false
        "PrimaryNameAttribute"  = "sprk_displayname"
        "Attributes"            = @(
            @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"    = "sprk_displayname"
                "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
                "MaxLength"     = 200
                "DisplayName"   = New-Label -Text "Display Name"
                "Description"   = New-Label -Text "Human-readable job name (primary name field). E.g., 'Membership Reconciliation'."
                "IsPrimaryName" = $true
            }
        )
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef

    Write-Host "  Entity created successfully" -ForegroundColor Green
}

function Add-EntityAttribute {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$EntityLogicalName,
        [object]$AttributeDef
    )

    $schemaName = $AttributeDef.SchemaName
    Write-Host "  Adding attribute: $schemaName..." -ForegroundColor Gray

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" `
        -Method "POST" -Body $AttributeDef

    Write-Host "    Added: $schemaName" -ForegroundColor Green
}

function Add-JobIdAlternateKey {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "  Adding alternate key: sprk_jobid_key (sprk_jobid unique)..." -ForegroundColor Gray

    $keyDef = @{
        "@odata.type"      = "Microsoft.Dynamics.CRM.EntityKeyMetadata"
        "SchemaName"       = "sprk_jobid_key"
        "DisplayName"      = New-Label -Text "Job Id Key"
        "KeyAttributes"    = @("sprk_jobid")
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='sprk_backgroundjob')/Keys" `
        -Method "POST" -Body $keyDef

    Write-Host "    Created: sprk_jobid_key" -ForegroundColor Green
}

function Publish-BackgroundJobCustomizations {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "Publishing customizations..." -ForegroundColor Cyan

    $publishXml = @{
        "ParameterXml" = "<importexportxml><entities><entity>sprk_backgroundjob</entity></entities></importexportxml>"
    }

    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "PublishXml" -Method "POST" -Body $publishXml
        Write-Host "  Customizations published" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Publish may have timed out, but entity should be available shortly" -ForegroundColor Yellow
    }
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------

function Main {
    Write-Host ""
    Write-Host "===========================================" -ForegroundColor Cyan
    Write-Host " Deploy sprk_backgroundjob Entity (R3-015)" -ForegroundColor Cyan
    Write-Host "===========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
    if ($DryRun) {
        Write-Host "MODE: DRY RUN (no Dataverse modifications)" -ForegroundColor Yellow
    }
    Write-Host ""

    # ---- Step 0: Get auth token ----
    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful" -ForegroundColor Green
    Write-Host ""

    # ---- Step 1: Check if entity exists ----
    Write-Host "Step 1: Checking if entity exists..." -ForegroundColor Cyan
    $entityExists = Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_backgroundjob"

    if ($entityExists) {
        Write-Host "  sprk_backgroundjob already exists — will verify/add missing fields only" -ForegroundColor Yellow
    }
    else {
        Write-Host "  sprk_backgroundjob does NOT exist — will create" -ForegroundColor Gray
        if ($DryRun) {
            Write-Host "  [DRY RUN] Would create entity sprk_backgroundjob with primary attribute sprk_displayname" -ForegroundColor Yellow
        }
        else {
            New-BackgroundJobEntity -Token $token -BaseUrl $EnvironmentUrl
        }
    }

    # ---- Step 2: Add attributes (idempotent) ----
    Write-Host ""
    Write-Host "Step 2: Adding/verifying attributes..." -ForegroundColor Cyan

    # Define all non-primary attributes
    $attributes = @(
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_jobid"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "MaxLength"     = 100
            "DisplayName"   = New-Label -Text "Job Id"
            "Description"   = New-Label -Text "Stable string identifier for the job (e.g., 'membership-reconciliation'). Unique key — used by ScheduledJobHost + admin endpoints to address jobs without GUIDs."
        },
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
            "SchemaName"    = "sprk_description"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 2000
            "DisplayName"   = New-Label -Text "Description"
            "Description"   = New-Label -Text "Human-readable description of what the job does. Surfaced in the admin endpoints + maker portal."
        },
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_handlertype"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 500
            "DisplayName"   = New-Label -Text "Handler Type"
            "Description"   = New-Label -Text "Fully-qualified C# class name (e.g., 'Spaarke.Scheduling.MembershipReconciliationJob'). Resolved at ScheduledJobHost startup via reflection + DI."
        },
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
            "SchemaName"    = "sprk_enabled"
            "RequiredLevel" = @{ "Value" = "None" }
            "DisplayName"   = New-Label -Text "Enabled"
            "Description"   = New-Label -Text "Master enable/disable. When false, ScheduledJobHost does not start a timer for this job. Toggleable at runtime via POST /api/admin/jobs/{jobId}/enable|disable."
            "DefaultValue"  = $true
            "OptionSet"     = @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.BooleanOptionSetMetadata"
                "OptionSetType" = "Boolean"
                "TrueOption"    = @{
                    "Value" = 1
                    "Label" = New-Label -Text "Yes"
                }
                "FalseOption"   = @{
                    "Value" = 0
                    "Label" = New-Label -Text "No"
                }
            }
        },
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_cronschedule"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 100
            "DisplayName"   = New-Label -Text "Cron Schedule"
            "Description"   = New-Label -Text "Standard 5-field cron expression (e.g., '0 2 * * *' for nightly 02:00). Parsed by the Cronos NuGet at startup. Empty/null means the job only runs via manual trigger."
        },
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
            "SchemaName"    = "sprk_configjson"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 100000
            "DisplayName"   = New-Label -Text "Config JSON"
            "Description"   = New-Label -Text "Handler-specific configuration JSON. Schema is owned by the handler (IScheduledJob). E.g., MembershipReconciliationJob may consume batch size + entity scope."
        },
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
            "SchemaName"    = "sprk_lastrunstartedon"
            "RequiredLevel" = @{ "Value" = "None" }
            "Format"        = "DateAndTime"
            "DateTimeBehavior" = @{ "Value" = "UserLocal" }
            "DisplayName"   = New-Label -Text "Last Run Started On"
            "Description"   = New-Label -Text "Denormalized from the most recent sprk_backgroundjobrun.sprk_startedon. Updated by ScheduledJobHost when a run starts. Lets list views show last-run time without joining."
        },
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
            "SchemaName"    = "sprk_lastruncompletedon"
            "RequiredLevel" = @{ "Value" = "None" }
            "Format"        = "DateAndTime"
            "DateTimeBehavior" = @{ "Value" = "UserLocal" }
            "DisplayName"   = New-Label -Text "Last Run Completed On"
            "Description"   = New-Label -Text "Denormalized from the most recent sprk_backgroundjobrun.sprk_completedon. Updated by ScheduledJobHost when a run finishes."
        },
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
            "SchemaName"    = "sprk_lastrunstatus"
            "RequiredLevel" = @{ "Value" = "None" }
            "DisplayName"   = New-Label -Text "Last Run Status"
            "Description"   = New-Label -Text "Status of the most recent run (denormalized from sprk_backgroundjobrun). Success=1, Failed=2, Running=3, Cancelled=4."
            "OptionSet"     = @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
                "OptionSetType" = "Picklist"
                "IsGlobal"      = $false
                "Options"       = @(
                    @{
                        "Value" = 1
                        "Label" = New-Label -Text "Success"
                    },
                    @{
                        "Value" = 2
                        "Label" = New-Label -Text "Failed"
                    },
                    @{
                        "Value" = 3
                        "Label" = New-Label -Text "Running"
                    },
                    @{
                        "Value" = 4
                        "Label" = New-Label -Text "Cancelled"
                    }
                )
            }
        },
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
            "SchemaName"    = "sprk_lastrunerror"
            "RequiredLevel" = @{ "Value" = "None" }
            "MaxLength"     = 2000
            "DisplayName"   = New-Label -Text "Last Run Error"
            "Description"   = New-Label -Text "Truncated error message from the most recent failed run (full detail in sprk_backgroundjobrun.sprk_errormessage). Cleared on next successful run."
        }
    )

    foreach ($attr in $attributes) {
        $name = $attr.SchemaName
        if (-not $entityExists) {
            # Entity is newly created; need to add this attribute
            if ($DryRun) {
                Write-Host "  [DRY RUN] Would add attribute: $name" -ForegroundColor Yellow
            }
            else {
                Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl `
                    -EntityLogicalName "sprk_backgroundjob" -AttributeDef $attr
            }
        }
        else {
            $attrExists = Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl `
                -EntityLogicalName "sprk_backgroundjob" -AttributeLogicalName $name
            if ($attrExists) {
                Write-Host "  $name already exists, skipping" -ForegroundColor Gray
            }
            else {
                if ($DryRun) {
                    Write-Host "  [DRY RUN] Would add attribute: $name" -ForegroundColor Yellow
                }
                else {
                    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl `
                        -EntityLogicalName "sprk_backgroundjob" -AttributeDef $attr
                }
            }
        }
    }

    # ---- Step 3: Add alternate key on sprk_jobid (idempotent) ----
    Write-Host ""
    Write-Host "Step 3: Adding/verifying alternate key on sprk_jobid..." -ForegroundColor Cyan

    if ($DryRun) {
        Write-Host "  [DRY RUN] Would check + create alternate key sprk_jobid_key (sprk_jobid unique)" -ForegroundColor Yellow
    }
    else {
        $keyExists = Test-EntityKeyExists -Token $token -BaseUrl $EnvironmentUrl `
            -EntityLogicalName "sprk_backgroundjob" -KeySchemaName "sprk_jobid_key"
        if ($keyExists) {
            Write-Host "  sprk_jobid_key already exists, skipping" -ForegroundColor Gray
        }
        else {
            # Verify sprk_jobid is present before attempting to add the key
            $jobIdAttrExists = Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl `
                -EntityLogicalName "sprk_backgroundjob" -AttributeLogicalName "sprk_jobid"
            if (-not $jobIdAttrExists) {
                throw "Cannot create sprk_jobid_key — sprk_jobid attribute is missing. Re-run after attribute creation completes."
            }
            Add-JobIdAlternateKey -Token $token -BaseUrl $EnvironmentUrl
        }
    }

    # ---- Step 4: Publish customizations ----
    Write-Host ""
    Write-Host "Step 4: Publishing customizations..." -ForegroundColor Cyan
    if ($DryRun) {
        Write-Host "  [DRY RUN] Would publish sprk_backgroundjob customizations" -ForegroundColor Yellow
    }
    else {
        Publish-BackgroundJobCustomizations -Token $token -BaseUrl $EnvironmentUrl
    }

    # ---- Step 5: Verify entity + count attributes ----
    Write-Host ""
    Write-Host "Step 5: Verifying entity + attributes..." -ForegroundColor Cyan

    if ($DryRun) {
        Write-Host "  [DRY RUN] Skipping verification" -ForegroundColor Yellow
    }
    else {
        if (Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_backgroundjob") {
            Write-Host "  sprk_backgroundjob exists and is accessible" -ForegroundColor Green

            # Verify each of the 11 spec'd columns is present
            $expectedColumns = @(
                "sprk_jobid",
                "sprk_displayname",
                "sprk_description",
                "sprk_handlertype",
                "sprk_enabled",
                "sprk_cronschedule",
                "sprk_configjson",
                "sprk_lastrunstartedon",
                "sprk_lastruncompletedon",
                "sprk_lastrunstatus",
                "sprk_lastrunerror"
            )
            $presentCount = 0
            $missingCols = @()
            foreach ($col in $expectedColumns) {
                if (Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl `
                        -EntityLogicalName "sprk_backgroundjob" -AttributeLogicalName $col) {
                    $presentCount++
                }
                else {
                    $missingCols += $col
                }
            }
            Write-Host "  Columns present: $presentCount / $($expectedColumns.Count)" -ForegroundColor Green
            if ($missingCols.Count -gt 0) {
                Write-Host "  MISSING: $($missingCols -join ', ')" -ForegroundColor Red
            }

            # Verify alternate key
            if (Test-EntityKeyExists -Token $token -BaseUrl $EnvironmentUrl `
                    -EntityLogicalName "sprk_backgroundjob" -KeySchemaName "sprk_jobid_key") {
                Write-Host "  Alternate key sprk_jobid_key: PRESENT" -ForegroundColor Green
            }
            else {
                Write-Host "  Alternate key sprk_jobid_key: MISSING" -ForegroundColor Red
            }
        }
        else {
            Write-Host "  Warning: Entity verification failed" -ForegroundColor Yellow
        }

        # Smoke-test the Web API collection endpoint
        Write-Host ""
        Write-Host "Step 6: Smoke-testing Web API collection endpoint..." -ForegroundColor Cyan
        try {
            $result = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
                -Endpoint "sprk_backgroundjobs?`$top=1" -Method "GET"
            Write-Host "  Web API query successful — collection endpoint reachable" -ForegroundColor Green
            Write-Host "  Current record count: $($result.value.Count)" -ForegroundColor Gray
        }
        catch {
            Write-Host "  Warning: Web API query failed (entity may still be publishing)" -ForegroundColor Yellow
            Write-Host "  Error: $_" -ForegroundColor Gray
        }
    }

    # ---- Done ----
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host " Deployment Complete!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Verify in Power Apps maker portal: $EnvironmentUrl" -ForegroundColor Gray
    Write-Host "  2. Task 016: Create sprk_backgroundjobrun entity (1:N from this entity)" -ForegroundColor Gray
    Write-Host "  3. Task 017: Author ADR-036 — Background-job infrastructure" -ForegroundColor Gray
    Write-Host "  4. Task 020/021/022: Admin endpoints (/api/admin/jobs/*)" -ForegroundColor Gray
    Write-Host "  5. Task 023: Migrate PlaybookSchedulerService onto Spaarke.Scheduling" -ForegroundColor Gray
    Write-Host ""
}

Main
