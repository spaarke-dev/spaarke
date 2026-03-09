# File Preview Dialog — Standardized Component Design

> **Author**: AI-assisted
> **Date**: 2026-03-09
> **Status**: Draft
> **Scope**: Corporate Workspace (LegalWorkspace solution)

---

## 1. Executive Summary

Create a standardized `FilePreviewDialog` component that provides a consistent file preview experience wherever documents (`sprk_document`) are listed in the application. The dialog shows a document preview in an embedded iframe with a standardized toolbar offering four actions: **Open Record**, **Open File**, **Copy Link**, and **Add to Workspace**.

This component consolidates 3 existing preview implementations into one reusable pattern, reducing code duplication and ensuring UX consistency.

---

## 2. Problem Statement

### Current Fragmentation

Documents are previewed in **3 different ways** across the codebase:

| Location | Preview Mechanism | Toolbar Actions | Issues |
|----------|------------------|-----------------|--------|
| **DocumentCard** (Dashboard) | Popover (600×400px) | Eye icon opens popover; overflow menu has Open Web, Open Desktop, Open Record, Find Similar, Summary | Small popover, preview + actions split across 2 UI elements |
| **SemanticSearch** (Code Page) | Fixed-position panel (880px × 85vh) | Open File, Open Record, Find Similar | Different layout, standalone fetch logic, code-page-specific auth |
| **FindSimilar** (Wizard) | 3 separate icon buttons per row | Preview (eye), Open File (desktop), Open Document (dialog) | No preview dialog — each button does a separate navigation |

### Consequences

- **Inconsistent UX**: Users encounter different preview patterns depending on where they are
- **Duplicated code**: Preview URL fetching, iframe rendering, error handling, and toolbar actions are reimplemented in each location
- **Missing features**: Some locations have "Open File" but not "Copy Link"; none have "Add to Workspace"
- **Maintenance burden**: Changes to preview behavior require updates in multiple files

---

## 3. Proposed Solution

### 3.1 Component: `FilePreviewDialog`

A single, reusable dialog component that can be triggered from any location where documents are listed.

**Visual Layout** (matches SemanticSearch pattern from screenshot):

```
┌─────────────────────────────────────────────────────────────────┐
│  📄 Document Name.pdf                                        ✕  │  ← Title bar
├─────────────────────────────────────────────────────────────────┤
│  [↗ Open File]  [📄 Open Record]  │  [📋 Copy Link]  [⊞ Add to Workspace]  │  ← Toolbar
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│                   ┌──────────────────────┐                      │
│                   │                      │                      │
│                   │   Document Preview   │                      │
│                   │   (iframe)           │                      │
│                   │                      │                      │
│                   └──────────────────────┘                      │
│                                                                 │  ← Preview area
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 Toolbar Actions

| # | Action | Icon | Behavior | Notes |
|---|--------|------|----------|-------|
| 1 | **Open File** | `OpenRegular` | Opens the file in the associated desktop app (Office protocol URL) or web browser (web URL). Tries desktop first; falls back to web. | Uses BFF `GET /documents/{id}/open-links` |
| 2 | **Open Record** | `DocumentOnePageRegular` | Opens the `sprk_document` Dataverse record in a new browser tab via `Xrm.Navigation.openForm`. | Uses `navigateToEntity()` utility |
| 3 | **Copy Link** | `CopyRegular` | Copies the Dataverse record URL (`{orgUrl}/main.aspx?etn=sprk_document&id={documentId}`) to clipboard. Shows a brief "Copied!" toast. | Constructs URL from `Xrm.Utility.getGlobalContext().getClientUrl()` |
| 4 | **Add to Workspace** | `PinRegular` / `PinOffRegular` | Sets `sprk_workspaceflag = true` on the `sprk_document` record via Xrm.WebApi. Icon toggles to "pinned" state after success. | Uses `Xrm.WebApi.updateRecord()` or BFF endpoint |

### 3.3 Props Interface

```typescript
export interface IFilePreviewDialogProps {
  /** Whether the dialog is open. */
  open: boolean;
  /** Document ID (sprk_document GUID). */
  documentId: string;
  /** Document display name (shown in title bar). */
  documentName: string;
  /** Called when the dialog should close. */
  onClose: () => void;
  /**
   * Optional: Current workspace flag state (for pin toggle).
   * When undefined, the pin button queries the record on mount.
   */
  isInWorkspace?: boolean;
  /**
   * Optional: Callback when workspace flag changes.
   * Parent can update its own state (e.g., refresh a document list).
   */
  onWorkspaceFlagChanged?: (documentId: string, isInWorkspace: boolean) => void;
}
```

### 3.4 Internal Architecture

```
FilePreviewDialog
├── Title bar (document name + close button)
├── Toolbar (Fluent Toolbar with ToolbarButtons)
│   ├── Open File (fetches open-links, opens URL)
│   ├── Open Record (navigateToEntity)
│   ├── ToolbarDivider
│   ├── Copy Link (clipboard API)
│   └── Add to Workspace (toggle pin)
├── Preview area
│   ├── Loading state (Spinner)
│   ├── Error state (message + retry)
│   └── Success state (iframe with preview URL)
└── Escape key handler (close on ESC)
```

**State management** (internal):
- `previewUrl: string | null` — ephemeral preview URL from BFF
- `isLoadingPreview: boolean` — loading spinner while fetching URL
- `previewError: string | null` — error message if fetch fails
- `isPinned: boolean` — current workspace flag state
- `isPinning: boolean` — loading state for pin toggle
- `copySuccess: boolean` — brief "Copied!" indicator (auto-clears after 2s)

**Data fetching**:
- Preview URL: `DocumentApiService.getDocumentPreviewUrl(documentId)`
- Open links: `DocumentApiService.getDocumentOpenLinks(documentId)` (lazy — only fetched when "Open File" clicked)
- Workspace flag: `Xrm.WebApi.updateRecord('sprk_document', documentId, { sprk_workspaceflag: true })`
- Record URL: Constructed from `getClientUrl()` — no API call needed

---

## 4. File Location and Imports

### New Files

```
src/solutions/LegalWorkspace/src/components/FilePreview/
├── FilePreviewDialog.tsx        # Main dialog component
└── filePreviewService.ts        # Copy link + workspace flag helpers
```

### Reused Dependencies (no modifications needed)

| Dependency | Import Path | Purpose |
|------------|-------------|---------|
| `getDocumentPreviewUrl` | `../../services/DocumentApiService` | Fetch ephemeral iframe preview URL |
| `getDocumentOpenLinks` | `../../services/DocumentApiService` | Fetch web + desktop URLs |
| `navigateToEntity` | `../../utils/navigation` | Open record in new tab |
| `getXrm` | `../../services/xrmProvider` | Xrm.WebApi for workspace flag |
| Fluent UI `Toolbar`, `ToolbarButton`, `ToolbarDivider` | `@fluentui/react-components` | Toolbar layout |
| Fluent icons | `@fluentui/react-icons` | Action button icons |

---

## 5. Integration Points — Where to Use

### 5.1 Find Similar Results Grid (Priority 1)

**File**: `FindSimilarResultsStep.tsx`
**Current**: 3 separate icon buttons per row (preview, open file, open document)
**Change**: Replace all 3 buttons with a single "Preview" (eye) icon button that opens `FilePreviewDialog`.

```tsx
// Before: 3 buttons per document row
<Button icon={<EyeRegular />} onClick={handlePreview} />
<Button icon={<DesktopRegular />} onClick={handleOpenFile} />
<Button icon={<DocumentOnePageRegular />} onClick={handleOpenDocument} />

