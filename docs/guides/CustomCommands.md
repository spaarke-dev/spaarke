# Custom Commands Guide

Complete guide for creating custom commands in the Universal Dataset Grid PCF component.

---

## Overview

The Universal Dataset Grid supports **custom commands** - actions that execute:
- **Custom APIs** (unbound or bound)
- **Actions** (OData Actions)
- **Functions** (OData Functions)
- **Workflows** (Classic Workflows)

Custom commands appear in the toolbar alongside built-in commands (Open, Create, Delete, Refresh).

---

## Custom Command Configuration

### ICustomCommandConfiguration Interface

```typescript
interface ICustomCommandConfiguration {
  // Display
  label: string;
  icon?: string;

  // Action
  actionType: "customapi" | "action" | "function" | "workflow";
  actionName: string;
  parameters?: Record<string, string>;

  // Selection
  requiresSelection?: boolean;
  minSelection?: number;
  maxSelection?: number;

  // Confirmation
  confirmationMessage?: string;

  // Behavior
  refresh?: boolean;
}
```

---

## Configuration Properties

### Display Properties

#### `label` (Required)
- **Type**: `string`
- **Description**: Button text displayed in toolbar

**Example**:
```json
{
  "label": "Approve Invoice"
}
```

---

#### `icon` (Optional)
- **Type**: `string`
- **Description**: Fluent UI icon name (without "Regular" suffix)

**Available Icons**:
- `Checkmark`, `Dismiss`, `DocumentArrowRight`, `Mail`, `CloudUpload`, `CloudDownload`, `Eye`, `Edit`, `Delete`, `Send`, `Archive`, etc.

