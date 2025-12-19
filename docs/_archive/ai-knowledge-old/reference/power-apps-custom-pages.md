# Power Apps Custom Pages - Reference Guide

> **Purpose**: Reference for building Custom Pages in Power Apps (Canvas-like pages within Model-Driven Apps)
> **Last Updated**: December 14, 2025
> **Applies To**: Custom page development, PCF integration, Analysis Workspace implementation

---

## TL;DR

Custom Pages are **Canvas-like pages** embedded in Model-Driven Apps. Use for rich, custom UX that standard forms can't provide. Key differences: Access via navigation, can host PCF controls, uses PowerFx but has MDA context.

---

## What Are Custom Pages?

**Custom Pages** allow you to build Canvas-like experiences within Model-Driven Apps.

| Feature | Custom Page | Canvas App | Model-Driven Form |
|---------|-------------|------------|-------------------|
| **Environment** | Inside MDA | Standalone | Inside MDA |
| **Language** | PowerFx | PowerFx | JavaScript |
| **Navigation** | MDA navigation | App navigation | Form navigation |
| **Data Access** | Dataverse connectors | All connectors | Dataverse direct |
| **PCF Controls** | ✅ Yes | ✅ Yes | ✅ Yes |
| **Fluent UI** | ✅ Yes | ✅ Yes | Limited |
| **Responsive** | ✅ Yes | Manual | Limited |

**Use Cases:**
- ✅ Analysis Workspace (two-column layout + chat)
- ✅ Complex dashboards
- ✅ Multi-step wizards
- ✅ Interactive data visualizations
- ❌ Simple data entry (use forms)
- ❌ List views (use grids)

---

## Display Settings (Critical for Dialogs)

When using Custom Pages in dialogs (modal windows), display settings are **critical** for proper sizing.

### Settings → Display → Layout

| Setting | Use Case | Behavior |
|---------|----------|----------|
| **Responsive** | Dialogs, varying screen sizes | Page resizes with container |
| **Fixed** | Legacy, specific dimensions | Page stays at fixed size |

**For PCF controls in dialogs: Always use "Responsive"**

### How to Configure

1. Open Custom Page in Power Apps Studio
2. Click **Settings** (gear icon) → **Display**
3. Select **"Responsive"** (not "Fixed")
4. Save and Publish

### Why This Matters

```
Fixed Mode:
  Dialog opens at 60%x70% → 1152x756px
  Custom Page stays at 640x520 → ❌ Wasted space

Responsive Mode:
  Dialog opens at 60%x70% → 1152x756px
  Custom Page fills dialog → 1152x756px → ✅ Full use
```

---

## Creating Custom Pages

### Method 1: Power Apps Studio (Recommended)

```powershell
# Prerequisites
pac auth create --environment https://yourorg.crm.dynamics.com

# Create new custom page
pac canvas create --msapp-name "AnalysisWorkspace" --output-directory ./src/pages/
```

**In Power Apps Studio:**
1. Open solution in https://make.powerapps.com
2. Click **New** → **Page** → **Custom page**
3. Name: `sprk_AnalysisWorkspace`
4. Design layout using Canvas designer
5. Save and publish

### Method 2: ALM / Source Control

```
src/
├── pages/
│   └── sprk_AnalysisWorkspace/
│       ├── sprk_AnalysisWorkspace.pa.yaml
│       ├── Src/
│       │   ├── EditorState/
│       │   └── ...
│       └── DataSources/
```

**Unpack .msapp:**
```powershell
pac canvas unpack --msapp AnalysisWorkspace.msapp --outdir ./src/pages/AnalysisWorkspace
```

**Pack to .msapp:**
```powershell
pac canvas pack --sources ./src/pages/AnalysisWorkspace --msapp AnalysisWorkspace.msapp
```

---

## Navigation to Custom Pages

### From Model-Driven Form (JavaScript)

