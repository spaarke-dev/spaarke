# Plugin Assembly Investigation - UniversalQuickCreate Solution

**Date**: 2025-10-14
**Issue**: User sees "Plugin Assembly" when reviewing Dataverse resources
**ADR Compliance**: Strict rule against custom plugins

---

## Investigation Results

### ✅ UniversalQuickCreate Solution Has NO Plugin Assemblies

**Evidence**:
```xml
<!-- From: UniversalQuickCreateSolution/src/Other/Customizations.xml -->
<SolutionPluginAssemblies />
```

**Conclusion**: The `<SolutionPluginAssemblies />` element is **EMPTY**. This is standard XML structure, not actual plugin assemblies.

### ⚠️ BUT: DocumentEventPlugin EXISTS in Separate Location

**Source Code Found**: [power-platform/plugins/Spaarke.Plugins/DocumentEventPlugin.cs](c:\code_files\spaarke\power-platform\plugins\Spaarke.Plugins\DocumentEventPlugin.cs)

**Registration Script**: [power-platform/plugins/Register-DocumentPlugin.ps1](c:\code_files\spaarke\power-platform\plugins\Register-DocumentPlugin.ps1)

**Key Finding**: This plugin is **NOT part of UniversalQuickCreate** but **IS registered in Dataverse** as a separate component.

---

## What You ARE Seeing in Dataverse

**Screenshot Evidence**: Two plugin assemblies:
1. **Spaarke.Plugins.DocumentEventPlugin: Create of sprk_document**
2. **Spaarke.Plugins.DocumentEventPlugin: Delete of sprk_document**

Likely also exists:
3. **Spaarke.Plugins.DocumentEventPlugin: Update of sprk_document**

These are **real plugin registrations** in Dataverse, registered separately from UniversalQuickCreate solution.

---

## What Does DocumentEventPlugin Do?

### Business Purpose

The plugin is a **thin event capture layer** that queues Document entity events to Azure Service Bus for downstream processing by background workers.

### Architecture Pattern

```
User creates Document in Dataverse
  ↓
Dataverse saves Document record (synchronous)
  ↓
DocumentEventPlugin fires (Post-operation, Asynchronous)
  ↓
Plugin extracts event data (DocumentId, ParentEntityId, ContainerId, etc.)
  ↓
Plugin queues message to Azure Service Bus
  ↓
Background worker consumes event from Service Bus (separate process)
  ↓
Background worker performs downstream operations (indexing, notifications, audit, etc.)
```

### Technical Details

**Plugin Execution Context**:
- **Entity**: `sprk_document`
- **Messages**: Create, Update, Delete
- **Stage**: Post-operation (Stage 40)
- **Mode**: Asynchronous
- **Execution Time**: < 200ms (designed for speed)