// After: 1 button opens standardized dialog
<Button icon={<EyeRegular />} onClick={() => openPreview(item.id, item.name)} />

// FilePreviewDialog handles all actions via its toolbar
<FilePreviewDialog
  open={previewOpen}
  documentId={previewDocId}
  documentName={previewDocName}
  onClose={() => setPreviewOpen(false)}
/>
```

**Benefits**: Cleaner grid rows, all document actions accessible from one dialog.

### 5.2 DocumentCard (Priority 2)

**File**: `RecordCards/DocumentCard.tsx`
**Current**: Popover preview (600×400px) + overflow menu with 5 items
**Change**: Replace popover + overflow menu with single "Preview" button that opens `FilePreviewDialog`. Keep "Find Similar" and "Summary" as separate actions (they are not document-preview-related).

```tsx
// Before: Popover + overflow menu
<Popover>
  <PopoverTrigger><Button icon={<EyeRegular />} /></PopoverTrigger>
  <PopoverSurface>...iframe...</PopoverSurface>
</Popover>
<Menu>
  <MenuItem>Open File in Web</MenuItem>
  <MenuItem>Open File in Desktop</MenuItem>
  <MenuItem>Open Record</MenuItem>
  <MenuItem>Find Similar</MenuItem>
  <MenuItem>Summary</MenuItem>
</Menu>

// After: Preview button + lean menu
<Button icon={<EyeRegular />} onClick={() => openPreview(doc.id, doc.name)} />
<Menu>
  <MenuItem icon={<DocumentSearchRegular />}>Find Similar</MenuItem>
  <MenuItem icon={<SparkleRegular />}>Summary</MenuItem>
</Menu>

<FilePreviewDialog ... />
```

**Benefits**: Removes ~80 lines of popover/iframe/loading code from DocumentCard. Open File, Open Record, Copy Link, and Add to Workspace are all handled by the dialog's toolbar.

### 5.3 Semantic Search Code Page (Priority 3 — Future)

**File**: `src/client/code-pages/SemanticSearch/src/components/DocumentPreviewDialog.tsx`
**Current**: Custom fixed-position panel with its own fetch logic and auth
**Change**: Could be replaced with `FilePreviewDialog`, but the Semantic Search code page has its own auth mechanism (`buildAuthHeaders`) and positioned panel UX. This is a lower-priority consolidation.

**Recommendation**: Leave SemanticSearch as-is for now but adopt the standardized toolbar actions (add Copy Link + Add to Workspace) in a future iteration.

### 5.4 Document Relationship Viewer Grid (Priority 3 — Future)

**File**: `src/client/code-pages/DocumentRelationshipViewer/src/components/RelationshipGrid.tsx`
**Current**: Open record + View in SharePoint links
**Change**: Add preview button that opens `FilePreviewDialog` (requires adapting auth for code-page context).

---

## 6. Dialog Rendering Strategy

The `FilePreviewDialog` renders as a **Fluent UI Dialog** (not a popover or fixed-position panel):

```tsx
<Dialog open={open} onOpenChange={(_, data) => { if (!data.open) onClose(); }}>
  <DialogSurface style={{ maxWidth: '880px', width: '85vw', height: '85vh', padding: 0 }}>
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      {/* Title bar */}
      <div>
        <Text weight="semibold">{documentName}</Text>
        <Button icon={<Dismiss24Regular />} onClick={onClose} />
      </div>

      {/* Toolbar */}
      <Toolbar size="small">
        <ToolbarButton icon={<OpenRegular />}>Open File</ToolbarButton>
        <ToolbarButton icon={<DocumentOnePageRegular />}>Open Record</ToolbarButton>
        <ToolbarDivider />
        <ToolbarButton icon={<CopyRegular />}>Copy Link</ToolbarButton>
        <ToolbarButton icon={<PinRegular />}>Add to Workspace</ToolbarButton>
      </Toolbar>

      {/* Preview iframe */}
      <div style={{ flex: 1 }}>
        <iframe src={previewUrl} sandbox="allow-scripts allow-same-origin allow-forms allow-popups" />
      </div>
    </div>
  </DialogSurface>
