# Advanced Scenarios

Complex use cases and advanced techniques for the Universal Dataset Grid.

---

## Scenario 1: Headless Mode (Non-PCF Usage)

Use the component outside of PCF framework in custom pages or canvas apps.

### Configuration

```typescript
import { UniversalDatasetGrid } from "@spaarke/ui-components";

const headlessConfig = {
  entityName: "account",
  records: [
    { id: "1", name: "Acme Corp", revenue: 1000000, city: "NYC" },
    { id: "2", name: "Contoso Ltd", revenue: 500000, city: "Seattle" },
    { id: "3", name: "Fabrikam Inc", revenue: 2000000, city: "Austin" }
  ],
  columns: [
    { name: "name", displayName: "Account Name", dataType: "SingleLine.Text" },
    { name: "revenue", displayName: "Revenue", dataType: "Currency" },
    { name: "city", displayName: "City", dataType: "SingleLine.Text" }
  ]
};

const mockContext = {
  webAPI: {
    createRecord: async (entityName, record) => ({ id: "new-id" }),
    updateRecord: async (entityName, id, record) => {},
    deleteRecord: async (entityName, id) => {}
  },
  navigation: {
    openForm: (options) => console.log("Navigate to:", options)
  },
  mode: { isControlDisabled: false }
};

<UniversalDatasetGrid
  context={mockContext}
  headlessConfig={headlessConfig}
  config={{
    viewMode: "Card",
    customCommands: {
      customAction: {
        label: "Custom Action",
        icon: "Rocket",
        actionType: "customapi",
        actionName: "myCustomAction",
        requiresSelection: true
      }
    }
  }}
/>
```

### Use Cases

- Custom pages in model-driven apps
- Canvas app integration
- React standalone apps
- Embedded in external websites

---

## Scenario 2: Dynamic Configuration Loading

Load configuration from external source at runtime.

### Implementation

```typescript
import { useState, useEffect } from "react";
import { UniversalDatasetGrid } from "@spaarke/ui-components";

const DynamicConfigGrid: React.FC = ({ context, dataset }) => {
  const [config, setConfig] = useState<any>(null);

  useEffect(() => {
    // Load config from API, environment variable, or configuration table
    const loadConfiguration = async () => {
      const entityName = dataset.getTargetEntityType();

      // Option 1: From environment variable
      const envVarResponse = await context.webAPI.retrieveRecord(
        "environmentvariabledefinition",
        "guid-of-config-env-var",
        "?$select=defaultvalue"
      );
      const config = JSON.parse(envVarResponse.defaultvalue);

      // Option 2: From configuration table
      const configResponse = await context.webAPI.retrieveMultipleRecords(
        "sprk_configuration",
        `?$filter=sprk_entityname eq '${entityName}' and sprk_isactive eq true`
      );
      const config = JSON.parse(configResponse.entities[0].sprk_configjson);

      setConfig(config);
    };

    loadConfiguration();
  }, [dataset]);

  if (!config) {
    return <div>Loading configuration...</div>;
  }

  return (
    <UniversalDatasetGrid
      dataset={dataset}
      context={context}
      config={config}
    />
  );
};
```

### Benefits

- Centralized configuration management
- Change config without solution update
- User/role-specific configurations
- A/B testing different layouts

---

## Scenario 3: Custom Column Renderers

Render custom UI for specific columns.

### Implementation