```javascript
// Open custom page from ribbon button or form script
Xrm.Navigation.navigateTo(
  {
    pageType: "custom",
    name: "sprk_analysisworkspace", // Logical name (lowercase)
    recordId: analysisId,             // Optional: Pass record context
    entityName: "sprk_analysis"       // Optional: Entity context
  },
  {
    target: 2,        // 1 = Inline (full page), 2 = Dialog (modal)
    position: 1,      // 1 = Center
    width: { value: 80, unit: "%" },
    height: { value: 90, unit: "%" }
  }
).then(
  (success) => console.log("Custom page opened"),
  (error) => console.error("Error opening page", error)
);
```

### From Sitemap (Direct Navigation)

```xml
<!-- customizations.xml - Sitemap entry -->
<SubArea Id="sprk.AnalysisWorkspace" 
         Url="/main.aspx?pagetype=custom&name=sprk_analysisworkspace" 
         Icon="/_imgs/area/analysis_icon.png"
         Title="Analysis Workspace" />
```

### From Custom Page to Another Page

```javascript
// PowerFx in Custom Page
Navigate('sprk_analysisworkspace', ScreenTransition.Fade, 
  {
    recordId: varAnalysisId,
    entityName: "sprk_analysis"
  }
)
```

---

## Passing Parameters

### Method 1: URL Parameters (Simple)

```javascript
// JavaScript - Open with query params
Xrm.Navigation.navigateTo({
  pageType: "custom",
  name: "sprk_analysisworkspace",
  recordId: analysisId
});
```

**In Custom Page (PowerFx):**
```javascript
// Access parameters
Set(varAnalysisId, Param("recordId"));
Set(varEntityName, Param("entityName"));

// Load data
LookUp(Analyses, 'Analysis' = GUID(varAnalysisId))
```

### Method 2: Session Storage (Complex Objects)

**Sending Page (JavaScript):**
```javascript
// Store complex object before navigation
const context = {
  analysisId: analysisId,
  documentId: documentId,
  sessionId: sessionId,
  scopes: {
    skillIds: ["guid1", "guid2"],
    knowledgeIds: ["guid3"]
  }
};

sessionStorage.setItem("analysisContext", JSON.stringify(context));

// Then navigate
Xrm.Navigation.navigateTo({
  pageType: "custom",
  name: "sprk_analysisworkspace"
});
```

**Custom Page (PowerFx with PCF Bridge):**
```javascript
// Use custom PCF control to read sessionStorage
// See pattern below
```

---

## PCF Controls in Custom Pages

### Adding PCF to Custom Page

1. **Insert** → **Custom** → **Import components**
2. Select PCF control from solution
3. Add to page canvas
4. Configure properties

### Data Binding

```javascript
// PowerFx - Bind PCF control to data
Set(varAnalysisData, 
  LookUp(
    Analyses,
    'Analysis' = GUID(Param("recordId"))
  )
);

// PCF Control properties
AnalysisWorkspaceControl.AnalysisId = varAnalysisData.'Analysis'
AnalysisWorkspaceControl.DocumentId = varAnalysisData.'Document'.'Document'
AnalysisWorkspaceControl.OnSaveComplete = UpdateContext({refresh: true})
```

### PCF Communication Pattern

**Custom Page → PCF Control:**
```javascript
// PowerFx: Set input properties
Set(varUserMessage, TextInput1.Text);
AnalysisWorkspaceControl.Message = varUserMessage;
```

**PCF Control → Custom Page:**
```typescript
// PCF TypeScript: Notify outputs changed
context.parameters.onAnalysisComplete.setValue({
  analysisId: this._analysisId,
  status: "completed"
});
context.notifyOutputChanged();
```

**Custom Page (PowerFx): React to output:**
```javascript
// Trigger: AnalysisWorkspaceControl.OnAnalysisComplete
If(
  AnalysisWorkspaceControl.Status = "completed",
  Notify("Analysis completed successfully", NotificationType.Success);
  Navigate(Back())
)
```

---

## PCF Controls in Dialog Custom Pages

When hosting PCF controls in Custom Pages opened as dialogs, follow this **complete pattern** for responsive sizing.

### Architecture Overview

```
Dialog (opened via Xrm.Navigation.navigateTo)
  └── Custom Page (Responsive mode)
       └── Screen (Max(App.Width/Height, MinScreen*))
            └── PCF Control (Parent.Width/Height)
                 └── trackContainerResize(true) receives dimensions
```