</Dialog>
```

**Why Dialog vs. Fixed Panel**:
- Dialog automatically handles backdrop, focus trapping, and ESC key
- Works correctly inside Dataverse iframes (no `window.top` needed)
- Consistent with other wizard dialogs in the workspace

**Dimensions**: 85vw × 85vh, max-width 880px — matches SemanticSearch panel size.

---

## 7. Copy Link Implementation

```typescript
async function copyDocumentLink(documentId: string): Promise<boolean> {
  const xrm = getXrm();
  const clientUrl = xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.() ?? '';

  if (!clientUrl) {
    console.warn('[FilePreview] Cannot construct link — no client URL available');
    return false;
  }

  // Build the Dataverse record URL
  const recordUrl = `${clientUrl}/main.aspx?etn=sprk_document&id=${documentId}`;

  try {
    await navigator.clipboard.writeText(recordUrl);
    return true;
  } catch {
    // Fallback for older browsers / iframe restrictions
    const textArea = document.createElement('textarea');
    textArea.value = recordUrl;
    textArea.style.position = 'fixed';
    textArea.style.left = '-9999px';
    document.body.appendChild(textArea);
    textArea.select();
    const ok = document.execCommand('copy');
    document.body.removeChild(textArea);
    return ok;
  }
}
```

---

## 8. Add to Workspace Implementation

```typescript
async function setWorkspaceFlag(
  documentId: string,
  flag: boolean
): Promise<boolean> {
  const xrm = getXrm();

  if (!xrm?.WebApi?.updateRecord) {
    console.warn('[FilePreview] Xrm.WebApi not available for workspace flag update');
    return false;
  }

  try {
    await xrm.WebApi.updateRecord('sprk_document', documentId, {
      sprk_workspaceflag: flag,
    });
    return true;
  } catch (err) {
    console.error('[FilePreview] Failed to update workspace flag:', err);
    return false;
  }
}
```

The toolbar button shows a toggle state:
- **Not pinned**: `PinRegular` icon, label "Add to Workspace"
- **Pinned**: `PinOffRegular` icon, label "Remove from Workspace"

After toggling, the `onWorkspaceFlagChanged` callback notifies the parent so it can refresh its document list if needed.

---

## 9. Consolidation Impact

### Code Removed (estimated)

| File | Lines Removed | What's Consolidated |
|------|--------------|---------------------|
| `DocumentCard.tsx` | ~80 lines | Preview popover, iframe state, loading/error handling, Open File/Desktop/Record menu items |
| `FindSimilarResultsStep.tsx` | ~60 lines | Three separate action handlers + button columns, preview webresource logic |
| **Total** | ~140 lines | Replaced by ~180 lines in `FilePreviewDialog.tsx` + ~40 lines in `filePreviewService.ts` |

### New Capabilities Added Everywhere

| Capability | Before | After |
|-----------|--------|-------|
| Open File (desktop/web) | DocumentCard only | All locations |
| Open Record | All (different patterns) | Standardized |
| Copy Link | Nowhere | All locations |
| Add to Workspace | Nowhere (field exists but no UI) | All locations |
| Full-size preview | SemanticSearch only | All locations |

---

## 10. Implementation Plan

### Phase 1: Create `FilePreviewDialog` Component
1. Create `src/solutions/LegalWorkspace/src/components/FilePreview/FilePreviewDialog.tsx`
2. Create `src/solutions/LegalWorkspace/src/components/FilePreview/filePreviewService.ts`
3. Implement all 4 toolbar actions
4. Handle loading, error, and success states
5. Build and verify

### Phase 2: Integrate into Find Similar Results
1. Update `FindSimilarResultsStep.tsx` — replace 3 row action buttons with single preview button
2. Add `FilePreviewDialog` instance (shared across rows via state)
3. Build and verify

### Phase 3: Integrate into DocumentCard
1. Update `DocumentCard.tsx` — remove popover preview + Open File/Desktop/Record menu items
2. Add `FilePreviewDialog` triggered by eye icon
3. Keep Find Similar and Summary as separate menu items
4. Build and verify

### Phase 4 (Future): Semantic Search + Relationship Viewer
1. Evaluate adapting `FilePreviewDialog` for code-page auth context
2. Add Copy Link + Add to Workspace to existing SemanticSearch panel

---

## 11. Technical Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Dialog vs. Popover | **Dialog** | Full-size preview, focus trapping, consistent with other wizards |
| Dialog vs. Fixed panel | **Dialog** | No positioning logic needed, works in iframes, accessible by default |
| Copy URL target | **Document record URL** (not file URL) | User request: "copies the URL of the Document, not the file" |
| Workspace flag mechanism | **Xrm.WebApi** | Direct Dataverse update; no BFF endpoint needed |
| Lazy-load dialog | **Yes** (React.lazy) | Dialog is not always shown; keeps main bundle smaller |
| Desktop vs. Web for Open File | **Desktop first, web fallback** | Desktop app provides richer editing experience |

---

## 12. Open Questions

1. **"Open File" behavior for non-Office files** (e.g., PDF, EML, images): Desktop URL may not be available. Should we show a "Download" option as fallback?
2. **"Add to Workspace" — should it also support "Remove from Workspace"** (toggle)? Design above assumes yes (toggle pin).
3. **Should the preview dialog show file metadata** (size, type, modified date) in the title bar or a subtitle? Current design shows only the file name.

---

## 13. Auth Consolidation — Prerequisite Workstream

### 13.1 Current State: 5 Separate Auth Implementations

Every code page has its own auth module doing the same job — acquire a Bearer token for
`api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` and call the BFF API.

| Code Page | Auth File | Lines | Strategy |
|-----------|-----------|-------|----------|
| **LegalWorkspace** | `bffAuthProvider.ts` | 267 | Parent token bridge → MSAL ssoSilent → anonymous |
| **AnalysisWorkspace** | `authService.ts` | 532 | Xrm platform (5 methods) → MSAL ssoSilent |
| **SemanticSearch** | `MsalAuthProvider.ts` | 235 | MSAL acquireTokenSilent → popup |
| **DocRelationshipViewer** | `MsalAuthProvider.ts` | 213 | MSAL acquireTokenSilent → popup |
| **PlaybookBuilder** | `authService.ts` | 331 | Xrm platform (5 methods) → MSAL ssoSilent |
| **Total** | | **~1,578** | 5 implementations of the same thing |

### 13.2 Key Inconsistencies

| Aspect | LegalWorkspace | Analysis/Playbook | SemanticSearch/DocViewer |
|--------|---------------|-------------------|------------------------|
| **Authority** | `organizations` (env-portable) | `organizations` (env-portable) | **Hardcoded tenant** (breaks in new env) |
| **Redirect URI** | `window.location.origin` (portable) | `window.location.origin` (portable) | **Hardcoded Dataverse URL** (breaks in new env) |
| **Token storage** | In-memory | In-memory | sessionStorage |
| **Xrm frame-walk** | No | Yes (3 frames) | No |
| **401 retry** | Yes (1 retry) | Exponential backoff (3×) | None |
| **Popup fallback** | No | No | Yes |

### 13.3 `authenticatedFetch()` vs `buildAuthHeaders()` + `fetch`

These are two patterns for the same operation — calling the BFF with a Bearer token:

**`authenticatedFetch(url, init?)` — LegalWorkspace pattern**
```typescript
// Caller doesn't think about auth at all
const response = await authenticatedFetch('/api/documents/123/preview-url');
// Internally: get token → set header → fetch → on 401, clear cache and retry once
```
- Pros: Auth is fully encapsulated; 401 retry is automatic
- Cons: Caller can't distinguish "auth failed" from "API error"; no RFC 7807 parsing

**`buildAuthHeaders()` + manual `fetch` — SemanticSearch pattern**
```typescript
// Caller assembles the request manually
const headers = await buildAuthHeaders();
const response = await fetch(url, { headers });
const data = await handleApiResponse<T>(response);  // RFC 7807 error parsing
```
- Pros: Full control; typed error handling via ProblemDetails
- Cons: Auth leaks into every call site; no 401 retry

**Recommendation**: `authenticatedFetch()` is the right abstraction — but it should incorporate
the RFC 7807 error parsing and exponential backoff retry from the other implementations.

### 13.4 The Parent-to-Child Token Bridge

When one page opens another as an iframe dialog, the child must acquire its own token. Today
this costs **500ms–1.3s** (MSAL initialization + ssoSilent). The token bridge eliminates this:

```
┌──────────────────────────────────────────────────────────────────────────┐
│  Parent page (e.g., LegalWorkspace — already authenticated)              │
│                                                                          │
│  1. User clicks "Find Similar" on a document                             │
│  2. LegalWorkspace sets: window.__SPAARKE_BFF_TOKEN__ = currentToken     │
│  3. LegalWorkspace opens DocumentRelationshipViewer as iframe dialog     │
└──────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ↓
┌──────────────────────────────────────────────────────────────────────────┐
│  Child code page (DocumentRelationshipViewer — inside iframe)            │
│                                                                          │
│  Token acquisition priority:                                             │
│  1. Check window.__SPAARKE_BFF_TOKEN__          → not found (own frame)  │
│  2. Check window.parent.__SPAARKE_BFF_TOKEN__   → FOUND! (~0.1ms)       │
│  3. (skipped) MSAL init + ssoSilent             → would be ~500-1300ms  │
│                                                                          │
│  Result: Child uses parent's token immediately                           │
└──────────────────────────────────────────────────────────────────────────┘
```

This pattern was originally created for PCF controls passing tokens to child dialogs, but it
works for **any parent-to-child relationship**. Any page that opens a child iframe can share
its token. Currently only LegalWorkspace and AnalysisWorkspace read the bridge variable —
SemanticSearch and DocumentRelationshipViewer do not.

### 13.5 Performance Implications

**Client-side token acquisition (per code page load):**

| Scenario | Latency | When |
|----------|---------|------|
| Token bridge from parent | ~0.1ms | Child reads parent's cached token |
| In-memory cache hit | ~0.1ms | Same page, token not expired |
| sessionStorage cache hit | ~5ms | Page refreshed, token still valid |
| MSAL acquireTokenSilent | ~100–300ms | Cached MSAL account, refresh token exchange |
| MSAL ssoSilent | ~300–800ms | No cached account, hidden iframe to Azure AD |
| MSAL initialization | ~200–500ms | First call only, loads MSAL library + config |
| **Total first-load (no bridge)** | **~500–1,300ms** | New code page with no parent token |
| **Total first-load (with bridge)** | **~0.1ms** | Parent already authenticated |

**Server-side BFF latency** (separate concern, not solved by auth consolidation):

| Operation | Latency | Cause |
|-----------|---------|-------|
| JWT validation | ~1–2ms | Token signature check |
| OBO exchange (Graph) | ~200–500ms | Azure AD token exchange |
| OBO exchange (Dataverse) | ~200–500ms | Azure AD token exchange |
| Graph API call (preview URL) | ~100–300ms | SharePoint Embedded round-trip |
| **Total preview-url request** | **~500–1,300ms** | Dominated by OBO + Graph |

Auth consolidation on the client improves **perceived first-load time** (eliminating redundant
MSAL initialization). BFF response latency is a separate optimization target (server-side
token caching, connection pooling, response caching).

### 13.6 Proposed: `@spaarke/auth` Shared Package

A shared auth package consumed as a build-time dependency by all code pages:

```
@spaarke/auth
├── SpaarkeAuthProvider (singleton)
│   ├── Token acquisition priority:
│   │   1. Parent token bridge (window.__SPAARKE_BFF_TOKEN__)
│   │   2. In-memory cache (with 5-min expiry buffer)
│   │   3. Xrm platform strategies (5 methods, with frame-walk)
│   │   4. MSAL ssoSilent (lazy init, multi-tenant authority)
│   │   5. MSAL popup (interactive fallback, last resort)
│   ├── Token caching: in-memory primary + sessionStorage backup
│   ├── JWT expiry parsing with 5-min buffer
│   ├── Proactive refresh (optional, for long-lived pages)
│   └── Frame-walk for Xrm resolution (window → parent → top)
│
├── authenticatedFetch(url, init?)
│   ├── Auto-attaches Bearer header from SpaarkeAuthProvider
│   ├── 401 retry with exponential backoff (3 attempts)
│   ├── RFC 7807 ProblemDetails error parsing
│   └── Returns typed response or throws AuthError / ApiError
│
├── Token bridge utilities
│   ├── publishToken(token)       — parent calls after acquiring token
│   ├── readBridgeToken()         — child checks parent frame
│   └── clearBridgeToken()        — cleanup on logout/close
│
└── Config (environment-portable)
    ├── clientId: window.__SPAARKE_MSAL_CLIENT_ID__ || default
    ├── authority: 'https://login.microsoftonline.com/organizations'
    ├── redirectUri: window.location.origin
    ├── bffApiScope: 'api://1e40baad-.../user_impersonation'
    └── bffBaseUrl: window.__SPAARKE_BFF_URL__ || default
