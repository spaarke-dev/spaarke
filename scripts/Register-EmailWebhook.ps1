<#
.SYNOPSIS
    Registers a Dataverse Service Endpoint and Webhook Step for email-to-document automation.

.DESCRIPTION
    This script creates:
    1. A Service Endpoint (WebHook type) pointing to the BFF API webhook endpoint
    2. A Step Registration on the 'email' entity for Create message

    The webhook triggers when new emails are created in Dataverse via Server-Side Sync,
    enabling automatic email-to-document conversion.

.PARAMETER DataverseUrl
    The Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com)

.PARAMETER WebhookUrl
    The BFF API webhook endpoint URL (e.g., https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/emails/webhook-trigger)

.PARAMETER WebhookSecret
    The shared secret for webhook signature validation (HMAC-SHA256)

.PARAMETER ServiceEndpointName
    Name for the Service Endpoint registration (default: "Email-to-Document Webhook")

.EXAMPLE
    .\Register-EmailWebhook.ps1 `
        -DataverseUrl "https://spaarkedev1.crm.dynamics.com" `
        -WebhookUrl "https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/emails/webhook-trigger" `
        -WebhookSecret "your-secret-here"

.NOTES
    Prerequisites:
    - Install Microsoft.Xrm.Tooling.CrmConnector.PowerShell module
    - User must have System Administrator or System Customizer role in Dataverse

    The webhook endpoint must be publicly accessible and support HTTPS.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DataverseUrl,

    [Parameter(Mandatory = $true)]
    [string]$WebhookUrl,

    [Parameter(Mandatory = $true)]
    [string]$WebhookSecret,

    [Parameter(Mandatory = $false)]
    [string]$ServiceEndpointName = "Email-to-Document Webhook",

    [Parameter(Mandatory = $false)]
    [switch]$Force
)

# Import required module
$moduleName = "Microsoft.Xrm.Tooling.CrmConnector.PowerShell"
if (-not (Get-Module -ListAvailable -Name $moduleName)) {
    Write-Host "Installing $moduleName module..." -ForegroundColor Yellow
    Install-Module -Name $moduleName -Scope CurrentUser -Force
}
Import-Module $moduleName

# ============================================================================
# Connect to Dataverse
# ============================================================================
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Connecting to Dataverse..." -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

$conn = Get-CrmConnection -InteractiveMode -ServerUrl $DataverseUrl

if (-not $conn.IsReady) {
    Write-Error "Failed to connect to Dataverse. Please check your credentials and try again."
    exit 1
}

Write-Host "Connected to: $($conn.ConnectedOrgFriendlyName)" -ForegroundColor Green

# ============================================================================
# Check for existing Service Endpoint
# ============================================================================
Write-Host "`nChecking for existing Service Endpoint..." -ForegroundColor Yellow

$existingEndpoint = Get-CrmRecords -conn $conn `
    -EntityLogicalName "serviceendpoint" `
    -FilterAttribute "name" `
    -FilterOperator "eq" `
    -FilterValue $ServiceEndpointName `
    -Fields "serviceendpointid", "name"

if ($existingEndpoint.Count -gt 0) {
    if ($Force) {
        Write-Host "Existing Service Endpoint found. Removing due to -Force flag..." -ForegroundColor Yellow
        $existingId = $existingEndpoint.CrmRecords[0].serviceendpointid

        # First remove any steps associated with this endpoint
        $existingSteps = Get-CrmRecords -conn $conn `
            -EntityLogicalName "sdkmessageprocessingstep" `
            -FilterAttribute "eventhandler" `
            -FilterOperator "eq" `
            -FilterValue $existingId `
            -Fields "sdkmessageprocessingstepid", "name"

        foreach ($step in $existingSteps.CrmRecords) {
            Write-Host "  Removing step: $($step.name)" -ForegroundColor Yellow
            Remove-CrmRecord -conn $conn -EntityLogicalName "sdkmessageprocessingstep" -Id $step.sdkmessageprocessingstepid
        }

        # Now remove the endpoint
        Remove-CrmRecord -conn $conn -EntityLogicalName "serviceendpoint" -Id $existingId
        Write-Host "Removed existing Service Endpoint" -ForegroundColor Green
    }
    else {
        Write-Host "Service Endpoint '$ServiceEndpointName' already exists." -ForegroundColor Yellow
        Write-Host "Use -Force to recreate, or use a different name." -ForegroundColor Yellow

        $existingId = $existingEndpoint.CrmRecords[0].serviceendpointid
        Write-Host "`nExisting Service Endpoint ID: $existingId" -ForegroundColor Cyan

        # Check for existing steps
        $existingSteps = Get-CrmRecords -conn $conn `
            -EntityLogicalName "sdkmessageprocessingstep" `
            -FilterAttribute "eventhandler" `
            -FilterOperator "eq" `
            -FilterValue $existingId `
            -Fields "sdkmessageprocessingstepid", "name", "stage", "mode"

        if ($existingSteps.Count -gt 0) {
            Write-Host "`nExisting Webhook Steps:" -ForegroundColor Cyan
            foreach ($step in $existingSteps.CrmRecords) {
                Write-Host "  - $($step.name) (ID: $($step.sdkmessageprocessingstepid))" -ForegroundColor Gray
            }
        }

        exit 0
    }
}

