<#
.SYNOPSIS
    Deploys the sprk_notificationoutbox entity to Dataverse.

.DESCRIPTION
    Creates the sprk_notificationoutbox entity (the notification spine's durable,
    per-user, kind-typed pending-notification outbox — Layer B) and its custom
    fields in Dataverse using the Metadata Web API. Idempotent — safe to re-run;
    checks for entity/attribute existence before creating anything.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com)

.EXAMPLE
    .\Deploy-NotificationOutboxEntity.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project: spaarke-notification-spine-r1
    Task: 011 - Create the sprk_ Kind-Typed Notification Outbox Dataverse Table
    Created: 2026-07-21
    Pattern: .claude/skills/dataverse-create-schema/SKILL.md (exemplar: Deploy-ChartDefinitionEntity.ps1)
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = $env:DATAVERSE_URL
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($EnvironmentUrl)) {
    $EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
}

# Get auth token from Azure CLI
function Get-DataverseToken {
    param([string]$EnvironmentUrl)

    Write-Host "Getting authentication token from Azure CLI..." -ForegroundColor Cyan

    $tokenResult = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get token from Azure CLI. Error: $tokenResult. Make sure you're logged in with 'az login'"
    }

    return $tokenResult.Trim()
}

# Make Web API request
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

function Test-EntityExists {
    param([string]$Token, [string]$BaseUrl, [string]$LogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$LogicalName')" -Method "GET" | Out-Null
        return $true
    }
    catch {
        if ($_.Exception.Message -match "does not exist|404|Could not find") {
            return $false
        }
        throw
    }
}

function Test-AttributeExists {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogicalName, [string]$AttributeLogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')" -Method "GET" | Out-Null
        return $true
    }
    catch {
        if ($_.Exception.Message -match "does not exist|404|Could not find") {
            return $false
        }
        throw
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

# Create the entity (user-owned; the native ownerid/owninguser field IS the
# per-user pending-row consumer key — no custom owning-user lookup needed)
function New-NotificationOutboxEntity {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "Creating sprk_notificationoutbox entity..." -ForegroundColor Yellow

    $entityDef = @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"            = "sprk_notificationoutbox"
        "DisplayName"           = New-Label -Text "Notification Outbox"
        "DisplayCollectionName" = New-Label -Text "Notification Outboxes"
        "Description"           = New-Label -Text "Durable, per-user, kind-typed pending-notification outbox (notification spine Layer B). Source of truth for FR-02/FR-06; appnotification stays an optional MDA-shell mirror."
        "OwnershipType"         = "UserOwned"
        "IsActivity"            = $false
        "HasNotes"              = $false
        "HasActivities"         = $false
        "PrimaryNameAttribute"  = "sprk_name"
        "Attributes"            = @(
            @{
                "@odata.type"      = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"       = "sprk_name"
                "RequiredLevel"    = @{ "Value" = "ApplicationRequired" }
                "MaxLength"        = 200
                "DisplayName"      = New-Label -Text "Name"
                "Description"      = New-Label -Text "System-generated identifier for the outbox row"
                "IsPrimaryName"    = $true
                "AutoNumberFormat" = "OUTBOX-{SEQNUM:6}"
            }
        )
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef | Out-Null

    Write-Host "  Entity created successfully" -ForegroundColor Green
}

function Add-EntityAttribute {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogicalName, [object]$AttributeDef)

    $schemaName = $AttributeDef.SchemaName
    Write-Host "  Adding attribute: $schemaName..." -ForegroundColor Gray

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Method "POST" -Body $AttributeDef | Out-Null

    Write-Host "    Added: $schemaName" -ForegroundColor Green
}

function Add-AttributeIfMissing {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogicalName, [object]$AttributeDef)

    $logicalName = $AttributeDef.SchemaName.ToLowerInvariant()
    if (Test-AttributeExists -Token $Token -BaseUrl $BaseUrl -EntityLogicalName $EntityLogicalName -AttributeLogicalName $logicalName) {
        Write-Host "  $logicalName already exists, skipping..." -ForegroundColor Yellow
    }
    else {
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName $EntityLogicalName -AttributeDef $AttributeDef
    }
}

