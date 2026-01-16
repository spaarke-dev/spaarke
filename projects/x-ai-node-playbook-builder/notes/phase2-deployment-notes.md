# Phase 2 Deployment Notes - Playbook Builder

> **Date**: 2026-01-09
> **Task**: 019 - Phase 2 Tests and PCF Deployment

---

## PCF Control Deployment

### Build Process

1. **Build playbook-builder React app** (deployed to BFF API wwwroot)
   ```bash
   cd src/client/playbook-builder
   npm run build
   # Output: ~710KB total, ~210KB gzipped
   # Copied to: src/server/api/Sprk.Bff.Api/wwwroot/playbook-builder/
   ```

2. **Build PlaybookBuilderHost PCF control**
   ```bash
   cd src/client/pcf/PlaybookBuilderHost
   npm install
   npm run build:prod
   # Output: bundle.js ~30KB
   ```

3. **Create solution package**
   ```bash
   cd src/client/pcf/PlaybookBuilderHost
   rm -rf Solution
   mkdir -p Solution && cd Solution
   pac solution init --publisher-name Spaarke --publisher-prefix sprk
   pac solution add-reference --path ..

   # Add ManagePackageVersionsCentrally=false to both .cdsproj and .pcfproj
   # (Required due to Directory.Packages.props in repo root)

   dotnet build --configuration Debug
   # Output: bin/Debug/Solution.zip
   ```

4. **Import to Dataverse**
   ```bash
   pac auth select --index 2  # Select spaarkedev1
   pac solution import --path bin/Debug/Solution.zip --publish-changes
   ```

### Control Properties

| Property | Type | Usage | Description |
|----------|------|-------|-------------|
| `playbookId` | SingleLine.Text | input | GUID of sprk_playbook record |
| `playbookName` | SingleLine.Text | input | Display name of playbook |
| `canvasJson` | Multiple | bound | Serialized canvas nodes/edges |
| `builderBaseUrl` | SingleLine.Text | input | Builder app URL |
| `isDirty` | TwoOptions | output | Unsaved changes indicator |

---

## Form Configuration (Manual Steps)

### Prerequisites

- PlaybookBuilderHost PCF control deployed (Solution imported)
- Builder app deployed to BFF API (`/playbook-builder/`)
- `sprk_canvaslayoutjson` field exists on sprk_aiplaybook entity

### Step 1: Open Form Editor

1. Navigate to https://make.powerapps.com
2. Select **Spaarke DEV 1** environment
3. Go to **Tables** > **AI Playbook** (sprk_aiplaybook)
4. Open the main form in **Edit** mode

### Step 2: Add Builder Section

1. Add a new **1-column section** for the builder
2. Set section label: "Playbook Builder"
3. Set section to take full width

### Step 3: Add Canvas Field

1. Add the `sprk_canvaslayoutjson` field to the section
2. This is the Multiple (multiline text) field that stores the canvas JSON

### Step 4: Configure PCF Control

1. Select the `sprk_canvaslayoutjson` field
2. Click **Components** > **Get more components**
3. Search for "PlaybookBuilderHost" and select it
4. Configure control properties:

| Property | Static Value |
|----------|--------------|
| `builderBaseUrl` | `https://spe-api-dev-67e2xz.azurewebsites.net/playbook-builder/` |
| `playbookId` | Bind to `sprk_aiplaybookid` (formula) |
| `playbookName` | Bind to `sprk_name` |
| `canvasJson` | Bound to field (automatic) |

### Step 5: Save and Publish

1. Save the form
2. Publish customizations
3. Refresh the model-driven app

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Dataverse Form                           │
│  ┌─────────────────────────────────────────────────────────┐│
│  │           PlaybookBuilderHost (PCF Control)             ││
│  │              React 16 + Fluent UI v9                    ││
│  │  ┌───────────────────────────────────────────────────┐  ││
│  │  │                  <iframe>                         │  ││
│  │  │         Playbook Builder (React 18)               │  ││
│  │  │       React Flow + Zustand + Fluent UI            │  ││
│  │  │                                                   │  ││
│  │  │  ┌─────────┐  ┌──────────────┐  ┌────────────┐   │  ││
│  │  │  │ Palette │  │    Canvas    │  │ Properties │   │  ││
│  │  │  │  Nodes  │  │  (ReactFlow) │  │   Panel    │   │  ││
│  │  │  └─────────┘  └──────────────┘  └────────────┘   │  ││
│  │  └───────────────────────────────────────────────────┘  ││
│  │                    postMessage Bridge                   ││
│  └─────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
            │                                │
            ▼                                ▼
      ┌──────────────┐              ┌──────────────────┐
      │   Dataverse  │              │     BFF API      │
      │  sprk_canvaslayoutjson      │  /playbook-builder/
      └──────────────┘              │  /api/ai/playbooks/{id}/canvas
                                    └──────────────────┘
```

### Communication Flow

1. **INIT**: PCF sends playbook data to iframe via postMessage
2. **READY**: Iframe confirms it's ready to receive data
3. **DIRTY_CHANGE**: Iframe notifies PCF when changes are made
4. **SAVE_REQUEST**: PCF triggers save when form saves
5. **SAVE_SUCCESS/ERROR**: Iframe reports save result

---

## Testing Checklist

### Functional Tests

- [ ] Builder app loads at `/playbook-builder/` (direct access)
- [ ] PCF control loads in Dataverse form
- [ ] Iframe renders builder canvas
- [ ] Can drag nodes from palette to canvas
- [ ] Can select nodes and view properties
- [ ] Can edit node properties
- [ ] Properties sync back to node
- [ ] Dirty state updates when changes made
- [ ] Save persists canvas to Dataverse

### Integration Tests

- [ ] Canvas layout API returns data: `GET /api/ai/playbooks/{id}/canvas`
- [ ] Canvas layout API saves data: `PUT /api/ai/playbooks/{id}/canvas`
- [ ] Authorization filters work (owner can save, others can view)

---

## Known Issues

### PAC CLI Build Issues

1. **Directory.Packages.props conflict**: Must add `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>` to both `.cdsproj` and `.pcfproj` files.

2. **pac pcf push fails**: Use solution import method instead:
   ```bash
   pac solution init + pac solution add-reference + dotnet build + pac solution import
   ```

---

## Deployment URLs

| Environment | BFF API | Builder App |
|-------------|---------|-------------|
| Dev | https://spe-api-dev-67e2xz.azurewebsites.net | https://spe-api-dev-67e2xz.azurewebsites.net/playbook-builder/ |

---

## Next Steps (Phase 3)

1. Implement parallel execution in orchestration service
2. Add template engine for variable substitution
3. Create additional node executors (CreateTask, SendEmail, etc.)
4. Add execution visualization overlay on canvas
