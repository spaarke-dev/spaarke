# Entity Configuration Examples

Complete entity configuration examples for common business scenarios.

---

## Example 1: CRM Sales Configuration

Multi-entity configuration for sales teams.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "create", "delete", "refresh"],
    "compactToolbar": false,
    "enableVirtualization": true,
    "virtualizationThreshold": 100,
    "enableKeyboardShortcuts": true,
    "enableAccessibility": true
  },
  "entityConfigs": {
    "account": {
      "viewMode": "Card",
      "compactToolbar": true,
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
            "AccountId": "{selectedRecordId}"
          },
          "refresh": true
        }
      }
    },
    "contact": {
      "viewMode": "Grid",
      "customCommands": {
        "sendEmail": {
          "label": "Send Email",
          "icon": "Mail",
          "actionType": "action",
          "actionName": "sprk_SendEmailToContact",
          "requiresSelection": true,
          "parameters": {
            "Target": "{selectedRecord}"
          }
        }
      }
    },
    "opportunity": {
      "viewMode": "Grid",
      "customCommands": {
        "qualify": {
          "label": "Qualify",
          "icon": "CheckboxChecked",
          "actionType": "workflow",
          "actionName": "sprk_QualifyOpportunity",
          "requiresSelection": true,
          "confirmationMessage": "Qualify {selectedCount} opportunity(ies)?",
          "refresh": true
        },
        "closeWon": {
          "label": "Close as Won",
          "icon": "Checkmark",
          "actionType": "customapi",
          "actionName": "sprk_CloseOpportunity",
          "requiresSelection": true,
          "parameters": {
            "OpportunityId": "{selectedRecordId}",
            "Status": "Won"
          },
          "refresh": true
        }
      }
    },
    "quote": {
      "viewMode": "Grid",
      "enabledCommands": ["open", "refresh"],
      "customCommands": {
        "activate": {
          "label": "Activate",
          "icon": "Play",
          "actionType": "customapi",
          "actionName": "sprk_ActivateQuote",
          "requiresSelection": true,
          "refresh": true
        }
      }
    }
  }
}
```

### Use Cases

- **Accounts**: Visual cards with quote generation
- **Contacts**: Standard grid with email capability
- **Opportunities**: Grid with qualify/close workflows
- **Quotes**: Read-mostly with activation

---

## Example 2: Document Management System

Configuration for document library with SharePoint integration.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Card",
    "compactToolbar": true,
    "enabledCommands": ["open", "refresh"],
    "enableVirtualization": true,
    "virtualizationThreshold": 50
  },
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
        },
        "checkOut": {
          "label": "Check Out",
          "icon": "LockClosed",
          "actionType": "customapi",
          "actionName": "sprk_CheckOutDocument",
          "requiresSelection": true,
          "minSelection": 1,
          "maxSelection": 1,
          "refresh": true
        },
        "checkIn": {
          "label": "Check In",
          "icon": "LockOpen",
          "actionType": "customapi",
          "actionName": "sprk_CheckInDocument",
          "requiresSelection": true,
          "minSelection": 1,
          "maxSelection": 1,
          "refresh": true
        }
      }
    },
    "sprk_documentfolder": {
      "viewMode": "List",
      "enabledCommands": ["open", "create", "delete", "refresh"]
    }
  }
}
```

### Features

- Card view for visual document browsing
- Upload/download/preview commands
- Check-out/check-in workflow
- Folder navigation with list view

---

## Example 3: Service Ticketing System

