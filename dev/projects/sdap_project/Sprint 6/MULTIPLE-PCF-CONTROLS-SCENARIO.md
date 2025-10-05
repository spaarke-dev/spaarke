# When Would You Need Multiple SDAP-Integrated PCF Controls?
**Date:** October 4, 2025
**Context:** Understanding Option 3 (Shared NPM Package) Use Cases

---

## Question

> "You mention in Option 3 Shared NPM 'When to Use: If building multiple SDAP-integrated PCF controls'. What does this mean and when would this arise?"

---

## Short Answer

**Multiple PCF controls** means building different custom controls (beyond the Universal Dataset Grid) that also need to interact with SDAP for file management.

**When this arises:**
- Different UI patterns for different scenarios (grid vs. form vs. gallery)
- Different entities that need file management
- Specialized controls for specific business processes

**For your current project:** Likely **NOT needed** - the Universal Dataset Grid should handle most scenarios.

---

## Detailed Explanation

### What is "Multiple PCF Controls"?

Each PCF control is a separate project that gets deployed as its own component:

**Example PCF Controls:**
1. **Universal Dataset Grid** (Sprint 6) - Grid view with file operations
2. **Document Form Viewer** (hypothetical) - Single document with file preview
3. **Gallery View Control** (hypothetical) - Card-based view of documents
4. **Document Upload Widget** (hypothetical) - Standalone upload component

If **2 or more** of these controls need SDAP integration, a shared NPM package becomes valuable.

---

## Current Spaarke Scenario

### What You Have Now

**Entities:**
- `sprk_document` - Documents with files
- `sprk_container` - Containers (parent for documents)
- Other entities (contacts, accounts, etc.)

**Current Sprint 6 Plan:**
- Build **ONE** PCF control: Universal Dataset Grid
- Use it on `sprk_document` entity for file management
- Grid shows documents, allows upload/download/delete

**SDAP Integration:**
- Only the Universal Dataset Grid needs SDAP API calls
- No other PCF controls planned that need SDAP

**Conclusion:** ✅ **Option 2 (Direct PCF) is sufficient** - No shared package needed.

---

## Scenarios Where Option 3 (Shared NPM) Would Be Valuable

### Scenario 1: Multiple Entity File Management

**Situation:** You want file management on multiple entities, each with a different UI pattern.

**Example:**

```
Entity: sprk_document
├── Universal Dataset Grid (PCF #1) ← Grid view with toolbar
└── Needs SDAP integration

Entity: sprk_contract
├── Contract Document Viewer (PCF #2) ← Form-based single doc view
└── Needs SDAP integration

Entity: sprk_attachment
├── Attachment Gallery (PCF #3) ← Card/gallery view
└── Needs SDAP integration
```

**Without Shared Package (Option 2):**
```
PCF #1: UniversalDatasetGrid
└── services/SdapApiClient.ts (300 lines) ← Duplicate code

PCF #2: ContractDocumentViewer
└── services/SdapApiClient.ts (300 lines) ← Duplicate code

PCF #3: AttachmentGallery
└── services/SdapApiClient.ts (300 lines) ← Duplicate code
```

**Problem:**
- Same SDAP API code copied 3 times (900 lines total)
- Bug fix requires updating 3 controls
- Version drift between controls

**With Shared Package (Option 3):**
```
@spaarke/sdap-client (NPM package)
└── SdapClient.ts (300 lines) ← Single source of truth

PCF #1: UniversalDatasetGrid
└── import { SdapClient } from '@spaarke/sdap-client'

PCF #2: ContractDocumentViewer
└── import { SdapClient } from '@spaarke/sdap-client'

PCF #3: AttachmentGallery
└── import { SdapClient } from '@spaarke/sdap-client'
```

**Benefits:**
- ✅ No code duplication
- ✅ Single update fixes all controls
- ✅ Consistent behavior across controls
- ✅ Independent versioning (update client without redeploying controls)

**Worth It?** ✅ Yes, if building 3+ controls

---

### Scenario 2: Different UI Patterns for Same Entity

**Situation:** Different views of the same entity with different UX requirements.

**Example: sprk_document entity**

```
Form View:
├── Document Form Viewer (PCF #1)
│   └── Single document with large preview
│   └── Upload/replace/download buttons
│   └── Needs SDAP integration

Grid View:
├── Universal Dataset Grid (PCF #2)
│   └── Table with multiple documents
│   └── Bulk operations (upload, download, delete)
│   └── Needs SDAP integration

Gallery View:
├── Document Gallery (PCF #3)
│   └── Card layout with thumbnails
│   └── Drag-and-drop upload
│   └── Needs SDAP integration
```

