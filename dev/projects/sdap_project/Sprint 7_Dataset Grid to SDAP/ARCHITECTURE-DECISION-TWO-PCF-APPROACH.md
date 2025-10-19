# Architecture Decision: Two-PCF Approach for SDAP Integration

**Date**: 2025-10-05
**Status**: Approved
**Decision Maker**: Development Team

---

## Decision

Build **two separate universal PCF controls** for SDAP document management instead of a single combined control or using standard Power Apps controls with C# plugins.

### The Two Controls

1. **Universal Dataset Grid** (Sprint 7A)
   - Display and manage existing document records
   - File operations: Download, Delete, Replace
   - Launches Quick Create form via "+ New Document" button

2. **Universal Quick Create** (Sprint 7B)
   - Create new document records with file upload
   - Upload files to SharePoint Embedded via SDAP API
   - Auto-populate default values from parent entity
   - Configurable for multiple entity types

---

## Context

### The Use Case

Users need to manage documents from a Matter form:

1. **View Documents**: Documents subgrid on Matter form shows existing documents
2. **Create Document**: Click "+ New Document" → Quick Create form opens
3. **Upload File**: Select file from file picker at top of form
4. **Fill Metadata**: Enter optional fields (title, description, etc.)
5. **Save**: System uploads file to SharePoint Embedded and creates Dataverse record
6. **Result**: Grid refreshes, shows new document with clickable SharePoint URL

### ADR Constraints

**Architecture Decision Record (ADR) Requirements**:
- ❌ No C# plugins (avoid server-side extensions)
- ❌ No naked JavaScript (avoid untyped, unmaintainable JS)
- ✅ TypeScript PCF controls only (type-safe, maintainable)

---

## Alternatives Considered

### Option 1: Standard Power Apps Grid + C# Plugin

**Approach**: Use standard Power Apps grid with C# plugin for file upload.

**Pros**:
- ✅ Standard grid behavior (native Power Apps)
- ✅ Less custom code to maintain

**Cons**:
- ❌ **Violates ADR**: Requires C# plugin for SPE upload
- ❌ Plugin deployment complexity
- ❌ Less control over UX
- ❌ Requires naked JavaScript for grid customization

