<#
.SYNOPSIS
    Registers the DocumentEventPlugin in Dataverse

.DESCRIPTION
    This script registers the DocumentEventPlugin assembly and configures plugin steps
    for Create, Update, and Delete operations on the sprk_document entity.

    Prerequisites:
    - Power Platform CLI (pac) installed
    - Authenticated to target environment (pac auth create)
    - Service Bus connection string stored securely

.PARAMETER Environment
    The Dataverse environment URL (e.g., https://org.crm.dynamics.com)

.PARAMETER ServiceBusConnectionString
    The Service Bus connection string (will be stored as secure configuration)

.PARAMETER QueueName
    The Service Bus queue name (default: document-events)

.EXAMPLE
    .\Register-DocumentPlugin.ps1 -Environment "https://org.crm.dynamics.com" -ServiceBusConnectionString "Endpoint=sb://..." -QueueName "document-events"
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$Environment,

    [Parameter(Mandatory=$true)]
    [string]$ServiceBusConnectionString,

    [Parameter(Mandatory=$false)]
    [string]$QueueName = "document-events"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Document Event Plugin Registration" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build the plugin assembly
Write-Host "[1/5] Building plugin assembly..." -ForegroundColor Yellow
Set-Location -Path "$PSScriptRoot\Spaarke.Plugins"
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    throw "Plugin build failed"
}
Write-Host "✓ Plugin built successfully" -ForegroundColor Green
Write-Host ""

# Step 2: Create plugin configuration JSON
Write-Host "[2/5] Preparing plugin configuration..." -ForegroundColor Yellow
$pluginConfig = @{
    ServiceBusConnectionString = $ServiceBusConnectionString
    QueueName = $QueueName
} | ConvertTo-Json -Compress
Write-Host "✓ Configuration prepared" -ForegroundColor Green
Write-Host ""

# Assembly path
$assemblyPath = "$PSScriptRoot\Spaarke.Plugins\bin\Release\net471\Spaarke.Plugins.dll"
if (-not (Test-Path $assemblyPath)) {
    throw "Plugin assembly not found at: $assemblyPath"
}

Write-Host "[3/5] Registering plugin assembly..." -ForegroundColor Yellow
Write-Host "Assembly: $assemblyPath" -ForegroundColor Gray
Write-Host "Environment: $Environment" -ForegroundColor Gray
Write-Host ""

# Register the plugin assembly using pac CLI
# Note: This creates or updates the plugin assembly in Dataverse
$registerCommand = "pac plugin push --assembly `"$assemblyPath`" --environment `"$Environment`""
Write-Host "Executing: $registerCommand" -ForegroundColor Gray
Invoke-Expression $registerCommand

if ($LASTEXITCODE -ne 0) {
    throw "Plugin assembly registration failed"
}
Write-Host "✓ Plugin assembly registered" -ForegroundColor Green
Write-Host ""

Write-Host "[4/5] Configuring plugin steps..." -ForegroundColor Yellow
Write-Host ""
Write-Host "The following plugin steps need to be configured:" -ForegroundColor Cyan
Write-Host ""

# Plugin step configurations
$steps = @(
    @{
        Name = "Document Create - Post-Operation"
        Message = "Create"
        Entity = "sprk_document"
        Stage = "Post-operation"
        Mode = "Asynchronous"
        Description = "Queues document create events to Service Bus for background processing"
    },
    @{
        Name = "Document Update - Post-Operation"
        Message = "Update"
        Entity = "sprk_document"
        Stage = "Post-operation"
        Mode = "Asynchronous"
        Description = "Queues document update events to Service Bus for background processing"
    },
    @{
        Name = "Document Delete - Post-Operation"
        Message = "Delete"
        Entity = "sprk_document"
        Stage = "Post-operation"
        Mode = "Asynchronous"
        Description = "Queues document delete events to Service Bus for background processing"
    }
)

foreach ($step in $steps) {
    Write-Host "• $($step.Name)" -ForegroundColor White
    Write-Host "  Message: $($step.Message)" -ForegroundColor Gray
    Write-Host "  Entity: $($step.Entity)" -ForegroundColor Gray
    Write-Host "  Stage: $($step.Stage)" -ForegroundColor Gray
    Write-Host "  Mode: $($step.Mode)" -ForegroundColor Gray
    Write-Host "  Images Required:" -ForegroundColor Gray
    if ($step.Message -eq "Update") {
        Write-Host "    - PreImage: All fields" -ForegroundColor Gray
        Write-Host "    - PostImage: All fields" -ForegroundColor Gray
    } elseif ($step.Message -eq "Create") {
        Write-Host "    - PostImage: All fields" -ForegroundColor Gray
    } elseif ($step.Message -eq "Delete") {
        Write-Host "    - PreImage: All fields" -ForegroundColor Gray
    }
    Write-Host ""
}

Write-Host "⚠ MANUAL STEP REQUIRED:" -ForegroundColor Yellow
Write-Host "Plugin steps must be registered manually using the Plugin Registration Tool because:" -ForegroundColor Yellow
Write-Host "1. Secure configuration (Service Bus connection string) needs to be set" -ForegroundColor Yellow
Write-Host "2. Entity images (PreImage/PostImage) need to be configured" -ForegroundColor Yellow
Write-Host "3. pac CLI doesn't support all registration options" -ForegroundColor Yellow
Write-Host ""

Write-Host "[5/5] Configuration details for manual registration:" -ForegroundColor Yellow
Write-Host ""
Write-Host "Plugin Class: Spaarke.Plugins.DocumentEventPlugin" -ForegroundColor Cyan
Write-Host "Secure Configuration (JSON):" -ForegroundColor Cyan
Write-Host $pluginConfig -ForegroundColor White
Write-Host ""

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Registration Steps for Plugin Registration Tool:" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Open Plugin Registration Tool" -ForegroundColor White
Write-Host "2. Connect to: $Environment" -ForegroundColor White
Write-Host "3. Locate assembly: Spaarke.Plugins" -ForegroundColor White
Write-Host "4. Locate plugin: DocumentEventPlugin" -ForegroundColor White
Write-Host "5. Register three plugin steps (one for each message)" -ForegroundColor White
Write-Host ""
Write-Host "For each step:" -ForegroundColor White
Write-Host "  a. Set Message, Entity, Stage, Mode as shown above" -ForegroundColor White
Write-Host "  b. Set Secure Configuration to the JSON shown above" -ForegroundColor White
Write-Host "  c. Add required entity images with all fields" -ForegroundColor White
Write-Host "  d. Save and activate the step" -ForegroundColor White
Write-Host ""

Write-Host "✓ Plugin assembly is ready for step registration" -ForegroundColor Green
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "1. Complete manual plugin step registration" -ForegroundColor White
Write-Host "2. Test by creating/updating/deleting a document in Dataverse" -ForegroundColor White
Write-Host "3. Verify events appear in Service Bus queue: $QueueName" -ForegroundColor White
Write-Host "4. Check plugin execution logs in Dataverse System Jobs" -ForegroundColor White
Write-Host ""
