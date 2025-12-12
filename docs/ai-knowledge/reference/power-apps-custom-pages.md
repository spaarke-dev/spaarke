# Power Apps Custom Pages - Reference Guide

> **Purpose**: Reference for building Custom Pages in Power Apps (Canvas-like pages within Model-Driven Apps)
> **Last Updated**: December 11, 2025
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
    target: 2,        // 1 = Dialog, 2 = Inline (replace main), 3 = New window
    width: { value: 80, unit: "%" },
    height: { value: 90, unit: "%" },
    position: 1       // 1 = Center
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

## Layout Patterns for Custom Pages

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

### Browser DevTools

```javascript
// In browser console (when page is open)
// Access Power Apps object
window.Power Apps

// View page variables
// (Limited access, more useful for PCF debugging)
```

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

*Last Updated: December 11, 2025*
*Next Review: Phase 2 (after initial implementation)*
