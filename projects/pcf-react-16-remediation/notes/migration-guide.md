# PCF React 16 Migration Guide

> **Purpose**: Step-by-step instructions for migrating a PCF control to platform React 16
> **Template Control**: VisualHost (already migrated)
> **Estimated Time**: 30-60 minutes per control

---

## Prerequisites

- Node.js and npm installed
- PAC CLI authenticated to target environment
- Control builds successfully before starting

---

## Step 1: Update ControlManifest.Input.xml

Add platform-library declarations inside the `<resources>` element:

```xml
<resources>
  <code path="index.ts" order="1" />
  <css path="css/styles.css" order="1" />
  <!-- Platform-provided: DO NOT bundle React/Fluent -->
  <platform-library name="React" version="16.14.0" />
  <platform-library name="Fluent" version="9.46.2" />
</resources>
```

**Location**: `control/ControlManifest.Input.xml`

---

## Step 2: Create featureconfig.json

Create a new file in the PCF root directory (same level as package.json):

```json
{
  "pcfReactPlatformLibraries": "on"
}
```

**Location**: `featureconfig.json` (NOT in control/ folder)

**Why**: pcf-scripts only externalizes React by default. This flag enables ReactDOM externalization.

---

## Step 3: Update package.json

Move React packages from `dependencies` to `devDependencies`:

**Before:**
```json
{
  "dependencies": {
    "react": "^18.2.0",
    "react-dom": "^18.2.0"
  }
}
```

**After:**
```json
{
  "dependencies": {
    // NO React here
  },
  "devDependencies": {
    "react": "^18.2.0",
    "react-dom": "^18.2.0",
    "@types/react": "^18.2.0",
    "@types/react-dom": "^18.2.0"
  }
}
```

Then run: `npm install`

---

## Step 4: Update index.ts

### 4.1 Change Import

**Before:**
```typescript
import * as ReactDOM from "react-dom/client";
```

**After:**
```typescript
import * as ReactDOM from "react-dom";
```

### 4.2 Change Class Properties

**Before:**
```typescript
private root: ReactDOM.Root | null = null;
```

**After:**
```typescript
private container: HTMLDivElement | null = null;
```

### 4.3 Change init() Method

**Before:**
```typescript
public init(context, notifyOutputChanged, state, container: HTMLDivElement): void {
  this.root = ReactDOM.createRoot(container);
  this.renderReactTree(context);
}
```

**After:**
```typescript
public init(context, notifyOutputChanged, state, container: HTMLDivElement): void {
  this.container = container;
  this.renderReactTree(context);
}
```

### 4.4 Change renderReactTree() Method

**Before:**
```typescript
private renderReactTree(context): void {
  this.root?.render(
    React.createElement(FluentProvider, { theme },
      React.createElement(MyComponent, { context })
    )
  );
}
```

**After:**
```typescript
private renderReactTree(context): void {
  if (!this.container) return;

  ReactDOM.render(
    React.createElement(FluentProvider, { theme },
      React.createElement(MyComponent, { context })
    ),
    this.container
  );
}
```

### 4.5 Change destroy() Method

**Before:**
```typescript
public destroy(): void {
  this.root?.unmount();
  this.root = null;
}
```

**After:**
```typescript
public destroy(): void {
  if (this.container) {
    ReactDOM.unmountComponentAtNode(this.container);
    this.container = null;
  }
}
```

---

## Step 5: Build and Verify

```bash
# Clean previous build
npm run clean

# Build for production
npm run build:prod

# Check bundle size - should be < 1MB
ls -lh out/controls/*/bundle.js
```

**Expected**: Bundle size ~400-600 KB (not 5+ MB)

---

## Step 6: Deploy to Dataverse

```bash
# Disable Central Package Management (if applicable)
mv /c/code_files/spaarke-wt-visualization-module/Directory.Packages.props /c/code_files/spaarke-wt-visualization-module/Directory.Packages.props.disabled

# Deploy
pac pcf push --publisher-prefix sprk

# If path error occurs, use manual fallback:
mkdir -p obj/PowerAppsToolsTemp_sprk/bin/net462/control
cp out/controls/*/bundle.js obj/PowerAppsToolsTemp_sprk/bin/net462/control/
cp out/controls/*/ControlManifest.xml obj/PowerAppsToolsTemp_sprk/bin/net462/control/
cp out/controls/*/styles.css obj/PowerAppsToolsTemp_sprk/bin/net462/control/ 2>/dev/null || true
cd obj/PowerAppsToolsTemp_sprk && dotnet build *.cdsproj --configuration Debug
pac solution import --path bin/Debug/PowerAppsToolsTemp_sprk.zip --publish-changes

# Restore CPM
mv /c/code_files/spaarke-wt-visualization-module/Directory.Packages.props.disabled /c/code_files/spaarke-wt-visualization-module/Directory.Packages.props
```

---

## Step 7: Test in Dataverse

1. Open a model-driven app form with the control
2. Open browser DevTools (F12)
3. Check Console for errors
4. Verify control renders correctly
5. Test all control functionality

**Expected**: No React-related errors, control functions normally.

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Bundle still > 5MB | React in dependencies | Move to devDependencies |
| Bundle ~500KB but errors | Missing featureconfig.json | Create file with pcfReactPlatformLibraries |
| `createRoot is not a function` | Wrong import path | Change to `react-dom` |
| `Cannot create property '_updatedFibers'` | Using React 18 API | Update to `ReactDOM.render()` |
| Styles missing in deployment | styles.css not copied | Copy from out/controls/ |

---

## Reference: VisualHost (Migrated Template)

Use VisualHost as reference for correct configuration:

| File | Path |
|------|------|
| Manifest | `src/client/pcf/VisualHost/control/ControlManifest.Input.xml` |
| Feature config | `src/client/pcf/VisualHost/featureconfig.json` |
| Index.ts | `src/client/pcf/VisualHost/control/index.ts` |
| Package.json | `src/client/pcf/VisualHost/package.json` |

---

## Checklist Template

Copy this checklist for each control migration:

```markdown
## [Control Name] Migration

- [ ] Step 1: Add platform-library to manifest
- [ ] Step 2: Create featureconfig.json
- [ ] Step 3: Move React to devDependencies
- [ ] Step 4.1: Change import to 'react-dom'
- [ ] Step 4.2: Change root to container property
- [ ] Step 4.3: Update init() method
- [ ] Step 4.4: Update renderReactTree() method
- [ ] Step 4.5: Update destroy() method
- [ ] Step 5: Build and verify bundle < 1MB
- [ ] Step 6: Deploy to Dataverse
- [ ] Step 7: Test in model-driven app
- [ ] No console errors
- [ ] Control functions correctly
```
