# DocumentRelationshipViewer PCF Deployment Guide

> **Last Updated**: January 2026
> **Current Version**: 1.0.16
> **Control Namespace**: `Spaarke.Controls.DocumentRelationshipViewer`

---

## Table of Contents

1. [Control Overview](#control-overview)
2. [Package Configuration](#package-configuration)
3. [Control Manifest Settings](#control-manifest-settings)
4. [Styling System](#styling-system)
5. [Force Layout Configuration](#force-layout-configuration)
6. [Node Configuration](#node-configuration)
7. [Edge Configuration](#edge-configuration)
8. [Deployment Process](#deployment-process)
9. [Form Registration](#form-registration)
10. [Version History](#version-history)
11. [Lessons Learned](#lessons-learned)
12. [Alternative: Field-Bound Approach](#alternative-field-bound-approach)

---

## Control Overview

The DocumentRelationshipViewer is a PCF virtual control that displays document relationships using a force-directed graph layout. It visualizes vector similarity between documents using:

- **d3-force** for physics-based node positioning
- **react-flow-renderer** for graph rendering and interaction
- **Fluent UI v9** for styling (ADR-021 compliant)

### Key Features

- Full-screen section layout (AnalysisWorkspace pattern)
- Force-directed graph with similarity-based edge lengths
- Smaller = more similar (edge distance = 400 * (1 - similarity))
- Dark mode support via Fluent design tokens
- Pan/zoom controls
- Node selection with click handlers

---

## Package Configuration

### package.json

```json
{
  "name": "document-relationship-viewer",
  "version": "1.0.16",
  "description": "PCF control for visualizing document relationships using graph layout",
  "scripts": {
    "build": "pcf-scripts build",
    "clean": "pcf-scripts clean",
    "lint": "pcf-scripts lint",
    "lint:fix": "pcf-scripts lint fix",
    "rebuild": "pcf-scripts rebuild",
    "start": "pcf-scripts start",
    "start:watch": "pcf-scripts start watch",
    "test": "jest",
    "test:watch": "jest --watch",
    "test:coverage": "jest --coverage"
  },
  "devDependencies": {
    "@fluentui/react-components": "^9.46.2",
    "@fluentui/react-icons": "^2.0.0",
    "@types/d3-force": "^3.0.10",
    "d3-force": "^3.0.0",
    "react-flow-renderer": "^10.3.17",
    "react": "^16.14.0",
    "react-dom": "^16.14.0",
    "typescript": "^5.8.3"
  }
}
```

### Critical Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `react-flow-renderer` | 10.3.17 | Graph visualization (React 16 compatible) |
| `d3-force` | 3.0.0 | Force simulation for node positioning |
| `@fluentui/react-components` | 9.46.2 | UI components (platform library) |
| `@fluentui/react-icons` | 2.0.0 | Icon library |

**Note**: Use `react-flow-renderer` v10.x (NOT `reactflow` v11+) for React 16 compatibility.

---

## Control Manifest Settings

### ControlManifest.Input.xml

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke.Controls"
           constructor="DocumentRelationshipViewer"
           version="1.0.16"
           display-name-key="Document Relationship Viewer"
           description-key="Interactive graph visualization of related documents based on vector similarity (v1.0.16)"
           control-type="virtual">

    <external-service-usage enabled="true">
      <domain>spe-api-dev-67e2xz.azurewebsites.net</domain>
    </external-service-usage>

    <!-- Input: Document ID to find relationships for -->
    <property name="documentId"
              display-name-key="Document ID"
              description-key="The GUID of the source document"
              of-type="SingleLine.Text"
              usage="bound"
              required="true" />

    <!-- Input: Tenant ID for multi-tenant routing -->
    <property name="tenantId"
              display-name-key="Tenant ID"
              description-key="Tenant identifier for API routing"
              of-type="SingleLine.Text"
              usage="input"
              required="false" />

    <!-- Input: API Base URL -->
    <property name="apiBaseUrl"
              display-name-key="API Base URL"
              description-key="BFF API base URL"
              of-type="SingleLine.Text"
              usage="input"
              required="false" />

    <!-- Output: Selected document ID when user clicks a node -->
    <property name="selectedDocumentId"
              display-name-key="Selected Document"
              description-key="The document selected by the user"
              of-type="SingleLine.Text"
              usage="output" />

    <resources>
      <code path="index.ts" order="1"/>
      <!-- Platform-provided libraries (not bundled) -->
      <platform-library name="React" version="16.14.0" />
      <platform-library name="Fluent" version="9.46.2" />
    </resources>

    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
      <uses-feature name="Utility" required="true" />
    </feature-usage>
  </control>
</manifest>
```

### Key Manifest Settings

| Setting | Value | Purpose |
|---------|-------|---------|
| `control-type` | `virtual` | No iframe, direct DOM access |
| `platform-library React` | 16.14.0 | Use platform-provided React |
| `platform-library Fluent` | 9.46.2 | Use platform-provided Fluent UI |
| `external-service-usage` | enabled | Allow BFF API calls |

---

## Styling System

### Container Sizing (Full-Section Layout)

The control uses viewport-based sizing to fill the Dataverse form section:

```typescript
const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        // Fill available vertical space (form section height)
        height: "calc(100vh - 180px)",
        minHeight: "500px",
        // Fill horizontal space (viewport minus Dataverse chrome)
        width: "calc(100vw - 320px)",
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
    },
    graphContainer: {
        flex: 1,
        display: "flex",
        position: "relative",
        minHeight: 0,  // Critical for flex child sizing
        width: "100%",
    },
});
```

### Key Sizing Values

| Property | Value | Rationale |
|----------|-------|-----------|
| `height` | `calc(100vh - 180px)` | Viewport height minus Dataverse header/tabs |
| `width` | `calc(100vw - 320px)` | Viewport width minus navigation sidebar |
| `minHeight` | `500px` | Minimum usable height |
| `minHeight: 0` | On flex child | Allows flex container to shrink properly |

### Fluent UI v9 Design Tokens (ADR-021)

All colors use Fluent design tokens for dark mode support:

```typescript
import { tokens } from "@fluentui/react-components";

// Backgrounds
tokens.colorNeutralBackground1    // Primary background
tokens.colorNeutralBackground2    // Secondary background
tokens.colorNeutralBackground3    // Tertiary background
tokens.colorBrandBackground       // Brand/accent background

// Foregrounds
tokens.colorNeutralForeground1    // Primary text
tokens.colorNeutralForeground3    // Secondary text
tokens.colorNeutralForegroundOnBrand  // Text on brand background

// Borders
tokens.colorNeutralStroke1        // Primary border
tokens.colorBrandStroke1          // Brand border

// Shadows
tokens.shadow4                    // Subtle shadow
tokens.shadow8                    // Medium shadow

// Spacing
tokens.spacingVerticalS           // Small vertical spacing
tokens.spacingHorizontalM         // Medium horizontal spacing

// Typography
tokens.fontSizeBase100            // Small text (10px)
tokens.fontSizeBase200            // Body text (12px)
tokens.fontWeightSemibold         // Semi-bold weight
```

---

## Force Layout Configuration

### useForceLayout.ts - Default Options

```typescript
const DEFAULT_OPTIONS: Required<ForceLayoutOptions> = {
    distanceMultiplier: 400,   // Edge length multiplier
    collisionRadius: 100,      // Node collision detection radius
    centerX: 0,                // Graph center X (React Flow handles viewport)
    centerY: 0,                // Graph center Y
    chargeStrength: -1000,     // Node repulsion strength (negative = repel)
};
```

### Force Parameters Explained

| Parameter | Current Value | Effect |
|-----------|---------------|--------|
| `distanceMultiplier` | 400 | Edge length = 400 * (1 - similarity). Higher = more spread |
| `collisionRadius` | 100 | Minimum distance between node centers |
| `chargeStrength` | -1000 | Node repulsion force. More negative = stronger push apart |
| `centerX/Y` | 0, 0 | Force center at origin. React Flow's fitView handles viewport |

### Edge Distance Formula

```
distance = distanceMultiplier * (1 - similarity)

Examples:
- similarity 0.9 (90%) → distance = 400 * 0.1 = 40px  (very close)
- similarity 0.7 (70%) → distance = 400 * 0.3 = 120px (medium)
- similarity 0.5 (50%) → distance = 400 * 0.5 = 200px (far apart)
```

### Simulation Parameters

```typescript
const simulation = forceSimulation(forceNodes)
    .force("link", forceLink(forceLinks)
        .id(d => d.id)
        .distance(link => opts.distanceMultiplier * (1 - link.similarity))
        .strength(0.5))           // Link spring strength
    .force("charge", forceManyBody().strength(opts.chargeStrength))
    .force("center", forceCenter(opts.centerX, opts.centerY))
    .force("collide", forceCollide().radius(opts.collisionRadius).strength(0.7))
    .alphaDecay(0.05)             // Faster convergence
    .velocityDecay(0.3);          // Damping for stability
```

### Tuning Guide

| Want More Spread? | Adjust |
|-------------------|--------|
| Longer edges | Increase `distanceMultiplier` (400 → 600) |
| More node repulsion | Decrease `chargeStrength` (-1000 → -1500) |
| More collision space | Increase `collisionRadius` (100 → 150) |

| Want Tighter Clustering? | Adjust |
|--------------------------|--------|
| Shorter edges | Decrease `distanceMultiplier` (400 → 250) |
| Less repulsion | Increase `chargeStrength` (-1000 → -500) |
| Closer nodes | Decrease `collisionRadius` (100 → 60) |

---

## Node Configuration

### DocumentNode.tsx - Sizing (v1.0.16)

```typescript
const useStyles = makeStyles({
    nodeContainer: {
        minWidth: "100px",
        maxWidth: "130px",
    },
    icon: {
        width: "20px",
        height: "20px",
        borderRadius: tokens.borderRadiusSmall,
    },
    documentName: {
        maxWidth: "80px",
        fontSize: tokens.fontSizeBase100,  // Smallest font size
    },
});
```

### Node Size History

| Version | minWidth | maxWidth | Icon Size | Font Size |
|---------|----------|----------|-----------|-----------|
| 1.0.14 | 180px | 220px | 32px | Base200 |
| 1.0.15 | 140px | 170px | 24px | Base200 |
| 1.0.16 | 100px | 130px | 20px | Base100 |

### Handle Positioning (v1.0.16)

Handles are positioned on left/right sides for radial layouts:

```typescript
// Target handle (incoming edges) - LEFT side
<Handle
    type="target"
    position={Position.Left}
    style={{
        width: "6px",
        height: "6px",
        background: tokens.colorBrandBackground,
        border: `1px solid ${tokens.colorNeutralBackground1}`,
    }}
/>

// Source handle (outgoing edges) - RIGHT side
<Handle
    type="source"
    position={Position.Right}
    style={{
        width: "6px",
        height: "6px",
        background: tokens.colorBrandBackground,
        border: `1px solid ${tokens.colorNeutralBackground1}`,
    }}
/>
```

### Handle Position Options

| Position | Best For |
|----------|----------|
| `Position.Top` / `Position.Bottom` | Hierarchical/tree layouts |
| `Position.Left` / `Position.Right` | Radial/force layouts (current) |
| All four positions | Maximum flexibility |

---

## Edge Configuration

### DocumentEdge.tsx

Custom edge with similarity-based styling:

```typescript
const getEdgeStyle = (similarity: number, isDarkMode: boolean) => {
    // Thicker lines for higher similarity
    const strokeWidth = 1 + similarity * 2;  // 1-3px

    // Opacity based on similarity
    const opacity = 0.3 + similarity * 0.5;  // 0.3-0.8

    return {
        stroke: isDarkMode ? "#888" : "#666",
        strokeWidth,
        opacity,
    };
};
```

---

## Deployment Process

### Prerequisites

1. PAC CLI authenticated: `pac auth list`
2. Connected to target environment: `pac org select`
3. Clean build artifacts

### Deployment Steps

```powershell
# 1. Navigate to PCF directory
cd src/client/pcf/DocumentRelationshipViewer

# 2. Clean temp directories (prevents file lock errors)
Remove-Item -Recurse -Force "obj/PowerAppsToolsTemp_sprk" -ErrorAction SilentlyContinue

# 3. Build the control
npm run build

# 4. Push to Dataverse
pac pcf push --publisher-prefix sprk
```

### Common Issues

| Error | Solution |
|-------|----------|
| `Unable to remove directory "obj\Debug\Metadata"` | Wait 3 seconds, delete `obj/PowerAppsToolsTemp_sprk`, retry |
| `Cannot start the requested operation [Import]` | Another PublishAll in progress. Wait 15-30 seconds, retry |
| Build fails on `Directory.Packages.props` | Temporarily rename to `.disabled` during push |

### Version Bumping Checklist

When updating the control, bump version in **4 locations**:

1. `ControlManifest.Input.xml` - `version` attribute AND description
2. `package.json` - `version` field
3. `DocumentRelationshipViewer.tsx` - `CONTROL_VERSION` constant
4. `RelationshipViewerModal.tsx` - `CONTROL_VERSION` constant

---

## Form Registration

### Adding to Dataverse Form

1. Open Power Apps Maker Portal
2. Navigate to Tables → sprk_document → Forms
3. Open the main form in edit mode
4. Add a new Tab (e.g., "Search") or use existing
5. Add a Section with full width (1 column, 100% width)
6. Add a field (e.g., sprk_documentid or a custom text field)
7. Select the field → Properties → Controls → Add Control
8. Choose "Document Relationship Viewer"
9. Configure properties:
   - **documentId**: Bind to sprk_documentid (or relevant field)
   - **tenantId**: Static value (your tenant ID)
   - **apiBaseUrl**: Static value (e.g., `https://spe-api-dev-67e2xz.azurewebsites.net`)
10. Save and Publish

### Form Configuration Tips

- Place in a dedicated tab for full-screen experience
- Use a 1-column section with 100% width
- The control reads `contextInfo.entityId` if documentId binding is empty

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.1 | Jan 2026 | Initial deployment with modal format |
| 1.0.9 | Jan 2026 | Portal to document.body for modal |
| 1.0.10 | Jan 2026 | Removed modal, adopted AnalysisWorkspace pattern |
| 1.0.11 | Jan 2026 | Fixed horizontal width issues |
| 1.0.12 | Jan 2026 | Wait for real dimensions before rendering |
| 1.0.13 | Jan 2026 | Viewport-based width `calc(100vw - 320px)` |
| 1.0.14 | Jan 2026 | Fixed graph centering (0,0 origin), removed minimap |
| 1.0.15 | Jan 2026 | Smaller nodes, increased force spread |
| 1.0.16 | Jan 2026 | Even smaller nodes, left/right handles for radial layout |

---

## Lessons Learned

### 1. Virtual Controls and CSS Containment

Virtual PCF controls (`control-type="virtual"`) render directly in the DOM without an iframe. However, Dataverse forms use CSS properties that create new containing blocks:

- `transform`, `filter`, `perspective` on parent elements
- These cause `position: fixed` to be relative to the containing block, not viewport

**Solution**: For full-screen modals, portal to `document.body` or use section-filling layouts instead.

### 2. Dimension Measurement Timing

React Flow requires explicit width/height. The container may not have dimensions immediately on mount.

**Solution**:
```typescript
const [dimensions, setDimensions] = useState<{width: number; height: number} | null>(null);

// Wait for real dimensions before rendering
if (!dimensions) return <Spinner />;

<ReactFlow width={dimensions.width} height={dimensions.height} />
```

### 3. Force Layout Centering

Using container center coordinates caused graph to render off-screen with large viewport widths.

**Solution**: Use (0, 0) as force center and let React Flow's `fitView` handle viewport positioning.

### 4. File Lock During Deployment

`pac pcf push` sometimes fails with "Unable to remove directory" due to file locks.

**Solution**: Clear temp directory before deploy:
```powershell
Remove-Item -Recurse -Force "obj/PowerAppsToolsTemp_sprk" -ErrorAction SilentlyContinue
```

### 5. Handle Positioning for Radial Layouts

Top/bottom handles work well for hierarchical layouts but look awkward in force-directed radial layouts where nodes surround the center.

**Solution**: Use left (target) and right (source) handles for radial layouts.

### 6. React Flow Version Compatibility

`reactflow` v11+ requires React 18. For React 16 compatibility, use `react-flow-renderer` v10.x.

---

## Alternative: Field-Bound Approach

The current implementation uses a section-filling layout. An alternative approach binds to a specific field and opens a modal dialog.

### When to Use Field-Bound

- Need the visualization as a popup/dialog
- Want to trigger from a ribbon button
- Need to preserve form layout

### Field-Bound Implementation (v1.0.1 - v1.0.9)

```typescript
// In main component - render trigger button
return (
    <FluentProvider theme={theme}>
        <Button onClick={() => setIsModalOpen(true)}>
            View Relationships
        </Button>
        <RelationshipViewerModal
            isOpen={isModalOpen}
            onClose={() => setIsModalOpen(false)}
            nodes={nodes}
            edges={edges}
            // ...
        />
    </FluentProvider>
);
```

### Modal CSS (Portal to document.body)

```typescript
// Portal the modal to document.body to escape Dataverse CSS containment
return ReactDOM.createPortal(
    <div className={styles.overlay}>
        <div className={styles.modalContainer}>
            {/* Modal content */}
        </div>
    </div>,
    document.body
);
```

### Modal Styles

```typescript
const useStyles = makeStyles({
    overlay: {
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "rgba(0, 0, 0, 0.5)",
        zIndex: 10000,  // High z-index for Dataverse
    },
    modalContainer: {
        position: "absolute",
        top: "16px",
        left: "16px",
        right: "16px",
        bottom: "16px",
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusLarge,
        boxShadow: tokens.shadow64,
    },
});
```

### Key Differences

| Aspect | Section Layout (Current) | Field-Bound Modal |
|--------|--------------------------|-------------------|
| Trigger | Automatic on tab | Button click or ribbon |
| Layout | Fills form section | Overlay on form |
| CSS issues | Viewport calc needed | Portal to body needed |
| User flow | See on tab navigate | Explicit open/close |

---

## File Locations

| File | Purpose |
|------|---------|
| `ControlManifest.Input.xml` | Control definition and properties |
| `package.json` | Dependencies and scripts |
| `DocumentRelationshipViewer.tsx` | Main component with form integration |
| `DocumentGraph.tsx` | React Flow wrapper with force layout |
| `DocumentNode.tsx` | Custom node component |
| `DocumentEdge.tsx` | Custom edge component |
| `useForceLayout.ts` | d3-force simulation hook |
| `RelationshipViewerModal.tsx` | Modal version (preserved for future use) |
| `types/graph.ts` | TypeScript interfaces |

---

*This guide should be updated when making significant changes to the control.*