# ============================================================================
# Create Service Endpoint
# ============================================================================
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Creating Service Endpoint..." -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Service Endpoint contract types:
# 1 = Queue (Azure Service Bus)
# 2 = Topic (Azure Service Bus)
# 3 = Event Hub
# 4 = Webhook
# 5 = Azure Queue Storage

# Authentication types for webhooks:
# 1 = No Auth
# 2 = QueryString
# 3 = HttpHeader
# 4 = WebKey

$serviceEndpointData = @{
    "name" = $ServiceEndpointName
    "description" = "Webhook endpoint for email-to-document automation. Triggered when new emails are created via Server-Side Sync."
    "url" = $WebhookUrl
    "contract" = (New-CrmOptionSetValue -Value 4)  # 4 = Webhook
    "authtype" = (New-CrmOptionSetValue -Value 4)  # 4 = WebKey (uses shared secret)
    "authvalue" = $WebhookSecret
    "messageformat" = (New-CrmOptionSetValue -Value 1)  # 1 = JSON
    "usekeyvaultconfiguration" = $false
}

try {
    $endpointId = New-CrmRecord -conn $conn -EntityLogicalName "serviceendpoint" -Fields $serviceEndpointData
    Write-Host "Created Service Endpoint: $ServiceEndpointName" -ForegroundColor Green
    Write-Host "  ID: $endpointId" -ForegroundColor Gray
    Write-Host "  URL: $WebhookUrl" -ForegroundColor Gray
}
catch {
    Write-Error "Failed to create Service Endpoint: $_"
    exit 1
}

# ============================================================================
# Get SDK Message and Filter IDs
# ============================================================================
Write-Host "`nLooking up SDK Message 'Create' for 'email' entity..." -ForegroundColor Yellow

# Get the 'Create' message
$createMessage = Get-CrmRecords -conn $conn `
    -EntityLogicalName "sdkmessage" `
    -FilterAttribute "name" `
    -FilterOperator "eq" `
    -FilterValue "Create" `
    -Fields "sdkmessageid", "name"

if ($createMessage.Count -eq 0) {
    Write-Error "Could not find SDK Message 'Create'"
    exit 1
}

$messageId = $createMessage.CrmRecords[0].sdkmessageid
Write-Host "  SDK Message ID: $messageId" -ForegroundColor Gray

# Get the message filter for 'email' entity with 'Create' message
$messageFilter = Get-CrmRecords -conn $conn `
    -EntityLogicalName "sdkmessagefilter" `
    -FilterAttribute "primaryobjecttypecode" `
    -FilterOperator "eq" `
    -FilterValue "email" `
    -Fields "sdkmessagefilterid", "sdkmessageid", "primaryobjecttypecode"

# Find the filter that matches our Create message
$createFilter = $messageFilter.CrmRecords | Where-Object {
    $_.sdkmessageid_Property.Value.Id -eq $messageId
} | Select-Object -First 1

if (-not $createFilter) {
    Write-Error "Could not find SDK Message Filter for 'Create' on 'email' entity"
    exit 1
}

