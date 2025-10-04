# Configuration Guide

Complete reference for configuring the Universal Dataset Grid PCF component using entity configuration JSON.

---

## Overview

The Universal Dataset Grid is **configuration-driven** - it requires no entity-specific code. All customization is achieved through a JSON configuration that defines:

- Default behavior across all entities
- Entity-specific overrides
- Custom commands
- View preferences
- Feature flags

---

## Configuration Schema

### Schema Version 1.0

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": { /* IDatasetConfig */ },
  "entityConfigs": {
    "entityName": { /* IDatasetConfig */ }
  }
}
```

### Schema Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `schemaVersion` | `string` | Yes | Configuration schema version (currently "1.0") |
| `defaultConfig` | `IDatasetConfig` | No | Default settings applied to all entities |
| `entityConfigs` | `Record<string, IDatasetConfig>` | No | Entity-specific configuration overrides |

---

## IDatasetConfig Interface

### Complete Interface Definition

```typescript
interface IDatasetConfig {
  // View Settings
  viewMode?: "Grid" | "List" | "Card";
  compactToolbar?: boolean;

  // Commands
  enabledCommands?: string[];
  customCommands?: Record<string, ICustomCommandConfiguration>;

  // Performance
  enableVirtualization?: boolean;
  virtualizationThreshold?: number;

  // Features
  enableKeyboardShortcuts?: boolean;
  enableAccessibility?: boolean;
}
```

---

## Configuration Properties

### View Settings

#### `viewMode`
- **Type**: `"Grid" | "List" | "Card"`
- **Default**: `"Grid"`
- **Description**: Initial view mode when control loads

**Example**:
```json
{
  "viewMode": "Card"
}
```

**Use Cases**:
- **Grid**: Tabular data with sorting/filtering (accounts, contacts)
- **List**: Simplified list view for mobile
- **Card**: Rich visual cards (documents, products)

---

#### `compactToolbar`
- **Type**: `boolean`
- **Default**: `false`
- **Description**: Use compact toolbar with icon-only buttons

**Example**:
```json
{
  "compactToolbar": true
}
```

**When to Use**:
- Limited screen space
- Mobile/tablet layouts
- Forms with multiple controls

---

### Commands

#### `enabledCommands`
- **Type**: `string[]`
- **Default**: `["open", "create", "delete", "refresh"]`
- **Description**: Array of built-in command keys to enable

**Built-in Commands**:

| Command | Key | Description | Requires Selection |
|---------|-----|-------------|-------------------|
| Open | `open` | Open selected record | Yes (single) |
| Create | `create` | Create new record | No |
| Delete | `delete` | Delete selected records | Yes |
| Refresh | `refresh` | Reload dataset | No |

**Example**:
```json
{
  "enabledCommands": ["open", "create", "refresh"]
}
```

---

#### `customCommands`
- **Type**: `Record<string, ICustomCommandConfiguration>`
- **Default**: `{}`
- **Description**: Dictionary of custom commands (key → configuration)

**Example**:
```json
{
  "customCommands": {
    "approve": {
      "label": "Approve",
      "icon": "Checkmark",
      "actionType": "customapi",
      "actionName": "sprk_ApproveRecord",
      "requiresSelection": true,
      "minSelection": 1,
      "maxSelection": 10
    }
  }
}
```

See [Custom Commands Guide](./CustomCommands.md) for detailed configuration.

---

### Performance Settings

#### `enableVirtualization`
- **Type**: `boolean`
- **Default**: `true`
- **Description**: Enable row virtualization for large datasets

**How It Works**:
- Only renders visible rows in viewport
- Dramatically improves performance for datasets >100 records
- Uses `react-window` library

**When to Disable**:
- Small datasets (<50 records)
- Need full accessibility tree
- Print/export scenarios

**Example**:
```json
{
  "enableVirtualization": false
}
```

---

#### `virtualizationThreshold`
- **Type**: `number`
- **Default**: `100`
- **Description**: Minimum record count to enable virtualization

**Example**:
```json
{
  "virtualizationThreshold": 50
}
```

**Tuning Guidance**:
- **50-100**: Good for most scenarios
- **25-50**: For complex row renderers
- **100+**: For simple grids with fast rendering

---

### Feature Flags

#### `enableKeyboardShortcuts`
- **Type**: `boolean`
- **Default**: `true`
- **Description**: Enable keyboard shortcuts (Ctrl+N, Ctrl+R, Delete, etc.)

**Shortcuts Available**:
- **Ctrl+N**: Create new record
- **Ctrl+R**: Refresh grid
- **Delete**: Delete selected records
- **Enter**: Open selected record
- **Ctrl+A**: Select all records

**Example**:
```json
{
  "enableKeyboardShortcuts": false
}
```

---

#### `enableAccessibility`
- **Type**: `boolean`
- **Default**: `true`
- **Description**: Enable WCAG 2.1 AA accessibility features

**Features Controlled**:
- ARIA labels and roles
- Keyboard navigation
- Screen reader announcements
- Focus management

**When to Disable**:
- Legacy browser compatibility
- Performance-critical scenarios (rare)

**Example**:
```json
{
  "enableAccessibility": true
}
```

---

## Configuration Inheritance

### How Defaults and Overrides Merge

1. **Start with built-in defaults** (from component)
2. **Apply `defaultConfig`** (global overrides)
3. **Apply `entityConfigs[entityName]`** (entity-specific overrides)

### Merge Behavior

- **Primitives** (string, boolean, number): Entity config **replaces** default
- **Arrays** (`enabledCommands`): Entity config **replaces** default
- **Objects** (`customCommands`): Entity config **merges** with default (deep merge)

### Example

**Configuration**:
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "create", "delete", "refresh"],
    "compactToolbar": false
  },
  "entityConfigs": {
    "account": {
      "viewMode": "Card",
      "enabledCommands": ["open", "create"]
    }
  }
}
```

