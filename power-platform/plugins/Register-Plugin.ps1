# Register Spaarke.Plugins assembly in Dataverse
# This script uses the Dataverse Client to register the plugin assembly and steps

param(
    [string]$ConnectionString = $env:DATAVERSE_CONNECTION_STRING
)

if ([string]::IsNullOrEmpty($ConnectionString)) {
    Write-Error "Connection string not provided. Set DATAVERSE_CONNECTION_STRING environment variable or pass -ConnectionString parameter."
    exit 1
}

$ErrorActionPreference = "Stop"

# Load assembly
$assemblyPath = Join-Path $PSScriptRoot "bin\Release\net48\Spaarke.Plugins.dll"
if (-not (Test-Path $assemblyPath)) {
    Write-Error "Plugin assembly not found at: $assemblyPath"
    exit 1
}

Write-Host "Loading assembly from: $assemblyPath" -ForegroundColor Cyan
$assemblyBytes = [System.IO.File]::ReadAllBytes($assemblyPath)
$assembly = [System.Reflection.Assembly]::Load($assemblyBytes)
$assemblyName = $assembly.GetName()

Write-Host "Assembly: $($assemblyName.Name) v$($assemblyName.Version)" -ForegroundColor Green

# Connect to Dataverse
Write-Host "Connecting to Dataverse..." -ForegroundColor Cyan
Add-Type -Path "$PSScriptRoot\bin\Release\net48\Microsoft.Xrm.Sdk.dll"
Add-Type -Path "$PSScriptRoot\bin\Release\net48\Microsoft.PowerPlatform.Dataverse.Client.dll"

$serviceClient = New-Object Microsoft.PowerPlatform.Dataverse.Client.ServiceClient($ConnectionString)

if (-not $serviceClient.IsReady) {
    Write-Error "Failed to connect to Dataverse: $($serviceClient.LastError)"
    exit 1
}

Write-Host "Connected to: $($serviceClient.ConnectedOrgFriendlyName)" -ForegroundColor Green

# Check if assembly already exists
Write-Host "Checking for existing plugin assembly..." -ForegroundColor Cyan
$query = New-Object Microsoft.Xrm.Sdk.Query.QueryExpression("pluginassembly")
$query.ColumnSet = New-Object Microsoft.Xrm.Sdk.Query.ColumnSet($true)
$query.Criteria.AddCondition("name", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, $assemblyName.Name)

$existingAssemblies = $serviceClient.RetrieveMultiple($query)

if ($existingAssemblies.Entities.Count -gt 0) {
    Write-Host "Found existing assembly. Updating..." -ForegroundColor Yellow
    $pluginAssembly = $existingAssemblies.Entities[0]
    $pluginAssembly["content"] = [Convert]::ToBase64String($assemblyBytes)
    $pluginAssembly["version"] = $assemblyName.Version.ToString()
    $serviceClient.Update($pluginAssembly)
    $assemblyId = $pluginAssembly.Id
    Write-Host "Assembly updated: $assemblyId" -ForegroundColor Green
} else {
    Write-Host "Creating new assembly..." -ForegroundColor Cyan
    $pluginAssembly = New-Object Microsoft.Xrm.Sdk.Entity("pluginassembly")
    $pluginAssembly["name"] = $assemblyName.Name
    $pluginAssembly["content"] = [Convert]::ToBase64String($assemblyBytes)
    $pluginAssembly["isolationmode"] = New-Object Microsoft.Xrm.Sdk.OptionSetValue(2) # Sandbox
    $pluginAssembly["sourcetype"] = New-Object Microsoft.Xrm.Sdk.OptionSetValue(0) # Database
    $pluginAssembly["version"] = $assemblyName.Version.ToString()
    $assemblyId = $serviceClient.Create($pluginAssembly)
    Write-Host "Assembly created: $assemblyId" -ForegroundColor Green
}

# Register plugin type
Write-Host "Registering plugin type: Spaarke.Plugins.DocumentEventPlugin" -ForegroundColor Cyan