$filterId = $createFilter.sdkmessagefilterid
Write-Host "  SDK Message Filter ID: $filterId" -ForegroundColor Gray

# ============================================================================
# Create SDK Message Processing Step (Webhook Step)
# ============================================================================
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Creating Webhook Step..." -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Step modes:
# 0 = Synchronous
# 1 = Asynchronous

# Step stages:
# 10 = Pre-validation
# 20 = Pre-operation
# 40 = Post-operation

# Supported deployment:
# 0 = Server only
# 1 = Offline only
# 2 = Both

$stepData = @{
    "name" = "Email-to-Document: Email Create"
    "description" = "Triggers email-to-document conversion when a new email is created in Dataverse"
    "mode" = (New-CrmOptionSetValue -Value 1)  # 1 = Asynchronous
    "stage" = (New-CrmOptionSetValue -Value 40)  # 40 = Post-operation
    "supporteddeployment" = (New-CrmOptionSetValue -Value 0)  # 0 = Server only
    "rank" = 1
    "asyncautodelete" = $true  # Clean up completed async jobs
    "sdkmessageid" = (New-CrmEntityReference -EntityLogicalName "sdkmessage" -Id $messageId)
    "sdkmessagefilterid" = (New-CrmEntityReference -EntityLogicalName "sdkmessagefilter" -Id $filterId)
    "eventhandler" = (New-CrmEntityReference -EntityLogicalName "serviceendpoint" -Id $endpointId)
    "filteringattributes" = ""  # Empty = trigger on any attribute change
    "statecode" = (New-CrmOptionSetValue -Value 0)  # 0 = Enabled
    "statuscode" = (New-CrmOptionSetValue -Value 1)  # 1 = Enabled
}

try {
    $stepId = New-CrmRecord -conn $conn -EntityLogicalName "sdkmessageprocessingstep" -Fields $stepData
    Write-Host "Created Webhook Step: Email-to-Document: Email Create" -ForegroundColor Green
    Write-Host "  ID: $stepId" -ForegroundColor Gray
    Write-Host "  Trigger: Email entity, Create message, Post-operation (async)" -ForegroundColor Gray
}
catch {
    Write-Error "Failed to create Webhook Step: $_"

    # Clean up the Service Endpoint if step creation failed
    Write-Host "Cleaning up Service Endpoint due to error..." -ForegroundColor Yellow
    Remove-CrmRecord -conn $conn -EntityLogicalName "serviceendpoint" -Id $endpointId

    exit 1
}

# ============================================================================
# Summary
# ============================================================================
Write-Host "`n========================================" -ForegroundColor Green
Write-Host "REGISTRATION COMPLETE" -ForegroundColor Green
Write-Host "========================================`n" -ForegroundColor Green

Write-Host "Service Endpoint:" -ForegroundColor Cyan
Write-Host "  Name: $ServiceEndpointName"
Write-Host "  ID: $endpointId"
Write-Host "  URL: $WebhookUrl"

Write-Host "`nWebhook Step:" -ForegroundColor Cyan
Write-Host "  Name: Email-to-Document: Email Create"
Write-Host "  ID: $stepId"
Write-Host "  Entity: email"
Write-Host "  Message: Create"
Write-Host "  Stage: Post-operation (Async)"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "NEXT STEPS" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

Write-Host "1. Verify the webhook is working:" -ForegroundColor Yellow
Write-Host "   - Send/receive a test email in Dataverse"
Write-Host "   - Check BFF API logs for webhook trigger"
Write-Host "   - Check Service Bus queue for enqueued job"

Write-Host "`n2. Configure the BFF API:" -ForegroundColor Yellow
Write-Host "   - Set 'EmailProcessing:WebhookSecret' to: $WebhookSecret"
Write-Host "   - Set 'EmailProcessing:EnableWebhook' to: true"
Write-Host "   - Set 'EmailProcessing:DefaultContainerId' to your SPE container ID"

Write-Host "`n3. Monitor in Dataverse:" -ForegroundColor Yellow
Write-Host "   - Go to Settings > Customizations > Plugin Trace Log"
Write-Host "   - Filter by 'Email-to-Document' to see webhook executions"

Write-Host "`n"
