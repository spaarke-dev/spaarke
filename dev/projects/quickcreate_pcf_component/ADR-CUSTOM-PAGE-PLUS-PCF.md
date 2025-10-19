Why Universal Quick Create Requires Custom Page + PCF

```markdown
# Why Custom Page + PCF is Required (Not HTML/JS Web Resource)

## Critical Constraint: Multiple Record Creation

```typescript
// ❌ FAILS - Quick Create + context.webAPI (current problem)
await context.webAPI.createRecord("sprk_document", record1); // ✅ Works
await context.webAPI.createRecord("sprk_document", record2); // ❌ 400 Error
// Quick Create forms are designed for SINGLE record only

// ✅ WORKS - Custom Page + Xrm.WebApi (required solution)
for (const file of files) {
    await Xrm.WebApi.createRecord("sprk_document", recordData); // ✅ All succeed
}
```

**Root cause**: `context.webAPI` in Quick Create/form context has lifecycle tied to form state. Second `createRecord()` call fails because form expects single-record operation.

**Solution**: Custom Page provides `Xrm.WebApi` which has no form-state restrictions.

## Why NOT Pure HTML/JS Web Resource

### 1. Deprecated Platform APIs
```javascript
// ❌ DEPRECATED - Being removed by Microsoft
window.parent.Xrm.WebApi.createRecord(...)
ClientGlobalContext.js.aspx  // Already deprecated
```

From ADR-006:
> "Critical APIs like ClientGlobalContext.js.aspx and parent.Xrm are deprecated, leaving web resources without reliable access to platform context"

### 2. No Reliable Context Access
```javascript
// HTML Web Resource problems:
// - Runs in iframe with cross-origin restrictions
// - No guaranteed timing (race conditions require setTimeout hacks)
// - No lifecycle management
// - Security boundaries block platform access
```

### 3. Cannot Receive Parameters Properly
```javascript
// ❌ HTML Web Resource - fragile query string parsing
const params = new URLSearchParams(window.location.search);
const parentId = params.get('id'); // Breaks with URL encoding issues

// ✅ PCF - strongly typed input properties
<property name="parentRecordId" usage="input" required="true" type="SingleLine.Text" />
```

## Why Custom Page + PCF is Required

### 1. Guaranteed Xrm Access
```typescript
export class UniversalQuickCreatePCF implements ComponentFramework.StandardControl {
    public init(context: ComponentFramework.Context<IInputs>) {
        // ✅ Xrm is GUARANTEED available in Custom Page context
        // ✅ No parent.Xrm hacks needed
        // ✅ Full TypeScript intellisense
        Xrm.WebApi.createRecord(...)
    }
}
```

### 2. Proper Lifecycle Management
```typescript
public init()       { /* Platform calls on load */ }
public updateView() { /* Platform calls on data change */ }
public destroy()    { /* Platform calls on cleanup - no memory leaks */ }

// vs HTML web resource: No lifecycle, manual event handlers, timing issues
```

### 3. Type-Safe Parameter Passing
```javascript
// Command button launches Custom Page
Xrm.Navigation.navigateTo({
    pageType: "custom",
    name: "sprk_universaldocumentupload_page",
    data: { 
        parentEntityName: "sprk_matter",  // ✅ Typed
        parentRecordId: "guid-here",       // ✅ Validated
        containerId: "container-guid"      // ✅ Required check
    }
});
```

```xml
<!-- PCF manifest enforces contracts -->
<property name="parentEntityName" usage="input" required="true" />
<property name="parentRecordId" usage="input" required="true" />
<property name="containerId" usage="input" required="true" />
```

### 4. Platform Integration
```typescript
// ✅ PCF works in ALL contexts:
// - Model-Driven Forms
// - Custom Pages (our case)
// - Canvas Apps
// - Dashboards
// - Embedded in other PCFs

// ❌ HTML web resource: Only works in forms (sometimes)
```

### 5. Modern Development Experience
```bash
# PCF Development
npm start              # Local test harness with hot reload
npm test               # Unit tests with Jest
npm run build          # TypeScript compilation with type checking