```typescript
import { UniversalDatasetGrid } from "@spaarke/ui-components";
import { Badge, Avatar } from "@fluentui/react-components";

const CustomRendererGrid: React.FC = ({ context, dataset }) => {
  const customRenderers = {
    // Revenue column - color-coded badges
    "revenue": (value: number, record: any) => {
      const color = value > 1000000 ? "success" : value > 500000 ? "warning" : "danger";
      return (
        <Badge color={color}>
          ${value.toLocaleString()}
        </Badge>
      );
    },

    // Owner column - avatar with name
    "ownerid": (value: any, record: any) => {
      return (
        <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
          <Avatar name={value.name} />
          <span>{value.name}</span>
        </div>
      );
    },

    // Status column - custom badge
    "statuscode": (value: number, record: any) => {
      const statusMap = {
        1: { label: "Active", color: "success" },
        2: { label: "Inactive", color: "danger" },
        3: { label: "Pending", color: "warning" }
      };
      const status = statusMap[value];
      return <Badge color={status.color}>{status.label}</Badge>;
    }
  };

  return (
    <UniversalDatasetGrid
      dataset={dataset}
      context={context}
      customRenderers={customRenderers}
    />
  );
};
```

### Use Cases

- Visual indicators (status, priority)
- Rich formatting (currency, dates)
- Custom UI components (avatars, icons)
- Sparklines and mini-charts

---

## Scenario 4: Field-Level Security Integration

Hide columns based on field-level security.

### Implementation

```typescript
import { useState, useEffect } from "react";
import { FieldSecurityService } from "@spaarke/ui-components";

const SecureGrid: React.FC = ({ context, dataset }) => {
  const [visibleColumns, setVisibleColumns] = useState<any[]>([]);

  useEffect(() => {
    const filterSecureColumns = async () => {
      const entityName = dataset.getTargetEntityType();
      const allColumns = dataset.columns;

      const filtered = [];
      for (const column of allColumns) {
        const canRead = await FieldSecurityService.canRead(entityName, column.name);
        if (canRead) {
          filtered.push(column);
        }
      }

      setVisibleColumns(filtered);
    };

    filterSecureColumns();
  }, [dataset]);

  return (
    <UniversalDatasetGrid
      dataset={dataset}
      context={context}
      visibleColumns={visibleColumns}
    />
  );
};
```

### Benefits

- Automatic field-level security enforcement
- No manual column filtering
- Cached for performance
- Works with Dataverse FLS

---

## Scenario 5: Command Visibility Based on Privileges

Show/hide commands based on user privileges.

### Implementation

```typescript
import { useState, useEffect } from "react";
import { PrivilegeService } from "@spaarke/ui-components";

const PrivilegeAwareGrid: React.FC = ({ context, dataset }) => {
  const [enabledCommands, setEnabledCommands] = useState<string[]>([]);

  useEffect(() => {
    const determineCommands = async () => {
      const entityName = dataset.getTargetEntityType();

      const commands = [];

      if (await PrivilegeService.hasPrivilege(entityName, "Read")) {
        commands.push("open", "refresh");
      }

      if (await PrivilegeService.hasPrivilege(entityName, "Create")) {
        commands.push("create");
      }

      if (await PrivilegeService.hasPrivilege(entityName, "Delete")) {
        commands.push("delete");
      }

      setEnabledCommands(commands);
    };

    determineCommands();
  }, [dataset]);

  return (
    <UniversalDatasetGrid
      dataset={dataset}
      context={context}
      config={{
        enabledCommands: enabledCommands
      }}
    />
  );
};
```

### Benefits

- Dynamic command visibility
- Respects user security roles
- No manual privilege checks
- Cached for performance

---

## Scenario 6: Multi-Step Custom Command with Confirmation

Complex workflow with user input and confirmation.

### Configuration

```json
{
  "customCommands": {
    "approveWithComment": {
      "label": "Approve with Comment",
      "icon": "Checkmark",
      "actionType": "customapi",
      "actionName": "sprk_ApproveWithComment",
      "requiresSelection": true,
      "confirmationMessage": "Approve {selectedCount} record(s)?"
    }
  }
}
```

### Custom API with Dialog