**Why Different Controls?**
- Form view optimized for single document workflow
- Grid view optimized for bulk management
- Gallery view optimized for visual browsing

**SDAP Integration:**
- All 3 need: upload, download, delete, chunked upload
- Shared package avoids duplicating ~300 lines in each control

**Worth It?** ✅ Yes, if building 3+ different UX patterns

---

### Scenario 3: Specialized Business Process Controls

**Situation:** Custom controls for specific workflows that need document operations.

**Example:**

```
Contract Approval Workflow:
├── Contract Approval Widget (PCF #1)
│   └── Shows contract, approval buttons, comments
│   └── Needs to download contract PDF for preview
│   └── Needs SDAP integration

Invoice Processing:
├── Invoice Upload & OCR (PCF #2)
│   └── Upload invoice, trigger OCR, show extracted data
│   └── Needs to upload file to SDAP
│   └── Needs SDAP integration

Document Comparison:
├── Side-by-Side Document Viewer (PCF #3)
│   └── Compare two versions of a document
│   └── Needs to download 2 files from SDAP
│   └── Needs SDAP integration
```

**Why Shared Package?**
- All need SDAP operations (upload/download)
- Each has different primary purpose
- Shared client ensures consistent API usage

**Worth It?** ✅ Yes, for workflow-specific controls

---

### Scenario 4: Reusable Components Library

**Situation:** Building a suite of reusable controls for the organization.

**Example: Spaarke Document Management Suite**

```
Component Library:
├── @spaarke/document-grid (PCF)
├── @spaarke/document-viewer (PCF)
├── @spaarke/document-upload (PCF)
├── @spaarke/document-search (PCF)
└── @spaarke/sdap-client (NPM) ← Shared by all above
```

**Benefits:**
- Standard document controls for any entity
- Consistent SDAP integration across all controls
- Easy to update all controls by updating sdap-client
- Other teams can use controls without knowing SDAP internals

**Worth It?** ✅ Yes, if building a component library

---

## When Option 3 (Shared NPM) is NOT Worth It

### Your Current Sprint 6 Situation

**Plan:**
- Build **ONE** PCF control: Universal Dataset Grid
- Use it everywhere documents need management
- Configurable via JSON (different entities, different settings)

**Configuration Example:**
```json
// On sprk_document entity
{
  "entityName": "sprk_document",
  "commands": ["addFile", "removeFile", "downloadFile"]
}

// On sprk_contract entity (if needed)
{
  "entityName": "sprk_contract",
  "commands": ["addFile", "downloadFile"]  // No remove
}

// On sprk_attachment entity (if needed)
{
  "entityName": "sprk_attachment",
  "commands": ["addFile", "removeFile", "downloadFile"]
}
```

**Single Control Handles All Scenarios:**
- ✅ Different entities → Same control, different config
- ✅ Different commands → Configure which buttons show
- ✅ Different styling → Fluent UI adapts to context

**No Need for Multiple Controls:**
- Universal Dataset Grid is... universal!
- Configuration handles most variations
- No code duplication (only one control)

**Conclusion:** ❌ Shared NPM package NOT needed for Sprint 6

---

## Decision Framework

### Use Shared NPM Package (Option 3) When:

1. ✅ **Planning 3+ different PCF controls** that need SDAP
2. ✅ **Different UX patterns** (grid vs. form vs. gallery vs. workflow)
3. ✅ **Building component library** for organization-wide use
4. ✅ **Specialized workflows** with custom controls
5. ✅ **Long-term roadmap** includes multiple SDAP-integrated controls

### Use Direct PCF Integration (Option 2) When:

1. ✅ **Single PCF control** (Universal Dataset Grid)
2. ✅ **Configuration handles variations** (different entities/settings)
3. ✅ **No plans for additional SDAP controls** in next 6-12 months
4. ✅ **Simple, focused use case**
5. ✅ **Want faster delivery** (avoid 8-hour package setup)

---

## Your Situation Assessment

### Current State

**Entities needing file management:**
- `sprk_document` ✅ Primary use case

**PCF controls planned:**
- Universal Dataset Grid ✅ Sprint 6

**Future controls (next 6 months):**
- ❓ Unknown

**Configuration flexibility:**
- ✅ High - JSON config supports different entities/commands

### Recommendation: Use Option 2 (Direct PCF)

**Why:**

1. **Single Control Sufficient** ✅
   - Universal Dataset Grid handles all scenarios
   - Configuration covers variations