```

**Each code page would reduce its auth code from 200–530 lines to ~10 lines:**

```typescript
import { initAuth, authenticatedFetch } from '@spaarke/auth';

// Initialize once at app startup
await initAuth();

// Use everywhere — auth is fully encapsulated
const response = await authenticatedFetch('/api/documents/123/preview-url');
```

### 13.7 Revised Project Phasing (Auth + Dialogs + Tech Debt)

The project combines three workstreams: `@spaarke/auth` shared package, `FilePreviewDialog`
standardization, and **Create New Document dialog replacement** (retiring the
UniversalQuickCreate + UniversalDocumentUpload PCF controls).

**Sequencing rationale**: FilePreviewDialog is a read-only preview component (low risk, fast
to build). Create New Document is a full CRUD flow — form + file upload + SPE storage +
Dataverse record creation (higher risk, more complex). Build the simpler component first to
prove `@spaarke/auth` and the code page dialog pattern, then tackle the complex replacement.

| Phase | Work | Dependency | Risk |
|-------|------|------------|------|
| **Phase 1** | Create `@spaarke/auth` shared package | None — foundation | Low |
| **Phase 2** | Build `FilePreviewDialog` in LegalWorkspace (using `@spaarke/auth`) | Phase 1 | Low |
| **Phase 3** | Integrate FilePreviewDialog into FindSimilar + DocumentCard | Phase 2 | Low |
| **Phase 4** | Build `CreateDocumentDialog` code page (replacing UniversalQuickCreate PCF) | Phase 1 | Medium-high |
| **Phase 5** | Migrate remaining code pages to `@spaarke/auth` | Phase 1 | Low |
| **Phase 6** | Migrate remaining PCF controls to `@spaarke/auth` | Phase 1 | Medium |
| **Phase 7** | Extract `FilePreviewDialog` to shared component library | Phase 5 | Low |
| **Phase 8** | Adopt shared `FilePreviewDialog` in all code pages | Phase 7 | Low |

Phases 4, 5, and 6 can run in parallel after Phase 1 completes. Phase 3 can run in parallel
with Phase 4. See **Section 14** for full Create New Document replacement design.

### 13.8 Auth Consolidation — What It Fixes

| Problem | Current State | After `@spaarke/auth` |
|---------|--------------|----------------------|
| **Hardcoded tenant/URLs** | SemanticSearch + DocViewer break in new environments | All use `organizations` authority + `window.location.origin` |
| **Redundant MSAL init** | Each child iframe does its own 500–1300ms init | Token bridge: parent passes token, child starts in ~0.1ms |
| **Inconsistent retry** | Some have backoff, some have single retry, some have none | All use exponential backoff with 3 attempts |
| **Duplicated code** | ~1,578 lines across 5 implementations | ~200 lines in shared package, ~10 lines per consumer |
| **Missing error types** | Only 2 of 5 implementations have typed AuthError | All consumers get AuthError + ApiError |
| **No RFC 7807 support** | Only SemanticSearch parses ProblemDetails | All consumers get typed API error responses |

---

## 14. Create New Document — Dialog Replacement (Phase 4)

### 14.1 Current Architecture (What We're Replacing)

The "Create New Document" flow currently uses the **UniversalQuickCreate** PCF control
(renamed `UniversalDocumentUpload` in v3.0.4). This is a field-bound React 16 PCF control
hosted inside a Custom Page. It has its own auth stack (805 lines), HTTP client, upload
services, and record creation logic.

**Current flow:**

```
User clicks "New Document" button
  → Opens Custom Page hosting UniversalQuickCreate PCF
  → PCF initializes own MSAL auth (805 lines, ~500-1300ms)
  → User fills form (name, type, description, client lookups)
  → User drops files into FileUploadZone
  → MultiFileUploadService → SdapApiClient → PUT /api/obo/containers/{id}/files/{path}
  → DocumentRecordService → context.webAPI.createRecord('sprk_document', ...)
  → NavMapClient → GET /api/navmap/{entity}/{relationship}/lookup
  → Success: navigate to created matter record
