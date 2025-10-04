# Custom Command Examples

Real-world examples of custom commands for the Universal Dataset Grid.

---

## Example 1: Approve Invoice

Execute Custom API to approve invoices.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "sprk_invoice": {
      "customCommands": {
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
    }
  }
}
```

### Custom API Implementation

```csharp
public class ApproveInvoicePlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
        var service = serviceFactory.CreateOrganizationService(context.UserId);

        var invoiceIds = context.InputParameters["InvoiceIds"] as string;
        var approvedBy = (Guid)context.InputParameters["ApprovedBy"];

        foreach (var id in invoiceIds.Split(','))
        {
            var invoice = new Entity("sprk_invoice", new Guid(id.Trim()));
            invoice["sprk_status"] = new OptionSetValue(2); // Approved
            invoice["sprk_approvedby"] = new EntityReference("systemuser", approvedBy);
            invoice["sprk_approvedon"] = DateTime.UtcNow;

            service.Update(invoice);
        }

        context.OutputParameters["Success"] = true;
    }
}
```

### pac CLI Setup

```bash
pac customapi create \
  --name "sprk_ApproveInvoice" \
  --displayname "Approve Invoice" \
  --boundentitylogicalname "sprk_invoice" \
  --executeprivileges "None"

pac customapi add-parameter \
  --customapiid <GUID> \
  --name "InvoiceIds" \
  --type "String" \
  --isoptional false

pac customapi add-parameter \
  --customapiid <GUID> \
  --name "ApprovedBy" \
  --type "Guid" \
  --isoptional false
```

---

## Example 2: Send Email to Contacts

Execute Action to send email to selected contacts.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "contact": {
      "customCommands": {
        "sendEmail": {
          "label": "Send Email",
          "icon": "Mail",
          "actionType": "action",
          "actionName": "sprk_SendEmailToContact",
          "requiresSelection": true,
          "minSelection": 1,
          "parameters": {
            "Target": "{selectedRecord}",
            "Subject": "Hello from Spaarke",
            "Body": "This is an automated email."
          }
        }
      }
    }
  }
}
```

### Action Implementation (Plugin)

```csharp
public class SendEmailToContactPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
        var service = serviceFactory.CreateOrganizationService(context.UserId);

        var target = (EntityReference)context.InputParameters["Target"];
        var subject = (string)context.InputParameters["Subject"];
        var body = (string)context.InputParameters["Body"];

        var email = new Entity("email");
        email["subject"] = subject;
        email["description"] = body;
        email["to"] = new Entity[] {
            new Entity("activityparty") {
                ["partyid"] = target
            }
        };

        var emailId = service.Create(email);

        var sendEmailRequest = new SendEmailRequest {
            EmailId = emailId,
            IssueSend = true
        };

        service.Execute(sendEmailRequest);
    }
}
```

---

## Example 3: Upload Documents to SharePoint

Execute Custom API to upload multiple documents.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "sprk_document": {
      "viewMode": "Card",
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
        }
      }
    }
  }
}
```

### Custom API Implementation

```csharp
public class UploadDocumentPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var service = GetOrganizationService(serviceProvider, context.UserId);

        var documentIds = (context.InputParameters["DocumentIds"] as string).Split(',');
        var parentId = (Guid)context.InputParameters["ParentId"];

        var sharePointService = new SharePointService(service);

        foreach (var id in documentIds)
        {
            var document = service.Retrieve("sprk_document", new Guid(id.Trim()), new ColumnSet("sprk_name", "sprk_content"));

            var uploadResult = sharePointService.UploadFile(
                parentId,
                document.GetAttributeValue<string>("sprk_name"),
                document.GetAttributeValue<byte[]>("sprk_content")
            );

            // Update document with SharePoint URL
            var update = new Entity("sprk_document", new Guid(id.Trim()));
            update["sprk_sharepointurl"] = uploadResult.Url;
            service.Update(update);
        }

        context.OutputParameters["Success"] = true;
    }
}
```

---

## Example 4: Generate Quote from Opportunity

Execute Function to calculate and create quote.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "opportunity": {
      "customCommands": {
        "generateQuote": {
          "label": "Generate Quote",
          "icon": "DocumentArrowRight",
          "actionType": "function",
          "actionName": "sprk_GenerateQuote",
          "requiresSelection": true,
          "minSelection": 1,
          "maxSelection": 1,
          "parameters": {
            "OpportunityId": "{selectedRecordId}"
          },
          "refresh": true
        }
      }
    }
  }
}
```