### Step 1: JavaScript Navigation (Ribbon/Form Script)

```javascript
// Open Custom Page as a responsive dialog
Xrm.Navigation.navigateTo(
  {
    pageType: "custom",
    name: "sprk_analysisbuilder_40af8"  // Custom Page logical name
  },
  {
    target: 2,                          // 2 = Dialog (modal)
    position: 1,                        // 1 = Center
    width: { value: 60, unit: '%' },    // 60% of viewport width
    height: { value: 70, unit: '%' }    // 70% of viewport height
  }
);
```

### Step 2: Custom Page Display Settings

In Power Apps Studio:
1. **Settings** → **Display** → Select **"Responsive"**

### Step 3: Screen Sizing (PowerFx)

**Important:** Screens cannot use `Parent.Width`. Use these formulas:

| Property | Formula |
|----------|---------|
| **Width** | `Max(App.Width, App.MinScreenWidth)` |
| **Height** | `Max(App.Height, App.MinScreenHeight)` |

### Step 4: PCF Control Sizing (PowerFx)

The PCF control inside the screen **can** use `Parent`:

| Property | Value |
|----------|-------|
| **Width** | `Parent.Width` |
| **Height** | `Parent.Height` |
| **X** | `0` |
| **Y** | `0` |

### Step 5: PCF Control Code (TypeScript)

In your PCF `index.ts`, enable container resize tracking:

```typescript
public init(
  context: ComponentFramework.Context<IInputs>,
  notifyOutputChanged: () => void,
  state: ComponentFramework.Dictionary,
  container: HTMLDivElement
): void {
  // CRITICAL: Enable resize tracking to receive dimensions
  context.mode.trackContainerResize(true);

  // Set container styles for responsive sizing
  container.style.display = "flex";
  container.style.flexDirection = "column";
  container.style.width = "100%";
  container.style.height = "100%";
  container.style.overflow = "hidden";
}

private renderComponent(): void {
  // Get allocated dimensions from Custom Page
  const allocatedWidth = this._context.mode.allocatedWidth;
  const allocatedHeight = this._context.mode.allocatedHeight;

  // Apply dimensions (-1 means fill available, use 100%)
  if (allocatedWidth > 0) {
    this._container.style.width = `${allocatedWidth}px`;
  } else {
    this._container.style.width = "100%";
  }

  if (allocatedHeight > 0) {
    this._container.style.height = `${allocatedHeight}px`;
  } else {
    this._container.style.height = "100%";
  }
}
```

### Complete Sizing Chain

