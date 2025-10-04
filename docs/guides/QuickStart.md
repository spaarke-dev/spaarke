# Universal Dataset Grid - Quick Start

Get up and running with the Universal Dataset Grid in 5 minutes.

---

## Prerequisites

- Power Apps Component Framework (PCF) project
- Node.js 18+ and npm
- TypeScript 5.3+
- React 18.2+

---

## Step 1: Install Package

```bash
npm install @spaarke/ui-components
```

---

## Step 2: Basic Usage

### In your PCF Control

```typescript
import * as React from 'react';
import { UniversalDatasetGrid } from '@spaarke/ui-components';

export class MyControl implements ComponentFramework.StandardControl<IInputs, IOutputs> {

  private notifyOutputChanged: () => void;
  private container: HTMLDivElement;

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this.notifyOutputChanged = notifyOutputChanged;
    this.container = container;
    this.renderControl(context);
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.renderControl(context);
  }

  private renderControl(context: ComponentFramework.Context<IInputs>): void {
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

## Step 3: Configure ControlManifest.Input.xml

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest>
  <control namespace="Spaarke" constructor="UniversalDataset" version="1.0.0">

    <!-- Dataset Property -->
    <data-set name="dataset" display-name-key="Dataset">
      <!-- PCF will provide dataset automatically -->
    </data-set>

    <!-- Resources -->
    <resources>
      <code path="index.ts" order="1"/>
      <css path="css/styles.css" order="1"/>
    </resources>

  </control>
</manifest>
```

---

## Step 4: Build and Test

```bash
# Build the control
npm run build

# Start test harness
npm start watch
```

---

## Step 5: Customize (Optional)

### Change View Mode

```typescript
<UniversalDatasetGrid
  dataset={context.parameters.dataset}
  context={context}
  config={{
    viewMode: "List"  // or "Card"
  }}
/>
```

### Enable Custom Commands

```typescript
const config = JSON.stringify({
  schemaVersion: "1.0",
  defaultConfig: {
    enabledCommands: ["open", "create", "delete", "refresh", "upload"]
  }
});

<UniversalDatasetGrid
  dataset={context.parameters.dataset}
  context={context}
  configJson={config}
/>
```

---

## Common Configurations

### Compact Toolbar

```typescript
<UniversalDatasetGrid
  dataset={dataset}
  context={context}
  config={{
    compactToolbar: true
  }}
/>
```

### Card View for Accounts

```typescript
const config = JSON.stringify({
  schemaVersion: "1.0",
  entityConfigs: {
    account: {
      viewMode: "Card",
      enabledCommands: ["open", "refresh"]
    }
  }
});

<UniversalDatasetGrid
  dataset={dataset}
  context={context}
  configJson={config}
/>
```

### Disable Virtualization (Small Datasets)

```typescript
<UniversalDatasetGrid
  dataset={dataset}
  context={context}
  config={{
    enableVirtualization: false
  }}
/>
```

---

## Next Steps

- [Complete Usage Guide](./UsageGuide.md)
- [Configuration Guide](./ConfigurationGuide.md)
- [Custom Commands Guide](./CustomCommands.md)
- [API Reference](../api/UniversalDatasetGrid.md)

---

## Troubleshooting

### Control not rendering?

1. Check browser console for errors
2. Verify dataset is bound in manifest
3. Ensure React/ReactDOM are imported
4. Check PCF framework initialization

### Commands not working?

1. Verify user privileges
2. Check command configuration
3. Inspect network tab for API calls
4. Enable debug logging

### Need Help?

- Check [Common Issues](../troubleshooting/CommonIssues.md)
- Review [Examples](../examples/)
- See [Developer Guide](./DeveloperGuide.md)