```

**Key files (PCF control):**

| File | Lines | Purpose |
|------|-------|---------|
| `src/client/pcf/UniversalQuickCreate/control/index.ts` | — | PCF entry point |
| `control/components/DocumentUploadForm.tsx` | — | Main UI (form + file drop zone) |
| `control/services/MultiFileUploadService.ts` | — | Multi-file upload orchestration |
| `control/services/FileUploadService.ts` | — | Single-file upload to SPE |
| `control/services/DocumentRecordService.ts` | — | Dataverse record creation |
| `control/services/NavMapClient.ts` | — | Metadata discovery (nav properties) |
| `control/services/SdapApiClient.ts` | — | HTTP client for BFF API |
| `control/services/auth/MsalAuthProvider.ts` | 805 | Auth (largest in codebase) |
| `control/services/auth/msalConfig.ts` | 337 | Hardcoded MSAL config |
| `control/types/auth.ts` | 184 | Auth type definitions |

### 14.2 Target Architecture (Code Page Dialog)

Replace with a **React 18 code page** (`CreateDocumentDialog`) using `@spaarke/auth` and
`authenticatedFetch()`. This eliminates the PCF's React 16 constraint, the 805-line auth
stack, and the separate HTTP client.

**New flow:**

```
User clicks "New Document" button
  → Xrm.Navigation.navigateTo({ pageType: 'webresource', webresourceName: 'sprk_createdocument' })
  → Code page initializes @spaarke/auth (~10 lines, reads parent bridge token in ~0.1ms)
  → WizardShell with dynamic steps:
    Step 1: Upload Files (drag-and-drop zone, progress bars)
    Step 2: Document Details (name, type, description, client lookups)
    Step 3: Next Steps (optional follow-on actions)
  → authenticatedFetch('PUT /api/obo/containers/{id}/files/{path}')
  → Xrm.WebApi.createRecord('sprk_document', ...) OR authenticatedFetch for record creation
  → Success screen with "Open Document" link
```

### 14.3 What Changes, What Stays

| Aspect | Current (PCF) | Target (Code Page) | Change? |
|--------|--------------|-------------------|---------|
| **Auth** | MsalAuthProvider (805 lines) | `@spaarke/auth` (~10 lines) | Replace |
| **HTTP client** | SdapApiClient (custom) | `authenticatedFetch()` from `@spaarke/auth` | Replace |
| **File upload UI** | FileUploadZone (React 16) | Port to React 18 + Fluent v9 | Port |
| **Upload service** | MultiFileUploadService | Rewrite using `authenticatedFetch` | Rewrite |
| **Record creation** | DocumentRecordService + NavMapClient | Reuse logic, swap HTTP layer | Adapt |
| **Form fields** | Custom form components (React 16) | Fluent v9 Input/Combobox (React 18) | Port |
| **Wizard shell** | Custom step manager | `WizardShell` (existing component) | Reuse |
| **BFF endpoints** | `PUT /api/obo/containers/{id}/files/{path}` | Same endpoints — no server changes | Keep |
| **NavMap endpoints** | `GET /api/navmap/{entity}/{relationship}/lookup` | Same endpoints — no server changes | Keep |
| **Dark mode** | Supported via Fluent v9 | Inherited from WizardShell | Keep |

### 14.4 BFF API Endpoints (No Changes Required)

The server-side BFF API remains unchanged. The code page calls the same endpoints:

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/obo/containers/{containerId}/files/{*path}` | PUT | Upload file to SPE container |
| `/api/navmap/{childEntity}/{relationship}/lookup` | GET | Discover nav property names for @odata.bind |
| `/api/navmap/{entityLogicalName}/entityset` | GET | Get plural entity set name |

### 14.5 Migration Considerations

**What makes this higher risk than FilePreviewDialog:**

| Factor | FilePreviewDialog | CreateDocumentDialog |
|--------|------------------|---------------------|
| **Operations** | Read-only (1 GET call) | CRUD (multiple PUT + POST calls) |
| **File handling** | None | Multi-file upload with progress tracking |
| **SPE integration** | Preview URL only | Full file storage (container, drive item) |
| **Dataverse writes** | Single field update (workspace flag) | Record creation with lookups + associations |
| **Error handling** | Graceful degradation (show "unavailable") | Must handle partial failures (file uploaded but record failed) |
| **User workflow** | Optional preview (user can dismiss) | Primary creation flow (must work reliably) |

**Risk mitigation:**
- Build and test the code page alongside the existing PCF (feature flag or separate button)
- Run both in parallel during validation period
- Switch over only after the code page passes all test scenarios
- Keep the PCF control in the codebase (deprecated) until the code page is proven

### 14.6 Proposed File Structure

