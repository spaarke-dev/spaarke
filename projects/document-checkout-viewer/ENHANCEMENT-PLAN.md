# SpeFileViewer Enhancement Plan: Option 2 Architecture

> **Created**: 2025-12-18
> **Status**: Approved
> **Approach**: PCF for Display + Dataverse Ribbon for Actions

---

## Overview

**Decision**: Keep SpeFileViewer simple (display only) and use Dataverse ribbon buttons for document operations.

**Rationale**:
- SpeFileViewer already works - opens documents in editable mode via Office Online
- Ribbon buttons leverage native Dataverse security profiles for visibility/enablement
- Clear separation of concerns: PCF = view, Ribbon = actions
- Existing `sprk_DocumentDelete.js` pattern can be extended for checkout/checkin

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                   Document Form (Model-Driven App)              │
├─────────────────────────────────────────────────────────────────┤
│  Command Bar (Ribbon)                                           │
│  ┌──────────┐ ┌──────────┐ ┌─────────┐ ┌──────────┐            │
│  │ Check Out│ │ Check In │ │ Discard │ │  Delete  │            │
│  └────┬─────┘ └────┬─────┘ └────┬────┘ └────┬─────┘            │
│       │            │            │           │                   │
│       └────────────┴────────────┴───────────┘                   │
│                          │                                      │
│                   sprk_DocumentOperations.js                    │
│                   (MSAL auth + BFF API calls)                   │
├─────────────────────────────────────────────────────────────────┤
│  Form Body                                                      │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │               SpeFileViewer PCF Control                  │   │
│  │                   (Display Only)                         │   │
│  │                                                          │   │
│  │     ┌────────────────────────────────────────────────┐  │   │
│  │     │                                                │  │   │
│  │     │         Office Online iframe embed              │  │   │
│  │     │         (Opens in edit mode natively)          │  │   │
│  │     │                                                │  │   │
│  │     └────────────────────────────────────────────────┘  │   │
│  │                                                          │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                        BFF API (Azure)                          │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │              DocumentOperationsEndpoints.cs              │   │
│  │  • POST /api/documents/{id}/checkout                     │   │
│  │  • POST /api/documents/{id}/checkin                      │   │
│  │  • POST /api/documents/{id}/discard                      │   │
│  │  • DELETE /api/documents/{id}                            │   │
│  │  • GET /api/documents/{id}/preview (SpeFileViewer uses) │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

---

## Components

### 1. SpeFileViewer PCF (No Changes Needed)

**Current State**: Working, displays documents via Office Online embed
**Action**: Keep as-is

The control already:
- Gets document preview URL from BFF API
- Renders Office Online iframe (editable by default)
- Handles authentication via MSAL

### 2. Ribbon Webresource: sprk_DocumentOperations.js

**Location**: `src/client/webresources/js/sprk_DocumentOperations.js`
**Action**: Create new file based on sprk_DocumentDelete.js pattern

**Functions to implement**:

| Function | Ribbon Button | API Endpoint |
|----------|---------------|--------------|
| `checkoutDocument(primaryControl)` | Check Out | POST /api/documents/{id}/checkout |
| `checkinDocument(primaryControl)` | Check In | POST /api/documents/{id}/checkin |
| `discardCheckout(primaryControl)` | Discard | POST /api/documents/{id}/discard |
| `deleteDocument(primaryControl)` | Delete | DELETE /api/documents/{id} |
| `canCheckout()` | Enable rule | Check if document is not checked out |
| `canCheckin()` | Enable rule | Check if checked out by current user |
| `canDiscard()` | Enable rule | Check if checked out by current user |
| `canDelete()` | Enable rule | Check if document can be deleted |

**Pattern from sprk_DocumentDelete.js**:
- Namespace: `Spaarke.Document`
- MSAL authentication via CDN-loaded library
- Environment-based BFF URL selection
- Xrm.Navigation dialogs for user interaction
- Correlation ID for request tracking