```
┌─────────────────────────────────────────────────────────────────┐
│ Viewport (1920x1080)                                            │
│ ┌─────────────────────────────────────────────────────────────┐ │
│ │ Dialog (60% x 70% = 1152x756px)                             │ │
│ │ ┌─────────────────────────────────────────────────────────┐ │ │
│ │ │ Custom Page (Responsive → fills dialog)                 │ │ │
│ │ │ ┌─────────────────────────────────────────────────────┐ │ │ │
│ │ │ │ Screen (Max formulas → 1152x756)                    │ │ │ │
│ │ │ │ ┌─────────────────────────────────────────────────┐ │ │ │ │
│ │ │ │ │ PCF Control (Parent.Width/Height → 1152x756)    │ │ │ │ │
│ │ │ │ │                                                 │ │ │ │ │
│ │ │ │ │  trackContainerResize receives: 1152x756       │ │ │ │ │
│ │ │ │ └─────────────────────────────────────────────────┘ │ │ │ │
│ │ │ └─────────────────────────────────────────────────────┘ │ │ │
│ │ └─────────────────────────────────────────────────────────┘ │ │
│ └─────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

### Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| PCF shows at fixed size | Screen uses fixed Width/Height | Use `Max(App.Width, ...)` formulas |
| PCF smaller than dialog | PCF uses fixed Width/Height | Set PCF to `Parent.Width/Height` |
| PCF doesn't resize | Missing `trackContainerResize` | Add `context.mode.trackContainerResize(true)` |
| Display mode is Fixed | Settings → Display → Fixed | Change to "Responsive" |

---

## Layout Patterns for Custom Pages

### Sizing Formula Reference

**Critical:** Screens and controls have different sizing capabilities.

| Element | Width Formula | Height Formula |
|---------|---------------|----------------|
| **Screen** | `Max(App.Width, App.MinScreenWidth)` | `Max(App.Height, App.MinScreenHeight)` |
| **Container** | `Parent.Width` | `Parent.Height` |
| **PCF Control** | `Parent.Width` | `Parent.Height` |
| **Button/Label** | `Parent.Width` or fixed | `Parent.Height` or fixed |

> **Why?** Screens are top-level and don't have a "Parent". They use `App` properties.
> Controls inside screens inherit from their container, so they use `Parent`.

### Two-Column Layout (Analysis Workspace)

```
┌─────────────────────────────────────────────────────────────┐
│  Header (Container)                                         │
│  • Title, breadcrumb, actions                               │
├───────────────────────────┬─────────────────────────────────┤
│  Left Panel (Container)   │  Right Panel (Container)        │
│  Width: 50%               │  Width: 50%                     │
│  ────────────────────────│──────────────────────────────── │
│  Working Document (PCF)   │  Source Preview (PCF)           │
│  • Rich text editor       │  • SpeFileViewer                │
│  • Monaco/Markdown        │  • Office Online embed          │
│                           │                                 │
├───────────────────────────┴─────────────────────────────────┤
│  Footer (Container)                                         │
│  • Chat input, send button                                  │
│  • AnalysisWorkspace PCF control (chat interface)           │
└─────────────────────────────────────────────────────────────┘
```

**PowerFx Layout:**
```javascript
// Header Container
Header.X = 0
Header.Y = 0
Header.Width = Parent.Width
Header.Height = 60

// Left Panel Container
LeftPanel.X = 0
LeftPanel.Y = Header.Y + Header.Height
LeftPanel.Width = Parent.Width / 2
LeftPanel.Height = Parent.Height - Header.Height - Footer.Height

// Right Panel Container
RightPanel.X = Parent.Width / 2
RightPanel.Y = Header.Y + Header.Height
RightPanel.Width = Parent.Width / 2
RightPanel.Height = Parent.Height - Header.Height - Footer.Height

// Footer Container
Footer.X = 0
Footer.Y = Parent.Height - 80
Footer.Width = Parent.Width
Footer.Height = 80
```

### Responsive Design

```javascript
// Detect screen size
Set(varScreenSize, 
  If(App.Width < 600, "Small",
     App.Width < 1200, "Medium",
     "Large")
);

// Adjust layout
LeftPanel.Width = If(
  varScreenSize = "Small", Parent.Width,        // Stack vertically
  varScreenSize = "Medium", Parent.Width * 0.6, // 60/40 split
  Parent.Width / 2                              // 50/50 split
)
```

---

## Dataverse Connector Usage

### Reading Data

```javascript
// OnVisible: Load analysis
Set(varAnalysisId, GUID(Param("recordId")));

Set(varAnalysis,
  LookUp(
    Analyses,
    'Analysis' = varAnalysisId
  )
);

// Load related data
ClearCollect(colChatMessages,
  Filter(
    'Analysis Chat Messages',
    'Analysis'.'Analysis' = varAnalysisId,
    SortBy('Created On', Descending)
  )
);
```

### Writing Data

```javascript
// Update analysis working document
Patch(
  Analyses,
  LookUp(Analyses, 'Analysis' = varAnalysisId),
  {
    'Working Document': varWorkingDocumentText,
    'Session ID': varSessionId
  }
);

// Create new chat message
Patch(
  'Analysis Chat Messages',
  Defaults('Analysis Chat Messages'),
  {
    'Analysis': LookUp(Analyses, 'Analysis' = varAnalysisId),
    'Role': "User",
    'Content': TextInput1.Text
  }
);
```

### Delegable Queries (Performance)

```javascript
// ✅ Good: Delegable filter
Filter(
  Analyses,
  'Document'.'Document' = varDocumentId,
  'Status' <> 'Completed'
)

// ❌ Bad: Non-delegable (StartsWith not supported)
Filter(
  Analyses,
  StartsWith('Name', "Analysis")
)