```
src/solutions/LegalWorkspace/src/components/CreateDocument/
├── CreateDocumentDialog.tsx          # WizardShell orchestrator
├── FileUploadStep.tsx                # Drag-and-drop file upload with progress
├── DocumentDetailsStep.tsx           # Name, type, description, client lookups
├── CreateDocumentNextStepsStep.tsx   # Optional follow-on actions
├── documentUploadService.ts          # authenticatedFetch wrapper for SPE upload
└── documentRecordService.ts          # Dataverse record creation via webApi
```

If implemented as a standalone code page (for use outside LegalWorkspace):
```
src/client/code-pages/CreateDocument/
├── src/index.tsx                     # React 18 createRoot entry point
├── src/App.tsx                       # Dialog shell
├── src/components/                   # Same components as above
├── src/services/                     # Upload + record services
├── index.html                        # HTML template
├── webpack.config.js                 # Bundle config
├── build-webresource.ps1             # Inline step
└── out/sprk_createdocument.html      # Deployable artifact
```

### 14.7 Relationship to Other Dialogs

The CreateDocumentDialog shares patterns with other wizard dialogs in the project:

| Dialog | Steps | Dynamic Steps | Auth | Status |
|--------|-------|---------------|------|--------|
| **Create Matter** | Upload → Details → Next Steps | Send Email, Assign Counsel | LegalWorkspace `bffAuthProvider` | Exists |
| **Create Project** | Project Details → Files → Next Steps | — | LegalWorkspace `bffAuthProvider` | Exists |
| **Summarize Files** | Upload → Run Analysis → Next Steps | Send Email, Create Project, Analysis | LegalWorkspace `bffAuthProvider` | Exists |
| **Create Document** (new) | Upload → Details → Next Steps | TBD | `@spaarke/auth` | Phase 4 |

All four use `WizardShell` for consistent wizard UX. The CreateDocumentDialog can reuse
`FileUploadZone` patterns from Create Matter and the `authenticatedFetch` pattern from
`@spaarke/auth`.

---

## 15. Auth Migration Inventory — Component-by-Component

This section catalogues **every component** in the codebase that has its own auth implementation.
Each entry documents the current state, what needs to change, and the migration path to
`@spaarke/auth`. This is the tech debt cleanup checklist.

### 15.1 Summary

| Category | Components | Total Auth Lines | After Migration |
|----------|-----------|-----------------|-----------------|
| Custom Pages (Solutions) | 1 | 268 | ~10 |
| Code Pages | 5 | 1,664 | ~50 (10 each) |
| PCF Controls | 7 | ~3,968 | ~70 (10 each) |
| Office Add-ins | 3 | 1,843 | Keep (NAA-specific) |
| **Total** | **16** | **~7,743** | **~1,973** |

> Office Add-ins use NAA (Nested App Authentication) which is specific to the Office host.
> They should keep their own auth layer but can share the `@spaarke/auth` config types.

---

### 15.2 Custom Pages (Solutions)

#### LegalWorkspace (Corporate Workspace)

| Item | Details |
|------|---------|
| **Type** | Custom page (React 18, bundled as `corporateworkspace.html`) |
| **Auth file** | `src/solutions/LegalWorkspace/src/services/bffAuthProvider.ts` |
| **Lines** | 268 |
| **Config files** | `src/solutions/LegalWorkspace/src/config/msalConfig.ts` (78 lines), `bffConfig.ts` (84 lines) |
| **Strategy** | In-memory cache → parent bridge (`__SPAARKE_BFF_TOKEN__`) → MSAL ssoSilent → anonymous |
| **Exports** | `bffAuthProvider` (singleton), `authenticatedFetch()`, `getTenantId()` |
| **Consumers** | DocumentApiService, matterService, projectService, analysisService, communicationService |
| **Issues** | None — this is the best pattern. Serves as the **template** for `@spaarke/auth`. |

**Migration**: This implementation becomes the basis for `@spaarke/auth`. After extraction, replace the local auth files with:
```typescript
import { initAuth, authenticatedFetch } from '@spaarke/auth';
```
Remove: `bffAuthProvider.ts` (268 lines), `msalConfig.ts` (78 lines), `bffConfig.ts` (84 lines) = **430 lines removed**.

---

### 15.3 Code Pages

#### AnalysisWorkspace

| Item | Details |
|------|---------|
| **Type** | Code page (React 18, `sprk_analysisworkspace.html`) |
| **Auth file** | `src/client/code-pages/AnalysisWorkspace/src/services/authService.ts` |
| **Lines** | 532 |
| **Config files** | `msalConfig.ts` (93 lines), `bffConfig.ts` (76 lines) |
| **Strategy** | In-memory cache → Xrm platform (5 strategies with frame-walk) → MSAL ssoSilent → exponential backoff |
| **Exports** | `getAccessToken()`, `clearTokenCache()`, `initializeAuth()`, `isXrmAvailable()`, `getClientUrl()`, `isSameOriginDataverse()` |
| **Hook** | `useAuth.ts` (67 lines) |
| **Consumers** | `analysisApi`, streaming hooks |
| **Issues** | Near-duplicate of LegalWorkspace + extra Xrm frame-walk logic |

**Migration**: Replace with `@spaarke/auth`. The Xrm frame-walk strategies will be absorbed into `@spaarke/auth`'s `XrmPlatformStrategy`. Remove: `authService.ts` (532), `msalConfig.ts` (93), `bffConfig.ts` (76), `useAuth.ts` (67) = **768 lines removed**.

#### PlaybookBuilder

| Item | Details |
|------|---------|
| **Type** | Code page (React 18, `sprk_playbookbuilder.html`) |
| **Auth file** | `src/client/code-pages/PlaybookBuilder/src/services/authService.ts` |
| **Lines** | 331 |
| **Config files** | `msalConfig.ts` (51 lines) |
| **Strategy** | Copy of AnalysisWorkspace + proactive 4-minute token refresh interval |
| **Exports** | `getAccessToken()`, `clearTokenCache()`, `initializeAuth()`, `isSameOriginDataverse()`, `stopTokenRefresh()` |
| **Hook** | `useAuth.ts` (62 lines) |
| **Issues** | ~99% copy-paste from AnalysisWorkspace. Proactive refresh is unique and should be an opt-in feature in `@spaarke/auth`. |

**Migration**: Replace with `@spaarke/auth` using `{ proactiveRefresh: true }` option. Remove: `authService.ts` (331), `msalConfig.ts` (51), `useAuth.ts` (62) = **444 lines removed**.

#### SprkChatPane

| Item | Details |
|------|---------|
| **Type** | Code page (React 18, `sprk_chatpane.html`) |
| **Auth file** | `src/client/code-pages/SprkChatPane/src/services/authService.ts` |
| **Lines** | 353 |
| **Config files** | `msalConfig.ts` (62 lines) |
| **Strategy** | Same as AnalysisWorkspace; throws if Xrm unavailable (chat pane requires Dataverse host) |
| **Exports** | `getAccessToken()`, `clearTokenCache()`, `initializeAuth()`, `isXrmAvailable()`, `getClientUrl()` |
| **Issues** | Same duplication. `requireXrm` behavior is unique. |