# HTML Web Resource Development
# - Edit in text editor
# - Manually upload to Dataverse
# - Refresh entire form to test
# - No debugging, no testing
```

### 6. Security & Permissions
```typescript
// ✅ PCF inherits user context automatically
// - Row-level security enforced
// - Field-level security respected
// - Cannot bypass platform security
// - Audit logging automatic

// ❌ HTML web resource: Manual security checks, easy to bypass
```

## Specific to Multi-Document Upload

### Architecture Flow (Current Design)
```
Subgrid → Command Button → Custom Page (dialog) → PCF Control → Services
   ↓            ↓               ↓                    ↓            ↓
sprk_       Launch with    Renders PCF        Uses Xrm.WebApi  Upload +
documents   parameters     in dialog          for unlimited    Create
grid                       (target: 2)        record creation  records
```

### Why Each Layer is Required

**Command Button (Web Resource)**: 
- Captures parent context (entity name, record ID, container ID)
- Launches Custom Page with parameters

**Custom Page**: 
- Provides `Xrm` object to PCF
- Renders as modal dialog
- Manages navigation/closing

**PCF Control**: 
- Receives typed parameters
- Access to `Xrm.WebApi` (unlimited record creation)
- React UI with Fluent UI v9
- Service orchestration

**Cannot Shortcut This**:
- ❌ Pure HTML web resource: No reliable Xrm access, deprecated APIs
- ❌ PCF in Quick Create: context.webAPI fails on 2nd record
- ❌ Canvas App only: Cannot embed in Model-Driven subgrid command
- ✅ Custom Page + PCF: Only solution that works

## Code Pattern Enforcement

```typescript
// ✅ ALWAYS USE (in Custom Page PCF)
await Xrm.WebApi.createRecord("sprk_document", {
    "sprk_documentname": fileName,
    [`${lookupFieldName}@odata.bind`]: `/${entitySetName}(${parentRecordId})`
});

// ❌ NEVER USE (Quick Create limitation)
await context.webAPI.createRecord("sprk_document", ...); // Fails on 2nd call

// ❌ NEVER USE (deprecated)
await window.parent.Xrm.WebApi.createRecord(...); // Being removed
```

## Alignment with Architecture Decisions

### ADR-006: Prefer PCF Over Web Resources
> "PCF controls provide typed, testable components and better performance characteristics"

### ADR-001: Modern Runtime
> "One middleware pipeline for authentication, authorization, correlation"
> PCF participates in platform pipeline; HTML web resources do not

### Technical Architecture Doc
> "**PCF Control Library** as the foundation for all custom UI components"
> "JavaScript web resources are deprecated technology with critical platform access methods being removed"

## Summary: Why This is Non-Negotiable

| Requirement | Custom Page + PCF | HTML Web Resource |
|-------------|-------------------|-------------------|
| Multiple record creation | ✅ Xrm.WebApi unlimited | ❌ Fails after 1st record |
| Platform API access | ✅ Guaranteed via context | ❌ Deprecated, unreliable |
| Type safety | ✅ TypeScript + manifest | ❌ Loose strings/parsing |
| Lifecycle management | ✅ Platform-managed | ❌ Manual, error-prone |
| Debugging | ✅ Full DevTools + source maps | ❌ Limited iframe inspection |
| Testing | ✅ Unit testable | ❌ Manual only |
| Future-proof | ✅ Microsoft's direction | ❌ Being deprecated |
| Universal design | ✅ Config-driven | ❌ Hardcoded entity logic |

## Conclusion for Claude Code

**DO NOT suggest or implement**:
- HTML/JavaScript web resources for this control
- Direct form embedding (Quick Create fails on multiple records)
- `context.webAPI.createRecord()` loops (breaks after 1st record)
- `window.parent.Xrm` access patterns (deprecated)

**ALWAYS implement**:
- Custom Page as dialog container
- PCF control with proper manifest
- `Xrm.WebApi.createRecord()` for record creation
- TypeScript services with interfaces
- Fluent UI v9 components

**This architecture is required by**:
1. Technical constraints (multiple record creation)
2. Platform direction (deprecated APIs)
3. Project standards (ADR-006, architecture doc)
4. Development best practices (testing, debugging, maintainability)

There is no viable alternative approach.
```