Configuration for IT help desk or customer service.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "List",
    "enabledCommands": ["open", "create", "refresh"],
    "enableVirtualization": true
  },
  "entityConfigs": {
    "incident": {
      "viewMode": "List",
      "customCommands": {
        "assign": {
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
        "resolve": {
          "label": "Resolve",
          "icon": "CheckboxChecked",
          "actionType": "customapi",
          "actionName": "sprk_ResolveCase",
          "requiresSelection": true,
          "minSelection": 1,
          "maxSelection": 1,
          "parameters": {
            "CaseId": "{selectedRecordId}"
          },
          "refresh": true
        },
        "escalate": {
          "label": "Escalate",
          "icon": "ArrowUp",
          "actionType": "customapi",
          "actionName": "sprk_EscalateCase",
          "requiresSelection": true,
          "confirmationMessage": "Escalate case to manager?",
          "refresh": true
        }
      }
    },
    "task": {
      "viewMode": "List",
      "enabledCommands": ["open", "create", "refresh"],
      "customCommands": {
        "complete": {
          "label": "Mark Complete",
          "icon": "Checkmark",
          "actionType": "customapi",
          "actionName": "sprk_CompleteTask",
          "requiresSelection": true,
          "refresh": true
        }
      }
    }
  }
}
```

### Features

- List view optimized for quick scanning
- Assign/resolve/escalate workflows
- Task management
- No delete command (audit trail)

---

## Example 4: Mobile Field Service

Optimized for mobile field workers.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "List",
    "compactToolbar": true,
    "enabledCommands": ["open", "refresh"],
    "enableKeyboardShortcuts": false,
    "enableVirtualization": true,
    "virtualizationThreshold": 50
  },
  "entityConfigs": {
    "msdyn_workorder": {
      "viewMode": "Card",
      "customCommands": {
        "startWork": {
          "label": "Start Work",
          "icon": "Play",
          "actionType": "customapi",
          "actionName": "sprk_StartWorkOrder",
          "requiresSelection": true,
          "minSelection": 1,
          "maxSelection": 1,
          "refresh": true
        },
        "completeWork": {
          "label": "Complete",
          "icon": "Checkmark",
          "actionType": "customapi",
          "actionName": "sprk_CompleteWorkOrder",
          "requiresSelection": true,
          "confirmationMessage": "Mark work order complete?",
          "refresh": true
        },
        "addPhoto": {
          "label": "Add Photo",
          "icon": "Camera",
          "actionType": "customapi",
          "actionName": "sprk_AddWorkOrderPhoto",
          "requiresSelection": true
        }
      }
    },
    "msdyn_customerasset": {
      "viewMode": "Card",
      "enabledCommands": ["open", "refresh"]
    }
  }
}
```

### Features

- Card view for visual asset/work order display
- Large touch-friendly buttons
- Offline-capable commands
- Photo capture integration

---

## Example 5: Compliance & Audit System

Configuration emphasizing security and audit trail.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "refresh"],
    "enableAccessibility": true,
    "enableVirtualization": false
  },
  "entityConfigs": {
    "sprk_auditlog": {
      "viewMode": "Grid",
      "enabledCommands": ["open", "refresh"],
      "enableVirtualization": false,
      "customCommands": {
        "exportAudit": {
          "label": "Export to PDF",
          "icon": "DocumentPdf",
          "actionType": "customapi",
          "actionName": "sprk_ExportAuditLog",
          "requiresSelection": false,
          "parameters": {
            "EntityName": "{entityName}"
          }
        }
      }
    },
    "sprk_compliancereview": {
      "viewMode": "Grid",
      "enabledCommands": ["open", "create", "refresh"],
      "customCommands": {
        "approve": {
          "label": "Approve",
          "icon": "Checkmark",
          "actionType": "customapi",
          "actionName": "sprk_ApproveReview",
          "requiresSelection": true,
          "minSelection": 1,
          "maxSelection": 1,
          "confirmationMessage": "Approve this compliance review?",
          "refresh": true
        },
        "reject": {
          "label": "Reject",
          "icon": "Dismiss",
          "actionType": "customapi",
          "actionName": "sprk_RejectReview",
          "requiresSelection": true,
          "confirmationMessage": "Reject this compliance review?",
          "refresh": true
        }
      }
    }
  }
}
```

### Features

- Read-only by default (no delete)
- Virtualization disabled (full accessibility tree)
- Audit export capability
- Approval workflows with confirmation

---

## Example 6: Marketing Campaign Management

Configuration for marketing teams.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "create", "delete", "refresh"]
  },
  "entityConfigs": {
    "campaign": {
      "viewMode": "Card",
      "customCommands": {
        "launch": {
          "label": "Launch Campaign",
          "icon": "Rocket",
          "actionType": "customapi",
          "actionName": "sprk_LaunchCampaign",
          "requiresSelection": true,
          "minSelection": 1,
          "maxSelection": 1,
          "confirmationMessage": "Launch this campaign?",
          "refresh": true
        },
        "generateReport": {
          "label": "Generate Report",
          "icon": "DocumentTable",
          "actionType": "customapi",
          "actionName": "sprk_GenerateCampaignReport",
          "requiresSelection": true
        }
      }
    },
    "list": {
      "viewMode": "Grid",
      "customCommands": {
        "importContacts": {
          "label": "Import Contacts",
          "icon": "ContactCard",
          "actionType": "customapi",
          "actionName": "sprk_ImportContactsToList",
          "requiresSelection": true,
          "refresh": true
        }
      }
    },
    "bulkoperation": {
      "viewMode": "List",
      "enabledCommands": ["open", "refresh"],
      "customCommands": {
        "retry": {
          "label": "Retry Failed",
          "icon": "ArrowReset",
          "actionType": "customapi",
          "actionName": "sprk_RetryBulkOperation",
          "requiresSelection": true,
          "refresh": true
        }
      }
    }
  }
}
```