### 3. Ribbon Buttons (RibbonDiff.xml)

**Location**: `infrastructure/dataverse/ribbon/DocumentRibbons/Entities/sprk_Document/RibbonDiff.xml`
**Action**: Add new buttons for Check Out, Check In, Discard

**Button Placement**: Form command bar (Mscrm.Form.sprk_document.MainTab.Save.Controls)

| Button ID | Label | Icon | Visibility |
|-----------|-------|------|------------|
| sprk.Document.Checkout.Button | Check Out | DocumentLock | When not checked out |
| sprk.Document.Checkin.Button | Check In | DocumentApproval | When checked out by user |
| sprk.Document.Discard.Button | Discard Checkout | Cancel | When checked out by user |
| sprk.Document.Delete.Button | Delete | Delete | Always (if has delete permission) |

---

## Implementation Steps

### Phase 1: Webresource (sprk_DocumentOperations.js)

1. Create `src/client/webresources/js/sprk_DocumentOperations.js`
2. Copy MSAL pattern from `sprk_DocumentDelete.js`
3. Implement `checkoutDocument()` - POST to /checkout endpoint
4. Implement `checkinDocument()` - Show comment dialog, POST to /checkin
5. Implement `discardCheckout()` - Confirm dialog, POST to /discard
6. Copy existing `deleteDocument()` from sprk_DocumentDelete.js
7. Implement enable rules for button visibility

### Phase 2: Ribbon Customization

1. Add Button definitions to RibbonDiff.xml
2. Add Command definitions with enable rules
3. Add localized labels
4. Test button visibility based on document state

### Phase 3: Solution Packaging

1. Add webresource to Dataverse solution
2. Import ribbon customization
3. Test end-to-end flow

---

## Enable Rule Logic

The buttons need visibility rules based on document checkout status:

```
Document State         | Check Out | Check In | Discard | Delete
-----------------------|-----------|----------|---------|--------
Not checked out        | ✅        | ❌       | ❌      | ✅
Checked out by me      | ❌        | ✅       | ✅      | ❌
Checked out by other   | ❌        | ❌       | ❌      | ❌
```

**Implementation**: Query `sprk_checkoutstate` field on form load to determine button visibility.

---

## Non-PCF Contexts

For Outlook add-ins, desktop apps, or other non-Dataverse contexts:
- Build separate React apps with same BFF API calls
- Not part of this enhancement (separate project)

---

## Files to Create/Modify

| File | Action | Purpose |
|------|--------|---------|
| `src/client/webresources/js/sprk_DocumentOperations.js` | Create | Ribbon button handlers |
| `infrastructure/dataverse/ribbon/DocumentRibbons/.../RibbonDiff.xml` | Modify | Add ribbon buttons |
| `src/client/pcf/SpeFileViewer/*` | No change | Keep as display-only |

---

## Testing Checklist

- [ ] Check Out button only visible when document is not checked out
- [ ] Check In button only visible when checked out by current user
- [ ] Discard button only visible when checked out by current user
- [ ] Delete button only visible when document is not checked out
- [ ] Check Out creates lock, shows confirmation
- [ ] Check In shows comment dialog, unlocks document
- [ ] Discard confirms and unlocks without saving
- [ ] Delete confirms and removes document + SPE file
- [ ] SpeFileViewer displays document correctly (no changes)

---

## ADR Compliance

| ADR | Compliance |
|-----|------------|
| ADR-006 | ✅ Ribbon scripts are minimal invocation only |
| ADR-018 | ✅ SpeFileViewer stays React 18.2.0 |
| ADR-007 | ✅ No Graph SDK types leak to client |
| ADR-008 | ✅ BFF endpoints use OBO auth (no auth filters) |

---

*Created as part of session 2025-12-18 pivot from SpeDocumentViewer to SpeFileViewer enhancement*