**Decision**: **Rejected** - Violates ADR (no C# plugins)

---

### Option 2: Single Combined PCF (Grid + Quick Create)

**Approach**: Build one mega-PCF that includes both grid display and Quick Create form.

**Pros**:
- ✅ All logic in one package
- ✅ No need to coordinate between controls

**Cons**:
- ❌ Mixing concerns (display vs. creation)
- ❌ Limited reusability (can't use grid without Quick Create, or vice versa)
- ❌ Larger bundle size
- ❌ More complex testing
- ❌ Harder to maintain
- ❌ Can't use standard Power Apps Quick Create launch mechanism

**Decision**: **Rejected** - Poor separation of concerns, limited reusability

---

### Option 3: Two Separate PCF Controls (SELECTED)

**Approach**: Build two universal PCF controls that work together via Power Apps context.

**Pros**:
- ✅ **ADR Compliant**: 100% TypeScript, no plugins, no naked JS
- ✅ **Separation of Concerns**: Grid handles display, Quick Create handles creation
- ✅ **Maximum Reusability**: Each control usable independently
- ✅ **Standard UX**: Uses native Power Apps Quick Create pattern
- ✅ **Context Sharing**: Power Apps provides automatic context via `context.mode.contextInfo`
- ✅ **Smaller Bundles**: Grid ~470 KB, Quick Create ~350 KB (vs. combined ~820 KB)
- ✅ **Independent Deployment**: Can update one without touching the other
- ✅ **Easier Testing**: Each control tested independently

**Cons**:
- ⚠️ Two controls to maintain (mitigated by shared SDAP API client)
- ⚠️ Need to understand Power Apps context sharing (well-documented in Power Apps)

**Decision**: **SELECTED** - Best balance of reusability, maintainability, and ADR compliance

---

## How Power Apps Provides Context

When Quick Create is launched from a subgrid, Power Apps **automatically provides context**:

```typescript
// In Universal Quick Create PCF
public async init(context: ComponentFramework.Context<IInputs>): Promise<void> {
    // Get parent context (automatically provided by Power Apps)
    const formContext = (context as any).mode?.contextInfo;

    if (formContext) {
        const parentEntityName = formContext.regardingEntityName;    // "sprk_matter"
        const parentRecordId = formContext.regardingObjectId;        // Matter GUID

        // Retrieve parent record data for default values
        const parentData = await context.webAPI.retrieveRecord(
            parentEntityName,
            parentRecordId,
            "?$select=sprk_name,sprk_containerid,ownerid"
        );

        // Use parent data to populate Quick Create defaults
        this.defaultValues = {
            sprk_containerid: parentData.sprk_containerid,
            sprk_documenttitle: parentData.sprk_name,
            ownerid: parentData._ownerid_value
        };
    }
}
```

**No custom messaging or state management needed** - Power Apps handles this natively.

---

## Control Interaction Flow

### Document Creation (Happy Path)

1. **User** in Matter form → Documents subgrid (Universal Dataset Grid PCF)
2. **User** clicks "+ New Document" button
3. **Power Apps** launches Quick Create form
   - Automatically provides context:
     - `regardingObjectId` = Matter GUID
     - `regardingEntityName` = "sprk_matter"
     - `entityName` = "sprk_document"
4. **Universal Quick Create PCF** receives context
5. **Quick Create** retrieves parent Matter data:
   - `sprk_containerid` (for SPE upload)
   - `sprk_name` (for document title default)
   - `ownerid` (for owner default)
6. **Quick Create** pre-populates form with defaults
7. **User** selects file, fills optional fields
8. **User** clicks Save
9. **Quick Create PCF**:
   - Uploads file to SPE via `Spe.Bff.Api`
   - Gets SPE metadata (URL, item ID, size)
   - Creates Dataverse document record with all metadata
10. **Power Apps** closes Quick Create form
11. **Power Apps** auto-refreshes grid (standard behavior)
12. **Grid** shows new document with clickable SharePoint URL

### File Operations (Download, Delete, Replace)

1. **User** selects document record in grid
2. **User** clicks operation button (Download/Delete/Replace)
3. **Universal Dataset Grid PCF**:
   - Calls `Spe.Bff.Api` for file operation
   - Updates/refreshes grid as needed
4. **Grid** shows updated state immediately

---

## Reusability Strategy

### Universal Dataset Grid

**Can be used for**:
- Documents subgrid on Matter
- Documents subgrid on Account
- Attachments subgrid on Email
- Any entity with file operations

**Configuration**: Power Apps form customization (field mappings, buttons)

### Universal Quick Create

**Can be used for**:
- Creating Documents from Matter
- Creating Tasks from Matter
- Creating Contacts from Account
- Any entity with parent context

**Configuration**: JSON mapping in PCF manifest parameter

```json
{
  "sprk_matter": {
    "sprk_containerid": "sprk_containerid",
    "sprk_name": "sprk_documenttitle",
    "_ownerid_value": "ownerid"
  },
  "account": {
    "name": "sprk_company",
    "_ownerid_value": "ownerid"
  }
}
```

---

## Shared Components

Both controls will share the **SDAP API Client** (from Sprint 7A Task 1):

```typescript
// services/SdapApiClient.ts (shared)
export class SdapApiClient {
    async uploadFile(file: File, containerId: string): Promise<SpeFileMetadata> { ... }
    async downloadFile(containerId: string, itemId: string): Promise<Blob> { ... }
    async deleteFile(containerId: string, itemId: string): Promise<void> { ... }
    // ... other methods
}
```

**Benefits**:
- ✅ Single source of truth for SDAP integration
- ✅ Shared error handling, logging, retry logic
- ✅ Consistent authentication across controls
- ✅ Easier to test and maintain

---

## Bundle Size Targets

| Control | Target Size | Rationale |
|---------|-------------|-----------|
| Universal Dataset Grid | <550 KB | Includes DataGrid, CommandBar, API client |
| Universal Quick Create | <400 KB | Smaller - just form, file picker, API client |
| **Combined** | **<950 KB** | Still well under 5 MB PCF limit |

**Note**: Controls are loaded independently, so users only download what they need.

---

## Implementation Timeline

### Sprint 7A: Universal Dataset Grid + SDAP (6-10 days)
1. API Client Setup (1-2 days)
2. File Download (0.5-1 day)
3. File Delete (1 day)
4. File Replace (0.5-1 day)
5. Field Mapping & SharePoint Links (0.5 day)
6. Testing & Deployment (1-2 days)

### Sprint 7B: Universal Quick Create + SPE Upload (4-6 days)
1. Quick Create PCF Setup (1-2 days)
2. File Upload Integration (1-2 days)
3. Default Value Mappings (0.5-1 day)
4. Testing & Deployment (1 day)

**Total**: 10-16 days

---

## Success Metrics

### Technical
- ✅ Bundle sizes meet targets (<550 KB, <400 KB)
- ✅ All operations < 2s response time
- ✅ Zero TypeScript errors
- ✅ Zero runtime errors
- ✅ ADR compliant (no plugins, no naked JS)

### User Experience
- ✅ Single-click file operations
- ✅ Standard Power Apps Quick Create UX
- ✅ Automatic metadata population
- ✅ Clickable SharePoint URLs
- ✅ Real-time grid updates

### Business
- ✅ Replace JavaScript web resource (Sprint 2)
- ✅ Improve reliability
- ✅ Reduce training requirements
- ✅ Enable reuse across multiple entities

---

## Risks and Mitigation

### Risk 1: Power Apps Context Not Available
**Impact**: Medium
**Mitigation**: Power Apps has provided `regardingObjectId` since v9.0. Well-documented and widely used. Fallback: Allow manual configuration via manifest parameters.

### Risk 2: Bundle Size Exceeds Targets
**Impact**: Low
**Mitigation**: Tree-shaking, code splitting, lazy loading. Current grid is 470 KB, plenty of headroom.

### Risk 3: Two Controls Increase Maintenance Burden
**Impact**: Low
**Mitigation**: Shared SDAP API client reduces duplication. Clear separation of concerns makes each control simpler to maintain.

### Risk 4: User Confusion Between Controls
**Impact**: Very Low
**Mitigation**: Standard Power Apps UX (grid + Quick Create). Users already familiar with this pattern.

---

## References

- **Sprint 7 Overview**: [SPRINT-7-OVERVIEW.md](SPRINT-7-OVERVIEW.md)
- **Master Resource**: [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md)
- **Sprint 2 Wrap-Up**: [../Sprint 2/SPRINT-2-WRAP-UP-REPORT.md](../Sprint%202/SPRINT-2-WRAP-UP-REPORT.md)
- **Universal Dataset Grid v2.0.7**: [../../UniversalDatasetGrid/SPRINT_5B_SUMMARY.md](../../UniversalDatasetGrid/SPRINT_5B_SUMMARY.md)

---

## Approval

**Date**: 2025-10-05
**Approved By**: Development Team
**Status**: Approved - Proceed with Sprint 7A

---

**Next Steps**:
1. ✅ Update SPRINT-7-MASTER-RESOURCE.md with context sharing details
2. ✅ Update SPRINT-7-OVERVIEW.md with split sprint plan
3. ✅ Document architecture decision (this file)
4. ⏭️ Begin Sprint 7A Task 1: SDAP API Client Setup