2. **No Duplication** ✅
   - Only one control = no duplicate SDAP code
   - Not building multiple controls

3. **Faster Delivery** ✅
   - No 8-hour package setup
   - Ship Sprint 6 faster

4. **YAGNI Principle** ✅
   - "You Aren't Gonna Need It"
   - Don't build infrastructure for hypothetical future needs
   - Can refactor to shared package later if needed

5. **Easy Migration Path** ✅
   - If you DO build a second SDAP control later:
   - Extract `SdapApiClient.ts` to NPM package (4 hours)
   - Update both controls to import package (2 hours)
   - Total migration: 6 hours

---

## Future Migration Path (If Needed)

### When to Reconsider Option 3

**Trigger: Building a 2nd SDAP-integrated PCF control**

**Example:** 6 months from now, you decide to build a specialized "Contract Document Viewer" PCF control.

**Migration Steps:**

1. **Extract API Client to Package** (2 hours)
   ```bash
   mkdir packages/sdap-client
   cp src/controls/UniversalDatasetGrid/.../SdapApiClient.ts packages/sdap-client/src/
   npm init @spaarke/sdap-client
   npm publish
   ```

2. **Update Universal Dataset Grid** (1 hour)
   ```bash
   cd src/controls/UniversalDatasetGrid
   npm install @spaarke/sdap-client
   # Replace local SdapApiClient with import
   ```

3. **Use in New Control** (1 hour)
   ```bash
   cd src/controls/ContractDocumentViewer
   npm install @spaarke/sdap-client
   import { SdapClient } from '@spaarke/sdap-client'
   ```

**Total Migration Time:** 4-6 hours (when actually needed)

**Benefits of Waiting:**
- ✅ Don't pay 8-hour setup cost now
- ✅ Know actual requirements when building 2nd control
- ✅ Avoid over-engineering
- ✅ Can still migrate later if needed

---

## Examples from Other Projects

### Real-World Example 1: Microsoft Fluent UI

**Pattern:** Shared component library

```
@fluentui/react-button        ← Standalone package
@fluentui/react-checkbox      ← Standalone package
@fluentui/react-dialog        ← Standalone package
...40+ more packages

Used by:
- Outlook (uses 20+ Fluent UI packages)
- Teams (uses 30+ Fluent UI packages)
- SharePoint (uses 15+ Fluent UI packages)
```

**Why Shared Packages?**
- Multiple apps need same components
- Avoids duplicating Button component 50 times

**Analogy to Spaarke:**
- If building 5+ PCF controls → shared package like Fluent UI
- If building 1 control → inline code like a standalone app

### Real-World Example 2: Power Apps Component Framework

**Microsoft's Own Approach:**

```
Power Apps PCF Controls (Microsoft-built):
├── Calendar Control       ← Has inline date logic
├── Map Control           ← Has inline geolocation logic
├── Rich Text Editor      ← Has inline editing logic
└── No shared NPM package between them
```

**Why No Sharing?**
- Each control is independent
- Different teams maintain different controls
- No shared logic between calendar and map

**Analogy to Spaarke:**
- Like Microsoft, you have one specialized control (Universal Dataset Grid)
- Doesn't need to share code with other controls (none exist yet)
- Direct implementation is simpler

---

## Bottom Line

### Your Question Answered

> "What does 'multiple SDAP-integrated PCF controls' mean and when would this arise?"

**Means:**
- Building 2+ separate PCF control projects (different .pcfproj files)
- Each control needs SDAP file operations (upload/download/delete)
- Each would duplicate ~300 lines of SDAP API code

**When It Arises:**
- Different UX patterns (grid vs. form vs. gallery)
- Different entities with specialized needs
- Different business processes with custom workflows
- Building a component library

**For Your Sprint 6:**
- ❌ Does NOT arise
- ✅ Single control (Universal Dataset Grid)
- ✅ Configuration handles variations
- ✅ Use Option 2 (Direct PCF integration)

**Future:**
- ⏳ If you build a 2nd SDAP control → Migrate to Option 3 (6 hours)
- ⏳ Until then → Keep it simple with Option 2

---

## Recommendation: Stay with Option 2 ✅

**For Sprint 6 and foreseeable future:**
- Use Direct PCF API integration (Option 2)
- Build SDAP client code directly in Universal Dataset Grid
- If you need a 2nd SDAP control later → Extract to shared package then
- YAGNI principle: Don't build infrastructure for hypothetical needs

**Decision:** ✅ Proceed with Option 2 (Direct PCF integration)

---

**Document Complete**
**Next Step:** Update Sprint 6 Phase 3 plan with direct PCF API integration approach
