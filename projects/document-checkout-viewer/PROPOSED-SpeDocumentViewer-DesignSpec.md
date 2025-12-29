# Proposed Design Spec: SpeDocumentViewer (Editable + Version-Controlled)

## 1) Goal
Create a single reusable viewer/editor experience that:
- Replaces `SpeFileViewer` and `SourceDocumentViewer` with a unified `SpeDocumentViewer` PCF surface.
- Supports preview (read-only) and edit (checkout/checkin/discard) with version metadata.
- Works in the Document main form (full capability) and Analysis Workspace (read-only, optionally no delete).

## 2) Non-goals (MVP scope guardrails)
- No new pages, wizards, or dashboards.
- No new sharing UX (explicitly avoid exposing Share from Office Online embeds).
- No “force unlock” UX unless explicitly requested; treat as admin-only/back-end capability.
- No additional document management operations beyond the spec (checkout, checkin, discard, refresh, download, open links, delete/inactivate per decision).

## 3) System Context
### 3.1 Runtime boundaries
- **PCF control** hosts a React UI, obtains tokens via MSAL, and calls the BFF.
- **BFF API** performs authorization, Dataverse updates, and SharePoint Embedded (SPE) operations (including OBO to Graph/SPE).
- **Dataverse** stores document metadata (`sprk_document`) and version history (`sprk_fileversion`).

### 3.2 Preview/edit URL strategy
- Preview: SharePoint `embed.aspx` URL returned by BFF for safe read-only embedding.
- Edit: Office Online `embedview` (e.g., `webUrl?action=embedview`) returned by BFF on successful checkout.

## 4) Dataverse Data Model
### 4.1 New entity: File Version (`sprk_fileversion`)
Purpose: persistent version history per checkout/checkin cycle.

Required fields:
- `sprk_documentid` (lookup to `sprk_document`, required)
- `sprk_versionnumber` (whole number)
- `sprk_checkedoutby` (lookup systemuser)
- `sprk_checkedoutdate` (datetime)
- `sprk_checkedinby` (lookup systemuser)
- `sprk_checkedindate` (datetime)
- `sprk_comment` (text 500)
- `sprk_filesize` (whole number)
- `sprk_status` (choice: CheckedOut, CheckedIn, Discarded)

### 4.2 Document (`sprk_document`) additions (denormalized checkout state)
- `sprk_currentversionid` (lookup to `sprk_fileversion`)
- `sprk_currentversionnumber` (whole number)
- `sprk_ischeckedout` (yes/no)
- `sprk_checkedoutby` (lookup systemuser)
- `sprk_checkedoutdate` (datetime)

## 5) BFF API Contracts
All requests include:
- `Authorization: Bearer {token}`
- `X-Correlation-Id: {uuid}`

### 5.1 Existing endpoints (used by viewer)
- `GET /api/documents/{id}/preview-url`
  - Returns `previewUrl`, `documentInfo`, and (modified) checkout + version info.
- `GET /api/documents/{id}/open-links`
  - Returns `desktopUrl`, `webUrl`, `downloadUrl`, `mimeType`, `fileName`.
- `GET /api/documents/{id}/content`
  - Returns binary content for download.

### 5.2 New endpoints (checkout lifecycle)
- `POST /api/documents/{id}/checkout`
  - Success: returns checkout metadata and `editUrl` + `desktopUrl`.
  - Conflict (409): returns `document_locked` with `checkedOutBy` + timestamp.
- `POST /api/documents/{id}/checkin` (body includes `comment`)
  - Success: returns `versionNumber`, `previewUrl`, `aiAnalysisTriggered`.
- `POST /api/documents/{id}/discard`
  - Success: returns `previewUrl` and a message.

### 5.3 Delete
- `DELETE /api/documents/{id}`
  - Success: deletes SPE file + Dataverse record.
  - Conflict (409): `document_locked` if checked out.

## 6) PCF Control Contract
### 6.1 Manifest properties (ControlManifest.Input.xml)
Match the spec’s proposed properties:
- `value` (bound, required for field control discoverability)
- `documentId` (optional; fallback to form record ID)
- `bffApiUrl`
- `clientAppId`, `bffAppId`, `tenantId`
- `controlHeight`
- Feature flags: `showToolbar`, `enableEdit`, `enableDelete`, `enableDownload`

### 6.2 Outputs
- No bound outputs required for MVP.
- Do not use `notifyOutputChanged()` as a render mechanism; only call it if/when a real output is introduced.

## 7) UX + State Machine
### 7.1 Top-level view modes
A single reducer-driven state machine (no ad-hoc booleans) with these states:
- `loading`: initializing, acquiring token, fetching preview data
- `preview`: iframe shows `previewUrl` (embed.aspx)
- `edit`: iframe shows `editUrl` (embedview) and checkout badge reflects lock
- `processing`: transient state during checkin/discard/delete
- `error`: shows user-facing message + correlation ID

