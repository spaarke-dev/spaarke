# How To: Create and Update Dataverse Schema via Web API

> **Last Updated**: January 2026
> **Author**: AI Node Playbook Builder Project (Task 009)
> **Related Skill**: `.claude/skills/dataverse-create-schema/SKILL.md`

---

## Table of Contents

1. [Overview](#overview)
2. [Why Web API Instead of PAC CLI](#why-web-api-instead-of-pac-cli)
3. [Prerequisites](#prerequisites)
4. [Authentication](#authentication)
5. [Global Option Sets (Choices)](#global-option-sets-choices)
6. [Creating Entities (Tables)](#creating-entities-tables)
7. [Adding Attributes (Columns)](#adding-attributes-columns)
8. [Creating Lookup Relationships (1:N)](#creating-lookup-relationships-1n)
9. [Creating Many-to-Many Relationships (N:N)](#creating-many-to-many-relationships-nn)
10. [Publishing Customizations](#publishing-customizations)
11. [Verification and Troubleshooting](#verification-and-troubleshooting)
12. [Complete Example Scripts](#complete-example-scripts)
13. [Best Practices](#best-practices)

---

## Overview

This guide explains how to programmatically create and update Dataverse schema components using the **Dataverse Web API** and PowerShell. This approach is preferred for:

- **Automated deployments**: CI/CD pipelines
- **Repeatable schema creation**: Same schema across environments
- **Bulk operations**: Creating many entities/fields at once
- **Version control**: Scripts can be committed to git

### What You Can Create

| Component | API Endpoint | Description |
|-----------|-------------|-------------|
| Global Option Sets | `GlobalOptionSetDefinitions` | Reusable choice lists (picklists) |
| Entities (Tables) | `EntityDefinitions` | Custom tables |
| Attributes (Columns) | `EntityDefinitions(...)/Attributes` | Fields on tables |
| 1:N Relationships | `RelationshipDefinitions` | Parent-child lookups |
| N:N Relationships | `RelationshipDefinitions` | Many-to-many associations |

---

## Why Web API Instead of PAC CLI

The Power Platform CLI (`pac`) is excellent for many operations, but as of v1.46+, it **does not support direct table/column creation**:

```powershell
# These commands DO NOT exist in PAC CLI:
pac table create  # ❌ Not available
pac column create # ❌ Not available
```

**PAC CLI strengths**: Solution export/import, auth management, PCF deployment
**Web API strengths**: Full metadata control, all attribute types, relationship configuration

The Web API provides access to the complete **EntityDefinitions** and **RelationshipDefinitions** endpoints which offer fine-grained control over all schema properties.

---

## Prerequisites

### 1. Tools Required

| Tool | Purpose | Installation |
|------|---------|-------------|
| PowerShell 7+ | Script execution | `winget install Microsoft.PowerShell` |
| Azure CLI | OAuth token generation | `winget install Microsoft.AzureCLI` |
| PAC CLI (optional) | Verification | `dotnet tool install -g Microsoft.PowerApps.CLI.Tool` |

### 2. Environment Information

Gather this information before starting:

```
Environment URL: https://[org].crm.dynamics.com
Publisher Prefix: sprk_ (or your organization's prefix)
Solution Name: (if adding to a solution)
```

### 3. Permissions Required

Your account needs:
- **System Administrator** or **System Customizer** role
- Or a custom role with `prvCreate*` privileges on metadata entities

---

## Authentication

The Web API requires an OAuth 2.0 bearer token. The easiest way to obtain one is via Azure CLI.

### Step 1: Login to Azure

```powershell
# Interactive login (opens browser)
az login

# Verify you're logged in
az account show --output table
```

### Step 2: Get Access Token

```powershell
$Environment = "spaarkedev1.crm.dynamics.com"  # Your org
$token = (az account get-access-token --resource "https://$Environment" --query accessToken -o tsv)

# Verify token was obtained
if (-not $token) {
    Write-Error "Failed to get token. Ensure you're logged into Azure CLI."
    exit 1
}
Write-Host "Token obtained successfully" -ForegroundColor Green
```

### Step 3: Set Up Headers

```powershell
$BaseUrl = "https://$Environment/api/data/v9.2"

$headers = @{
    "Authorization"    = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
    "Content-Type"     = "application/json"
    "Accept"           = "application/json"
    "Prefer"           = "return=representation"
}
```

### Helper Function for API Calls

Create this function to simplify all API operations:

```powershell
function Invoke-DataverseApi {
    param(
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )

    $uri = "$BaseUrl/$Endpoint"
    $params = @{
        Uri     = $uri
        Headers = $headers
        Method  = $Method
    }

    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress)
    }

    try {
        $response = Invoke-RestMethod @params
        return @{ Success = $true; Data = $response }
    }
    catch {
        $errorMessage = $_.Exception.Message
        if ($_.Exception.Response) {
            try {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $errorMessage = $reader.ReadToEnd()
            } catch {}
        }
        return @{ Success = $false; Error = $errorMessage }
    }
}
```

---

## Global Option Sets (Choices)

Global option sets are reusable choice lists that can be used by multiple entities.

### Why Create Option Sets First?

Option sets must exist **before** you can create picklist attributes that reference them.

### Create a Global Option Set

```powershell
# Helper for localized labels
function New-Label {
    param([string]$Text)
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label"       = $Text
                "LanguageCode" = 1033  # English (US)
            }
        )
    }
}

# Define the option set
$optionSetDef = @{
    "@odata.type"  = "Microsoft.Dynamics.CRM.OptionSetMetadata"
    "Name"         = "sprk_taskstatus"
    "DisplayName"  = New-Label -Text "Task Status"
    "Description"  = New-Label -Text "Status values for tasks"
    "IsGlobal"     = $true
    "OptionSetType" = "Picklist"
    "Options"      = @(
        @{ "Value" = 0; "Label" = New-Label -Text "Pending" }
        @{ "Value" = 1; "Label" = New-Label -Text "In Progress" }
        @{ "Value" = 2; "Label" = New-Label -Text "Completed" }
        @{ "Value" = 3; "Label" = New-Label -Text "Failed" }
        @{ "Value" = 4; "Label" = New-Label -Text "Cancelled" }
    )
}

# Create it
$result = Invoke-DataverseApi -Endpoint "GlobalOptionSetDefinitions" -Method "POST" -Body $optionSetDef
if ($result.Success) {
    Write-Host "Created option set: sprk_taskstatus" -ForegroundColor Green
} else {
    Write-Host "Error: $($result.Error)" -ForegroundColor Red
}
```

### Check If Option Set Already Exists

```powershell
function Test-GlobalOptionSetExists {
    param([string]$Name)
    $result = Invoke-DataverseApi -Endpoint "GlobalOptionSetDefinitions(Name='$Name')"
    return $result.Success
}

# Usage
if (-not (Test-GlobalOptionSetExists -Name "sprk_taskstatus")) {
    # Create it
} else {
    Write-Host "Option set already exists, skipping..." -ForegroundColor Yellow
}
```

---

## Creating Entities (Tables)

### Basic Entity Creation

Every Dataverse entity needs at minimum:
- **SchemaName**: Logical name (e.g., `sprk_mytable`)
- **DisplayName**: User-friendly name
- **PrimaryNameAttribute**: The main text field

```powershell
$entityDef = @{
    "@odata.type"         = "Microsoft.Dynamics.CRM.EntityMetadata"
    "SchemaName"          = "sprk_project"
    "DisplayName"         = New-Label -Text "Project"
    "DisplayCollectionName" = New-Label -Text "Projects"
    "Description"         = New-Label -Text "Project records for tracking work"
    "OwnershipType"       = "OrganizationOwned"  # or "UserOwned"
    "IsActivity"          = $false
    "HasNotes"            = $false
    "HasActivities"       = $false
    "PrimaryNameAttribute" = "sprk_name"
    "Attributes"          = @(
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_name"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "MaxLength"     = 200
            "DisplayName"   = New-Label -Text "Name"
            "Description"   = New-Label -Text "Project name"
            "IsPrimaryName" = $true
        }
    )
}

$result = Invoke-DataverseApi -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef
if ($result.Success) {
    Write-Host "Created entity: sprk_project" -ForegroundColor Green
} else {
    Write-Host "Error: $($result.Error)" -ForegroundColor Red
}
```

### Entity with Auto-Number Primary Field

For entities like "Run" records where you want auto-generated names:

```powershell
$entityDef = @{
    "@odata.type"         = "Microsoft.Dynamics.CRM.EntityMetadata"
    "SchemaName"          = "sprk_taskrun"
    "DisplayName"         = New-Label -Text "Task Run"
    "DisplayCollectionName" = New-Label -Text "Task Runs"
    "Description"         = New-Label -Text "Execution records"
    "OwnershipType"       = "OrganizationOwned"
    "PrimaryNameAttribute" = "sprk_name"
    "Attributes"          = @(
        @{
            "@odata.type"     = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"      = "sprk_name"
            "RequiredLevel"   = @{ "Value" = "ApplicationRequired" }
            "MaxLength"       = 200
            "DisplayName"     = New-Label -Text "Run ID"
            "IsPrimaryName"   = $true
            "AutoNumberFormat" = "RUN-{SEQNUM:6}"  # e.g., RUN-000001
        }
    )
}
```

### Check If Entity Exists

```powershell
function Test-EntityExists {
    param([string]$LogicalName)
    $result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$LogicalName')"
    return $result.Success
}
```

---

## Adding Attributes (Columns)

After creating an entity, add additional columns using the Attributes endpoint.

### String Attribute

```powershell
$stringAttr = @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName"    = "sprk_code"
    "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
    "MaxLength"     = 50
    "DisplayName"   = New-Label -Text "Code"
    "Description"   = New-Label -Text "Unique project code"
}

Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_project')/Attributes" -Method "POST" -Body $stringAttr
```

### Memo (Multiline Text) Attribute

For large text fields like descriptions, notes, or JSON:

```powershell
$memoAttr = @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName"    = "sprk_configjson"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength"     = 1048576  # 1MB max
    "DisplayName"   = New-Label -Text "Configuration"
    "Description"   = New-Label -Text "JSON configuration data"
}

Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_project')/Attributes" -Method "POST" -Body $memoAttr
```

### Integer Attribute

```powershell
$intAttr = @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
    "SchemaName"    = "sprk_priority"
    "RequiredLevel" = @{ "Value" = "None" }
    "MinValue"      = 1
    "MaxValue"      = 10
    "DisplayName"   = New-Label -Text "Priority"
    "Description"   = New-Label -Text "Priority level (1-10)"
}

Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_project')/Attributes" -Method "POST" -Body $intAttr
```

**Important**: Do NOT include `DefaultValue` for integers - it's not supported and will cause errors.

### Boolean (Yes/No) Attribute

Boolean attributes require explicit TrueOption/FalseOption definitions:

```powershell
$boolAttr = @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
    "SchemaName"    = "sprk_isactive"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName"   = New-Label -Text "Is Active"
    "Description"   = New-Label -Text "Whether this record is active"
    "OptionSet"     = @{
        "TrueOption"  = @{ "Value" = 1; "Label" = New-Label -Text "Yes" }
        "FalseOption" = @{ "Value" = 0; "Label" = New-Label -Text "No" }
    }
}

Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_project')/Attributes" -Method "POST" -Body $boolAttr
```

### Picklist (Choice) Attribute

Reference a global option set:

```powershell
$picklistAttr = @{
    "@odata.type"                = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName"                 = "sprk_status"
    "RequiredLevel"              = @{ "Value" = "ApplicationRequired" }
    "DisplayName"                = New-Label -Text "Status"
    "Description"                = New-Label -Text "Current status"
    "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions(Name='sprk_taskstatus')"
}

Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_project')/Attributes" -Method "POST" -Body $picklistAttr
```

### DateTime Attribute

```powershell
$dateTimeAttr = @{
    "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
    "SchemaName"       = "sprk_duedate"
    "RequiredLevel"    = @{ "Value" = "None" }
    "DisplayName"      = New-Label -Text "Due Date"
    "Description"      = New-Label -Text "When the project is due"
    "Format"           = "DateAndTime"  # or "DateOnly"
    "DateTimeBehavior" = @{ "Value" = "UserLocal" }  # or "TimeZoneIndependent"
}

Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_project')/Attributes" -Method "POST" -Body $dateTimeAttr
```

### Check If Attribute Exists

```powershell
function Test-AttributeExists {
    param([string]$EntityLogicalName, [string]$AttributeLogicalName)
    $result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')"
    return $result.Success
}
```

---

## Creating Lookup Relationships (1:N)

**CRITICAL**: Lookup columns CANNOT be created directly via the Attributes endpoint. You MUST create them via the RelationshipDefinitions endpoint.

### Why RelationshipDefinitions?

When you try this:
```powershell
# ❌ THIS WILL FAIL
$lookupAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.LookupAttributeMetadata"
    "SchemaName" = "sprk_projectid"
    # ...
}
Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_task')/Attributes" -Method "POST" -Body $lookupAttr
# Error: "Attribute of type LookupAttributeMetadata cannot be created through the SDK"
```

### Correct Approach: OneToManyRelationshipMetadata

```powershell
$relationshipDef = @{
    "@odata.type"          = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
    "SchemaName"           = "sprk_project_task_projectid"  # Relationship name
    "ReferencedEntity"     = "sprk_project"                  # Parent (1 side)
    "ReferencingEntity"    = "sprk_task"                     # Child (N side)
    "CascadeConfiguration" = @{
        "Assign"   = "NoCascade"
        "Delete"   = "Cascade"     # What happens when parent is deleted
        "Merge"    = "NoCascade"
        "Reparent" = "NoCascade"
        "Share"    = "NoCascade"
        "Unshare"  = "NoCascade"
    }
    "Lookup" = @{
        "SchemaName"    = "sprk_projectid"
        "DisplayName"   = New-Label -Text "Project"
        "Description"   = New-Label -Text "Parent project"
        "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
    }
}

$result = Invoke-DataverseApi -Endpoint "RelationshipDefinitions" -Method "POST" -Body $relationshipDef
if ($result.Success) {
    Write-Host "Created lookup: sprk_projectid on sprk_task" -ForegroundColor Green
}
```

### Delete Cascade Options

| Behavior | When Parent Deleted... |
|----------|----------------------|
| `Cascade` | Child records are also deleted |
| `Restrict` | Deletion is blocked if children exist |
| `RemoveLink` | Lookup value is cleared (set to null) |

### Lookup to System Entities

To create a lookup to built-in entities like `systemuser`:

```powershell
$userLookup = @{
    "@odata.type"          = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
    "SchemaName"           = "sprk_systemuser_project_ownerid"
    "ReferencedEntity"     = "systemuser"      # Built-in Users entity
    "ReferencingEntity"    = "sprk_project"
    "CascadeConfiguration" = @{
        "Delete" = "Restrict"  # Don't allow user deletion if assigned
    }
    "Lookup" = @{
        "SchemaName"    = "sprk_ownerid"
        "DisplayName"   = New-Label -Text "Assigned To"
        "RequiredLevel" = @{ "Value" = "None" }
    }
}
```

---

## Creating Many-to-Many Relationships (N:N)

Many-to-many relationships connect two entities where records on either side can be related to multiple records on the other.

### Example: Projects can have multiple Skills, Skills can belong to multiple Projects

```powershell
$nnRelationship = @{
    "@odata.type"                       = "Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata"
    "SchemaName"                        = "sprk_project_skill"
    "Entity1LogicalName"                = "sprk_project"
    "Entity2LogicalName"                = "sprk_skill"
    "Entity1AssociatedMenuConfiguration" = @{
        "Behavior" = "UseCollectionName"
        "Group"    = "Details"
        "Order"    = 10000
    }
    "Entity2AssociatedMenuConfiguration" = @{
        "Behavior" = "UseCollectionName"
        "Group"    = "Details"
        "Order"    = 10000
    }
    "IntersectEntityName"               = "sprk_project_skill"  # Junction table name
}

$result = Invoke-DataverseApi -Endpoint "RelationshipDefinitions" -Method "POST" -Body $nnRelationship
if ($result.Success) {
    Write-Host "Created N:N: sprk_project_skill" -ForegroundColor Green
}
```

**Note**: Both entities must already exist before creating the N:N relationship.

---

## Publishing Customizations

After creating schema components, you must **publish** them to make them available in the user interface.

### Publish Specific Entities

```powershell
$entities = @("sprk_project", "sprk_task", "sprk_skill")
$entityXml = ($entities | ForEach-Object { "<entity>$_</entity>" }) -join ""

$publishRequest = @{
    "ParameterXml" = "<importexportxml><entities>$entityXml</entities></importexportxml>"
}

$result = Invoke-DataverseApi -Endpoint "PublishXml" -Method "POST" -Body $publishRequest
Write-Host "Customizations published" -ForegroundColor Green
```

### Publish All Customizations

```powershell
$publishRequest = @{
    "ParameterXml" = "<importexportxml><entities/></importexportxml>"
}

Invoke-DataverseApi -Endpoint "PublishXml" -Method "POST" -Body $publishRequest
```

---

## Verification and Troubleshooting

### Verify Entity Was Created

```powershell
$entity = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_project')?`$select=LogicalName,DisplayName"
if ($entity.Success) {
    Write-Host "Entity exists: $($entity.Data.LogicalName)" -ForegroundColor Green
}
```

### List All Custom Attributes on an Entity

```powershell
$result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_project')?`$expand=Attributes(`$select=LogicalName,AttributeType)"
$result.Data.Attributes |
    Where-Object { $_.LogicalName -like "sprk_*" } |
    ForEach-Object {
        Write-Host "  $($_.LogicalName) ($($_.AttributeType))"
    }
```

### List Lookup Relationships

```powershell
$result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_task')/ManyToOneRelationships?`$select=SchemaName,ReferencedEntity"
$result.Data.value | ForEach-Object {
    Write-Host "  $($_.SchemaName) -> $($_.ReferencedEntity)"
}
```

### List N:N Relationships

```powershell
$result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_project')/ManyToManyRelationships?`$select=SchemaName,Entity1LogicalName,Entity2LogicalName"
$result.Data.value | ForEach-Object {
    Write-Host "  $($_.SchemaName): $($_.Entity1LogicalName) <-> $($_.Entity2LogicalName)"
}
```

### Common Errors and Solutions

| Error Message | Cause | Solution |
|---------------|-------|----------|
| `Attribute of type LookupAttributeMetadata cannot be created through the SDK` | Tried to create lookup via Attributes endpoint | Use `RelationshipDefinitions` endpoint instead |
| `The property 'DefaultValue' does not exist on type 'Microsoft.Dynamics.CRM.IntegerAttributeMetadata'` | DefaultValue not valid for integers | Remove DefaultValue property |
| `An unexpected error occurred` | Malformed JSON or transient API error | Check JSON structure; retry if transient |
| `Entity with name 'X' already exists` | Entity already created | Add existence check before creation |
| `GlobalOptionSet not found` | Picklist references non-existent option set | Create global option sets first (Phase 1) |

---

## Complete Example Scripts

### Reference Scripts in Repository

| Script | Purpose | Location |
|--------|---------|----------|
| Deploy-PlaybookNodeSchema.ps1 | Complete 5-phase deployment | `projects/ai-node-playbook-builder/scripts/` |
| Fix-PlaybookNodeAttributes.ps1 | Add missing attributes | `projects/ai-node-playbook-builder/scripts/` |
| Create-NNRelationships.ps1 | Create N:N relationships | `projects/ai-node-playbook-builder/scripts/` |
| Deploy-ChartDefinitionEntity.ps1 | Original pattern reference | `scripts/` |

### Recommended Deployment Order

```
╔════════════════════════════════════════════════════════════╗
║  Dataverse Schema Deployment Order                          ║
╠════════════════════════════════════════════════════════════╣
║                                                              ║
║  Phase 1: Global Option Sets                                ║
║     ↓  Must exist before picklist attributes                ║
║                                                              ║
║  Phase 2: Lookup Reference Entities                         ║
║     ↓  Small lookup tables (status types, categories)       ║
║                                                              ║
║  Phase 3: Extend Existing Entities                          ║
║     ↓  Add new columns to existing tables                   ║
║                                                              ║
║  Phase 4: Create New Entities                               ║
║     ↓  Full entities with attributes and lookups            ║
║                                                              ║
║  Phase 5: Create N:N Relationships                          ║
║     ↓  Both entities must exist first                       ║
║                                                              ║
║  Phase 6: Publish Customizations                            ║
║        Make changes available in UI                         ║
║                                                              ║
╚════════════════════════════════════════════════════════════╝
```

---

## Best Practices

### 1. Make Scripts Idempotent

Always check if a component exists before creating:

```powershell
if (-not (Test-EntityExists -LogicalName "sprk_myentity")) {
    # Create entity
} else {
    Write-Host "Entity already exists, skipping..." -ForegroundColor Yellow
}
```

### 2. Use Consistent Naming

| Component | Naming Convention | Example |
|-----------|------------------|---------|
| Entity | `sprk_{singular}` | `sprk_project` |
| Attribute | `sprk_{name}` | `sprk_startdate` |
| Lookup | `sprk_{parentname}id` | `sprk_projectid` |
| Relationship | `sprk_{parent}_{child}_{lookup}` | `sprk_project_task_projectid` |
| Option Set | `sprk_{name}` | `sprk_status` |
| N:N Relationship | `sprk_{entity1}_{entity2}` | `sprk_project_skill` |

### 3. Include Error Handling

```powershell
try {
    $result = Invoke-DataverseApi -Endpoint "..." -Method "POST" -Body $body
    if ($result.Success) {
        Write-Host "Success" -ForegroundColor Green
    } else {
        Write-Host "Failed: $($result.Error)" -ForegroundColor Red
    }
} catch {
    if ($_.Exception.Message -match "already exists") {
        Write-Host "Already exists, skipping..." -ForegroundColor Yellow
    } else {
        throw
    }
}
```

### 4. Log All Operations

For troubleshooting and audit:

```powershell
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$logEntry = "$timestamp | Created entity: sprk_project"
Add-Content -Path "schema-deployment.log" -Value $logEntry
```

### 5. Verify After Deployment

Always run verification queries after deployment:

```powershell
# Verify all expected attributes exist
$expected = @("sprk_name", "sprk_code", "sprk_status", "sprk_projectid")
$actual = (Get-EntityAttributes -EntityLogicalName "sprk_task").LogicalName
$missing = $expected | Where-Object { $_ -notin $actual }
if ($missing) {
    Write-Warning "Missing attributes: $($missing -join ', ')"
}
```

---

## Related Resources

- **Skill File**: `.claude/skills/dataverse-create-schema/SKILL.md`
- **ADR-022**: Unmanaged solutions only
- **Dataverse Web API Reference**: [Microsoft Docs](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/overview)
- **EntityDefinitions Reference**: [Microsoft Docs](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/reference/entitymetadata)

---

*Guide created from AI Node Playbook Builder project (Task 009). January 2026.*