**Icon Reference**: [Fluent UI Icons](https://react.fluentui.dev/?path=/docs/icons-catalog--default)

**Example**:
```json
{
  "label": "Approve",
  "icon": "Checkmark"
}
```

**Rendering**:
- Normal toolbar: Icon + Label
- Compact toolbar: Icon only (label as tooltip)

---

### Action Properties

#### `actionType` (Required)
- **Type**: `"customapi" | "action" | "function" | "workflow"`
- **Description**: Type of Dataverse action to execute

| Type | Use Case | Example |
|------|----------|---------|
| `customapi` | Custom API (most common) | Upload document, generate report |
| `action` | OData Action | Send email, assign record |
| `function` | OData Function | Calculate total, validate data |
| `workflow` | Classic Workflow | Multi-step business process |

---

#### `actionName` (Required)
- **Type**: `string`
- **Description**: Unique name of the action in Dataverse

**Naming Conventions**:
- Custom API: `prefix_ActionName` (e.g., `sprk_ApproveInvoice`)
- Action: `prefix_ActionName` (e.g., `sprk_SendEmail`)
- Function: `prefix_FunctionName` (e.g., `sprk_CalculateTotal`)
- Workflow: Workflow unique name (e.g., `sprk_QualifyLead`)

**Example**:
```json
{
  "actionType": "customapi",
  "actionName": "sprk_ApproveInvoice"
}
```

---

#### `parameters` (Optional)
- **Type**: `Record<string, string>`
- **Description**: Input parameters for the action (supports token interpolation)

**Parameter Types**:
- **Literal values**: `"Status": "Approved"`
- **Tokens**: `"RecordId": "{selectedRecordId}"`

**Example**:
```json
{
  "parameters": {
    "InvoiceId": "{selectedRecordId}",
    "Status": "Approved",
    "ApprovedBy": "{currentUserId}"
  }
}
```

---

### Selection Properties

#### `requiresSelection` (Optional)
- **Type**: `boolean`
- **Default**: `false`
- **Description**: Whether command requires selected record(s)

**Example**:
```json
{
  "requiresSelection": true
}
```

**Behavior**:
- `true`: Button disabled when no records selected
- `false`: Button always enabled

---

#### `minSelection` (Optional)
- **Type**: `number`
- **Default**: `1`
- **Description**: Minimum number of records that must be selected

**Example**:
```json
{
  "requiresSelection": true,
  "minSelection": 2  // Requires at least 2 records
}
```

**Error Message**: "Select at least {minSelection} record(s)"

---

#### `maxSelection` (Optional)
- **Type**: `number`
- **Default**: `undefined` (no limit)
- **Description**: Maximum number of records that can be selected

**Example**:
```json
{
  "requiresSelection": true,
  "maxSelection": 10  // Max 10 records
}
```

**Error Message**: "Select no more than {maxSelection} record(s)"

---

### Confirmation Properties

#### `confirmationMessage` (Optional)
- **Type**: `string`
- **Description**: Confirmation dialog message (supports token interpolation)

**Example**:
```json
{
  "confirmationMessage": "Delete {selectedCount} record(s)?"
}
```

**Behavior**:
- If specified: Shows confirmation dialog before execution
- If omitted: Executes immediately

---

### Behavior Properties

#### `refresh` (Optional)
- **Type**: `boolean`
- **Default**: `false`
- **Description**: Refresh grid after successful execution

**Example**:
```json
{
  "refresh": true
}
```

**Use Cases**:
- Command modifies records (approve, update status)
- Command creates new records
- Command deletes records

---

## Token Interpolation

### Available Tokens

| Token | Type | Description | Example Value |
|-------|------|-------------|---------------|
| `{selectedRecordId}` | `string` | ID of selected record (single selection) | `"a1b2c3d4-..."` |
| `{selectedRecordIds}` | `string` | Comma-separated IDs (multi-selection) | `"id1,id2,id3"` |
| `{selectedRecord}` | `object` | Full record object (single selection) | `{ id: "...", name: "..." }` |
| `{selectedCount}` | `number` | Number of selected records | `3` |
| `{entityName}` | `string` | Logical entity name | `"account"` |
| `{parentRecordId}` | `string` | Parent record ID (if in subgrid) | `"parent-id"` |
| `{currentUserId}` | `string` | Current user's ID | `"user-id"` |

### Usage Examples

**Single Record ID**:
```json
{
  "parameters": {
    "RecordId": "{selectedRecordId}"
  }
}
```

**Multiple Record IDs**:
```json
{
  "parameters": {
    "RecordIds": "{selectedRecordIds}"  // "id1,id2,id3"
  }
}
```

**Full Record Object** (for Actions):
```json
{
  "actionType": "action",
  "parameters": {
    "Target": "{selectedRecord}"
  }
}
```

**Dynamic Message**:
```json
{
  "confirmationMessage": "Process {selectedCount} {entityName} record(s)?"
}
```

---

## Action Types

### Custom API

**Best for**: Most custom business logic

**Example: Approve Invoice**
```json
{
  "approve": {
    "label": "Approve",
    "icon": "Checkmark",
    "actionType": "customapi",
    "actionName": "sprk_ApproveInvoice",
    "requiresSelection": true,
    "minSelection": 1,
    "maxSelection": 10,
    "parameters": {
      "InvoiceIds": "{selectedRecordIds}",
      "ApprovedBy": "{currentUserId}"
    },
    "confirmationMessage": "Approve {selectedCount} invoice(s)?",
    "refresh": true
  }
}
```

**Dataverse Setup**:
```bash
pac customapi create \
  --name "sprk_ApproveInvoice" \
  --displayname "Approve Invoice" \
  --boundentitylogicalname "sprk_invoice" \
  --executeprivileges "None"

pac customapi add-parameter \
  --customapiid <GUID> \
  --name "InvoiceIds" \
  --type "String"

pac customapi add-parameter \
  --customapiid <GUID> \
  --name "ApprovedBy" \
  --type "Guid"
```

---

### Action (OData Action)

**Best for**: Standard Dataverse actions (SendEmail, Assign, etc.)

**Example: Send Email**
```json
{
  "sendEmail": {
    "label": "Send Email",
    "icon": "Mail",
    "actionType": "action",
    "actionName": "sprk_SendEmailToContact",
    "requiresSelection": true,
    "minSelection": 1,
    "maxSelection": 1,
    "parameters": {
      "Target": "{selectedRecord}",
      "Subject": "Hello from Spaarke",
      "Body": "This is an automated email."
    }
  }
}
```

**Dataverse Setup**:
1. Create custom Action in solution
2. Add input parameters (Target, Subject, Body)
3. Implement logic in plugin or workflow

---

### Function (OData Function)

**Best for**: Read-only operations that return data

**Example: Calculate Total**
```json
{
  "calculateTotal": {
    "label": "Calculate Total",
    "icon": "Calculator",
    "actionType": "function",
    "actionName": "sprk_CalculateInvoiceTotal",
    "requiresSelection": true,
    "parameters": {
      "InvoiceId": "{selectedRecordId}"
    }
  }
}
```

**Dataverse Setup**:
1. Create custom Function in solution
2. Add input/output parameters
3. Implement logic in plugin

**Note**: Functions use `retrieveMultipleRecords` API, not `execute`.

---

### Workflow (Classic Workflow)

**Best for**: Legacy workflows (consider Custom APIs for new development)

**Example: Qualify Lead**
```json
{
  "qualify": {
    "label": "Qualify",
    "icon": "CheckboxChecked",
    "actionType": "workflow",
    "actionName": "sprk_QualifyLead",
    "requiresSelection": true,
    "minSelection": 1,
    "confirmationMessage": "Qualify {selectedCount} lead(s)?",
    "refresh": true
  }
}
```

**Dataverse Setup**:
1. Create on-demand workflow
2. Configure workflow steps
3. Publish workflow

---

## Complete Examples

### Example 1: Document Management

```json
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "sprk_document": {
      "customCommands": {
        "upload": {
          "label": "Upload to SharePoint",
          "icon": "CloudUpload",
          "actionType": "customapi",
          "actionName": "sprk_UploadDocument",
          "requiresSelection": true,
          "minSelection": 1,
          "maxSelection": 10,
          "parameters": {
            "DocumentIds": "{selectedRecordIds}",
            "ParentId": "{parentRecordId}"
          },
          "confirmationMessage": "Upload {selectedCount} document(s) to SharePoint?",
          "refresh": true
        },
        "download": {
          "label": "Download",
          "icon": "CloudDownload",
          "actionType": "customapi",
          "actionName": "sprk_DownloadDocument",
          "requiresSelection": true,
          "minSelection": 1,
          "maxSelection": 1,
          "parameters": {
            "DocumentId": "{selectedRecordId}"
          }
        },
        "preview": {
          "label": "Preview",
          "icon": "Eye",
          "actionType": "function",
          "actionName": "sprk_PreviewDocument",
          "requiresSelection": true,
          "minSelection": 1,
          "maxSelection": 1,
          "parameters": {
            "DocumentId": "{selectedRecordId}"
          }
        }
      }
    }
  }
}
```

---

### Example 2: Sales Process

```json
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "opportunity": {
      "customCommands": {
        "generateQuote": {
          "label": "Generate Quote",
          "icon": "DocumentArrowRight",
          "actionType": "customapi",
          "actionName": "sprk_GenerateQuote",
          "requiresSelection": true,
          "minSelection": 1,
          "maxSelection": 1,
          "parameters": {
            "OpportunityId": "{selectedRecordId}"
          },
          "refresh": true
        },
        "assignToMe": {
          "label": "Assign to Me",
          "icon": "Person",
          "actionType": "action",
          "actionName": "Assign",
          "requiresSelection": true,
          "parameters": {
            "Target": "{selectedRecord}",
            "Assignee": "{currentUserId}"
          },
          "refresh": true
        },
        "closeWon": {
          "label": "Close as Won",
          "icon": "CheckboxChecked",
          "actionType": "workflow",
          "actionName": "sprk_CloseOpportunityAsWon",
          "requiresSelection": true,
          "minSelection": 1,
          "confirmationMessage": "Close {selectedCount} opportunity(ies) as Won?",
          "refresh": true
        }
      }
    }
  }
}
```

---

### Example 3: Bulk Operations

```json
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "account": {
      "customCommands": {
        "bulkUpdate": {
          "label": "Update Status",
          "icon": "Edit",
          "actionType": "customapi",
          "actionName": "sprk_BulkUpdateStatus",
          "requiresSelection": true,
          "minSelection": 1,
          "maxSelection": 100,
          "parameters": {
            "AccountIds": "{selectedRecordIds}",
            "NewStatus": "Active"
          },
          "confirmationMessage": "Update status for {selectedCount} account(s)?",
          "refresh": true
        },
        "export": {
          "label": "Export to Excel",
          "icon": "DocumentTableArrowRight",
          "actionType": "customapi",
          "actionName": "sprk_ExportToExcel",
          "requiresSelection": true,
          "parameters": {
            "EntityName": "{entityName}",
            "RecordIds": "{selectedRecordIds}"
          }
        }
      }
    }
  }
}
```

---

## Custom API Implementation

### Example: Approve Invoice Custom API

**1. Create Custom API (pac CLI)**:
```bash
pac customapi create \
  --name "sprk_ApproveInvoice" \
  --displayname "Approve Invoice" \
  --description "Approves one or more invoices" \
  --boundentitylogicalname "sprk_invoice" \
  --executeprivileges "None"
```

**2. Add Input Parameters**:
```bash
pac customapi add-parameter \
  --customapiid <GUID> \
  --name "InvoiceIds" \
  --displayname "Invoice IDs" \
  --type "String" \
  --isoptional false

pac customapi add-parameter \
  --customapiid <GUID> \
  --name "ApprovedBy" \
  --displayname "Approved By" \
  --type "Guid" \
  --isoptional true
```

**3. Implement Plugin**:
```csharp
public class ApproveInvoicePlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
        var service = serviceFactory.CreateOrganizationService(context.UserId);

        // Get parameters
        var invoiceIds = context.InputParameters["InvoiceIds"] as string;
        var approvedBy = context.InputParameters["ApprovedBy"] as Guid?;

        // Parse comma-separated IDs
        var ids = invoiceIds.Split(',').Select(id => new Guid(id.Trim()));

        // Update each invoice
        foreach (var id in ids)
        {
            var invoice = new Entity("sprk_invoice", id);
            invoice["sprk_status"] = new OptionSetValue(2); // Approved
            invoice["sprk_approvedby"] = new EntityReference("systemuser", approvedBy ?? context.UserId);
            invoice["sprk_approvedon"] = DateTime.UtcNow;

            service.Update(invoice);
        }

        // Return success
        context.OutputParameters["Success"] = true;
    }
}
```

**4. Register Plugin**:
```bash
pac plugin push \
  --assembly ApproveInvoicePlugin.dll \
  --settings plugin-registration.json
```

---

## Error Handling

### Validation Errors

**Selection Validation**:
```typescript
// minSelection validation
if (selectedRecords.length < minSelection) {
  throw new Error(`Select at least ${minSelection} record(s)`);
}

// maxSelection validation
if (selectedRecords.length > maxSelection) {
  throw new Error(`Select no more than ${maxSelection} record(s)`);
}
```

**User Experience**:
- Error displayed in notification bar
- Command execution stops
- Grid remains unchanged

---

### Execution Errors

**Custom API Error**:
```typescript
try {
  await context.webAPI.execute(request);
} catch (error) {
  // Error automatically displayed to user
  console.error("Custom API failed:", error);
}
```

**User Experience**:
- Error message from Custom API/plugin displayed
- Grid refreshes if `refresh: true` (even on error)

---

## Best Practices

### 1. Use Custom APIs (Not Workflows)
- **Why**: Better performance, type safety, error handling
- **When**: New development
- **Exception**: Legacy workflows already implemented

### 2. Always Set `requiresSelection` for Record-Specific Commands
```json
{
  "approve": {
    "requiresSelection": true,  // Always required for record actions
    "minSelection": 1
  }
}
```

### 3. Use `confirmationMessage` for Destructive Actions
```json
{
  "delete": {
    "confirmationMessage": "Permanently delete {selectedCount} record(s)?",
    "refresh": true
  }
}
```

### 4. Set Reasonable `maxSelection` Limits
```json
{
  "bulkUpdate": {
    "maxSelection": 100  // Prevent timeouts
  }
}
```

### 5. Use Token Interpolation
```json
{
  "parameters": {
    "RecordIds": "{selectedRecordIds}",  // Not: "selectedRecordIds"
    "EntityName": "{entityName}"
  }
}
```

### 6. Test Error Scenarios
- No selection when required
- Exceeding max selection
- Custom API failure
- Network timeout

---

## Troubleshooting

### Command Not Appearing

**Symptom**: Custom command not visible in toolbar

**Causes**:
1. Command key in `enabledCommands` array (should not be)
2. Configuration invalid (syntax error)
3. Command requires selection but none selected

**Solution**:
1. Remove command key from `enabledCommands`
2. Validate JSON configuration
3. Select a record

---

### Command Button Disabled

**Symptom**: Command button visible but grayed out

**Causes**:
1. `requiresSelection: true` but no records selected
2. Selection count below `minSelection`
3. Selection count above `maxSelection`

**Solution**:
- Select appropriate number of records

---

### Custom API Not Executing

**Symptom**: Button click does nothing or shows error

**Causes**:
1. Custom API not registered in Dataverse
2. User lacks privileges
3. Input parameters invalid
4. Plugin error

**Solution**:
1. Verify Custom API exists: **Advanced Settings > Customizations > Custom APIs**
2. Check user security role
3. Check browser console for parameter errors
4. Check plugin trace logs

---

### Token Interpolation Not Working

**Symptom**: Token literal (e.g., "{selectedRecordId}") passed to API instead of value

**Causes**:
1. Typo in token name
2. Token not supported in parameter type

**Solution**:
1. Verify token spelling (case-sensitive)
2. Use correct token for parameter type:
   - Single ID: `{selectedRecordId}`
   - Multiple IDs: `{selectedRecordIds}`
   - Full record: `{selectedRecord}`

---

## Next Steps

- [Configuration Guide](./ConfigurationGuide.md) - Complete configuration reference
- [API Reference](../api/UniversalDatasetGrid.md) - Component API documentation
- [Deployment Guide](./DeploymentGuide.md) - Deploy Custom APIs
- [Examples](../examples/CustomCommands.md) - More custom command examples