### 7.2 Checkout/lock rules
- If `checkoutStatus.isCheckedOut` and `isCurrentUser=false`:
  - Disable Edit/Check In/Discard actions.
  - Show status badge with who/when.
- If checked out by current user:
  - Allow Check In and Discard.

### 7.3 Refresh behavior
- Refresh does not require a BFF call if the current URL is still valid:
  - Cache-bust by appending/replacing `_cb={timestamp}`.
- If a refresh is requested while in `error` or if preview URL is missing:
  - Re-fetch via `GET /preview-url`.

## 8) React Component Architecture
### 8.1 Component tree
- `SpeDocumentViewerHost` (PCF adapter)
  - resolves documentId (bound prop or current record)
  - manages MSAL token + correlation ID
  - passes props down
- `SpeDocumentViewer` (React root)
  - orchestrates state machine and renders UI
  - owns toolbar + iframe region + dialogs

### 8.2 Core UI components
- `Toolbar`
  - Buttons (conditional): Refresh, Download, Edit (Checkout), Check In, Discard
  - Optional: Open in Desktop, Open in Web (based on file type and product decision)
- `CheckoutStatusBadge`
  - Shows checked out state; indicates current user vs other user
- Dialogs:
  - `CheckInDialog` (comment input)
  - `DiscardConfirmDialog`
  - `DeleteConfirmDialog` (only if delete is inside PCF; see §11)
- `FileFrame`
  - iframe wrapper with sandbox allowlist
  - iframe load timeout handling + spinner

### 8.3 Hooks
- `useAccessToken()`
  - MSAL init + silent acquisition + popup fallback
  - handles redirect processing
- `useTheme()`
  - reads theme from Power Platform context + localStorage + navbar fallback
  - emits `isDarkTheme`
- `useDocumentPreview()`
  - fetches preview-url; tracks iframe loading + timeouts
  - supports `AbortSignal`
- `useCheckoutFlow()`
  - checkout/checkin/discard calls
  - returns commands + flags and updates reducer state

### 8.4 Services
- `AuthService`
  - owns MSAL `PublicClientApplication` setup
  - configuration driven (no hard-coded redirect URI)
- `BffClient`
  - typed methods for endpoints above
  - maps problem-details into user-friendly messages
  - supports `AbortSignal` per request
- `CorrelationIdService`
  - per user action / request batch correlation ID generation

## 9) Error Handling + User Messages
- Prefer BFF-provided error codes (e.g., `document_not_found`, `mapping_missing_drive`, `throttled_retry`).
- Fall back to HTTP status messaging (401/403/404/409/5xx).
- Always display the correlation ID in error state for support.
- For 409 `document_locked`, show a specific locked message and the lock owner.

## 10) Performance + Cancellation
- Every in-flight BFF call accepts an `AbortSignal`.
- On PCF `updateView()` parameter changes (document id, feature flags, dimensions), abort prior fetches and restart.
- Keep iframe refresh local (cache-bust) where possible.

## 11) Delete/Deactivate UX Decision
The spec recommends ribbon-triggered delete (Option A JS) as the default.

### 11.1 Recommended (default): Ribbon-triggered delete
- The PCF does not own record deletion.
- Ribbon button calls BFF `DELETE /api/documents/{id}` and then navigates away.

### 11.2 If delete must exist in PCF
- Implement `enableDelete` gating.
- Use confirm dialog + progress + BFF delete call.
- Navigate away via Xrm APIs.

## 12) Security Notes
- Prefer `embed.aspx` for preview to avoid Share UI surfaces.
- Treat `embedview` as edit-mode only after explicit checkout.
- Do not store tokens in localStorage.
- Ensure the manifest’s external service usage accurately reflects outbound calls.

## 13) Reuse in Analysis Workspace
Expose the same React component as a shared library module (per ADR-012):
- Analysis Workspace composes `SpeDocumentViewer` with `enableEdit=false`, `enableDelete=false`.
- PCF host remains the only place that deals with PCF lifecycle.

## 14) Migration Plan
1. Extract shared React component(s) + services into shared library location.
2. Update existing `SpeFileViewer` PCF to use the shared `SpeDocumentViewer` component.
3. Migrate Analysis Workspace `SourceDocumentViewer` usage to `SpeDocumentViewer`.
4. Deprecate/remove legacy components after validation.

## 15) Open Questions (need explicit decisions)
1. “Open in Desktop” during checkout: is it allowed, and does it require checkout first, or can it remain a separate action?
2. Delete vs inactivate: is “inactivate” required, or is hard delete the only supported path?
3. Office Online edit security: do we accept any Share button exposure in edit mode, or must the BFF provide a constrained edit URL strategy?