function Main {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Deploy sprk_notificationoutbox Entity" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
    Write-Host ""

    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful" -ForegroundColor Green
    Write-Host ""

    Write-Host "Step 1: Checking if entity exists..." -ForegroundColor Cyan
    $entityExisted = Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_notificationoutbox"
    if ($entityExisted) {
        Write-Host "  Entity sprk_notificationoutbox already exists!" -ForegroundColor Yellow
        Write-Host "  Skipping entity creation. Will verify/add fields (idempotent)." -ForegroundColor Yellow
    }
    else {
        Write-Host ""
        Write-Host "Step 2: Creating entity..." -ForegroundColor Cyan
        New-NotificationOutboxEntity -Token $token -BaseUrl $EnvironmentUrl
    }

    Write-Host ""
    Write-Host "Step 3: Adding/verifying attributes (idempotent)..." -ForegroundColor Cyan

    # sprk_kind — the kind discriminator (TEXT, matches task 013's C# closed-set
    # string taxonomy verbatim; no parallel Choice-int mapping to drift).
    Add-AttributeIfMissing -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_notificationoutbox" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_kind"
        "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        "MaxLength"     = 50
        "DisplayName"   = (New-Label -Text "Kind")
        "Description"   = (New-Label -Text "Kind discriminator — kebab-case, matches the task-013 closed taxonomy (suggestion, communication-assessed, communication-arrived; reserved: job-complete, share, system-alert)")
    }

    # sprk_envelope — the typed envelope JSON payload (task 013 contract: IDs +
    # minimal display metadata only, never message bodies/privileged content/action tokens).
    Add-AttributeIfMissing -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_notificationoutbox" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = "sprk_envelope"
        "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        "MaxLength"     = 4000
        "DisplayName"   = (New-Label -Text "Envelope")
        "Description"   = (New-Label -Text "Typed envelope JSON payload (task-013 contract) — IDs and minimal display metadata only")
    }

    # sprk_regardingrecordid — ADR-024 MINIMAL pattern (text GUID, no lookup —
    # no subgrid-filtering requirement on this table).
    Add-AttributeIfMissing -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_notificationoutbox" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_regardingrecordid"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 50
        "DisplayName"   = (New-Label -Text "Regarding Record Id")
        "Description"   = (New-Label -Text "ADR-024 MINIMAL pattern — GUID of the regarding record (envelope's regardingRecordId)")
    }

    # sprk_regardingrecordtype — ADR-024 MINIMAL pattern record-type discriminator
    # (plain text entity logical name; not a lookup to sprk_recordtype_ref).
    Add-AttributeIfMissing -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_notificationoutbox" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_regardingrecordtype"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 100
        "DisplayName"   = (New-Label -Text "Regarding Record Type")
        "Description"   = (New-Label -Text "ADR-024 MINIMAL pattern — regarding record's entity logical name (e.g. sprk_communication, sprk_matter)")
    }

    # sprk_delivered — nullable; set when Layer C (SignalR) push succeeds.
    Add-AttributeIfMissing -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_notificationoutbox" -AttributeDef @{
        "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "SchemaName"       = "sprk_delivered"
        "RequiredLevel"    = @{ "Value" = "None" }
        "Format"           = "DateAndTime"
        "DateTimeBehavior" = @{ "Value" = "UserLocal" }
        "DisplayName"      = (New-Label -Text "Delivered")
        "Description"      = (New-Label -Text "Timestamp the row was pushed via Layer C (SignalR); null = not yet delivered live")
    }

    # sprk_dismissed — nullable; set when the user acknowledges/dismisses the item.
    Add-AttributeIfMissing -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_notificationoutbox" -AttributeDef @{
        "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "SchemaName"       = "sprk_dismissed"
        "RequiredLevel"    = @{ "Value" = "None" }
        "Format"           = "DateAndTime"
        "DateTimeBehavior" = @{ "Value" = "UserLocal" }
        "DisplayName"      = (New-Label -Text "Dismissed")
        "Description"      = (New-Label -Text "Timestamp the user dismissed/acknowledged the pending item; null = still pending")
    }

    # sprk_expiresat — schema-optional (task-012 service invariant populates it;
    # see notes/011-outbox-table-schema-proposal.md decision #4).
    Add-AttributeIfMissing -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_notificationoutbox" -AttributeDef @{
        "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "SchemaName"       = "sprk_expiresat"
        "RequiredLevel"    = @{ "Value" = "None" }
        "Format"           = "DateAndTime"
        "DateTimeBehavior" = @{ "Value" = "UserLocal" }
        "DisplayName"      = (New-Label -Text "Expires At")
        "Description"      = (New-Label -Text "Expiry timestamp for the pending row (expiry sweep boundary)")
    }

    Write-Host ""
    Write-Host "Step 4: Publishing customizations..." -ForegroundColor Cyan

    $publishXml = @{
        "ParameterXml" = "<importexportxml><entities><entity>sprk_notificationoutbox</entity></entities></importexportxml>"
    }

    try {
        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "PublishXml" -Method "POST" -Body $publishXml | Out-Null
        Write-Host "  Customizations published" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Publish may have timed out, but entity should be available" -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "Step 5: Verifying entity..." -ForegroundColor Cyan

    if (Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_notificationoutbox") {
        Write-Host "  Entity sprk_notificationoutbox exists and is accessible" -ForegroundColor Green
    }
    else {
        Write-Host "  Warning: Entity verification failed" -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "Step 6: Testing Web API query..." -ForegroundColor Cyan

    try {
        $result = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "sprk_notificationoutboxes" -Method "GET"
        Write-Host "  Web API query successful!" -ForegroundColor Green
        Write-Host "  Current record count: $($result.value.Count)" -ForegroundColor Gray
    }
    catch {
        Write-Host "  Warning: Web API query test failed - entity may need publishing" -ForegroundColor Yellow
        Write-Host "  Error: $_" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host " Deployment Complete!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
}

Main