```csharp
public class ApproveWithCommentPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var service = GetOrganizationService(serviceProvider, context.UserId);

        // Step 1: Validate selection
        var recordIds = (string)context.InputParameters["RecordIds"];
        if (string.IsNullOrEmpty(recordIds))
        {
            throw new InvalidPluginExecutionException("No records selected");
        }

        // Step 2: Get approval comment (from input parameter)
        var comment = context.InputParameters.ContainsKey("Comment")
            ? (string)context.InputParameters["Comment"]
            : "";

        // Step 3: Update records
        foreach (var id in recordIds.Split(','))
        {
            var update = new Entity("sprk_invoice", new Guid(id.Trim()));
            update["sprk_status"] = new OptionSetValue(2); // Approved
            update["sprk_approvalcomment"] = comment;
            update["sprk_approvedby"] = new EntityReference("systemuser", context.UserId);
            update["sprk_approvedon"] = DateTime.UtcNow;

            service.Update(update);
        }

        // Step 4: Send notification
        SendApprovalNotification(service, recordIds, comment);

        context.OutputParameters["Success"] = true;
        context.OutputParameters["Message"] = $"Approved {recordIds.Split(',').Length} record(s)";
    }

    private void SendApprovalNotification(IOrganizationService service, string recordIds, string comment)
    {
        // Send email/notification logic
    }
}
```

---

## Scenario 7: Conditional Command Visibility

Show commands only when certain conditions are met.

### Implementation

```typescript
const ConditionalCommandGrid: React.FC = ({ context, dataset, selectedRecords }) => {
  const getCustomCommands = () => {
    const commands: any = {};

    // Show "Approve" only if all selected records are pending
    const allPending = selectedRecords.every(r => r.statuscode === 1);
    if (allPending) {
      commands.approve = {
        label: "Approve",
        icon: "Checkmark",
        actionType: "customapi",
        actionName: "sprk_Approve",
        requiresSelection: true
      };
    }

    // Show "Reject" only if user is manager
    const currentUser = context.userSettings;
    if (currentUser.roles.some(r => r.name === "Manager")) {
      commands.reject = {
        label: "Reject",
        icon: "Dismiss",
        actionType: "customapi",
        actionName: "sprk_Reject",
        requiresSelection: true
      };
    }

    // Show "Export" only if <100 records selected
    if (selectedRecords.length > 0 && selectedRecords.length < 100) {
      commands.export = {
        label: "Export",
        icon: "DocumentArrowRight",
        actionType: "customapi",
        actionName: "sprk_Export"
      };
    }

    return commands;
  };

  return (
    <UniversalDatasetGrid
      dataset={dataset}
      context={context}
      config={{
        customCommands: getCustomCommands()
      }}
    />
  );
};
```

---

## Scenario 8: Real-Time Updates with SignalR

Update grid in real-time when records change.

### Implementation

```typescript
import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";

const RealtimeGrid: React.FC = ({ context, dataset }) => {
  const [refreshTrigger, setRefreshTrigger] = useState(0);

  useEffect(() => {
    // Connect to SignalR hub
    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/dataversehub")
      .build();

    connection.on("RecordUpdated", (entityName: string, recordId: string) => {
      // Refresh grid if entity matches
      if (entityName === dataset.getTargetEntityType()) {
        setRefreshTrigger(prev => prev + 1);
      }
    });

    connection.start();

    return () => {
      connection.stop();
    };
  }, [dataset]);

  useEffect(() => {
    if (refreshTrigger > 0) {
      dataset.refresh();
    }
  }, [refreshTrigger]);

  return (
    <UniversalDatasetGrid
      dataset={dataset}
      context={context}
    />
  );
};
```

---

## Scenario 9: Batch Operations with Progress

Execute commands with progress indication.

### Configuration

```json
{
  "customCommands": {
    "bulkUpdate": {
      "label": "Bulk Update",
      "icon": "Edit",
      "actionType": "customapi",
      "actionName": "sprk_BulkUpdate",
      "requiresSelection": true,
      "minSelection": 1,
      "maxSelection": 1000
    }
  }
}
```

### Custom API with Progress