**Migration**: Replace with `@spaarke/auth` using `{ requireXrm: true }` option. Remove: `authService.ts` (353), `msalConfig.ts` (62) = **415 lines removed**.

#### SemanticSearch

| Item | Details |
|------|---------|
| **Type** | Code page (React 18, `sprk_semanticsearch.html`) |
| **Auth file** | `src/client/code-pages/SemanticSearch/src/services/auth/MsalAuthProvider.ts` |
| **Lines** | 235 |
| **Config files** | `msalConfig.ts` (66 lines) |
| **Strategy** | Class-based singleton; MSAL acquireTokenSilent → popup fallback; sessionStorage caching |
| **Exports** | `msalAuthProvider` (singleton), `getAuthHeader()`, `getToken()`, `clearCache()`, `isAuthenticated()` |
| **Consumers** | `SemanticSearchApiService`, `RecordSearchApiService` |
| **Issues** | **Hardcoded** tenant ID and redirect URI — breaks in non-dev environments. Does NOT use parent token bridge. Inconsistent class-based pattern vs function-based in other code pages. |

**Migration**: Replace with `@spaarke/auth`. Critical fix: environment portability. Also gains parent token bridge support (eliminates MSAL init when opened from LegalWorkspace). Remove: `MsalAuthProvider.ts` (235), `msalConfig.ts` (66) = **301 lines removed**.

#### DocumentRelationshipViewer

| Item | Details |
|------|---------|
| **Type** | Code page (React 18, `sprk_documentrelationshipviewer.html`) |
| **Auth file** | `src/client/code-pages/DocumentRelationshipViewer/src/services/auth/MsalAuthProvider.ts` |
| **Lines** | 213 |
| **Config files** | `msalConfig.ts` (57 lines) |
| **Type defs** | `src/client/code-pages/DocumentRelationshipViewer/src/types/auth.ts` (30 lines) |
| **Strategy** | Class-based singleton with `getInstance()`; implements `IAuthProvider` interface |
| **Issues** | **Hardcoded** tenant ID and redirect URI (same as SemanticSearch). No parent token bridge. |

**Migration**: Replace with `@spaarke/auth`. Same critical fix as SemanticSearch. Remove: `MsalAuthProvider.ts` (213), `msalConfig.ts` (57), `auth.ts` types (30) = **300 lines removed**.

---

### 15.4 PCF Controls

PCF controls run inside Dataverse form iframes with platform-provided React 16. They use MSAL to acquire tokens for BFF API calls.

#### UniversalDatasetGrid

| Item | Details |
|------|---------|
| **Type** | PCF (field-bound dataset grid) |
| **Auth file** | `src/client/pcf/UniversalDatasetGrid/control/services/auth/MsalAuthProvider.ts` |
| **Lines** | 793 |
| **Config files** | `msalConfig.ts` (328 lines) |
| **Type defs** | `auth.ts` (184 lines) |
| **Strategy** | Class-based singleton; sessionStorage caching; scope validation; acquireTokenSilent → popup |
| **Issues** | **Hardcoded** environment config. Largest auth implementation. Identical to UniversalQuickCreate. |

**Migration**: Replace with `@spaarke/auth`. Remove: ~1,305 lines total.

#### UniversalQuickCreate

| Item | Details |
|------|---------|
| **Type** | PCF (field-bound quick create form) |
| **Auth file** | `src/client/pcf/UniversalQuickCreate/control/services/auth/MsalAuthProvider.ts` |
| **Lines** | 805 |
| **Config files** | `msalConfig.ts` (337 lines) |
| **Type defs** | `auth.ts` (184 lines) |
| **Strategy** | Exact copy of UniversalDatasetGrid |
| **Issues** | **Largest** auth file in codebase. 100% duplicate of UniversalDatasetGrid. |

**Migration**: Replace with `@spaarke/auth`. Remove: ~1,326 lines total.

#### SemanticSearchControl

| Item | Details |
|------|---------|
| **Type** | PCF (field-bound semantic search) |
| **Auth file** | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/auth/MsalAuthProvider.ts` |
| **Lines** | 360 |
| **Config files** | `msalConfig.ts` (120 lines) |
| **Strategy** | Simplified singleton (shorter than Universal* controls) |

**Migration**: Replace with `@spaarke/auth`. Remove: ~480 lines total.

#### DocumentRelationshipViewer (PCF)

| Item | Details |
|------|---------|
| **Type** | PCF (field-bound document relationships) |
| **Auth file** | `src/client/pcf/DocumentRelationshipViewer/DocumentRelationshipViewer/services/auth/MsalAuthProvider.ts` |
| **Lines** | 277 |
| **Config files** | `msalConfig.ts` (124 lines) |
| **Type defs** | `auth.ts` (45 lines) |
| **Strategy** | Singleton pattern |

**Migration**: Replace with `@spaarke/auth`. Remove: ~446 lines total.

#### AnalysisWorkspace (PCF)

| Item | Details |
|------|---------|
| **Type** | PCF (field-bound analysis workspace) |
| **Auth file** | `src/client/pcf/AnalysisWorkspace/control/services/AuthService.ts` (243 lines) |
| **Extended auth** | `services/auth/MsalAuthProvider.ts` (793 lines) |
| **Config files** | `msalConfig.ts` (337 lines) |
| **Type defs** | `auth.ts` (45 lines) |
| **Strategy** | Constructor-based MSAL wrapper; named scope `api://{BFF_APP_ID}/SDAP.Access` |
| **Issues** | Uses different scope name (`SDAP.Access` vs `user_impersonation`). Needs scope reconciliation. |

**Migration**: Replace with `@spaarke/auth`. Reconcile scope naming. Remove: ~1,418 lines total.

#### SpeDocumentViewer

| Item | Details |
|------|---------|
| **Type** | PCF (field-bound document viewer) |
| **Auth file** | `src/client/pcf/SpeDocumentViewer/control/AuthService.ts` |
| **Lines** | 239 |
| **BFF client** | `BffClient.ts` (456 lines) — wraps auth + HTTP calls |
| **Strategy** | Constructor-based (not singleton) |

**Migration**: Replace `AuthService.ts` with `@spaarke/auth`; refactor `BffClient.ts` to use `authenticatedFetch()`. Remove: ~695 lines total.

#### SpeFileViewer

| Item | Details |
|------|---------|
| **Type** | PCF (field-bound file viewer) |
| **Auth file** | `src/client/pcf/SpeFileViewer/control/AuthService.ts` |
| **Lines** | 239 |
| **Strategy** | Identical copy of SpeDocumentViewer |