$query = New-Object Microsoft.Xrm.Sdk.Query.QueryExpression("plugintype")
$query.ColumnSet = New-Object Microsoft.Xrm.Sdk.Query.ColumnSet($true)
$query.Criteria.AddCondition("pluginassemblyid", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, $assemblyId)
$query.Criteria.AddCondition("typename", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, "Spaarke.Plugins.DocumentEventPlugin")

$existingTypes = $serviceClient.RetrieveMultiple($query)

if ($existingTypes.Entities.Count -gt 0) {
    $pluginTypeId = $existingTypes.Entities[0].Id
    Write-Host "Plugin type already exists: $pluginTypeId" -ForegroundColor Yellow
} else {
    $pluginType = New-Object Microsoft.Xrm.Sdk.Entity("plugintype")
    $pluginType["pluginassemblyid"] = New-Object Microsoft.Xrm.Sdk.EntityReference("pluginassembly", $assemblyId)
    $pluginType["typename"] = "Spaarke.Plugins.DocumentEventPlugin"
    $pluginType["name"] = "DocumentEventPlugin"
    $pluginType["friendlyname"] = "Document Event Plugin"
    $pluginTypeId = $serviceClient.Create($pluginType)
    Write-Host "Plugin type created: $pluginTypeId" -ForegroundColor Green
}

# Get sprk_document entity type code
Write-Host "Getting entity metadata for sprk_document..." -ForegroundColor Cyan
$request = New-Object Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
$request.LogicalName = "sprk_document"
$request.EntityFilters = [Microsoft.Xrm.Sdk.Metadata.EntityFilters]::Entity
$response = [Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse]$serviceClient.Execute($request)
$entityTypeCode = $response.EntityMetadata.ObjectTypeCode

Write-Host "Entity type code: $entityTypeCode" -ForegroundColor Green

# Register plugin steps
$steps = @(
    @{
        Name = "Spaarke.Plugins.DocumentEventPlugin: Create of sprk_document"
        MessageName = "Create"
        Stage = 40 # PostOperation
        Mode = 1   # Asynchronous
    },
    @{
        Name = "Spaarke.Plugins.DocumentEventPlugin: Update of sprk_document"
        MessageName = "Update"
        Stage = 40
        Mode = 1
        PreImageName = "PreImage"
        PostImageName = "PostImage"
    },
    @{
        Name = "Spaarke.Plugins.DocumentEventPlugin: Delete of sprk_document"
        MessageName = "Delete"
        Stage = 40
        Mode = 1
        PreImageName = "PreImage"
    }
)