**Result for Account Entity**:
```json
{
  "viewMode": "Card",              // Overridden
  "enabledCommands": ["open", "create"],  // Overridden
  "compactToolbar": false          // Inherited from default
}
```

**Result for Contact Entity**:
```json
{
  "viewMode": "Grid",
  "enabledCommands": ["open", "create", "delete", "refresh"],
  "compactToolbar": false
}
```

---

## Complete Configuration Examples

### Example 1: Multi-Entity CRM Configuration

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
      "enabledCommands": ["open", "create", "refresh"],
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
          "label": "Qualify Lead",
          "icon": "CheckboxChecked",
          "actionType": "workflow",
          "actionName": "sprk_QualifyLead",
          "requiresSelection": true,
          "minSelection": 1,
          "refresh": true
        }
      }
    }
  }
}
```

---

### Example 2: Document Management Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Card",
    "enabledCommands": ["open", "refresh"],
    "compactToolbar": true,
    "enableVirtualization": true,
    "virtualizationThreshold": 50
  },
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

### Example 3: Mobile-Optimized Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "List",
    "compactToolbar": true,
    "enabledCommands": ["open", "create", "refresh"],
    "enableVirtualization": true,
    "virtualizationThreshold": 50,
    "enableKeyboardShortcuts": false
  }
}
```

---