### Function Implementation

```csharp
[DataContract]
public class GenerateQuoteResponse
{
    [DataMember]
    public Guid QuoteId { get; set; }
}

public class GenerateQuoteFunction : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var service = GetOrganizationService(serviceProvider, context.UserId);

        var opportunityId = (Guid)context.InputParameters["OpportunityId"];

        var opportunity = service.Retrieve("opportunity", opportunityId, new ColumnSet(
            "name", "customerid", "totalamount"
        ));

        var quote = new Entity("quote");
        quote["name"] = opportunity.GetAttributeValue<string>("name") + " - Quote";
        quote["customerid"] = opportunity.GetAttributeValue<EntityReference>("customerid");
        quote["opportunityid"] = new EntityReference("opportunity", opportunityId);
        quote["totalamount"] = opportunity.GetAttributeValue<Money>("totalamount");

        var quoteId = service.Create(quote);

        context.OutputParameters["QuoteId"] = quoteId;
    }
}
```

---

## Example 5: Bulk Update Status

Execute Custom API to update status for multiple records.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "account": {
      "customCommands": {
        "activate": {
          "label": "Activate",
          "icon": "CheckboxChecked",
          "actionType": "customapi",
          "actionName": "sprk_BulkUpdateStatus",
          "requiresSelection": true,
          "minSelection": 1,
          "maxSelection": 100,
          "parameters": {
            "EntityName": "{entityName}",
            "RecordIds": "{selectedRecordIds}",
            "StatusCode": "1"
          },
          "confirmationMessage": "Activate {selectedCount} account(s)?",
          "refresh": true
        },
        "deactivate": {
          "label": "Deactivate",
          "icon": "DismissCircle",
          "actionType": "customapi",
          "actionName": "sprk_BulkUpdateStatus",
          "requiresSelection": true,
          "parameters": {
            "EntityName": "{entityName}",
            "RecordIds": "{selectedRecordIds}",
            "StatusCode": "2"
          },
          "confirmationMessage": "Deactivate {selectedCount} account(s)?",
          "refresh": true
        }
      }
    }
  }
}
```

### Custom API Implementation

```csharp
public class BulkUpdateStatusPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var service = GetOrganizationService(serviceProvider, context.UserId);

        var entityName = (string)context.InputParameters["EntityName"];
        var recordIds = (string)context.InputParameters["RecordIds"];
        var statusCode = int.Parse((string)context.InputParameters["StatusCode"]);

        foreach (var id in recordIds.Split(','))
        {
            var update = new Entity(entityName, new Guid(id.Trim()));
            update["statecode"] = new OptionSetValue(statusCode == 1 ? 0 : 1);
            update["statuscode"] = new OptionSetValue(statusCode);

            service.Update(update);
        }

        context.OutputParameters["Success"] = true;
    }
}
```

---

## Example 6: Export to Excel

Execute Custom API to generate Excel export.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "account": {
      "customCommands": {
        "exportToExcel": {
          "label": "Export to Excel",
          "icon": "DocumentTableArrowRight",
          "actionType": "customapi",
          "actionName": "sprk_ExportToExcel",
          "requiresSelection": false,
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

### Custom API Implementation

```csharp
using ClosedXML.Excel;