foreach ($stepConfig in $steps) {
    Write-Host "Registering step: $($stepConfig.MessageName)..." -ForegroundColor Cyan

    # Check if step already exists
    $query = New-Object Microsoft.Xrm.Sdk.Query.QueryExpression("sdkmessageprocessingstep")
    $query.ColumnSet = New-Object Microsoft.Xrm.Sdk.Query.ColumnSet($true)
    $query.Criteria.AddCondition("plugintypeid", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, $pluginTypeId)

    $messageFilter = $query.AddLink("sdkmessagefilter", "sdkmessagefilterid", "sdkmessagefilterid")
    $messageFilter.LinkCriteria.AddCondition("primaryobjecttypecode", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, $entityTypeCode)

    $message = $query.AddLink("sdkmessage", "sdkmessageid", "sdkmessageid")
    $message.LinkCriteria.AddCondition("name", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, $stepConfig.MessageName)

    $existingSteps = $serviceClient.RetrieveMultiple($query)

    if ($existingSteps.Entities.Count -gt 0) {
        Write-Host "Step already exists for $($stepConfig.MessageName)" -ForegroundColor Yellow
        $stepId = $existingSteps.Entities[0].Id
    } else {
        # Get message ID
        $messageQuery = New-Object Microsoft.Xrm.Sdk.Query.QueryExpression("sdkmessage")
        $messageQuery.ColumnSet = New-Object Microsoft.Xrm.Sdk.Query.ColumnSet("sdkmessageid")
        $messageQuery.Criteria.AddCondition("name", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, $stepConfig.MessageName)
        $messages = $serviceClient.RetrieveMultiple($messageQuery)
        $messageId = $messages.Entities[0].Id

        # Get message filter ID
        $filterQuery = New-Object Microsoft.Xrm.Sdk.Query.QueryExpression("sdkmessagefilter")
        $filterQuery.ColumnSet = New-Object Microsoft.Xrm.Sdk.Query.ColumnSet("sdkmessagefilterid")
        $filterQuery.Criteria.AddCondition("sdkmessageid", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, $messageId)
        $filterQuery.Criteria.AddCondition("primaryobjecttypecode", [Microsoft.Xrm.Sdk.Query.ConditionOperator]::Equal, $entityTypeCode)
        $filters = $serviceClient.RetrieveMultiple($filterQuery)
        $filterId = $filters.Entities[0].Id

        # Create step
        $step = New-Object Microsoft.Xrm.Sdk.Entity("sdkmessageprocessingstep")
        $step["name"] = $stepConfig.Name
        $step["plugintypeid"] = New-Object Microsoft.Xrm.Sdk.EntityReference("plugintype", $pluginTypeId)
        $step["sdkmessageid"] = New-Object Microsoft.Xrm.Sdk.EntityReference("sdkmessage", $messageId)
        $step["sdkmessagefilterid"] = New-Object Microsoft.Xrm.Sdk.EntityReference("sdkmessagefilter", $filterId)
        $step["stage"] = New-Object Microsoft.Xrm.Sdk.OptionSetValue($stepConfig.Stage)
        $step["mode"] = New-Object Microsoft.Xrm.Sdk.OptionSetValue($stepConfig.Mode)
        $step["rank"] = 1
        $step["asyncautodelete"] = $true

        $stepId = $serviceClient.Create($step)
        Write-Host "Step created: $stepId" -ForegroundColor Green

        # Register images if specified
        if ($stepConfig.PreImageName) {
            Write-Host "  Registering PreImage..." -ForegroundColor Cyan
            $preImage = New-Object Microsoft.Xrm.Sdk.Entity("sdkmessageprocessingstepimage")
            $preImage["sdkmessageprocessingstepid"] = New-Object Microsoft.Xrm.Sdk.EntityReference("sdkmessageprocessingstep", $stepId)
            $preImage["imagetype"] = New-Object Microsoft.Xrm.Sdk.OptionSetValue(0) # PreImage
            $preImage["name"] = $stepConfig.PreImageName
            $preImage["entityalias"] = $stepConfig.PreImageName
            $preImage["attributes"] = "sprk_documentname,sprk_containerid,sprk_documentdescription,sprk_hasfile,sprk_filename,sprk_filesize,sprk_mimetype,sprk_graphitemid,sprk_graphdriveid,statuscode,statecode"
            $imageId = $serviceClient.Create($preImage)
            Write-Host "  PreImage created: $imageId" -ForegroundColor Green
        }

        if ($stepConfig.PostImageName) {
            Write-Host "  Registering PostImage..." -ForegroundColor Cyan
            $postImage = New-Object Microsoft.Xrm.Sdk.Entity("sdkmessageprocessingstepimage")
            $postImage["sdkmessageprocessingstepid"] = New-Object Microsoft.Xrm.Sdk.EntityReference("sdkmessageprocessingstep", $stepId)
            $postImage["imagetype"] = New-Object Microsoft.Xrm.Sdk.OptionSetValue(1) # PostImage
            $postImage["name"] = $stepConfig.PostImageName
            $postImage["entityalias"] = $stepConfig.PostImageName
            $postImage["attributes"] = "sprk_documentname,sprk_containerid,sprk_documentdescription,sprk_hasfile,sprk_filename,sprk_filesize,sprk_mimetype,sprk_graphitemid,sprk_graphdriveid,statuscode,statecode"
            $imageId = $serviceClient.Create($postImage)
            Write-Host "  PostImage created: $imageId" -ForegroundColor Green
        }
    }
}

Write-Host "`nPlugin registration completed successfully!" -ForegroundColor Green
Write-Host "Assembly ID: $assemblyId" -ForegroundColor Cyan
Write-Host "Plugin Type ID: $pluginTypeId" -ForegroundColor Cyan

$serviceClient.Dispose()