// ✅ Workaround: Use server-side view
Filter(
  'Analyses (Active Analyses View)',
  'Document'.'Document' = varDocumentId
)
```

---

## Environment Variables in Custom Pages

```javascript
// Access environment variables
Set(varBffApiUrl, Environment('sprk_BffApiBaseUrl'));
Set(varOpenAiEndpoint, Environment('sprk_AzureOpenAiEndpoint'));

// Use in HTTP requests (via PCF or Power Automate)
// Note: Direct HTTP from Custom Pages requires premium connector
```

**Recommendation:** Use PCF control to make HTTP calls (see next section).

---

## HTTP Requests from Custom Pages

### Option 1: Via PCF Control (Recommended)

**Why:** Custom Pages don't have native `fetch()` API access. PCF controls do.

```typescript
// PCF Control: ApiClient.tsx
export class ApiClient implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  
  public async callBffApi(endpoint: string, method: string, body?: any): Promise<any> {
    const apiBaseUrl = this.getEnvironmentVariable('sprk_BffApiBaseUrl');
    const token = await this.acquireToken();
    
    const response = await fetch(`${apiBaseUrl}${endpoint}`, {
      method: method,
      headers: {
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json'
      },
      body: body ? JSON.stringify(body) : undefined
    });
    
    return await response.json();
  }
  
  private getEnvironmentVariable(name: string): string {
    return (this.context as any).webAPI.retrieveMultipleRecords(
      'environmentvariabledefinition',
      `?$filter=schemaname eq '${name}'`
    ).then(/* ... */);
  }
}
```

**Custom Page (PowerFx):**
```javascript
// Trigger API call via PCF
Set(varRequestPayload, {
  documentId: varDocumentId,
  message: TextInput1.Text
});

ApiClientControl.Endpoint = "/api/ai/analysis/continue";
ApiClientControl.Method = "POST";
ApiClientControl.Body = JSON(varRequestPayload);
ApiClientControl.Execute = true; // Trigger

// React to response
If(
  !IsBlank(ApiClientControl.Response),
  Set(varApiResponse, ParseJSON(ApiClientControl.Response));
  Notify("Analysis updated", NotificationType.Success)
)
```

### Option 2: Via Power Automate Cloud Flow

```javascript
// Custom Page: Trigger flow
Set(varFlowResponse,
  'Analysis Workspace Flow'.Run(
    varAnalysisId,
    varUserMessage
  )
);

// Flow: Make HTTP request to BFF API
// Returns: Response data
Set(varAnalysisResult, varFlowResponse.result);
```

**Trade-offs:**
| Method | Pros | Cons |
|--------|------|------|
| PCF Control | Fast, synchronous, full control | More complex |
| Power Automate | Simple, no code | Async, slower, flow limits |

---

## Debugging Custom Pages

### Power Apps Monitor

```powershell
# Start monitoring session
https://make.powerapps.com/environments/{envId}/monitor
```

**What to Monitor:**
- Formula errors (red dots)
- Dataverse queries (delegation warnings)
- PCF control events
- Navigation events

### Browser DevTools (for PCF Debugging)

```javascript
// In browser console (F12) when Custom Page is open

// Check if PCF received correct dimensions
console.log("Allocated:", context.mode.allocatedWidth, context.mode.allocatedHeight);

// Inspect PCF container
document.querySelector('[data-control-name="YourPCFControlName"]');

