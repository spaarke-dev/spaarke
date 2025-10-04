# Basic Grid Examples

Simple examples demonstrating basic Universal Dataset Grid usage.

---

## Example 1: Minimal Configuration

Display a grid with default settings.

### Configuration

```json
{
  "schemaVersion": "1.0"
}
```

### Behavior

- **View Mode**: Grid (default)
- **Commands**: Open, Create, Delete, Refresh
- **Virtualization**: Enabled (threshold: 100 records)
- **Keyboard Shortcuts**: Enabled
- **Toolbar**: Normal mode (icon + label)

### Use Case

Quick replacement for standard Dataverse grid with no customization needed.

---

## Example 2: Simple Grid with Card View

Display accounts as cards instead of grid rows.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Card"
  }
}
```

### Behavior

- **View Mode**: Card
- **Layout**: Responsive (1-4 columns based on screen width)
- **Commands**: All default commands
- **Selection**: Checkbox on each card

### Use Case

Visual display for accounts, contacts, or products.

---

## Example 3: Read-Only Grid

Display data without create/delete capabilities.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "refresh"]
  }
}
```

### Behavior

- **Commands**: Open and Refresh only
- **No Create/Delete**: Buttons hidden
- **Read-Only**: Users can only view and navigate

### Use Case

Reports, dashboards, or entities where users should not modify data.

---

## Example 4: Compact Toolbar

Save space with icon-only toolbar.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "compactToolbar": true
  }
}
```

### Behavior

- **Toolbar**: Icon-only buttons
- **Labels**: Shown as tooltips on hover
- **Commands**: All default commands

### Use Case

Forms with limited space or multiple controls.

---

## Example 5: List View for Mobile

Optimized for mobile devices.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "List",
    "compactToolbar": true,
    "enableKeyboardShortcuts": false
  }
}
```

### Behavior

- **View Mode**: Simplified list
- **Toolbar**: Compact (icons only)
- **Keyboard Shortcuts**: Disabled (mobile devices)

### Use Case

Field service apps, mobile sales apps.

---

## Example 6: High-Performance Grid

Optimized for large datasets (1000+ records).

### Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enableVirtualization": true,
    "virtualizationThreshold": 50,
    "compactToolbar": true
  }
}
```

### Behavior

- **Virtualization**: Enabled at 50+ records (default: 100)
- **Performance**: Renders 1000+ records smoothly
- **Toolbar**: Compact to save space

### Use Case

Transaction lists, activity feeds, large entity lists.

---

## Example 7: Multi-Entity Configuration

Different settings for different entities.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "create", "delete", "refresh"]
  },
  "entityConfigs": {
    "account": {
      "viewMode": "Card"
    },
    "contact": {
      "viewMode": "Grid",
      "compactToolbar": true
    },
    "task": {
      "viewMode": "List",
      "enabledCommands": ["open", "create", "refresh"]
    }
  }
}
```

### Behavior

- **Account**: Card view, normal toolbar
- **Contact**: Grid view, compact toolbar
- **Task**: List view, no delete command
- **Other Entities**: Grid view (default)

### Use Case

Organization-wide configuration supporting multiple entity types.

---

## Example 8: Accessibility-First

Prioritize accessibility features.

### Configuration

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enableAccessibility": true,
    "enableKeyboardShortcuts": true,
    "enableVirtualization": false
  }
}
```

### Behavior

- **Virtualization**: Disabled (full accessibility tree)
- **Keyboard Nav**: Full support
- **Screen Reader**: All ARIA labels and announcements

### Use Case

Organizations with strict accessibility requirements (government, healthcare).

---

## PCF Integration Code

### Basic Integration

```typescript
import * as React from "react";
import * as ReactDOM from "react-dom";
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { UniversalDatasetGrid } from "@spaarke/ui-components";

export class UniversalDatasetGridControl implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement;
  private context: ComponentFramework.Context<IInputs>;

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this.container = container;
    this.context = context;
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;

    ReactDOM.render(
      React.createElement(UniversalDatasetGrid, {
        dataset: context.parameters.dataset,
        context: context
      }),
      this.container
    );
  }

  public destroy(): void {
    ReactDOM.unmountComponentAtNode(this.container);
  }
}
```

---

### With Configuration Property

```typescript
public updateView(context: ComponentFramework.Context<IInputs>): void {
  this.context = context;

  const configJson = context.parameters.configJson?.raw || "";

  ReactDOM.render(
    React.createElement(UniversalDatasetGrid, {
      dataset: context.parameters.dataset,
      context: context,
      config: configJson ? JSON.parse(configJson) : undefined
    }),
    this.container
  );
}
```

---

### ControlManifest.Input.xml

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest>
  <control namespace="Spaarke.UI.Components"
           constructor="UniversalDatasetGridControl"
           version="1.0.0"
           display-name-key="Universal Dataset Grid"
           description-key="Universal grid for all Dataverse entities">

    <!-- Dataset -->
    <data-set name="dataset" display-name-key="Dataset" />

    <!-- Optional: Configuration JSON -->
    <property name="configJson"
              display-name-key="Configuration JSON"
              description-key="Entity configuration in JSON format"
              of-type="Multiple"
              usage="input"
              required="false" />

    <resources>
      <code path="index.ts" order="1" />
      <platform-library name="React" version="18.2.0" />
      <platform-library name="Fluent" version="9.46.2" />
    </resources>
  </control>
</manifest>
```

---

## Testing Configuration Changes

### Test in Browser Console

```javascript
// Get current configuration
const config = JSON.parse(Xrm.Page.getAttribute("configJson").getValue());
console.log("Current config:", config);

// Update configuration
config.defaultConfig.viewMode = "Card";
Xrm.Page.getAttribute("configJson").setValue(JSON.stringify(config));
Xrm.Page.data.entity.save();

// Refresh form to see changes
Xrm.Page.data.refresh();
```

---

## Next Steps

- [Configuration Guide](../guides/ConfigurationGuide.md) - Complete configuration reference
- [Custom Commands Examples](./CustomCommands.md) - Add custom actions
- [Entity Configuration Examples](./EntityConfiguration.md) - Multi-entity setup
- [Advanced Scenarios](./AdvancedScenarios.md) - Complex use cases