public class ExportToExcelPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var service = GetOrganizationService(serviceProvider, context.UserId);

        var entityName = (string)context.InputParameters["EntityName"];
        var recordIds = ((string)context.InputParameters["RecordIds"])?.Split(',') ?? new string[0];

        var query = new QueryExpression(entityName);
        query.ColumnSet = new ColumnSet(true);
        if (recordIds.Length > 0)
        {
            query.Criteria.AddCondition($"{entityName}id", ConditionOperator.In, recordIds);
        }

        var records = service.RetrieveMultiple(query);

        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add(entityName);

            // Headers
            var headers = records.Entities[0].Attributes.Keys.ToArray();
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = headers[i];
            }

            // Data
            for (int row = 0; row < records.Entities.Count; row++)
            {
                for (int col = 0; col < headers.Length; col++)
                {
                    worksheet.Cell(row + 2, col + 1).Value = records.Entities[row][headers[col]]?.ToString();
                }
            }

            using (var stream = new MemoryStream())
            {
                workbook.SaveAs(stream);
                context.OutputParameters["ExcelFile"] = Convert.ToBase64String(stream.ToArray());
            }
        }
    }
}
```

---

## Example 7: Assign to Me

Execute Action to assign records to current user.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "opportunity": {
      "customCommands": {
        "assignToMe": {
          "label": "Assign to Me",
          "icon": "Person",
          "actionType": "action",
          "actionName": "Assign",
          "requiresSelection": true,
          "minSelection": 1,
          "parameters": {
            "Target": "{selectedRecord}",
            "Assignee": "{currentUserId}"
          },
          "refresh": true
        }
      }
    }
  }
}
```

**Note**: Uses built-in Dataverse `Assign` action - no plugin needed!

---

## Example 8: Workflow Execution

Execute Classic Workflow on selected records.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "lead": {
      "customCommands": {
        "qualify": {
          "label": "Qualify Lead",
          "icon": "CheckboxChecked",
          "actionType": "workflow",
          "actionName": "sprk_QualifyLead",
          "requiresSelection": true,
          "minSelection": 1,
          "confirmationMessage": "Qualify {selectedCount} lead(s)?",
          "refresh": true
        }
      }
    }
  }
}
```

### Workflow Setup

1. Create on-demand workflow in Dataverse
2. Configure workflow steps
3. Publish workflow
4. Use workflow unique name in configuration

---

## Testing Custom Commands

### Browser Console Testing

```javascript
// Get PCF context
const context = Xrm.Page.context;

// Simulate command execution
const testCommand = {
  key: "approve",
  actionType: "customapi",
  actionName: "sprk_ApproveInvoice",
  parameters: {
    InvoiceIds: "guid1,guid2",
    ApprovedBy: Xrm.Utility.getGlobalContext().userSettings.userId
  }
};

// Execute via WebAPI
context.webAPI.execute({
  customApiName: testCommand.actionName,
  parameters: testCommand.parameters
}).then(result => {
  console.log("Success:", result);
}).catch(error => {
  console.error("Error:", error);
});
```

---

## Error Handling Best Practices

### Plugin Error Handling

```csharp
public void Execute(IServiceProvider serviceProvider)
{
    var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

    try
    {
        tracingService.Trace("Starting command execution");

        // ... command logic

        tracingService.Trace("Command completed successfully");
    }
    catch (Exception ex)
    {
        tracingService.Trace($"Error: {ex.Message}");
        throw new InvalidPluginExecutionException(
            $"Command failed: {ex.Message}",
            ex
        );
    }
}
```

### User-Friendly Error Messages

```csharp
// Bad
throw new InvalidPluginExecutionException("NullReferenceException");

// Good
throw new InvalidPluginExecutionException(
    "Unable to approve invoice. Please ensure all required fields are populated."
);
```

---

## Next Steps

- [Configuration Guide](../guides/ConfigurationGuide.md) - Complete configuration reference
- [Custom Commands Guide](../guides/CustomCommands.md) - Detailed command documentation
- [Entity Configuration Examples](./EntityConfiguration.md) - Multi-entity configurations
- [Advanced Scenarios](./AdvancedScenarios.md) - Complex use cases
