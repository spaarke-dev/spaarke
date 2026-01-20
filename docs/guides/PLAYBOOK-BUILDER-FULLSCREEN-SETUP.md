# Playbook Builder Full-Screen Setup Guide

> **Purpose**: Deploy the Playbook Builder PCF control as a full-screen Custom Page dialog
> **Entity**: sprk_analysisplaybook (Analysis Playbook)
> **Components**: Custom Page + Ribbon Button + JavaScript Web Resource

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│  Analysis Playbook Form                                      │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ [Open Builder] ← Ribbon Button                       │    │
│  └─────────────────────────────────────────────────────┘    │
│                          │                                   │
│                          ▼ Xrm.Navigation.navigateTo()       │
│  ┌─────────────────────────────────────────────────────┐    │
│  │           Custom Page Dialog (95% x 95%)             │    │
│  │  ┌─────────────────────────────────────────────┐    │    │
│  │  │       PlaybookBuilderHost PCF Control       │    │    │
│  │  │  ┌─────────────────────────────────────┐   │    │    │
│  │  │  │        React Flow Canvas            │   │    │    │
│  │  │  │   (nodes, edges, properties)        │   │    │    │
│  │  │  └─────────────────────────────────────┘   │    │    │
│  │  └─────────────────────────────────────────────┘    │    │
│  └─────────────────────────────────────────────────────┘    │
│                          │                                   │
│                          ▼ On Save → Patch sprk_canvasjson   │
└─────────────────────────────────────────────────────────────┘
```

---

## Step 1: Deploy Web Resource

Upload the JavaScript command script to Dataverse.

### 1.1 Create Web Resource

1. Navigate to **make.powerapps.com** → **Solutions**
2. Open your solution (e.g., `SpaarkePlatform`)
3. Click **New** → **More** → **Web resource**
4. Configure:
   - **Display Name**: `Playbook Commands`
   - **Name**: `sprk_playbook_commands` (will become `sprk_playbook_commands.js`)
   - **Type**: `JavaScript (JScript)`
5. Click **Choose file** and select:
   ```
   src/client/webresources/js/sprk_playbook_commands.js
   ```
6. Click **Save** → **Publish**

### 1.2 Verify Deployment

```bash
# List web resources
pac solution export --path temp.zip --name SpaarkePlatform
# Check for sprk_playbook_commands in the solution
```

---

## Step 2: Create Custom Page

Create a Canvas App Custom Page that hosts the PCF control.

### 2.1 Create New Custom Page

1. Navigate to **make.powerapps.com** → **Apps**
2. Click **New app** → **Page**
3. Configure:
   - **Name**: `Playbook Builder Page`
   - **Table**: `Analysis Playbook (sprk_analysisplaybook)`
4. Click **Create**

### 2.2 Add PCF Control

1. In Power Apps Studio, click **Insert** → **Get more components**
2. Search for `PlaybookBuilderHost`
3. Select and add the control
4. Configure control properties (see section 2.3)

### 2.3 Configure Control Data Binding

In Power Apps Studio, set the control properties:

| Property | Formula | Purpose |
|----------|---------|---------|
| `playbookId` | `Param("recordId")` | Record ID from dialog |
| `playbookName` | `LookUp('Analysis Playbooks', sprk_analysisplaybookid = GUID(Param("recordId"))).sprk_name` | Display name |
| `canvasJson` | `LookUp('Analysis Playbooks', sprk_analysisplaybookid = GUID(Param("recordId"))).sprk_canvasjson` | Current canvas state |

### 2.4 Add Save Logic

Add an OnChange handler to save canvas changes back to Dataverse:

```powerfx
// In App.OnStart or control OnChange
Set(varPlaybookId, GUID(Param("recordId")));

