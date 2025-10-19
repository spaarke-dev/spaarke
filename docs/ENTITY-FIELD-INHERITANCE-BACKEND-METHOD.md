# Entity Field Inheritance - Backend Implementation Design

**Version:** 1.0.0
**Date:** 2025-10-07
**Status:** Design Document (Not Yet Implemented)
**Sprint:** Future - Bulk Import/Background Processing

---

## Table of Contents

1. [Problem Statement](#problem-statement)
2. [Current Frontend Solution](#current-frontend-solution)
3. [Backend Solution Design](#backend-solution-design)
4. [Reusable Components](#reusable-components)
5. [Implementation Options](#implementation-options)
6. [Code Samples](#code-samples)
7. [Configuration Management](#configuration-management)
8. [High-Level Implementation Plan](#high-level-implementation-plan)
9. [Testing Strategy](#testing-strategy)
10. [Appendix](#appendix)

---

## Problem Statement

### Business Need

When creating child records in Dataverse, field values often need to be inherited from parent records. Currently, this is handled in the **Universal Quick Create PCF control** (frontend), which works well for **interactive form-based record creation**.

However, there are scenarios where records are created **without user interaction**:

1. **Bulk Import** - Importing 100s or 1000s of records from CSV/Excel
2. **API-Based Creation** - External systems creating records via API
3. **Background Jobs** - Scheduled processes creating records
4. **Data Migration** - Moving records from legacy systems
5. **Integration Flows** - Power Automate or Logic Apps creating records

In these scenarios, **frontend PCF controls do not execute**, so field inheritance logic must be **implemented on the backend**.

### Current Limitation

The Universal Quick Create control (TypeScript/JavaScript) only runs:
- ✅ When user opens Quick Create form in browser
- ❌ Does NOT run during bulk imports
- ❌ Does NOT run during API-based creation
- ❌ Does NOT run in Power Automate flows
- ❌ Does NOT run in background jobs

### Desired Outcome

A **backend service or plugin** that:
- Applies field inheritance logic **server-side**
- Works for bulk imports, API creation, and background jobs
- Reuses the **same field mapping configuration** as frontend
- Supports both **simple field mappings** and **lookup field mappings**
- Can be triggered automatically or on-demand

---

## Current Frontend Solution

### Overview

The **Universal Quick Create PCF Control** implements field inheritance in TypeScript/JavaScript that runs in the user's browser.

**Location:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/`

### How It Works

**Step-by-Step Flow:**

```
1. User clicks "+ New Document" from Matter subgrid
         ↓
2. PCF control initializes in browser
         ↓
3. Power Apps provides parent context (entity name, record ID)
         ↓
4. PCF retrieves parent record from Dataverse (Web API)
         ↓
5. PCF loads field mapping configuration (from manifest)
         ↓
6. PCF executes getDefaultValues() - CORE ALGORITHM
         ↓
7. PCF passes default values to React form
         ↓
8. User sees pre-filled form, fills remaining fields
         ↓
9. User clicks Save
         ↓
10. PCF creates child record with inherited + user values
```

### Core Algorithm: `getDefaultValues()`

**File:** `UniversalQuickCreatePCF.ts` (Lines 317-367)

**Purpose:** Apply field mappings to inherit values from parent record

**Pseudocode:**
```
FUNCTION getDefaultValues():
    defaults = empty object

    IF no parent record data:
        RETURN empty defaults

    mappings = get field mappings for parent entity type

    FOR EACH (parentField, childField) in mappings:
        parentValue = get value from parent record

        IF parentValue exists:
            IF is lookup field mapping:
                # Create OData bind reference for relationship
                defaults[childField + "@odata.bind"] = "/entity_set_name(parent_id)"
            ELSE:
                # Copy value directly
                defaults[childField] = parentValue
            END IF
        END IF
    END FOR

    RETURN defaults
END FUNCTION
```

**Key Decision Logic:**
```
FUNCTION isLookupFieldMapping(parentField, childField):
    # Pattern 1: Child field matches parent entity name
    IF childField == parentEntityName:
        RETURN TRUE  # e.g., "sprk_matter" child field on Document

    # Pattern 2: Known lookup fields
    IF childField in lookup_fields_list:
        RETURN TRUE  # e.g., "parentaccountid", "accountid"

    RETURN FALSE  # Simple field
END FUNCTION
```

### Components Built

| Component | File | Purpose | Reusable for Backend? |
|-----------|------|---------|----------------------|
| `UniversalQuickCreatePCF` | UniversalQuickCreatePCF.ts | Main PCF control | ❌ No (browser-only) |
| `getDefaultValues()` | UniversalQuickCreatePCF.ts:317-367 | **CORE ALGORITHM** | ✅ **Logic reusable** |
| `isLookupFieldMapping()` | UniversalQuickCreatePCF.ts:369-401 | Detect lookup fields | ✅ **Logic reusable** |
| `getEntitySetName()` | UniversalQuickCreatePCF.ts:403-421 | Map entity to OData set name | ✅ **Logic reusable** |
| `getParentSelectFields()` | UniversalQuickCreatePCF.ts:227-249 | Define fields to retrieve | ✅ **Logic reusable** |
| `loadParentRecordData()` | UniversalQuickCreatePCF.ts:204-225 | Fetch parent record | ✅ **Pattern reusable** |
| `QuickCreateForm` | QuickCreateForm.tsx | React UI component | ❌ No (UI-specific) |
| `DynamicFormFields` | DynamicFormFields.tsx | React form renderer | ❌ No (UI-specific) |

### Field Mapping Configuration

**Format:** JSON object stored in PCF manifest parameter

**Example:**
```json
{
  "defaultValueMappings": {
    "sprk_matter": {
      "sprk_matternumber": "sprk_matter",
      "sprk_containerid": "sprk_containerid"
    },
    "account": {
      "name": "sprk_companyname",
      "address1_composite": "address1_composite"
    }
  }
}
```

**Structure:**
- **Key:** Parent entity logical name (e.g., "sprk_matter")
- **Value:** Object mapping parent fields to child fields
  - **Key:** Parent field logical name (e.g., "sprk_matternumber")
  - **Value:** Child field logical name (e.g., "sprk_matter")

---

## Backend Solution Design

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                    Backend Field Inheritance                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌────────────────────┐         ┌─────────────────────────┐    │
│  │  Trigger Options:  │         │  Field Inheritance       │    │
│  │                    │         │  Service                 │    │
│  │  • Dataverse Plugin│────────▶│                          │    │
│  │  • Azure Function  │         │  Core Methods:           │    │
│  │  • Power Automate  │         │  • ApplyFieldMappings()  │    │
│  │  • API Endpoint    │         │  • IsLookupField()       │    │
│  └────────────────────┘         │  • GetEntitySetName()    │    │
│                                  └─────────────────────────┘    │
│                                              │                   │
│                                              ▼                   │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │            Configuration Storage                          │  │
│  │                                                            │  │
│  │  Option 1: Dataverse Configuration Table                 │  │
│  │  Option 2: Azure App Settings                            │  │
│  │  Option 3: Shared JSON Configuration File                │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                              │                   │
│                                              ▼                   │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │                Dataverse / CRM                            │  │
│  │  • Retrieve parent record                                │  │
│  │  • Create child record with inherited values             │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Core Service Interface

**Language-Agnostic Interface Definition:**

```
SERVICE FieldInheritanceService:

    METHOD ApplyFieldMappings(
        parentEntityName: string,
        parentRecordId: guid,
        childEntityName: string,
        childRecordData: object
    ) RETURNS object:
        """
        Applies field inheritance from parent to child record.

        Parameters:
            parentEntityName - Logical name of parent entity (e.g., "sprk_matter")
            parentRecordId - GUID of parent record
            childEntityName - Logical name of child entity (e.g., "sprk_document")
            childRecordData - Partial child record data from user/import

        Returns:
            Complete child record data with inherited fields
        """

    METHOD IsLookupFieldMapping(
        parentField: string,
        childField: string,
        parentEntityName: string
    ) RETURNS boolean:
        """
        Determines if a field mapping represents a lookup relationship.

        Returns:
            TRUE if lookup field, FALSE if simple field
        """

    METHOD GetFieldMappingConfiguration(
        parentEntityName: string
    ) RETURNS object:
        """
        Retrieves field mapping configuration for parent entity.

        Returns:
            Object mapping parent fields to child fields
        """
END SERVICE
```

### Core Algorithm (Language-Agnostic)

**Main Method:**
```
FUNCTION ApplyFieldMappings(parentEntityName, parentRecordId, childEntityName, childRecordData):

    # 1. Retrieve parent record from Dataverse
    selectFields = GetParentSelectFields(parentEntityName)
    parentRecord = DataverseService.Retrieve(parentEntityName, parentRecordId, selectFields)

    # 2. Load field mapping configuration
    mappings = GetFieldMappingConfiguration(parentEntityName)

    IF mappings is empty:
        RETURN childRecordData  # No mappings configured, return as-is
    END IF

    # 3. Apply each field mapping
    FOR EACH (parentField, childField) IN mappings:

        IF parentRecord.Contains(parentField):
            parentValue = parentRecord.Get(parentField)

            IF parentValue is not null:

                # Determine if lookup or simple field
                IF IsLookupFieldMapping(parentField, childField, parentEntityName):
                    # LOOKUP FIELD: Create entity reference
                    childRecordData[childField] = CreateEntityReference(parentEntityName, parentRecordId)
                ELSE:
                    # SIMPLE FIELD: Copy value
                    childRecordData[childField] = parentValue
                END IF

            END IF
        END IF

    END FOR

    RETURN childRecordData

END FUNCTION
```

**Lookup Detection Logic:**
```
FUNCTION IsLookupFieldMapping(parentField, childField, parentEntityName):

    # Known lookup field patterns
    lookupMappings = {
        "sprk_matter": ["sprk_matter"],
        "account": ["parentaccountid", "accountid"],
        "contact": ["parentcontactid", "contactid"]
    }

    # Pattern 1: Child field matches parent entity name
    IF childField == parentEntityName:
        RETURN TRUE
    END IF

    # Pattern 2: Known lookup fields for this parent entity
    IF parentEntityName IN lookupMappings:
        IF childField IN lookupMappings[parentEntityName]:
            RETURN TRUE
        END IF
    END IF

    # Pattern 3: Common lookup field naming patterns
    IF childField.EndsWith("id") OR childField.EndsWith("Id"):
        RETURN TRUE
    END IF

    RETURN FALSE

END FUNCTION
```

**Parent Field Selection:**
```
FUNCTION GetParentSelectFields(parentEntityName):

    # Define fields to retrieve based on parent entity type
    fieldSelections = {
        "sprk_matter": [
            "sprk_name",
            "sprk_containerid",
            "_ownerid_value",
            "sprk_matternumber"
        ],
        "account": [
            "name",
            "address1_composite",
            "_ownerid_value"
        ],
        "contact": [
            "fullname",
            "_ownerid_value"
        ]
    }

    IF parentEntityName IN fieldSelections:
        RETURN fieldSelections[parentEntityName]
    ELSE:
        RETURN ["name"]  # Default fallback
    END IF

END FUNCTION
```

---

## Reusable Components

### What Can Be Reused

| Component | Frontend Format | Backend Format | Reusability |
|-----------|----------------|----------------|-------------|
| **Field Mapping Logic** | TypeScript function | C# method / JS function | ✅ 100% - Algorithm is identical |
| **Lookup Detection Logic** | TypeScript function | C# method / JS function | ✅ 100% - Same patterns apply |
| **Configuration Format** | JSON object | JSON object | ✅ 100% - Identical structure |
| **Entity Set Mapping** | TypeScript map | C# dictionary / JS object | ✅ 100% - Same mappings |
| **Field Selection Lists** | TypeScript array | C# array / JS array | ✅ 100% - Same field lists |

### What Cannot Be Reused

| Component | Reason |
|-----------|--------|
| PCF Control Classes | Browser-only framework |
| React Components | UI rendering, not applicable to backend |
| Browser Web API Calls | Backend uses different SDK |
| TypeScript Code Files | Different runtime environment |

### Adaptation Guide

**Frontend → Backend Translation:**

| Frontend Concept | Backend Equivalent (C#) | Backend Equivalent (JavaScript/Azure Function) |
|------------------|-------------------------|-----------------------------------------------|
| `context.webAPI.retrieveRecord()` | `service.Retrieve()` | `dataverseClient.retrieveRecord()` |
| `Record<string, unknown>` | `Entity` object | Plain JavaScript object |
| `@odata.bind` syntax | `EntityReference` object | `{ "@odata.bind": "..." }` |
| JSON.parse() config | Deserialize JSON to object | JSON.parse() - same |
| `for...of` loop | `foreach` loop | `for...of` loop - same |

---

## Implementation Options

### Option 1: Dataverse Plugin (Recommended for Real-Time)

**Technology:** C# (.NET Framework or .NET Core)
**Execution:** Synchronous or asynchronous on Dataverse server
**Best For:** Real-time validation during record creation

**Pros:**
- ✅ Runs automatically on record create
- ✅ No additional infrastructure needed
- ✅ Low latency (runs on Dataverse server)
- ✅ Can validate before record is saved

**Cons:**
- ❌ Requires C# development skills
- ❌ 2-minute execution timeout limit
- ❌ Harder to debug than Azure Functions

**When to Use:**
- Need field inheritance to apply automatically
- Importing records via Dataverse API
- Power Automate flows creating records
- Third-party integrations

---

### Option 2: Azure Function (Recommended for Bulk/Async)

**Technology:** C# or JavaScript/TypeScript
**Execution:** HTTP-triggered or queue-triggered
**Best For:** Bulk imports, scheduled jobs, async processing

**Pros:**
- ✅ No timeout limits (can run for hours)
- ✅ Easy to scale for large datasets
- ✅ Can use JavaScript (similar to frontend)
- ✅ Better monitoring and logging

**Cons:**
- ❌ Requires Azure subscription
- ❌ Additional cost for compute time
- ❌ Not automatic (must be triggered)

**When to Use:**
- Importing 1000s of records from CSV
- Scheduled batch processing
- Complex transformation logic
- Integration with external systems

---

### Option 3: Power Automate Flow (Recommended for Simple Cases)

**Technology:** Low-code/no-code workflow
**Execution:** Triggered by events or schedules
**Best For:** Simple field mappings, non-technical users

**Pros:**
- ✅ No coding required
- ✅ Visual workflow builder
- ✅ Easy to modify by business users
- ✅ Built into Power Platform

**Cons:**
- ❌ Limited to simple mappings
- ❌ Performance issues with large datasets
- ❌ Hard to implement complex logic

**When to Use:**
- Simple field copying (no lookup detection needed)
- Small datasets (<100 records)
- Business users managing configuration
- Prototyping before custom development

---

### Option 4: Custom API Endpoint

**Technology:** C# Web API or Azure Function HTTP endpoint
**Execution:** Called explicitly by external systems
**Best For:** API-based integrations

**Pros:**
- ✅ Full control over implementation
- ✅ Can expose REST API for external systems
- ✅ Supports batch operations
- ✅ Easy to version and update

**Cons:**
- ❌ Requires security implementation
- ❌ Must be called explicitly (not automatic)
- ❌ Additional infrastructure

**When to Use:**
- External systems creating records
- Custom import tools
- Migration scripts
- API-first architecture

---

## Code Samples

### C# Implementation (Dataverse Plugin)

```csharp
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Spaarke.Plugins
{
    /// <summary>
    /// Plugin to apply field inheritance from parent records
    /// Triggers on Create of child entities (Document, Contact, etc.)
    /// </summary>
    public class FieldInheritancePlugin : IPlugin
    {
        // Field mapping configuration (loaded from secure config or config entity)
        private readonly Dictionary<string, Dictionary<string, string>> _fieldMappings;

        public FieldInheritancePlugin(string unsecureConfig, string secureConfig)
        {
            // Load configuration from plugin config or retrieve from Dataverse
            _fieldMappings = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(
                secureConfig ?? GetDefaultConfiguration()
            );
        }

        public void Execute(IServiceProvider serviceProvider)
        {
            // Get plugin execution context
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            try
            {
                tracingService.Trace("FieldInheritancePlugin: Starting execution");

                // Get the child entity being created
                if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity)
                {
                    Entity childEntity = (Entity)context.InputParameters["Target"];

                    tracingService.Trace($"FieldInheritancePlugin: Processing entity {childEntity.LogicalName}");

                    // Check if this entity has a parent reference that we should inherit from
                    // Common patterns: regardingobjectid, sprk_matter, etc.
                    EntityReference parentReference = GetParentReference(childEntity);

                    if (parentReference != null)
                    {
                        tracingService.Trace($"FieldInheritancePlugin: Found parent reference: {parentReference.LogicalName} ({parentReference.Id})");

                        // Apply field inheritance
                        ApplyFieldMappings(service, tracingService, childEntity, parentReference);
                    }
                    else
                    {
                        tracingService.Trace("FieldInheritancePlugin: No parent reference found, skipping field inheritance");
                    }
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace($"FieldInheritancePlugin: Error - {ex.Message}");
                throw new InvalidPluginExecutionException($"Field inheritance failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Apply field mappings from parent record to child entity
        /// </summary>
        private void ApplyFieldMappings(
            IOrganizationService service,
            ITracingService tracingService,
            Entity childEntity,
            EntityReference parentReference)
        {
            string parentEntityName = parentReference.LogicalName;
            Guid parentRecordId = parentReference.Id;

            tracingService.Trace($"Applying field mappings for parent entity: {parentEntityName}");

            // 1. Get field mappings for this parent entity type
            if (!_fieldMappings.ContainsKey(parentEntityName))
            {
                tracingService.Trace($"No field mappings configured for {parentEntityName}");
                return;
            }

            Dictionary<string, string> mappings = _fieldMappings[parentEntityName];

            // 2. Retrieve parent record with required fields
            ColumnSet selectFields = GetParentSelectFields(parentEntityName);
            Entity parentRecord = service.Retrieve(parentEntityName, parentRecordId, selectFields);

            tracingService.Trace($"Retrieved parent record with {parentRecord.Attributes.Count} attributes");

            // 3. Apply each field mapping
            foreach (var mapping in mappings)
            {
                string parentField = mapping.Key;
                string childField = mapping.Value;

                if (parentRecord.Contains(parentField) && parentRecord[parentField] != null)
                {
                    object parentValue = parentRecord[parentField];

                    tracingService.Trace($"Processing mapping: {parentField} → {childField}");

                    // Determine if this is a lookup field or simple field
                    if (IsLookupFieldMapping(parentField, childField, parentEntityName))
                    {
                        // LOOKUP FIELD: Create EntityReference
                        tracingService.Trace($"  Detected as LOOKUP field");
                        childEntity[childField] = new EntityReference(parentEntityName, parentRecordId);
                    }
                    else
                    {
                        // SIMPLE FIELD: Copy value directly
                        tracingService.Trace($"  Detected as SIMPLE field, copying value: {parentValue}");
                        childEntity[childField] = parentValue;
                    }
                }
                else
                {
                    tracingService.Trace($"Parent field {parentField} not found or is null, skipping");
                }
            }

            tracingService.Trace("Field inheritance completed successfully");
        }

        /// <summary>
        /// Determine if a field mapping represents a lookup relationship
        /// </summary>
        private bool IsLookupFieldMapping(string parentField, string childField, string parentEntityName)
        {
            // Known lookup field mappings
            var lookupMappings = new Dictionary<string, List<string>>
            {
                { "sprk_matter", new List<string> { "sprk_matter" } },
                { "account", new List<string> { "parentaccountid", "accountid" } },
                { "contact", new List<string> { "parentcontactid", "contactid" } }
            };

            // Pattern 1: Child field matches parent entity name
            if (childField == parentEntityName)
            {
                return true;
            }

            // Pattern 2: Known lookup fields for this parent entity
            if (lookupMappings.ContainsKey(parentEntityName))
            {
                if (lookupMappings[parentEntityName].Contains(childField))
                {
                    return true;
                }
            }

            // Pattern 3: Common lookup field naming patterns
            if (childField.EndsWith("id") || childField.EndsWith("Id"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get the parent reference from child entity
        /// Checks common parent reference fields
        /// </summary>
        private EntityReference GetParentReference(Entity childEntity)
        {
            // Check for common parent reference fields
            string[] parentFieldNames = new[]
            {
                "sprk_matter",          // Document → Matter
                "regardingobjectid",    // Activity → Any entity
                "parentaccountid",      // Contact → Account
                "parentcontactid"       // Contact → Contact
            };

            foreach (string fieldName in parentFieldNames)
            {
                if (childEntity.Contains(fieldName) && childEntity[fieldName] is EntityReference)
                {
                    return (EntityReference)childEntity[fieldName];
                }
            }

            return null;
        }

        /// <summary>
        /// Get fields to retrieve from parent record
        /// </summary>
        private ColumnSet GetParentSelectFields(string parentEntityName)
        {
            var fieldSelections = new Dictionary<string, string[]>
            {
                {
                    "sprk_matter",
                    new[] { "sprk_name", "sprk_containerid", "ownerid", "sprk_matternumber" }
                },
                {
                    "account",
                    new[] { "name", "address1_composite", "ownerid" }
                },
                {
                    "contact",
                    new[] { "fullname", "ownerid" }
                }
            };

            if (fieldSelections.ContainsKey(parentEntityName))
            {
                return new ColumnSet(fieldSelections[parentEntityName]);
            }

            return new ColumnSet("name"); // Default fallback
        }

        /// <summary>
        /// Default configuration if not provided in plugin config
        /// </summary>
        private string GetDefaultConfiguration()
        {
            return @"{
                ""sprk_matter"": {
                    ""sprk_matternumber"": ""sprk_matter"",
                    ""sprk_containerid"": ""sprk_containerid""
                },
                ""account"": {
                    ""name"": ""sprk_companyname"",
                    ""address1_composite"": ""address1_composite""
                }
            }";
        }
    }
}
```

**Plugin Registration:**
```
Entity: sprk_document
Message: Create
Stage: Pre-Operation (Before Save)
Mode: Synchronous
Configuration: JSON field mapping config
```

---

### C# Implementation (Azure Function)

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Collections.Generic;

namespace Spaarke.Functions
{
    /// <summary>
    /// Azure Function to handle bulk record creation with field inheritance
    /// Endpoint: POST /api/BulkCreateWithInheritance
    /// </summary>
    public static class BulkCreateWithInheritance
    {
        [FunctionName("BulkCreateWithInheritance")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("BulkCreateWithInheritance: Function triggered");

            try
            {
                // 1. Parse request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var request = JsonConvert.DeserializeObject<BulkCreateRequest>(requestBody);

                log.LogInformation($"Processing {request.Records.Count} records");

                // 2. Connect to Dataverse
                string connectionString = Environment.GetEnvironmentVariable("DataverseConnectionString");
                var service = new ServiceClient(connectionString);

                if (!service.IsReady)
                {
                    log.LogError("Failed to connect to Dataverse");
                    return new StatusCodeResult(500);
                }

                // 3. Load field mapping configuration
                var fieldMappings = LoadFieldMappingConfiguration();

                // 4. Process each record
                var results = new List<BulkCreateResult>();

                foreach (var record in request.Records)
                {
                    try
                    {
                        // Apply field inheritance
                        Entity childEntity = ApplyFieldMappings(
                            service,
                            log,
                            record.ParentEntityName,
                            record.ParentRecordId,
                            record.ChildEntityName,
                            record.ChildRecordData,
                            fieldMappings
                        );

                        // Create record in Dataverse
                        Guid createdRecordId = service.Create(childEntity);

                        results.Add(new BulkCreateResult
                        {
                            Success = true,
                            RecordId = createdRecordId.ToString(),
                            Message = "Record created successfully"
                        });

                        log.LogInformation($"Created record: {createdRecordId}");
                    }
                    catch (Exception ex)
                    {
                        results.Add(new BulkCreateResult
                        {
                            Success = false,
                            Message = $"Failed to create record: {ex.Message}"
                        });

                        log.LogError($"Error creating record: {ex.Message}");
                    }
                }

                // 5. Return results
                return new OkObjectResult(new
                {
                    TotalRecords = request.Records.Count,
                    SuccessCount = results.FindAll(r => r.Success).Count,
                    FailureCount = results.FindAll(r => !r.Success).Count,
                    Results = results
                });
            }
            catch (Exception ex)
            {
                log.LogError($"Function error: {ex.Message}");
                return new StatusCodeResult(500);
            }
        }

        /// <summary>
        /// Apply field mappings from parent record to child entity
        /// </summary>
        private static Entity ApplyFieldMappings(
            ServiceClient service,
            ILogger log,
            string parentEntityName,
            Guid parentRecordId,
            string childEntityName,
            Dictionary<string, object> childRecordData,
            Dictionary<string, Dictionary<string, string>> fieldMappings)
        {
            log.LogInformation($"Applying field mappings: {parentEntityName} → {childEntityName}");

            // 1. Create child entity with provided data
            Entity childEntity = new Entity(childEntityName);
            foreach (var kvp in childRecordData)
            {
                childEntity[kvp.Key] = kvp.Value;
            }

            // 2. Check if field mappings exist for this parent entity
            if (!fieldMappings.ContainsKey(parentEntityName))
            {
                log.LogWarning($"No field mappings configured for {parentEntityName}");
                return childEntity;
            }

            Dictionary<string, string> mappings = fieldMappings[parentEntityName];

            // 3. Retrieve parent record
            ColumnSet selectFields = GetParentSelectFields(parentEntityName);
            Entity parentRecord = service.Retrieve(parentEntityName, parentRecordId, selectFields);

            log.LogInformation($"Retrieved parent record with {parentRecord.Attributes.Count} attributes");

            // 4. Apply each field mapping
            foreach (var mapping in mappings)
            {
                string parentField = mapping.Key;
                string childField = mapping.Value;

                if (parentRecord.Contains(parentField) && parentRecord[parentField] != null)
                {
                    object parentValue = parentRecord[parentField];

                    if (IsLookupFieldMapping(parentField, childField, parentEntityName))
                    {
                        // LOOKUP FIELD
                        childEntity[childField] = new EntityReference(parentEntityName, parentRecordId);
                        log.LogInformation($"  LOOKUP: {parentField} → {childField}");
                    }
                    else
                    {
                        // SIMPLE FIELD
                        childEntity[childField] = parentValue;
                        log.LogInformation($"  SIMPLE: {parentField} → {childField} = {parentValue}");
                    }
                }
            }

            return childEntity;
        }

        /// <summary>
        /// Determine if field mapping is lookup or simple
        /// (Same logic as plugin version)
        /// </summary>
        private static bool IsLookupFieldMapping(string parentField, string childField, string parentEntityName)
        {
            var lookupMappings = new Dictionary<string, List<string>>
            {
                { "sprk_matter", new List<string> { "sprk_matter" } },
                { "account", new List<string> { "parentaccountid", "accountid" } },
                { "contact", new List<string> { "parentcontactid", "contactid" } }
            };

            if (childField == parentEntityName) return true;
            if (lookupMappings.ContainsKey(parentEntityName) && lookupMappings[parentEntityName].Contains(childField)) return true;
            if (childField.EndsWith("id") || childField.EndsWith("Id")) return true;

            return false;
        }

        /// <summary>
        /// Get fields to retrieve from parent
        /// (Same logic as plugin version)
        /// </summary>
        private static ColumnSet GetParentSelectFields(string parentEntityName)
        {
            var fieldSelections = new Dictionary<string, string[]>
            {
                { "sprk_matter", new[] { "sprk_name", "sprk_containerid", "ownerid", "sprk_matternumber" } },
                { "account", new[] { "name", "address1_composite", "ownerid" } },
                { "contact", new[] { "fullname", "ownerid" } }
            };

            return fieldSelections.ContainsKey(parentEntityName)
                ? new ColumnSet(fieldSelections[parentEntityName])
                : new ColumnSet("name");
        }

        /// <summary>
        /// Load field mapping configuration from App Settings
        /// </summary>
        private static Dictionary<string, Dictionary<string, string>> LoadFieldMappingConfiguration()
        {
            string configJson = Environment.GetEnvironmentVariable("FieldMappingConfig");
            return JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(configJson);
        }
    }

    #region Request/Response Models

    public class BulkCreateRequest
    {
        public List<RecordToCreate> Records { get; set; }
    }

    public class RecordToCreate
    {
        public string ParentEntityName { get; set; }
        public Guid ParentRecordId { get; set; }
        public string ChildEntityName { get; set; }
        public Dictionary<string, object> ChildRecordData { get; set; }
    }

    public class BulkCreateResult
    {
        public bool Success { get; set; }
        public string RecordId { get; set; }
        public string Message { get; set; }
    }

    #endregion
}
```

**Function Configuration (local.settings.json):**
```json
{
  "Values": {
    "DataverseConnectionString": "AuthType=OAuth;Username=...;Password=...;Url=https://org.crm.dynamics.com",
    "FieldMappingConfig": "{\"sprk_matter\":{\"sprk_matternumber\":\"sprk_matter\",\"sprk_containerid\":\"sprk_containerid\"}}"
  }
}
```

**Example Request:**
```json
POST https://your-function.azurewebsites.net/api/BulkCreateWithInheritance

{
  "Records": [
    {
      "ParentEntityName": "sprk_matter",
      "ParentRecordId": "abc-123-guid",
      "ChildEntityName": "sprk_document",
      "ChildRecordData": {
        "sprk_documenttitle": "Contract.pdf",
        "sprk_description": "Service agreement"
      }
    },
    {
      "ParentEntityName": "sprk_matter",
      "ParentRecordId": "abc-123-guid",
      "ChildEntityName": "sprk_document",
      "ChildRecordData": {
        "sprk_documenttitle": "Invoice.pdf",
        "sprk_description": "Monthly invoice"
      }
    }
  ]
}
```

---

### JavaScript Implementation (Azure Function)

```javascript
const { ServiceClient } = require("@microsoft/powerplatform-dataverse-client");

/**
 * Azure Function to handle bulk record creation with field inheritance
 * Language: JavaScript (similar to frontend TypeScript)
 */
module.exports = async function (context, req) {
    context.log("BulkCreateWithInheritance: Function triggered");

    try {
        // 1. Parse request
        const { records } = req.body;
        context.log(`Processing ${records.length} records`);

        // 2. Connect to Dataverse
        const service = new ServiceClient(process.env.DataverseConnectionString);

        // 3. Load field mapping configuration
        const fieldMappings = JSON.parse(process.env.FieldMappingConfig);

        // 4. Process each record
        const results = [];

        for (const record of records) {
            try {
                // Apply field inheritance
                const childEntity = await applyFieldMappings(
                    service,
                    context,
                    record.parentEntityName,
                    record.parentRecordId,
                    record.childEntityName,
                    record.childRecordData,
                    fieldMappings
                );

                // Create record
                const createdRecordId = await service.create(childEntity);

                results.push({
                    success: true,
                    recordId: createdRecordId,
                    message: "Record created successfully"
                });

                context.log(`Created record: ${createdRecordId}`);
            } catch (err) {
                results.push({
                    success: false,
                    message: `Failed: ${err.message}`
                });

                context.log(`Error: ${err.message}`);
            }
        }

        // 5. Return results
        context.res = {
            status: 200,
            body: {
                totalRecords: records.length,
                successCount: results.filter(r => r.success).length,
                failureCount: results.filter(r => !r.success).length,
                results
            }
        };
    } catch (err) {
        context.log(`Function error: ${err.message}`);
        context.res = {
            status: 500,
            body: { error: err.message }
        };
    }
};

/**
 * Apply field mappings from parent to child
 * NOTE: This is nearly IDENTICAL to frontend TypeScript version!
 */
async function applyFieldMappings(service, context, parentEntityName, parentRecordId, childEntityName, childRecordData, fieldMappings) {
    context.log(`Applying field mappings: ${parentEntityName} → ${childEntityName}`);

    // 1. Start with provided child data
    const childEntity = {
        "@odata.type": `Microsoft.Dynamics.CRM.${childEntityName}`,
        ...childRecordData
    };

    // 2. Check if mappings exist for this parent
    if (!fieldMappings[parentEntityName]) {
        context.log(`No mappings for ${parentEntityName}`);
        return childEntity;
    }

    const mappings = fieldMappings[parentEntityName];

    // 3. Retrieve parent record
    const selectFields = getParentSelectFields(parentEntityName);
    const parentRecord = await service.retrieve(
        parentEntityName,
        parentRecordId,
        selectFields.join(",")
    );

    context.log(`Retrieved parent with ${Object.keys(parentRecord).length} attributes`);

    // 4. Apply each mapping (SAME LOGIC AS FRONTEND!)
    for (const [parentField, childField] of Object.entries(mappings)) {
        const parentValue = parentRecord[parentField];

        if (parentValue !== undefined && parentValue !== null) {
            if (isLookupFieldMapping(parentField, childField, parentEntityName)) {
                // LOOKUP FIELD: Use OData bind syntax
                const entitySetName = getEntitySetName(parentEntityName);
                childEntity[`${childField}@odata.bind`] = `/${entitySetName}(${parentRecordId})`;

                context.log(`  LOOKUP: ${parentField} → ${childField}`);
            } else {
                // SIMPLE FIELD: Copy value
                childEntity[childField] = parentValue;

                context.log(`  SIMPLE: ${parentField} → ${childField} = ${parentValue}`);
            }
        }
    }

    return childEntity;
}

/**
 * Determine if field is lookup or simple
 * NOTE: IDENTICAL to frontend TypeScript version!
 */
function isLookupFieldMapping(parentField, childField, parentEntityName) {
    const lookupMappings = {
        'sprk_matter': ['sprk_matter'],
        'account': ['parentaccountid', 'accountid'],
        'contact': ['parentcontactid', 'contactid']
    };

    // Pattern 1: Child field matches parent entity
    if (childField === parentEntityName) {
        return true;
    }

    // Pattern 2: Known lookup fields
    if (lookupMappings[parentEntityName]?.includes(childField)) {
        return true;
    }

    // Pattern 3: Ends with "id" or "Id"
    if (childField.endsWith('id') || childField.endsWith('Id')) {
        return true;
    }

    return false;
}

/**
 * Get entity set name for OData URLs
 * NOTE: IDENTICAL to frontend TypeScript version!
 */
function getEntitySetName(entityName) {
    const entitySetMap = {
        'sprk_matter': 'sprk_matters',
        'sprk_client': 'sprk_clients',
        'account': 'accounts',
        'contact': 'contacts'
    };

    return entitySetMap[entityName] || `${entityName}s`;
}

/**
 * Get fields to retrieve from parent
 * NOTE: IDENTICAL to frontend TypeScript version!
 */
function getParentSelectFields(entityName) {
    const fieldMappings = {
        'sprk_matter': ['sprk_name', 'sprk_containerid', '_ownerid_value', 'sprk_matternumber'],
        'account': ['name', 'address1_composite', '_ownerid_value'],
        'contact': ['fullname', '_ownerid_value']
    };

    return fieldMappings[entityName] || ['name'];
}
```

**Key Observation:** The JavaScript Azure Function version is **almost identical** to the frontend TypeScript version! This demonstrates how well the logic translates.

---

## Configuration Management

### Configuration Storage Options

#### Option 1: Dataverse Configuration Table (Recommended)

**Pros:**
- ✅ Centralized configuration
- ✅ Version control via solutions
- ✅ Easy to update without redeployment
- ✅ Frontend and backend read same source

**Structure:**
```
Entity: sprk_fieldmappingconfig

Columns:
- sprk_name (Text): Configuration name (e.g., "Matter to Document")
- sprk_parententityname (Text): Parent entity logical name
- sprk_childentityname (Text): Child entity logical name
- sprk_mappingjson (Multiline Text): JSON mapping configuration
- sprk_isactive (Boolean): Enable/disable mapping
```

**Example Record:**
| Name | Parent Entity | Child Entity | Mapping JSON |
|------|---------------|--------------|--------------|
| Matter to Document | sprk_matter | sprk_document | `{"sprk_matternumber":"sprk_matter","sprk_containerid":"sprk_containerid"}` |
| Account to Contact | account | contact | `{"name":"sprk_companyname","address1_composite":"address1_composite"}` |

**Frontend Loading:**
```typescript
// UniversalQuickCreatePCF.ts
private async loadConfiguration(context: ComponentFramework.Context<IInputs>): Promise<void> {
    // Retrieve config from Dataverse
    const configRecords = await context.webAPI.retrieveMultipleRecords(
        "sprk_fieldmappingconfig",
        "?$select=sprk_parententityname,sprk_mappingjson&$filter=sprk_isactive eq true"
    );

    // Parse and store
    configRecords.entities.forEach(record => {
        const parentEntity = record.sprk_parententityname;
        const mappingJson = record.sprk_mappingjson;
        this.defaultValueMappings[parentEntity] = JSON.parse(mappingJson);
    });
}
```

**Backend Loading (C# Plugin):**
```csharp
private Dictionary<string, Dictionary<string, string>> LoadConfiguration(IOrganizationService service)
{
    var query = new QueryExpression("sprk_fieldmappingconfig")
    {
        ColumnSet = new ColumnSet("sprk_parententityname", "sprk_mappingjson"),
        Criteria = new FilterExpression
        {
            Conditions = {
                new ConditionExpression("sprk_isactive", ConditionOperator.Equal, true)
            }
        }
    };

    var results = service.RetrieveMultiple(query);
    var config = new Dictionary<string, Dictionary<string, string>>();

    foreach (var entity in results.Entities)
    {
        string parentEntity = entity.GetAttributeValue<string>("sprk_parententityname");
        string mappingJson = entity.GetAttributeValue<string>("sprk_mappingjson");
        config[parentEntity] = JsonConvert.DeserializeObject<Dictionary<string, string>>(mappingJson);
    }

    return config;
}
```

---

#### Option 2: Azure App Settings

**Pros:**
- ✅ Easy to update via Azure Portal
- ✅ Environment-specific configs (dev/test/prod)
- ✅ Secure storage

**Cons:**
- ❌ Requires Azure Function redeployment
- ❌ Frontend cannot access (PCF uses manifest params)
- ❌ Separate configs for frontend/backend

**Configuration:**
```json
// Azure App Settings
{
  "FieldMappingConfig": {
    "sprk_matter": {
      "sprk_matternumber": "sprk_matter",
      "sprk_containerid": "sprk_containerid"
    },
    "account": {
      "name": "sprk_companyname",
      "address1_composite": "address1_composite"
    }
  }
}
```

---

#### Option 3: Shared JSON Configuration File

**Pros:**
- ✅ Single source of truth
- ✅ Version controlled in Git
- ✅ Easy to review changes

**Cons:**
- ❌ Requires deployment to update
- ❌ Frontend cannot access file directly

**File:** `shared-config/field-mappings.json`
```json
{
  "version": "1.0",
  "lastUpdated": "2025-10-07",
  "mappings": {
    "sprk_matter": {
      "sprk_matternumber": "sprk_matter",
      "sprk_containerid": "sprk_containerid"
    },
    "account": {
      "name": "sprk_companyname",
      "address1_composite": "address1_composite"
    }
  }
}
```

---

### Recommended Approach

**Use Dataverse Configuration Table** for:
- Production environments
- Non-technical users managing config
- Frequent configuration changes

**Use Azure App Settings** for:
- Azure Functions only (backend)
- Environment-specific overrides
- Sensitive configurations

**Use JSON File** for:
- Development/testing
- Version control of configs
- Documentation purposes

---

## High-Level Implementation Plan

### Phase 1: Design & Planning (1-2 weeks)

**Tasks:**
1. ✅ Review current frontend implementation (THIS DOCUMENT)
2. Choose implementation option (Plugin, Azure Function, or both)
3. Design configuration storage strategy
4. Define trigger scenarios (import, API, scheduled)
5. Create technical specification document
6. Estimate development effort

**Deliverables:**
- Technical specification
- Architecture diagrams
- Development timeline
- Resource requirements

---

### Phase 2: Backend Development (2-4 weeks)

**Option A: Dataverse Plugin**
1. Set up plugin development environment
2. Create `FieldInheritancePlugin` class
3. Implement `ApplyFieldMappings()` method
4. Implement `IsLookupFieldMapping()` method
5. Add configuration loading (from config table or secure config)
6. Add error handling and logging
7. Write unit tests

**Option B: Azure Function**
1. Create Azure Function project
2. Implement `BulkCreateWithInheritance` function
3. Implement core field mapping logic
4. Add Dataverse connection
5. Add configuration loading
6. Add error handling and logging
7. Write integration tests

**Shared Tasks:**
1. Port TypeScript logic to C#/JavaScript
2. Validate lookup field detection logic
3. Test with sample data

**Deliverables:**
- Compiled plugin DLL OR Azure Function deployment package
- Unit tests (80%+ coverage)
- Technical documentation

---

### Phase 3: Configuration Setup (1 week)

**Tasks:**
1. Create `sprk_fieldmappingconfig` entity in Dataverse (if using config table)
2. Add field mapping records for each parent-child relationship
3. Configure Azure App Settings (if using Azure Function)
4. Set up environment variables
5. Document configuration management process

**Deliverables:**
- Configuration entity created
- Sample configurations loaded
- Admin documentation

---

### Phase 4: Testing (2-3 weeks)

**Unit Testing:**
- Test `ApplyFieldMappings()` with mock data
- Test `IsLookupFieldMapping()` with various field names
- Test configuration loading
- Test error scenarios (parent not found, null values, etc.)

**Integration Testing:**
- Test with real Dataverse environment
- Test bulk import of 100+ records
- Test lookup field creation
- Test simple field copying
- Verify no duplicate records created
- Verify correct parent-child relationships

**Performance Testing:**
- Measure processing time for 1000 records
- Identify bottlenecks
- Optimize Dataverse queries
- Test concurrent execution

**User Acceptance Testing:**
- Business users test bulk import scenarios
- Validate field mappings are correct
- Confirm error messages are helpful

**Deliverables:**
- Test plan
- Test results report
- Performance benchmarks

---

### Phase 5: Deployment (1 week)

**Dataverse Plugin Deployment:**
1. Register plugin assembly
2. Register plugin steps (Create message, Pre-Operation)
3. Configure entity filters (sprk_document, contact, etc.)
4. Enable plugin in test environment
5. Validate plugin execution
6. Deploy to production

**Azure Function Deployment:**
1. Create Azure Function App
2. Configure App Settings
3. Deploy function code
4. Configure authentication/authorization
5. Create API Management policy (if needed)
6. Set up monitoring and alerts

**Deliverables:**
- Deployed plugin/function
- Deployment runbook
- Rollback plan

---

### Phase 6: Documentation & Training (1 week)

**Documentation:**
- Developer documentation (code comments, architecture)
- Admin documentation (configuration management)
- User documentation (bulk import process)
- Troubleshooting guide

**Training:**
- Train admins on configuration management
- Train users on bulk import process
- Create video tutorials

**Deliverables:**
- Complete documentation set
- Training materials
- Knowledge base articles

---

### Total Timeline: 8-12 weeks

| Phase | Duration | Dependencies |
|-------|----------|--------------|
| Phase 1: Design & Planning | 1-2 weeks | None |
| Phase 2: Backend Development | 2-4 weeks | Phase 1 complete |
| Phase 3: Configuration Setup | 1 week | Phase 2 complete |
| Phase 4: Testing | 2-3 weeks | Phase 3 complete |
| Phase 5: Deployment | 1 week | Phase 4 complete |
| Phase 6: Documentation & Training | 1 week | Phase 5 complete |

---

## Testing Strategy

### Unit Test Cases

#### Test Case 1: Simple Field Mapping
```csharp
[TestMethod]
public void ApplyFieldMappings_SimpleField_CopiesValueCorrectly()
{
    // Arrange
    var parentRecord = new Entity("sprk_matter");
    parentRecord["sprk_containerid"] = "b!ABC123";

    var mappings = new Dictionary<string, string>
    {
        { "sprk_containerid", "sprk_containerid" }
    };

    // Act
    var result = ApplyFieldMappings(parentRecord, "sprk_matter", Guid.NewGuid(), mappings);

    // Assert
    Assert.AreEqual("b!ABC123", result["sprk_containerid"]);
}
```

#### Test Case 2: Lookup Field Mapping
```csharp
[TestMethod]
public void ApplyFieldMappings_LookupField_CreatesEntityReference()
{
    // Arrange
    var parentId = Guid.NewGuid();
    var parentRecord = new Entity("sprk_matter");
    parentRecord["sprk_matternumber"] = "MAT-2025-001";

    var mappings = new Dictionary<string, string>
    {
        { "sprk_matternumber", "sprk_matter" }
    };

    // Act
    var result = ApplyFieldMappings(parentRecord, "sprk_matter", parentId, mappings);

    // Assert
    Assert.IsInstanceOfType(result["sprk_matter"], typeof(EntityReference));
    var entityRef = (EntityReference)result["sprk_matter"];
    Assert.AreEqual("sprk_matter", entityRef.LogicalName);
    Assert.AreEqual(parentId, entityRef.Id);
}
```

#### Test Case 3: Null Value Handling
```csharp
[TestMethod]
public void ApplyFieldMappings_NullParentValue_SkipsMapping()
{
    // Arrange
    var parentRecord = new Entity("sprk_matter");
    parentRecord["sprk_containerid"] = null;

    var mappings = new Dictionary<string, string>
    {
        { "sprk_containerid", "sprk_containerid" }
    };

    // Act
    var result = ApplyFieldMappings(parentRecord, "sprk_matter", Guid.NewGuid(), mappings);

    // Assert
    Assert.IsFalse(result.Contains("sprk_containerid"));
}
```

#### Test Case 4: Lookup Detection
```csharp
[TestMethod]
public void IsLookupFieldMapping_ChildFieldMatchesParentEntity_ReturnsTrue()
{
    // Act
    bool result = IsLookupFieldMapping("sprk_matternumber", "sprk_matter", "sprk_matter");

    // Assert
    Assert.IsTrue(result);
}

[TestMethod]
public void IsLookupFieldMapping_SimpleField_ReturnsFalse()
{
    // Act
    bool result = IsLookupFieldMapping("sprk_containerid", "sprk_containerid", "sprk_matter");

    // Assert
    Assert.IsFalse(result);
}
```

### Integration Test Cases

#### Test Case 5: Bulk Import from CSV
```
Scenario: Import 100 documents from CSV file
Given: CSV with columns: MatterId, DocumentTitle, Description
When: Azure Function processes CSV
Then: 100 Document records created with correct Matter lookups
And: All Container IDs inherited from parent Matters
```

#### Test Case 6: API-Based Creation
```
Scenario: External system creates document via API
Given: API request with parent Matter ID
When: Plugin executes on record create
Then: Document created with inherited fields
And: Matter lookup correctly populated
```

### Performance Benchmarks

| Scenario | Target | Acceptable | Unacceptable |
|----------|--------|-----------|--------------|
| Single record creation | <500ms | <1s | >2s |
| Bulk create (100 records) | <30s | <60s | >120s |
| Bulk create (1000 records) | <5min | <10min | >20min |
| Configuration load | <200ms | <500ms | >1s |

---

## Appendix

### A. Frontend to Backend Translation Guide

| Frontend Code | Backend C# Code | Backend JavaScript Code |
|---------------|-----------------|-------------------------|
| `Record<string, unknown>` | `Entity` | Plain object `{}` |
| `context.webAPI.retrieveRecord()` | `service.Retrieve()` | `service.retrieve()` |
| `@odata.bind` syntax | `EntityReference` | `{"@odata.bind": "..."}` |
| `for...of` loop | `foreach` loop | `for...of` loop |
| TypeScript interfaces | C# classes | JavaScript objects |
| `undefined` / `null` | `null` | `undefined` / `null` |

### B. Common Parent-Child Relationships

| Parent Entity | Child Entity | Common Use Case |
|---------------|--------------|-----------------|
| sprk_matter | sprk_document | Matter documents |
| sprk_matter | task | Matter tasks |
| account | contact | Account contacts |
| account | opportunity | Sales opportunities |
| contact | task | Contact follow-ups |
| sprk_client | sprk_matter | Client matters |

### C. Field Mapping Examples

#### Document from Matter
```json
{
  "sprk_matter": {
    "sprk_matternumber": "sprk_matter",
    "sprk_containerid": "sprk_containerid",
    "_ownerid_value": "ownerid"
  }
}
```

#### Contact from Account
```json
{
  "account": {
    "name": "sprk_companyname",
    "address1_composite": "address1_composite",
    "_ownerid_value": "ownerid"
  }
}
```

#### Task from Matter
```json
{
  "sprk_matter": {
    "sprk_name": "subject",
    "_ownerid_value": "ownerid"
  }
}
```

### D. Error Handling Scenarios

| Error Scenario | Frontend Behavior | Backend Behavior |
|----------------|-------------------|------------------|
| Parent record not found | Show error message | Log error, skip mapping |
| Parent field is null | Skip mapping | Skip mapping |
| Invalid configuration | Show error message | Log error, use defaults |
| Dataverse connection fails | Show error message | Throw exception, retry |
| Permission denied | Show error message | Log error, notify admin |

### E. Security Considerations

**Frontend (PCF Control):**
- User must have read permission on parent entity
- User must have create permission on child entity
- Field-level security applies

**Backend (Plugin/Function):**
- Plugin runs with system privileges (bypass security)
- Azure Function uses service account
- Must validate user permissions explicitly if needed
- Sensitive fields should be excluded from mappings

### F. Monitoring & Logging

**Key Metrics to Track:**
- Number of records processed
- Average processing time
- Success vs. failure rate
- Configuration load time
- Parent record retrieval time

**Recommended Logging:**
```
[INFO] FieldInheritance: Processing 100 records
[DEBUG] FieldInheritance: Retrieved parent sprk_matter(guid-123)
[DEBUG] FieldInheritance: Applying mapping: sprk_containerid → sprk_containerid
[DEBUG] FieldInheritance: Detected LOOKUP: sprk_matternumber → sprk_matter
[INFO] FieldInheritance: Created record sprk_document(guid-456)
[ERROR] FieldInheritance: Failed to retrieve parent: Record not found
```

### G. Future Enhancements

**Potential Features:**
1. **Conditional Mappings** - Apply different mappings based on parent field values
2. **Transformation Functions** - Apply transformations during mapping (uppercase, concat, etc.)
3. **Multi-Parent Inheritance** - Inherit from multiple parent records
4. **Validation Rules** - Validate inherited values before creating record
5. **Audit Trail** - Track which fields were inherited vs. user-entered
6. **UI Configuration** - Visual configuration builder for field mappings
7. **Rollback Support** - Undo bulk operations if errors occur

---

## Document Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0.0 | 2025-10-07 | System | Initial design document created |

---

## References

### Internal Documentation
- [Universal Quick Create - Admin Guide](UNIVERSAL-QUICK-CREATE-ADMIN-GUIDE.md)
- [Field Inheritance Flow](../dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/FIELD-INHERITANCE-FLOW.md)
- [Sprint 7B Task 4 Completion Summary](../dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/TASK-7B-4-COMPLETION-SUMMARY.md)

### Frontend Implementation
- `src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts` (Lines 317-421)
- `src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityFieldDefinitions.ts`

### Microsoft Documentation
- [Dataverse Plugin Development](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/plug-ins)
- [Azure Functions with Dataverse](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/overview)
- [Entity References (Lookups)](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/entity-references)

---

**Document Status:** READY FOR IMPLEMENTATION
**Next Action:** Review with development team and select implementation option
**Owner:** Development Team
**Created:** 2025-10-07