// Check for JavaScript errors in Console tab
// Look for "[YourPCF]" prefixed log messages
```

**Tips:**
- Use `console.log` in your PCF `init()` and `updateView()` to trace sizing
- Check Network tab for failed API calls from PCF
- Elements tab to inspect PCF container dimensions

### Common Issues

| Issue | Symptom | Solution |
|-------|---------|----------|
| **Delegation Warning** | Yellow warning triangle | Use server-side views, limit data |
| **PCF Not Loading** | Blank space | Check solution includes PCF, republish |
| **Parameters Missing** | `Param()` returns blank | Verify navigation passes parameters correctly |
| **Environment Variable Null** | Variable not found | Ensure solution includes env var, set value |

---

## Best Practices

### Performance

- ✅ **Load data OnVisible**, not continuously
- ✅ **Use collections** for repeated access (`ClearCollect`)
- ✅ **Limit Gallery items** to < 500 (use pagination)
- ✅ **Defer heavy operations** until user action
- ❌ **Avoid OnChange triggers** that query Dataverse

### User Experience

- ✅ **Show loading spinners** during async operations
- ✅ **Validate inputs** before submitting
- ✅ **Handle errors gracefully** (Notify with clear messages)
- ✅ **Provide back navigation** (Button with `Back()`)
- ❌ **Don't block UI** during background work

### Maintainability

- ✅ **Use named variables** (`varAnalysisId`, not `var1`)
- ✅ **Group related controls** in containers
- ✅ **Document complex formulas** (comments in formula bar)
- ✅ **Test in multiple screen sizes**
- ❌ **Avoid deeply nested `If()` statements** (use `Switch()`)

---

## Example: Analysis Workspace Custom Page

### Structure

```
sprk_AnalysisWorkspace (Custom Page)
├── HeaderContainer
│   ├── BackButton (Icon)
│   ├── TitleLabel (Label)
│   └── SaveButton (Button)
├── BodyContainer
│   ├── LeftPanelContainer
│   │   └── AnalysisWorkspacePCF (PCF Control)
│   └── RightPanelContainer
│       └── SpeFileViewerPCF (PCF Control)
└── FooterContainer
    ├── ChatInputText (Text Input)
    └── SendButton (Button)
```

### Key Formulas

**OnVisible (Page Load):**
```javascript
// Load analysis
Set(varAnalysisId, GUID(Param("recordId")));
Set(varAnalysis, LookUp(Analyses, 'Analysis' = varAnalysisId));

// Load document
Set(varDocument, 
  LookUp(Documents, 'Document' = varAnalysis.'Document'.'Document')
);

// Load chat history
ClearCollect(colChatHistory,
  Filter(
    'Analysis Chat Messages',
    'Analysis'.'Analysis' = varAnalysisId
  )
);

// Generate session ID
Set(varSessionId, GUID());
```

**SendButton.OnSelect:**
```javascript
// Validate input
If(
  IsBlank(ChatInputText.Text),
  Notify("Please enter a message", NotificationType.Warning);
  Return
);

// Add user message to collection
Collect(colChatHistory, {
  Role: "User",
  Content: ChatInputText.Text,
  Timestamp: Now()
});

// Clear input
Reset(ChatInputText);

// Trigger PCF to send message
AnalysisWorkspacePCF.Message = Last(colChatHistory).Content;
AnalysisWorkspacePCF.SendMessage = true;

// PCF will stream response and update working document
```

**AnalysisWorkspacePCF.OnResponseChunk:**
```javascript
// PCF outputs response chunks via SSE
// Collect chunks
Collect(colResponseChunks, {
  Content: AnalysisWorkspacePCF.ResponseChunk,
  Timestamp: Now()
});

// Update working document preview (concatenate chunks)
Set(varWorkingDocument, 
  varWorkingDocument & AnalysisWorkspacePCF.ResponseChunk
);
```

---

## Related Documentation

- [PCF Component Development](./pcf-component-patterns.md) (TODO)
- [Power Apps Canvas Apps](https://docs.microsoft.com/power-apps/maker/canvas-apps/)
- [Custom Pages Overview](https://docs.microsoft.com/power-apps/maker/model-driven-apps/design-page-for-model-app)
- [PowerFx Formula Reference](https://docs.microsoft.com/power-platform/power-fx/formula-reference)

---

## Changes from Canvas Apps

| Feature | Canvas App | Custom Page |
|---------|------------|-------------|
| **Host** | Standalone | Inside MDA |
| **Navigation** | Screens | Pages (via MDA nav) |
| **Data context** | Manual connectors | Inherits MDA context |
| **Theming** | Custom | Inherits MDA theme |
| **User context** | `User()` | `User()` + MDA security |
| **URL** | app.powerapps.com | org.crm.dynamics.com |

---

*Last Updated: December 14, 2025*
*Based on: AnalysisBuilder PCF implementation in Custom Page dialog*