### Features

- Campaign launch with confirmation
- Report generation
- Contact import
- Bulk operation retry

---

## Example 7: E-Commerce Product Catalog

Configuration for product management.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Card",
    "enabledCommands": ["open", "create", "delete", "refresh"],
    "enableVirtualization": true
  },
  "entityConfigs": {
    "product": {
      "viewMode": "Card",
      "customCommands": {
        "publish": {
          "label": "Publish",
          "icon": "Globe",
          "actionType": "customapi",
          "actionName": "sprk_PublishProduct",
          "requiresSelection": true,
          "minSelection": 1,
          "maxSelection": 50,
          "confirmationMessage": "Publish {selectedCount} product(s)?",
          "refresh": true
        },
        "unpublish": {
          "label": "Unpublish",
          "icon": "GlobeProhibited",
          "actionType": "customapi",
          "actionName": "sprk_UnpublishProduct",
          "requiresSelection": true,
          "refresh": true
        },
        "updateInventory": {
          "label": "Update Inventory",
          "icon": "Box",
          "actionType": "customapi",
          "actionName": "sprk_UpdateInventory",
          "requiresSelection": true
        }
      }
    },
    "pricelevel": {
      "viewMode": "Grid",
      "customCommands": {
        "applyDiscount": {
          "label": "Apply Discount",
          "icon": "Tag",
          "actionType": "customapi",
          "actionName": "sprk_ApplyDiscount",
          "requiresSelection": true
        }
      }
    }
  }
}
```

### Features

- Card view for visual product display
- Publish/unpublish workflows
- Bulk inventory updates
- Price list management

---

## Example 8: Multi-Environment Configuration

Different configs for Dev/Test/Prod environments.

### Development Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "create", "delete", "refresh"],
    "compactToolbar": false,
    "enableVirtualization": true,
    "virtualizationThreshold": 100
  },
  "entityConfigs": {
    "account": {
      "customCommands": {
        "debug": {
          "label": "Debug",
          "icon": "Bug",
          "actionType": "customapi",
          "actionName": "sprk_DebugAccount",
          "requiresSelection": true
        }
      }
    }
  }
}
```

### Production Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "create", "delete", "refresh"],
    "compactToolbar": true,
    "enableVirtualization": true,
    "virtualizationThreshold": 50
  },
  "entityConfigs": {
    "account": {
      "customCommands": {
        "approve": {
          "label": "Approve",
          "icon": "Checkmark",
          "actionType": "customapi",
          "actionName": "sprk_ApproveAccount",
          "requiresSelection": true,
          "confirmationMessage": "Approve this account?",
          "refresh": true
        }
      }
    }
  }
}
```

### Deployment Strategy

Use environment variables to store configuration:

1. Create environment variable: `spaarke_DatasetGridConfig`
2. Store Dev config in Dev environment
3. Store Prod config in Prod environment
4. Component reads from environment variable

---

## Storage Options Comparison

### Option A: Form Property

**Configuration**:
```xml
<property name="configJson" of-type="Multiple" usage="input" />
```

**Pros**:
- Configuration versioned with solution
- Different configs per form

**Cons**:
- Must update solution to change config

---

### Option B: Environment Variable

**Setup**:
```bash
pac env var create \
  --name "spaarke_DatasetGridConfig" \
  --displayname "Dataset Grid Configuration" \
  --type "String"
```

**Pros**:
- Centralized configuration
- Change without solution update
- Environment-specific values

**Cons**:
- All forms use same config

---

### Option C: Configuration Table

**Table Schema**:
```
sprk_configuration
├── sprk_name (Primary Name)
├── sprk_entityname (String)
├── sprk_formname (String)
├── sprk_configjson (MultiLine Text)
└── sprk_isactive (Boolean)
```

**Pros**:
- Most flexible
- Per-entity, per-form, per-user configs
- UI for configuration management

**Cons**:
- Most complex
- Additional query on init

---

## Next Steps

- [Configuration Guide](../guides/ConfigurationGuide.md) - Complete configuration reference
- [Custom Commands Guide](../guides/CustomCommands.md) - Command documentation
- [Basic Grid Examples](./BasicGrid.md) - Simple examples
- [Advanced Scenarios](./AdvancedScenarios.md) - Complex use cases