// When canvasJson output changes, patch back to Dataverse
If(
    !IsBlank(PlaybookBuilderHost.canvasJson),
    Patch(
        'Analysis Playbooks',
        LookUp('Analysis Playbooks', sprk_analysisplaybookid = varPlaybookId),
        { sprk_canvasjson: PlaybookBuilderHost.canvasJson }
    )
)
```

### 2.5 Configure Page Layout

1. Set page dimensions to fill container:
   - Width: `Parent.Width`
   - Height: `Parent.Height`
2. Remove default header/footer elements for clean full-screen experience
3. Set PCF control to fill page:
   - X: `0`
   - Y: `0`
   - Width: `Parent.Width`
   - Height: `Parent.Height`

### 2.6 Save and Publish

1. Click **File** → **Save**
2. Click **File** → **Publish**
3. Note the Custom Page logical name (shown in URL or properties)
   - Example: `sprk_playbookbuilderpage_xxxxx`
4. Update `sprk_playbook_commands.js` with the actual logical name

---

## Step 3: Add Ribbon Button

Add a command bar button to the Analysis Playbook form.

### 3.1 Ribbon XML

Create or update the ribbon definition for `sprk_analysisplaybook`:

```xml
<RibbonDiffXml>
  <CustomActions>
    <!-- Open Builder button on form command bar -->
    <CustomAction Id="Spaarke.Playbook.OpenBuilder.CustomAction"
                  Location="Mscrm.Form.sprk_analysisplaybook.MainTab.Actions.Controls._children"
                  Sequence="10">
      <CommandUIDefinition>
        <Button Id="Spaarke.Playbook.OpenBuilder.Button"
                Command="Spaarke.Playbook.OpenBuilder.Command"
                LabelText="Open Builder"
                Alt="Open Builder"
                ToolTipTitle="Open Builder"
                ToolTipDescription="Open the visual Playbook Builder in full-screen mode"
                TemplateAlias="o1"
                Image16by16="/_imgs/ribbon/Grid_Expand_16.png"
                Image32by32="/_imgs/ribbon/Grid_Expand_32.png"
                ModernImage="GridExpand" />
      </CommandUIDefinition>
    </CustomAction>
  </CustomActions>

  <CommandDefinitions>
    <CommandDefinition Id="Spaarke.Playbook.OpenBuilder.Command">
      <EnableRules>
        <EnableRule Id="Spaarke.Playbook.OpenBuilder.EnableRule" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_playbook_commands.js"
                           FunctionName="Spaarke_OpenPlaybookBuilder">
          <CrmParameter Value="PrimaryControl" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>
  </CommandDefinitions>

  <RuleDefinitions>
    <EnableRules>
      <EnableRule Id="Spaarke.Playbook.OpenBuilder.EnableRule">
        <JavaScriptFunction Library="$webresource:sprk_playbook_commands.js"
                           FunctionName="Spaarke_EnableOpenPlaybookBuilder">
          <CrmParameter Value="PrimaryControl" />
        </JavaScriptFunction>
      </EnableRule>
    </EnableRules>
  </RuleDefinitions>