```csharp
public class BulkUpdatePlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var service = GetOrganizationService(serviceProvider, context.UserId);

        var recordIds = ((string)context.InputParameters["RecordIds"]).Split(',');
        var totalRecords = recordIds.Length;
        var processed = 0;

        foreach (var id in recordIds)
        {
            try
            {
                // Update record
                var update = new Entity("account", new Guid(id.Trim()));
                update["sprk_lastupdated"] = DateTime.UtcNow;
                service.Update(update);

                processed++;

                // Report progress (via output parameter or callback)
                context.OutputParameters["Progress"] = $"{processed}/{totalRecords}";
            }
            catch (Exception ex)
            {
                // Log error but continue processing
                context.OutputParameters["Errors"] = $"Failed: {id} - {ex.Message}";
            }
        }

        context.OutputParameters["Success"] = true;
        context.OutputParameters["ProcessedCount"] = processed;
    }
}
```

---

## Scenario 10: Integration with External Systems

Call external APIs from custom commands.

### Configuration

```json
{
  "customCommands": {
    "syncToSharePoint": {
      "label": "Sync to SharePoint",
      "icon": "CloudSync",
      "actionType": "customapi",
      "actionName": "sprk_SyncToSharePoint",
      "requiresSelection": true,
      "confirmationMessage": "Sync {selectedCount} record(s) to SharePoint?"
    }
  }
}
```

### Custom API with External Call

```csharp
public class SyncToSharePointPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var service = GetOrganizationService(serviceProvider, context.UserId);

        var recordIds = (string)context.InputParameters["RecordIds"];

        using (var httpClient = new HttpClient())
        {
            // Get SharePoint access token
            var token = GetSharePointAccessToken();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            foreach (var id in recordIds.Split(','))
            {
                var record = service.Retrieve("sprk_document", new Guid(id.Trim()),
                    new ColumnSet("sprk_name", "sprk_content"));

                // Upload to SharePoint
                var content = new ByteArrayContent(record.GetAttributeValue<byte[]>("sprk_content"));
                var response = httpClient.PostAsync(
                    $"https://tenant.sharepoint.com/_api/web/folders/Documents/files/add(url='{record["sprk_name"]}')",
                    content
                ).Result;

                if (response.IsSuccessStatusCode)
                {
                    // Update record with SharePoint URL
                    var update = new Entity("sprk_document", new Guid(id.Trim()));
                    update["sprk_sharepointurl"] = response.Headers.Location.ToString();
                    service.Update(update);
                }
            }
        }

        context.OutputParameters["Success"] = true;
    }
}
```

---

## Performance Optimization Techniques

### 1. Lazy Loading Configuration

```typescript
const LazyConfigGrid: React.FC = ({ context, dataset }) => {
  const [config, setConfig] = useState<any>(null);

  useEffect(() => {
    // Load config only when component mounts
    import(`./configs/${dataset.getTargetEntityType()}.json`)
      .then(module => setConfig(module.default))
      .catch(() => setConfig({}));
  }, []);

  return config ? <UniversalDatasetGrid dataset={dataset} context={context} config={config} /> : null;
};
```

### 2. Memoized Command Handlers

```typescript
const MemoizedGrid: React.FC = ({ context, dataset }) => {
  const customCommands = useMemo(() => ({
    approve: {
      label: "Approve",
      handler: async (ctx) => {
        await context.webAPI.execute({ /* ... */ });
      }
    }
  }), [context]);

  return <UniversalDatasetGrid dataset={dataset} context={context} config={{ customCommands }} />;
};
```

### 3. Virtual Scrolling for Large Datasets

```json
{
  "enableVirtualization": true,
  "virtualizationThreshold": 50
}
```

---

## Next Steps

- [Configuration Guide](../guides/ConfigurationGuide.md) - Complete configuration reference
- [Custom Commands Guide](../guides/CustomCommands.md) - Command documentation
- [Developer Guide](../guides/DeveloperGuide.md) - Architecture and extension
- [Troubleshooting](../troubleshooting/CommonIssues.md) - Common issues and solutions