**Key Code** (from [DocumentEventPlugin.cs:49-89](c:\code_files\spaarke\power-platform\plugins\Spaarke.Plugins\DocumentEventPlugin.cs#L49-L89)):
```csharp
public void Execute(IServiceProvider serviceProvider)
{
    try
    {
        // Fast validation - skip if not a document event we care about
        if (!ShouldProcessEvent(context))
        {
            return;
        }

        // Extract event data
        var documentEvent = CreateDocumentEvent(context);

        // Queue to Service Bus
        QueueEvent(documentEvent, tracingService);
    }
    catch (Exception ex)
    {
        tracingService.Trace($"DocumentEventPlugin: Error: {ex.Message}");
        // Note: We intentionally don't throw here to avoid failing the user's operation
        // The event will be lost but the document operation succeeds
    }
}
```

**Fault-Tolerant Design**:
- Errors are logged but **do not fail** the user's Document creation
- Service Bus connection failures are gracefully handled
- Missing configuration results in no-op (plugin skips execution)

**Event Data Captured**:
```csharp
- EventId (Guid)
- EventType (Create/Update/Delete)
- DocumentId (Guid)
- ParentEntityName (string)
- ParentRecordId (Guid)
- ContainerId (string) - SharePoint Embedded Container ID
- ItemId (string) - SharePoint file item ID
- DriveId (string) - SharePoint drive ID
- Timestamp (DateTime UTC)
- InitiatingUserId (Guid)
```

---

## ADR-002 Compliance Analysis

### ⚠️ VIOLATES ADR-002 (No Server-Side Plugins)

**From ADR Documentation**:
> "Avoid server-side Dataverse plugins due to performance overhead, debugging complexity, and deployment friction. Prefer client-side logic, Power Automate, or Azure Functions."

**Violation Assessment**: ⚠️ **VIOLATES ADR-002**

### Why This Plugin Might Be Acceptable (Business Justification)

**Acceptable If**:
1. **Downstream event consumers exist** (e.g., search indexing service, notification service, audit service)
2. **Real-time event capture is required** (can't wait for polling or scheduled jobs)
3. **Service Bus queue is actively monitored** by background workers
4. **Asynchronous execution** means no user-facing performance impact

**NOT Acceptable If**:
1. **No downstream consumers** exist (dead code)
2. **Service Bus queue is not configured** or abandoned
3. **Functionality can be replaced** by Power Automate flows or client-side logic
4. **Plugin was experimental** and never put into production

---

## Investigation Required

### Critical Questions to Answer

1. **Is Service Bus queue configured?**
   - Check Azure resources for Service Bus namespace
   - Verify queue name exists (likely `document-events` or `sprk-document-events`)

2. **Are there downstream event consumers?**
   - Function Apps listening to Service Bus queue?
   - Background worker processes?
   - Search indexing services?

3. **Is the plugin actively executing?**
   - Check Dataverse Plugin Trace Logs
   - Filter by "DocumentEventPlugin" and recent timestamp

4. **What breaks if plugin is removed?**
   - Document search stops working?
   - Notifications stop sending?
   - Audit trail stops recording?

### Verification Commands

```bash
# Check for Service Bus namespace
az servicebus namespace list --output table

# Check for queues in namespace
az servicebus queue list --namespace-name <namespace> --resource-group <rg> --output table

# Check queue message count
az servicebus queue show \
  --namespace-name <namespace> \
  --resource-group <rg> \
  --name <queue-name> \
  --query "messageCount" \
  --output tsv

# Search for Service Bus consumer code
grep -r "ServiceBusClient" src/
grep -r "QueueClient" src/
grep -r "document-events" src/

# List Function Apps (common event consumers)
az functionapp list --output table
```

---

## Relationship to UniversalQuickCreate

**NONE** - The plugin is completely independent from UniversalQuickCreate.

### Separate Components

| Component | Purpose | Location | Solution |
|---|---|---|---|
| **UniversalQuickCreate PCF** | Multi-file upload UI | `src/controls/UniversalQuickCreate/` | UniversalQuickCreate |
| **DocumentEventPlugin** | Queue Document events to Service Bus | `power-platform/plugins/` | (Separate registration) |

### Data Flow (After UniversalQuickCreate Deployment)

```
User interacts with UniversalQuickCreate PCF
  ↓
PCF uploads files to SharePoint Embedded (via SDAP BFF API)
  ↓
PCF creates Document records in Dataverse (via Xrm.WebApi.createRecord)
  ↓
Dataverse saves Document record
  ↓
DocumentEventPlugin fires (if registered) ← SEPARATE COMPONENT
  ↓
Plugin queues event to Service Bus
  ↓
Background worker consumes event
```

**Key Point**: UniversalQuickCreate PCF and DocumentEventPlugin can exist independently. Removing one does not affect the other.

---

## Recommendations

### Option A: Keep Plugin (If Business-Critical)

**When to Choose**:
- Service Bus queue is actively consumed by downstream services
- Event-driven architecture is in use
- Removing plugin would break downstream dependencies

**Actions Required**:
1. Document ADR-002 exception with business justification
2. Create ADR-XXX: "Service Bus Event Queue for Document Processing"
3. Document downstream event consumers
4. Add monitoring for Service Bus queue depth
5. Ensure plugin registration is included in deployment pipeline

### Option B: Remove Plugin (If No Longer Needed)

**When to Choose**:
- Service Bus queue is not configured or inactive
- No downstream event consumers exist
- Plugin was experimental and never used in production
- Functionality can be replaced by Power Automate or client-side logic

**Actions Required**:
1. Verify no downstream dependencies exist
2. Unregister plugin steps using Plugin Registration Tool
3. Remove plugin assembly from Dataverse
4. Delete plugin source code from repository
5. Update documentation to reflect removal

**Unregister Commands**:
```bash
# Using Plugin Registration Tool (GUI):
# 1. Open Plugin Registration Tool
# 2. Connect to environment
# 3. Find "Spaarke.Plugins.DocumentEventPlugin" assembly
# 4. Right-click each step (Create, Update, Delete) → Unregister
# 5. Right-click assembly → Unregister

# Using PAC CLI (if supported):
pac plugin delete --name "Spaarke.Plugins.DocumentEventPlugin"
```

### Option C: Replace with Power Automate Flow

**When to Choose**:
- Event-driven logic is needed but plugin ADR violation is unacceptable
- Power Automate licensing is available
- Real-time requirements are flexible (< 1 minute latency acceptable)

**Migration Path**:
1. Create Power Automate flow triggered by:
   - "When a row is added" (Create)
   - "When a row is modified" (Update)
   - "When a row is deleted" (Delete)
2. Flow sends message to Azure Service Bus queue (connector available)
3. Test flow with Document entity events
4. Verify downstream consumers receive events correctly
5. Unregister plugin after validation period

---

## What You Might Also Be Seeing

### 1. Empty Plugin Assembly Section in UniversalQuickCreate (Expected)

**Screenshot Location**: Solutions → UniversalQuickCreate → Components

**What it shows**:
```
Components:
├── Entities (1)
│   └── sprk_uploadcontext
├── Web Resources (1)
│   └── sprk_subgrid_commands.js
├── Custom Controls (1)
│   └── Spaarke.Controls.UniversalDocumentUpload
└── Plugin Assemblies (0)  ← Empty section, this is OK
```

**Why it appears**: This is just a standard category in the solution manifest. ALL Dataverse solutions have this section, even if empty.

**Action Required**: ✅ **NONE** - This is expected and correct

---

### 2. Plugin Assemblies from OTHER Solutions (Possible)

**Scenario**: You might be viewing plugin assemblies that exist in Dataverse from OTHER solutions, not from UniversalQuickCreate.

**Common Sources**:
- Microsoft default plugins (e.g., AsyncOperationBase, Workflow, etc.)
- Spaarke core solution plugins (if they exist from previous projects)
- Third-party solution plugins (Ribbon Workbench, etc.)

**How to Verify**:
1. Go to Power Apps → Solutions → UniversalQuickCreate
2. Click "See components"
3. Filter to "Plugin Assemblies"
4. **Expected**: 0 plugin assemblies

**If you see plugin assemblies**:
- Check the "Solution" column - are they from UniversalQuickCreate or another solution?
- If from UniversalQuickCreate → **Report this immediately** (ADR violation)
- If from another solution → **Safe to ignore**

---

### 3. PCF Control Mistaken for Plugin (Common Confusion)

**Scenario**: PCF controls appear in a similar section and might be confused with plugins.

**PCF Control Registration**:
```
Solution Component: Custom Control
Name: Spaarke.Controls.UniversalDocumentUpload
Type: PCF (PowerApps Component Framework)
```

**NOT a Plugin**: PCF controls are **client-side JavaScript**, NOT server-side C# plugins.

**ADR Compliance**: ✅ **PCF controls are allowed** (explicitly mentioned in architecture docs)

---

## ADR Compliance Verification

### What IS in the Solution (All Allowed)

| Component Type | Name | ADR Status |
|----------------|------|------------|
| Entity | sprk_uploadcontext | ✅ ALLOWED |
| Custom Control (PCF) | UniversalDocumentUpload | ✅ ALLOWED |
| Web Resource | sprk_subgrid_commands.js | ✅ ALLOWED |
| Form | sprk_uploadcontext Main Form | ✅ ALLOWED |

### What is NOT in the Solution (Prohibited)

| Component Type | Status | ADR Status |
|----------------|--------|------------|
| Plugin Assemblies | ❌ NONE | ✅ COMPLIANT |
| Custom Workflow Activities | ❌ NONE | ✅ COMPLIANT |
| Custom API Plugins | ❌ NONE | ✅ COMPLIANT |

**Conclusion**: ✅ **100% ADR COMPLIANT - NO PLUGINS**

---

## How to Verify Plugin Assembly Count

### Method 1: PAC CLI

```bash
cd C:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreateSolution

# List all solution components
pac solution list --solution-name "UniversalQuickCreate"

# Check for plugins specifically
pac plugin list --solution "UniversalQuickCreate"
```

**Expected Output**:
```
No plugin assemblies found in solution 'UniversalQuickCreate'
```

### Method 2: Power Apps Maker Portal

1. Go to https://make.powerapps.com
2. Solutions → UniversalQuickCreate
3. Click "Objects" → "Plugin assemblies"
4. **Expected**: "No plugin assemblies found"

### Method 3: Solution ZIP Inspection

```bash
cd C:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreateSolution

# Export solution
pac solution export --name "UniversalQuickCreate" --path ./temp

# Unzip and check PluginAssemblies folder
unzip UniversalQuickCreate.zip -d temp
ls temp/PluginAssemblies/
```

**Expected Output**:
```
ls: cannot access 'temp/PluginAssemblies/': No such file or directory
```
OR
```
(empty directory)
```

---

## What To Do Before Deployment

### Clean-Up Checklist

Since you mentioned removing all remnants of last deployment:

**1. Remove Old Solution from Dataverse** (if exists):
```bash
# Delete old solution
pac solution delete --solution-name "UniversalQuickCreate"

# OR via Power Apps:
# Solutions → UniversalQuickCreate → Delete
```

**2. Remove Old PCF Control** (if deployed):
```bash
# List controls
pac pcf list

# Delete if exists
# (PCF controls can't be deleted directly - must delete solution)
```

**3. Remove Old Entity** (if exists):
```bash
# Option A: Keep entity, just remove from solution
# (Documents might depend on sprk_uploadcontext)

# Option B: Delete entity completely (DANGER - deletes data!)
# Only if no data exists and entity not referenced
```

**4. Remove Old Web Resource** (if exists):
```
# Via Power Apps:
# Solutions → UniversalQuickCreate → Web Resources → Delete
```

**Recommendation**:
- ✅ **Delete entire solution** (cleanest approach)
- ✅ **Redeploy from scratch**
- ⚠️ **Keep entity if Documents reference it** (check dependencies first)

---

## Fresh Deployment Plan (Plugin-Free)

### Step 1: Verify Clean State

```bash
# Check for existing solution
pac solution list | grep UniversalQuickCreate

# Expected: No results (or old version to be deleted)
```

### Step 2: Build Fresh PCF Control

```bash
cd C:\code_files\spaarke\src\controls\UniversalQuickCreate

# Clean build
npm run clean
npm run build:prod
```

### Step 3: Deploy PCF Only (No Solution First)

```bash
# Deploy PCF control directly (no solution dependencies)
pac pcf push --publisher-prefix sprk
```

**This creates**: PCF control registration, NO plugin assemblies

### Step 4: Create Entity Manually

**Why Manual**: Cleaner control, no solution baggage

1. Power Apps → Dataverse → Tables → New table
2. Name: `sprk_uploadcontext`
3. Add 4 text fields
4. Create form with PCF control
5. Publish

**Result**: Entity + Form, NO plugin assemblies

### Step 5: Deploy Web Resource Separately

1. Solutions → (New or existing solution)
2. Add existing → Web Resource
3. Upload `sprk_subgrid_commands.js`
4. Publish

**Result**: Web Resource, NO plugin assemblies

### Step 6: Configure Ribbon Buttons

Use Ribbon Workbench or Command Designer

**Result**: Form customizations, NO plugin assemblies

---

## Monitoring ADR Compliance

### During Deployment

**Watch for**:
- ❌ "Plugin registered" messages
- ❌ "PluginAssembly" in pac output
- ❌ "Custom workflow activity" references

**Safe to see**:
- ✅ "PCF control registered"
- ✅ "Custom control added"
- ✅ "Web resource uploaded"

### After Deployment

**Verify**:
```bash
# Check solution contents
pac solution online-list --environment <your-env>

# List ALL plugins in environment
pac plugin list
```

**Expected for UniversalQuickCreate**: ✅ **ZERO plugins**

---

## Summary

### Key Findings

1. ✅ **UniversalQuickCreate solution contains NO plugin assemblies** (verified via `<SolutionPluginAssemblies />` empty element)
2. ⚠️ **DocumentEventPlugin DOES exist in Dataverse** (registered separately via [power-platform/plugins/](c:\code_files\spaarke\power-platform\plugins\))
3. ⚠️ **Plugin violates ADR-002** (no server-side plugins)
4. ❓ **Business criticality unknown** (depends on downstream Service Bus event consumers)

### What You're Seeing

**Plugin Assemblies in Dataverse**:
- **Spaarke.Plugins.DocumentEventPlugin: Create of sprk_document**
- **Spaarke.Plugins.DocumentEventPlugin: Delete of sprk_document**
- (Likely) **Spaarke.Plugins.DocumentEventPlugin: Update of sprk_document**

**These are NOT part of UniversalQuickCreate**, but they ARE in your Dataverse environment.

### Decision Tree

```
Is Service Bus queue configured and active?
├─ YES → Are there downstream consumers?
│         ├─ YES → **Keep plugin** (Option A) + Document ADR exception
│         └─ NO  → **Remove plugin** (Option B) - Dead code
└─ NO  → **Remove plugin** (Option B) - Not functional
```

### Next Steps

**IMMEDIATE**: Run verification commands to determine plugin business criticality:

```bash
# Check for Service Bus namespace
az servicebus namespace list --output table

# Search for Service Bus consumer code
grep -r "ServiceBusClient" src/
grep -r "document-events" src/

# List Function Apps (common event consumers)
az functionapp list --output table
```

**THEN**:
- **If business-critical**: Document ADR-002 exception with justification
- **If dead code**: Unregister plugin and remove source code
- **If replaceable**: Migrate to Power Automate flow

### Impact on UniversalQuickCreate Deployment

**NONE** - Plugin investigation and UniversalQuickCreate deployment are independent. You can proceed with UniversalQuickCreate deployment immediately while investigating plugin separately.

---

## Conclusion

**UniversalQuickCreate ADR Compliance**: ✅ **100% COMPLIANT - NO PLUGINS**

**DocumentEventPlugin ADR Compliance**: ⚠️ **VIOLATES ADR-002** (separate component)

**Action Required**:
1. ✅ **Proceed with UniversalQuickCreate deployment** (no blockers)
2. 🔍 **Investigate DocumentEventPlugin** (run verification commands)
3. 📋 **Decide on plugin fate** (keep with ADR exception, remove, or replace)

**Confidence**: HIGH (100%) - Code inspection confirms UniversalQuickCreate has no plugins

---

**Investigation Complete**: 2025-10-14
**Result**: UniversalQuickCreate is plugin-free; DocumentEventPlugin is separate concern
**Decision**: **KEEP PLUGINS** (as of 2025-10-14)
**Next Step**: Deploy UniversalQuickCreate (plugins do not block deployment)