</RibbonDiffXml>
```

### 3.2 Deploy Ribbon via Ribbon Workbench

1. Open **XrmToolBox** → **Ribbon Workbench**
2. Load your solution
3. Select `sprk_analysisplaybook` entity
4. Add button to Form command bar:
   - Right-click **MainTab** → **Customize Command**
   - Add new Button with above configuration
5. Publish

### 3.3 Alternative: Deploy via Solution XML

1. Export solution containing `sprk_analysisplaybook` entity
2. Extract solution ZIP
3. Edit `customizations.xml`:
   - Find `<Entity>` element for `sprk_analysisplaybook`
   - Add/update `<RibbonDiffXml>` section with above XML
4. Pack and import solution

---

## Step 4: Update Custom Page Name

After creating the Custom Page, update the JavaScript with the actual logical name:

```javascript
// In sprk_playbook_commands.js, line ~92
var pageInput = {
    pageType: "custom",
    name: "sprk_playbookbuilderpage_xxxxx",  // ← UPDATE WITH ACTUAL NAME
    recordId: params.playbookId
};
```

Re-upload the web resource after updating.

---

## Step 5: Test

### 5.1 Test Button Visibility

1. Open an existing Analysis Playbook record
2. Verify "Open Builder" button appears in command bar
3. Verify button is disabled for unsaved records

### 5.2 Test Dialog Opening

1. Click "Open Builder" button
2. Verify Custom Page opens as 95% x 95% dialog
3. Verify PCF control loads with correct playbook data
4. Verify canvas is interactive (add nodes, connect edges)

### 5.3 Test Save Flow

1. Make changes in the builder (add/modify nodes)
2. Click Save button in PCF toolbar
3. Close dialog
4. Verify `sprk_canvasjson` field is updated in Dataverse
5. Re-open builder and verify changes persisted

---

## Troubleshooting

### Button Not Appearing

1. Verify web resource is published
2. Clear browser cache (`Ctrl+Shift+R`)
3. Check browser console for JavaScript errors
4. Verify ribbon XML is correctly formatted

### Dialog Shows Error

1. Check Custom Page logical name matches JavaScript
2. Verify Custom Page is published
3. Check browser console for navigation errors
4. Verify PCF control is added to Custom Page

### Canvas Changes Not Saving

1. Check PCF `canvasJson` output is bound correctly
2. Verify Custom Page has save logic (Patch formula)
3. Check browser console for errors
4. Verify user has write permission on entity

---

## AI Assistant

The Playbook Builder includes an integrated AI Assistant that helps users build playbooks through natural language conversation.

### AI Assistant Overview

The AI Assistant provides intelligent guidance for:
- **Intent classification** - Understanding user requests and mapping to canvas operations
- **Scope selection** - Finding and suggesting existing scopes to reuse
- **Scope creation** - Creating new Actions, Skills, Tools, and Knowledge scopes
- **Playbook construction** - Building complete workflows through conversation

### Model Selection

The AI Assistant supports multiple models for different use cases:

| Model | Use Case | Latency | Token Cost |
|-------|----------|---------|------------|
| **GPT-4o** | Complex analysis, multi-step reasoning | ~2-3s | Higher |
| **GPT-4o-mini** | Simple operations, fast responses | ~500ms-1.5s | Lower |

**Selecting a model:**
1. Click the model selector in the AI Assistant panel header
2. Choose between GPT-4o (default) or GPT-4o-mini
3. Model selection persists for the session

**When to use each model:**
- **GPT-4o**: Building complex playbooks, analyzing lease documents, multi-node workflows
- **GPT-4o-mini**: Quick operations, scope searches, simple node additions

### Clarification Flow

When the AI Assistant cannot confidently classify user intent (confidence < 80%), it initiates a clarification flow:

1. **User submits message** - "Add a classification node"
2. **AI detects ambiguity** - Multiple possible interpretations
3. **Clarification questions displayed** - "What type of classification? Document type, sentiment, or custom?"
4. **User provides clarification** - "Document type classification"
5. **AI proceeds with high confidence** - Adds the appropriate node

**Clarification indicators:**
- Yellow status bar indicates clarification needed
- Questions are displayed as numbered options
- User can type response or click suggested options

### Intent Classification

The AI Assistant classifies user messages into operations:

| Intent | Description | Example Messages |
|--------|-------------|------------------|
| `add_node` | Add a new node to canvas | "Add an AI action node", "Insert condition" |
| `remove_node` | Remove existing node | "Delete the classifier", "Remove that node" |
| `connect_nodes` | Create edge between nodes | "Connect start to action", "Link them" |
| `configure_node` | Update node settings | "Set timeout to 30 seconds" |
| `search_scopes` | Find existing scopes | "Find lease analysis scopes" |
| `create_scope` | Create new scope | "Create a contract review action" |
| `validate` | Validate playbook | "Check for errors", "Validate" |
| `execute_test` | Run test mode | "Test the playbook" |

---

## Scope Management

The Playbook Builder supports full CRUD operations for scopes (Actions, Skills, Tools, Knowledge).

### Scope Types

| Type | Prefix | Purpose | Example |
|------|--------|---------|---------|
| **Actions** | ACT- | AI operations (classify, extract, summarize) | ACT-LEASE-CLASSIFY |
| **Skills** | SKL- | Reusable workflow patterns | SKL-CONTRACT-REVIEW |
| **Tools** | TL- | Canvas manipulation operations | TL-BUILDER-ADDNODE |
| **Knowledge** | KNW- | Reference data and catalogs | KNW-SCOPE-CATALOG |

### Ownership Model

Scopes follow a two-tier ownership model:

| Prefix | Owner | Editable | Description |
|--------|-------|----------|-------------|
| `SYS-` | System | No | Pre-built, immutable scopes |
| `CUST-` | Customer | Yes | User-created, fully editable |

**Rules:**
- System scopes (SYS-) cannot be modified or deleted
- Customer scopes (CUST-) support full CRUD operations
- Attempting to edit a SYS- scope returns an error

### Scope Operations

**Create** - Create a new customer scope:
1. Use AI Assistant: "Create a lease analysis action"
2. Or use Scope Browser: Click "New" button
3. Configure scope properties
4. Save creates `CUST-{NAME}` record in Dataverse

**Read** - Browse and search scopes:
1. Click "Browse Scopes" in the node configuration panel
2. Search by name, type, or description
3. Filter by owner (System/Customer)
4. Select scope to view details

**Update** - Modify customer scopes:
1. Select a CUST- scope
2. Click "Edit" button
3. Modify configuration
4. Save updates the Dataverse record

**Delete** - Remove customer scopes:
1. Select a CUST- scope
2. Click "Delete" button
3. Confirm deletion
4. Scope is removed from Dataverse

### Save As / Extend

**Save As** - Create a copy of any scope:
1. Select a scope (SYS- or CUST-)
2. Click "Save As" button
3. Enter new name (automatically prefixed with CUST-)
4. New scope created with `basedon` reference to original

**Extend** - Create a child scope that inherits updates:
1. Select a scope
2. Click "Extend" button
3. Configure extensions
4. Child scope maintains `parentscope` reference

### Search Capabilities

The scope search supports:
- **Semantic search** - Find scopes by meaning, not just keywords
- **Filter by type** - Actions, Skills, Tools, Knowledge
- **Filter by owner** - System only, Customer only, or All
- **Sort options** - Name, date created, relevance

---

## Test Execution Modes

The Playbook Builder supports three test modes for validating playbooks before production use.

### Mode Overview

| Mode | Storage | External Calls | Cleanup | Use Case |
|------|---------|----------------|---------|----------|
| **Mock** | None | No | N/A | Rapid iteration, UI testing |
| **Quick** | Temp container | Yes | 24 hours | Integration testing with real AI |
| **Production** | Production | Yes | No | Final validation |

### Mock Mode

**Purpose:** Fast iteration without any external calls or data persistence.

**Behavior:**
- Uses sample/mock data for all operations
- No Azure OpenAI calls
- No Dataverse updates
- Executes in < 1 second

**Use when:**
- Validating playbook structure and flow
- Testing UI interactions
- Rapid prototyping
- No AI credits to spend

**Example workflow:**
1. Build playbook with 5 nodes
2. Select "Mock" mode
3. Click "Test"
4. Instant feedback on flow execution

### Quick Mode

**Purpose:** Real AI execution with temporary storage that auto-cleans.

**Behavior:**
- Uses `playbook-test-documents` blob container
- Real Azure OpenAI calls
- Results stored temporarily
- Auto-cleanup after 24 hours

**Use when:**
- Validating AI responses and accuracy
- Testing scope configurations
- Pre-production verification
- Demo or training scenarios

**Example workflow:**
1. Build lease analysis playbook
2. Select "Quick" mode
3. Upload test lease document
4. Click "Test"
5. Review AI extraction results
6. Documents auto-deleted after 24 hours

### Production Mode

**Purpose:** Full execution against production data and storage.

**Behavior:**
- Uses production blob containers
- Real Azure OpenAI calls
- Results persist permanently
- Full audit logging

**Use when:**
- Final validation before deployment
- Testing with actual production documents
- Verifying permissions and access
- Compliance testing

**Caution:** Production mode creates permanent data. Use only for final validation.

### Selecting Test Mode

1. Click "Test" button in the toolbar
2. Test Mode Selector dialog appears
3. Choose mode: Mock, Quick, or Production
4. Configure test options (document selection, parameters)
5. Click "Run Test"
6. Monitor execution progress
7. Review results in the Test Results panel

---

## Troubleshooting

### Common Issues

#### AI Assistant Not Responding

**Symptoms:** Messages submitted but no response appears

**Solutions:**
1. Check browser console for network errors
2. Verify BFF API is running and accessible
3. Check Azure OpenAI service status
4. Verify authentication token is valid (refresh page)

#### Model Selection Not Working

**Symptoms:** Model dropdown doesn't change behavior

**Solutions:**
1. Clear browser cache and refresh
2. Check `aiAssistantStore.ts` for model state
3. Verify backend accepts model parameter in requests

#### Scope Search Returns No Results

**Symptoms:** Search shows empty results despite known scopes

**Solutions:**
1. Check Dataverse connection in BFF logs
2. Verify `ScopeResolverService` is registered
3. Confirm scopes exist in target environment
4. Check search index status (if using semantic search)

#### Test Mode Fails to Execute

**Symptoms:** Test starts but fails immediately

**Solutions:**
1. **Mock mode:** Check for JavaScript errors in console
2. **Quick mode:** Verify `playbook-test-documents` container exists
3. **Production mode:** Check user permissions on production containers
4. All modes: Verify `PlaybookOrchestrationService` is healthy

#### Clarification Loop

**Symptoms:** AI keeps asking for clarification repeatedly

**Solutions:**
1. Provide more specific input
2. Include explicit operation type (e.g., "add an AI action node")
3. Check intent classification confidence threshold in settings
4. Review `AiPlaybookBuilderService` logs for classification details

#### Scope Save Fails

**Symptoms:** Error when saving new or modified scope

**Solutions:**
1. Verify scope name is unique
2. Check for invalid characters in name
3. Confirm user has create/update permissions in Dataverse
4. For SYS- scopes, remember they are immutable (use Save As instead)

#### Canvas Operations Not Applying

**Symptoms:** AI says it added a node but canvas is unchanged

**Solutions:**
1. Check `canvasStore.ts` for pending operations
2. Verify PCF control is receiving tool calls
3. Look for React state update issues in console
4. Refresh the control and retry

### Debug Logging

Enable detailed logging for troubleshooting:

**Backend (BFF API):**
```json
{
  "Logging": {
    "LogLevel": {
      "Sprk.Bff.Api.Services.Ai": "Debug"
    }
  }
}
```

**Frontend (PCF):**
```typescript
// In aiAssistantStore.ts
localStorage.setItem('PLAYBOOK_DEBUG', 'true');
```

### Support Resources

- Check BFF API health: `GET /healthz`
- View AI service status: `GET /api/ai/status`
- Review Azure OpenAI quotas in Azure Portal
- Monitor Application Insights for detailed telemetry

---

## Files Reference

| File | Purpose |
|------|---------|
| `src/client/webresources/js/sprk_playbook_commands.js` | Ribbon button command script |
| `src/client/pcf/PlaybookBuilderHost/` | PCF control source |
| `src/client/pcf/PlaybookBuilderHost/control/stores/aiAssistantStore.ts` | AI Assistant state management |
| `src/server/api/Sprk.Bff.Api/Services/Ai/AiPlaybookBuilderService.cs` | Backend AI service |
| `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` | Scope CRUD operations |
| `docs/guides/PLAYBOOK-BUILDER-FULLSCREEN-SETUP.md` | This guide |

---

## Related Guides

- [PCF-CUSTOM-PAGE-DEPLOY.md](PCF-CUSTOM-PAGE-DEPLOY.md) - Custom Page deployment details
- [RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md](RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md) - Ribbon button patterns
- [PCF-V9-PACKAGING.md](PCF-V9-PACKAGING.md) - PCF packaging and versioning
- [SPAARKE-AI-ARCHITECTURE.md](SPAARKE-AI-ARCHITECTURE.md) - AI architecture and patterns

---

*Last updated: January 2026*
