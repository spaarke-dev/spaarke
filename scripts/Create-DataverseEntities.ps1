# PowerShell script to create Dataverse entities for Spaarke Document Management
# Prerequisites:
# 1. Install Power Platform CLI: https://learn.microsoft.com/en-us/power-platform/developer/cli/introduction
# 2. Authenticate to your Dataverse environment

param(
    [Parameter(Mandatory = $true)]
    [string]$EnvironmentUrl,

    [Parameter(Mandatory = $false)]
    [string]$PublisherPrefix = "sprk"
)

Write-Host "Creating Spaarke Document Management entities in Dataverse..." -ForegroundColor Green
Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
Write-Host "Publisher Prefix: $PublisherPrefix" -ForegroundColor Yellow

# Authenticate to Power Platform (if not already authenticated)
Write-Host "Checking authentication..." -ForegroundColor Blue
try {
    pac auth list
}
catch {
    Write-Host "Please authenticate to Power Platform first:" -ForegroundColor Red
    Write-Host "pac auth create --url $EnvironmentUrl" -ForegroundColor Yellow
    exit 1
}

# Set the environment
Write-Host "Setting environment..." -ForegroundColor Blue
pac org select --environment $EnvironmentUrl

# Define entity schemas
$containerEntitySchema = @"
{
    "LogicalName": "sprk_container",
    "DisplayName": "Spaarke Container",
    "PluralDisplayName": "Spaarke Containers",
    "Description": "Container entity for SharePoint Embedded storage",
    "EntitySetName": "sprk_containers",
    "PrimaryNameAttribute": "sprk_name",
    "Attributes": [
        {
            "LogicalName": "sprk_name",
            "DisplayName": "Container Name",
            "AttributeType": "String",
            "MaxLength": 850,
            "IsRequired": true,
            "Description": "Name of the container"
        },
        {
            "LogicalName": "sprk_specontainerid",
            "DisplayName": "SPE Container ID",
            "AttributeType": "String",
            "MaxLength": 1000,
            "IsRequired": true,
            "Description": "SharePoint Embedded Container ID"
        },
        {
            "LogicalName": "sprk_documentcount",
            "DisplayName": "Document Count",
            "AttributeType": "Integer",
            "MinValue": 0,
            "MaxValue": 2147483647,
            "DefaultValue": 0,
            "Description": "Number of documents in this container"
        },
        {
            "LogicalName": "sprk_driveid",
            "DisplayName": "Drive ID",
            "AttributeType": "String",
            "MaxLength": 1000,
            "Description": "SharePoint Embedded Drive ID"
        }
    ]
}
"@

$documentEntitySchema = @"
{
    "LogicalName": "sprk_document",
    "DisplayName": "Spaarke Document",
    "PluralDisplayName": "Spaarke Documents",
    "Description": "Document entity for file management",
    "EntitySetName": "sprk_documents",
    "PrimaryNameAttribute": "sprk_name",
    "Attributes": [
        {
            "LogicalName": "sprk_name",
            "DisplayName": "Document Name",
            "AttributeType": "String",
            "MaxLength": 255,
            "IsRequired": true,
            "Description": "Name of the document"
        },
        {
            "LogicalName": "sprk_containerid",
            "DisplayName": "Container",
            "AttributeType": "Lookup",
            "Targets": ["sprk_container"],
            "IsRequired": true,
            "Description": "Reference to the container"
        },
        {
            "LogicalName": "sprk_hasfile",
            "DisplayName": "Has File",
            "AttributeType": "Boolean",
            "DefaultValue": false,
            "TrueOption": "Yes",
            "FalseOption": "No",
            "Description": "Indicates if document has an associated file"
        },
        {
            "LogicalName": "sprk_filename",
            "DisplayName": "File Name",
            "AttributeType": "String",
            "MaxLength": 255,
            "Description": "Name of the file in storage"
        },
        {
            "LogicalName": "sprk_filesize",
            "DisplayName": "File Size",
            "AttributeType": "BigInt",
            "MinValue": 0,
            "Description": "File size in bytes"
        },
        {
            "LogicalName": "sprk_mimetype",
            "DisplayName": "MIME Type",
            "AttributeType": "String",
            "MaxLength": 100,
            "Description": "File MIME type"
        },
        {
            "LogicalName": "sprk_graphitemid",
            "DisplayName": "Graph Item ID",
            "AttributeType": "String",
            "MaxLength": 1000,
            "Description": "SharePoint Embedded Graph Item ID"
        },
        {
            "LogicalName": "sprk_graphdriveid",
            "DisplayName": "Graph Drive ID",
            "AttributeType": "String",
            "MaxLength": 1000,
            "Description": "SharePoint Embedded Graph Drive ID"
        }
    ]
}
"@

# Create status option set
Write-Host "Creating status option set..." -ForegroundColor Blue
$statusOptionSet = @"
{
    "LogicalName": "sprk_documentstatus",
    "DisplayName": "Document Status",
    "Description": "Status of document processing",
    "Options": [
        {
            "Value": 421500001,
            "Label": "Draft",
            "Color": "#0078D4"
        },
        {
            "Value": 421500002,
            "Label": "Processing",
            "Color": "#FF8C00"
        },
        {
            "Value": 421500003,
            "Label": "Active",
            "Color": "#107C10"
        },
        {
            "Value": 421500004,
            "Label": "Error",
            "Color": "#D13438"
        }
    ]
}
"@

try {
    # Create Container entity
    Write-Host "Creating sprk_container entity..." -ForegroundColor Blue
    $containerEntitySchema | Out-File -FilePath "$env:TEMP\container_entity.json" -Encoding UTF8
    pac entity create --schemafile "$env:TEMP\container_entity.json"

    # Create Document entity (after container exists for lookup)
    Write-Host "Creating sprk_document entity..." -ForegroundColor Blue
    $documentEntitySchema | Out-File -FilePath "$env:TEMP\document_entity.json" -Encoding UTF8
    pac entity create --schemafile "$env:TEMP\document_entity.json"

    # Add status option set to document entity
    Write-Host "Adding status option set to document entity..." -ForegroundColor Blue
    $statusOptionSet | Out-File -FilePath "$env:TEMP\status_optionset.json" -Encoding UTF8
    pac optionset create --schemafile "$env:TEMP\status_optionset.json"

    # Add status attribute to document entity
    pac attribute create --entitylogicalname "sprk_document" --attributelogicalname "sprk_status" --attributetype "Picklist" --optionsetlogicalname "sprk_documentstatus" --displayname "Status" --description "Document processing status"

    Write-Host "✅ Entities created successfully!" -ForegroundColor Green
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "1. Configure security roles in Power Platform admin center" -ForegroundColor White
    Write-Host "2. Test the entities in the maker portal" -ForegroundColor White
    Write-Host "3. Run the API health check: GET /healthz/dataverse" -ForegroundColor White

    # Clean up temp files
    Remove-Item "$env:TEMP\container_entity.json" -ErrorAction SilentlyContinue
    Remove-Item "$env:TEMP\document_entity.json" -ErrorAction SilentlyContinue
    Remove-Item "$env:TEMP\status_optionset.json" -ErrorAction SilentlyContinue
}
catch {
    Write-Host "❌ Error creating entities: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Please check your authentication and permissions." -ForegroundColor Yellow
    exit 1
}