**Migration**: Same as SpeDocumentViewer. Remove: ~239 lines total.

#### EmailProcessingMonitor

| Item | Details |
|------|---------|
| **Type** | PCF (field-bound email monitor) |
| **Auth file** | `src/client/pcf/EmailProcessingMonitor/control/AuthService.ts` |
| **Lines** | 212 |
| **Strategy** | Constructor-based MSAL wrapper (shorter variant) |

**Migration**: Replace with `@spaarke/auth`. Remove: ~212 lines total.

---

### 15.5 Office Add-ins (Keep Separate)

Office Add-ins use **NAA (Nested App Authentication)**, a fundamentally different auth flow
from web-hosted code pages and PCF controls. NAA uses `createNestablePublicClientApplication()`
and a broker redirect URI (`brk-multihub://localhost`), with Dialog API fallback for hosts
that don't support NAA.

| File | Lines | Purpose |
|------|-------|---------|
| `src/client/office-addins/shared/auth/authConfig.ts` | 215 | Config factory (NAA + Dialog) |
| `src/client/office-addins/shared/auth/NaaAuthService.ts` | 629 | NAA token acquisition |
| `src/client/office-addins/shared/auth/DialogAuthService.ts` | 557 | Dialog API fallback |
| `src/client/office-addins/shared/services/AuthService.ts` | 442 | Facade (NAA → Dialog) |
| **Total** | **1,843** | |

**Migration**: No code replacement. These files stay as-is. However, they should:
- Import shared **types** from `@spaarke/auth` (`IAuthConfig`, `TokenCacheEntry`, etc.)
- Import shared **BFF config** from `@spaarke/auth` (scope URI, base URL)
- Keep NAA/Dialog-specific logic local

---

### 15.6 Known Issues to Fix During Migration

| Issue | Affected Components | Fix |
|-------|-------------------|-----|
| **Hardcoded tenant ID** | SemanticSearch (code page + PCF), DocRelationshipViewer (code page + PCF) | Use `organizations` authority |
| **Hardcoded redirect URI** | SemanticSearch (code page), DocRelationshipViewer (code page) | Use `window.location.origin` |
| **No parent token bridge** | SemanticSearch, DocRelationshipViewer (both code pages) | Add bridge token check as first strategy |
| **Scope inconsistency** | AnalysisWorkspace PCF uses `SDAP.Access`; all others use `user_impersonation` | Reconcile to single scope name |
| **No 401 retry** | SemanticSearch, DocRelationshipViewer (both code pages) | `authenticatedFetch()` adds automatic retry |
| **sessionStorage token storage** | SemanticSearch, DocRelationshipViewer (code pages); all PCF controls | Migrate to in-memory primary + sessionStorage backup |
| **No RFC 7807 error parsing** | All except SemanticSearch code page | `authenticatedFetch()` adds ProblemDetails parsing |
| **Missing test coverage** | All code page and PCF auth implementations (0 tests) | `@spaarke/auth` ships with comprehensive unit tests |

---

### 15.7 Migration Order (Recommended)

Migrate in order of risk and impact. This aligns with the project phasing in Section 13.7.

| Phase | Components | Why This Order |
|-------|-----------|---------------|
| **1. Create `@spaarke/auth`** | New shared package | Foundation for everything else |
| **2. LegalWorkspace + FilePreviewDialog** | Custom page + new component | Prove the pattern; lowest risk (closest to current design) |
| **3. FilePreviewDialog integration** | FindSimilar + DocumentCard | Consolidate existing preview patterns |
| **4. CreateDocumentDialog** | New code page replacing UniversalQuickCreate PCF | Retires largest auth stack (805 lines); see Section 14 |
| **5. AnalysisWorkspace + PlaybookBuilder + SprkChatPane** | Code pages (function-based) | Same pattern family; batch migration |
| **6. SemanticSearch + DocumentRelationshipViewer** | Code pages (class-based, hardcoded) | Fix environment portability + add bridge support |
| **7. SpeDocumentViewer + SpeFileViewer** | PCF controls (constructor-based) | Small, isolated; good PCF pilot |
| **8. UniversalDatasetGrid** | PCF control (largest auth file after QuickCreate retired) | Biggest remaining payoff |
| **9. SemanticSearchControl + DocRelationshipViewer PCF + EmailProcessingMonitor** | Remaining PCF controls | Complete the sweep |
| **10. AnalysisWorkspace PCF** | PCF (scope reconciliation) | Requires scope name decision (`SDAP.Access` vs `user_impersonation`) |

Phases 4, 5, 6, and 7 can run in parallel after Phase 1. Phase 3 runs after Phase 2.

**Estimated effort**: ~2–3 days for Phase 1, ~2–3 days for Phase 4 (CreateDocumentDialog is complex), ~0.5 day per remaining component migration, total ~8–12 days.

---

### 15.8 Lines of Code Impact

| Category | Before | After | Removed |
|----------|--------|-------|---------|
| LegalWorkspace (custom page) | 430 | ~10 | **420** |
| Code pages (5) | 2,228 | ~50 | **2,178** |
| PCF controls (7) | ~6,121 | ~70 | **~6,051** |
| `@spaarke/auth` (new) | 0 | ~500 | — |
| **Net change** | **~8,779** | **~630** | **~8,149 lines removed** |

> Note: PCF line counts include auth files + msalConfig + type defs + BffClient wrappers.
> The `@spaarke/auth` package adds ~500 lines (core + strategies + tests) but eliminates
> ~8,100 lines of duplicated auth code across 13 components.

---

## 16. Related Documentation

| Resource | Path |
|----------|------|
| Auth patterns (all 7 patterns) | `docs/architecture/sdap-auth-patterns.md` |
| Custom Dialogs Pattern | `.claude/patterns/webresource/custom-dialogs-in-dataverse.md` |
| DocumentApiService | `src/solutions/LegalWorkspace/src/services/DocumentApiService.ts` |
| Navigation utilities | `src/solutions/LegalWorkspace/src/utils/navigation.ts` |
| SemanticSearch preview | `src/client/code-pages/SemanticSearch/src/components/DocumentPreviewDialog.tsx` |
| DocumentCard (current) | `src/solutions/LegalWorkspace/src/components/RecordCards/DocumentCard.tsx` |
| FindSimilar results | `src/solutions/LegalWorkspace/src/components/FindSimilar/FindSimilarResultsStep.tsx` |
| Workspace flag field | `sprk_workspaceflag` on `sprk_document` entity |
| BFF preview endpoint | `GET /api/documents/{id}/preview-url` |
| BFF open-links endpoint | `GET /api/documents/{id}/open-links` |