### Example 4: High-Performance Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enableVirtualization": true,
    "virtualizationThreshold": 50,
    "compactToolbar": true,
    "enabledCommands": ["open", "refresh"]
  }
}
```

---

## Configuration Storage Options

### Option A: Form Property (Recommended)

**Pros**:
- Configuration tied to specific form
- Easy version control
- No additional infrastructure

**Cons**:
- Must update each form separately
- Cannot change without solution update

**Implementation**:
1. Add `configJson` property to ControlManifest.Input.xml
2. Set value in form designer
3. Component reads from `context.parameters.configJson.raw`

---

### Option B: Environment Variable

**Pros**:
- Centralized configuration
- Can change without solution update
- Environment-specific values (Dev/Test/Prod)

**Cons**:
- Requires environment variable setup
- All forms use same config

**Implementation**:
1. Create environment variable: `spaarke_DatasetGridConfig`
2. Store JSON in default value
3. Component reads via `context.webAPI.retrieveRecord()`

---

### Option C: Configuration Table

**Pros**:
- Most flexible
- Per-entity, per-form, per-user configurations
- Can edit via UI
- Audit trail

**Cons**:
- Most complex
- Requires custom table
- Additional queries

**Implementation**:
1. Create `sprk_configuration` table
2. Store JSON per entity/form
3. Component queries on init

---

## Configuration Validation

### Schema Validation

The component validates configuration on load:

```typescript
// Valid configuration
{
  "schemaVersion": "1.0",
  "defaultConfig": { "viewMode": "Grid" }
}

// Invalid - will throw error
{
  "schemaVersion": "2.0",  // Unsupported version
  "defaultConfig": { "viewMode": "Table" }  // Invalid view mode
}
```

### Error Handling

**Invalid JSON**:
- Error logged to console
- Component uses built-in defaults
- User sees default Grid view

**Invalid Schema Version**:
- Error logged to console
- Component uses built-in defaults

**Invalid Property Values**:
- Warning logged to console
- Invalid properties ignored
- Valid properties applied

---

## Configuration Best Practices

### 1. Start with Defaults
Define `defaultConfig` for common behavior, override only when needed:

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "create", "delete", "refresh"]
  },
  "entityConfigs": {
    "sprk_document": {
      "viewMode": "Card"  // Only override viewMode
    }
  }
}
```

### 2. Use Descriptive Command Keys
Choose command keys that describe the action:

```json
{
  "customCommands": {
    "approveInvoice": { /* ... */ },     // Good
    "cmd1": { /* ... */ }                // Bad
  }
}
```

### 3. Enable Virtualization for Large Datasets
If entity typically has >100 records, keep virtualization enabled:

```json
{
  "enableVirtualization": true,
  "virtualizationThreshold": 100
}
```

### 4. Disable Unused Commands
Only enable commands users will actually use:

```json
{
  "enabledCommands": ["open", "refresh"]  // Remove create/delete if not needed
}
```

### 5. Test Configuration Changes
Always test configuration in Dev environment before promoting to Production.

---

## Troubleshooting Configuration Issues

### Configuration Not Loading

**Symptom**: Control uses default Grid view, custom commands missing

**Causes**:
1. Invalid JSON syntax
2. Configuration property not bound
3. Schema version mismatch

**Solution**:
1. Validate JSON at [jsonlint.com](https://jsonlint.com/)
2. Check form designer - ensure `configJson` bound
3. Check browser console for errors

---

### Custom Commands Not Appearing

**Symptom**: Custom commands defined but not visible in toolbar

**Causes**:
1. Command key not in `enabledCommands` array
2. `requiresSelection: true` but no records selected
3. Command configuration invalid

**Solution**:
1. Ensure command key is NOT in `enabledCommands` (custom commands appear automatically)
2. Select a record
3. Validate command configuration structure

---

### Performance Issues

**Symptom**: Grid slow with large datasets

**Solution**:
1. Enable virtualization:
   ```json
   {
     "enableVirtualization": true,
     "virtualizationThreshold": 50
   }
   ```
2. Reduce visible columns
3. Use compact toolbar

---

## Migration Guide

### From Hard-Coded Configuration

**Before** (in code):
```typescript
const config = {
  viewMode: "Grid",
  enabledCommands: ["open", "create"]
};
```

**After** (in JSON):
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "create"]
  }
}
```

---

## Next Steps

- [Custom Commands Guide](./CustomCommands.md) - Create custom actions
- [Deployment Guide](./DeploymentGuide.md) - Deploy configuration to environments
- [API Reference](../api/UniversalDatasetGrid.md) - Complete API documentation
- [Examples](../examples/EntityConfiguration.md) - More configuration examples
