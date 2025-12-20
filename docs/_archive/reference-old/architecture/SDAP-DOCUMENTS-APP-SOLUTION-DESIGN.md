# SDAP “Documents” App — Solution Design Specification

> **Purpose**: Define a streamlined, file-manager-like “Documents” application that reduces user friction vs. logging into a bulky browser DMS UI, while integrating cleanly with Spaarke/SDAP’s existing BFF, authorization, and SharePoint Embedded (SPE) / Microsoft Graph patterns.
>
> **Status**: Draft (v0.1)
> **Last updated**: 2025-12-19

---

## 1. Executive Summary

Users are frustrated by needing to log into a heavy web (browser) application and navigate multiple screens to find and work with documents. This spec proposes a **lightweight desktop accessible “Documents” app** that behaves like **Windows File Explorer / OneDrive**:

- A **desktop-class experience** (installable, fast, keyboard-friendly)
- Works on **Windows and macOS**, and is **reusable on tablet/mobile**
- Supports **offline mode** focused on **checked-out documents**
- Integrates to **Spaarke/SDAP** via the existing **Sprk.Bff.Api** patterns (OBO to Microsoft Graph + Dataverse-backed authorization)

**Recommended delivery approach**: ship a **secure desktop app from day one** (Windows + macOS) using a lightweight desktop shell (recommended: **Tauri**) hosting a React/TypeScript UI. This enables enterprise-friendly capabilities that are hard to achieve reliably in browser-based approaches, especially:

- Offline storage with **OS-protected at-rest encryption** (DPAPI on Windows, Keychain on macOS)
- A scalable **Offline Vault** that can grow from “checked-out docs only” to broader offline availability over time
- Reliable **local file access** patterns and (optionally) background retry/sync

---

## 2. Product Concept

### 2.1 Concept
A dedicated “Documents” desktop app that opens into a familiar file manager layout, adapted to SDAP’s **record-centric** document organization:

- SharePoint Embedded storage is effectively **flat** for the user experience (no folder taxonomy)
- Documents are found via association to business records: **Projects, Matters, Invoices, Accounts, Contacts**

- **Left pane**: “My Checked Out” + “Browse by” record types (Projects / Matters / Invoices / Accounts / Contacts) + Recents/Pinned
- **Main pane**: Document list (flat) scoped to the selected record (e.g., Matter → its documents)
- **Right pane**: Preview/details panel (metadata + secure preview)

The app prioritizes **speed-to-document**, **predictable navigation**, and **safe operations** (aligned to SDAP authorization and SharePoint Embedded behavior).

### 2.2 Information Architecture (Critical UX Constraint)

To keep the app very simple while matching how documents are actually organized:

- The primary navigation is **Record → Documents** (not Folder → Files).
- “Where is this document?” should always be answerable as “It’s on Matter X” (or Project/Invoice/etc.).
- The UI should avoid exposing SharePoint concepts (drives, folders, containers) unless an admin/debug view.

This intentionally mirrors how legal users think: “open the Matter and see its documents.”

### 2.3 Design Principles
- **Fast path**: open → search → open/preview in ≤10 seconds for typical users
- **Keyboard-first**: search, navigation, and common actions without mouse
- **Low cognitive load**: consistent layout, simple affordances, minimal “app chrome”
- **Secure by default**: preview is safe; destructive or risky actions are gated
- **Offline as a feature, not a mode-switch**: offline availability is explicit per item

---

## 3. Primary Personas

- **Knowledge worker**: frequent browsing/searching; opens files to review, sometimes edits
- **Case/project team member**: works within a matter/project context; needs quick “recent + pinned”
- **Occasional user**: needs a document now; doesn’t remember where it lives
- **Mobile reviewer** (phase 2/3): preview and light operations on tablet/phone

---

## 4. Key Use Cases (User Journeys)

### UC1 — “Find and preview quickly”
1. User opens the app (already installed)
2. Uses global semantic search or navigates to a Project/Matter/Invoice/Account/Contact
3. Selects a document → preview loads immediately

### UC1b — “Semantic search (AI)”
1. User types a natural-language query (e.g., “lease assignment executed in 2023 for Acme”) into the global search box
2. Search returns ranked results across documents and associated records (Matter/Project/Invoice/etc.)
3. User refines via simple filters (record type, “My Checked Out”, offline available)
4. User opens the document or jumps directly to the associated record

### UC2 — “Open in desktop Office”
1. User selects a Word/Excel/PPT file
2. Clicks **Open in Desktop**
3. Document opens via the **desktop URL** (protocol link)

### UC3 — “Check out for editing (online)”
1. User selects a file → clicks **Check out**
2. App indicates checked-out state and shows who holds lock
3. User edits (desktop Office preferred)

### UC4 — “Offline checkout (checked-out docs)”
1. User checks out a document
2. App downloads and stores an offline copy in the **Offline Vault**
3. User later opens the app without connectivity and can still access the offline copy

### UC4b — “Offline search (limited, but useful)”
1. User is offline
2. User searches by filename, record name/number, and previously computed metadata (e.g., summary, entities) for documents in the Offline Vault
3. App returns results limited to locally available documents

### UC5 — “Check in and publish changes”
1. User returns online
2. App detects local changes (or user selects “Check in”)
3. App uploads changes and finalizes check-in

### UC6 — “Conflict / lock safety”
- App clearly explains why an operation fails (checked out by another user, no rights, connectivity)
- App offers safe recovery paths: refresh, open read-only, request unlock (if supported)

---

## 5. Requirements

### 5.1 Platform Requirements (given)
1. **Desktop app experience** (secure, IT-governed packaging)
2. **Cross-platform**: Windows + macOS
3. **Reusable/adaptable** to mobile/tablet (responsive UI + shared components)
4. **Offline mode** (at minimum for checked-out docs)

### 5.2 Functional Requirements (v1)
- Browse record types and records (Projects/Matters/Invoices/Accounts/Contacts)
- View a record’s document list (flat list)
- Global search (across all accessible documents, with incremental results)
- Semantic search (AI-assisted) with hybrid ranking (keyword + semantic relevance)
- Search results that include clear linkage to associated records (Matter/Project/Invoice/Account/Contact)
- Search within a selected record (e.g., within a Matter)
- “My Checked Out” view (single click) showing documents checked out by the current user
- Preview (secure-by-default; consistent with SDAP preview guidance)
- Open in Desktop (Office protocol + fallback)
- Check-out / check-in / discard checkout
- Offline Vault:
  - Explicit action to make a checked-out document available offline
  - Clear offline indicator per document
  - Manage offline storage footprint (basic: remove offline copy)
  - Offline search over locally available documents (see Section 9.5)

### 5.3 Non-Functional Requirements
- **Performance**
  - App cold start < 3s on standard corporate laptop (target)
  - Document listing response time < 500ms typical (excluding network latency)
  - Preview should show an immediate loading state (no black-screen flash)
  - Global search results returned quickly with progressive rendering (target: first page < 1s typical, excluding network latency)
- **Reliability**
  - Fail gracefully when offline; clear messaging
  - Resumable downloads/uploads when possible
- **Security & compliance**
  - All access must be enforced through SDAP authorization model and Graph permissions
  - Audit actions (checkout/checkin/download/open-links)
  - AI/search must be security-trimmed (no leakage of document existence via search)
  - Offline storage must be explicitly designed for corporate data handling (see Section 9)
- **Accessibility**
  - Keyboard navigation
  - Screen reader support for lists and commands

---

## 6. UX Features That Strongly Improve Adoption

These are “high leverage” features that make the app feel like a true file manager:

- **Recents** (documents opened recently) and **Pinned** (favorites)
- **Breadcrumb navigation** (clickable path)
- **Record breadcrumbs** (e.g., Matter → Documents) with a single back path
- **Type-ahead** global search (Ctrl/Cmd+K) is the primary entry point
- **My Checked Out**: always visible in left navigation and available as a one-click filter
- **Context menu** (right click): Preview, Open, Copy link, Check out/in
- **Multi-select + bulk actions** (download, remove offline, move where supported)
- **Preview pane toggle** (on/off)
- **Clear status chips**: Checked out by me / by other / Offline available / Sync pending
- **Human-friendly error messages** aligned to SDAP reason codes
- **Deep links**: open app directly to a record or document

Search UX emphasis (keep simple):
- One global search bar, with a small set of high-value filters:
  - Record type (Matter/Project/Invoice/Account/Contact)
  - “My Checked Out”
  - Offline available
  - (Optional later) File type
- Results show: document name, associated record, last modified, and a short AI snippet when available

Notes specific to SDAP/SPE:
- SharePoint Embedded preview iframes are cross-origin and cannot be themed; the app should theme **surrounding UI** and keep iframe area clean and stable.

---

## 7. Technology Approach

### 7.1 Recommended: Secure Desktop App (Tauri + React)

**Recommendation**: a **secure desktop shell** + **web UI**.

- **Desktop shell**: Tauri (Rust) for a small, signed, enterprise-friendly desktop footprint
- **UI**: React + TypeScript
- **Design system**: align with existing Fluent UI v9 usage in Spaarke client components
- **Auth**: Entra ID via MSAL (desktop)
  - Prefer system browser interactive auth; cache tokens securely
- **Offline Vault (required)**:
  - Store encrypted content using OS capabilities (DPAPI/Keychain)
  - Persist metadata and sync state locally (SQLite recommended)
- **Sync**:
  - Foreground sync for MVP; add background worker if needed for resilience

Why this matches your constraints:
- Satisfies “desktop app” expectations in IT governance
- Cross-platform (Windows/macOS) while reusing the same UI code
- Provides a credible offline-at-rest security posture from the start

### 7.2 Mobile/Tablet Reuse Strategy

To satisfy the “reusable/adaptable to mobile/tablet” requirement without introducing a separate web-delivered application:

 - Reuse the **same React component library** and API client where practical
 - Add a mobile shell later if required (e.g., React Native or .NET MAUI) while keeping backend contracts stable

### 7.3 Other Alternatives (Summary)

Pick based on security posture, team skills, and packaging expectations:

- **Electron + React**
  - Pros: mature ecosystem; easiest for web teams; many enterprise precedents
  - Cons: heavier footprint; more patch management; security hardening required
- **Tauri + React (recommended)**
  - Pros: smaller footprint; strong security story; native bridge only where needed
  - Cons: fewer “batteries included” than Electron; some native plugins required
- **.NET MAUI**
  - Pros: native UI controls; strong enterprise tooling; good for deep OS integration
  - Cons: slower UI iteration vs web stack; less reuse with existing TS/React assets

---

## 8. High-Level Architecture

### 8.1 System Context

```mermaid
flowchart LR
  U[User] -->|Uses| APP[Documents App
(Desktop)]

  APP -->|OAuth token| AAD[Entra ID]
  APP -->|HTTPS| BFF[Sprk.Bff.Api
(.NET 8 BFF)]

  BFF -->|OBO| GRAPH[Microsoft Graph
(SharePoint Embedded)]
  BFF --> DV[Dataverse
(doc metadata + access snapshot)]

  BFF --> AIS[Azure AI Search
  (semantic + vector)]
  BFF --> AOAI[Azure OpenAI / Foundry Models
  (embeddings + rerank/summaries)]

  BFF --> OBS[Logs/Tracing
(OpenTelemetry)]
```

### 8.2 Client Components
- **Shell & routing**: app layout, navigation, deep linking
- **Document browser**: virtualized list, keyboard navigation
- **Record navigator**: browse/search Projects/Matters/Invoices/Accounts/Contacts
- **My Checked Out view**: dedicated list + bulk actions (open, check-in, remove offline)
- **Search experience**: global search box + small filter set + AI snippets
- **Preview panel**: uses SDAP endpoints (preview-url/preview/view-url)
- **Action layer**: commands (open, checkout/in, download offline)
- **Offline Vault**:
  - OS-protected storage for encrypted content (desktop)
  - Local metadata + sync state (SQLite recommended)
  - Sync engine state machine (pending uploads, retries)
  - Optional background worker for retry and queue processing (desktop)
- **Telemetry**: usage metrics + performance timings

### 8.3 Backend Components (existing + additions)
Existing SDAP patterns already include:
- Document operations (checkout/checkin/discard/delete)
- File access endpoints (preview, content, office, open-links)
- OBO endpoints for user-scoped operations (e.g., listing children)

Existing AI patterns already include:
- AI document analysis endpoints (summaries/entities/keywords)
- Record matching using Azure AI Search (used for document-to-record association)

Likely additions for this app:
- User preferences endpoints (recents/pins)
- Offline manifest endpoints (what should be cached; hashes/ETags)
- Semantic document search endpoints (hybrid keyword + vector + semantic ranking)
- “My Checked Out” endpoints (list current user’s checkouts with record associations)
- Optional “delta/sync” endpoints to support incremental refresh

---

## 9. Offline Mode Design (Checked-Out Docs)

### 9.1 Offline Scope
Offline capability is **critical for legal DMS workflows**, so the design maximizes offline potential while still allowing incremental delivery.

**Baseline (v1)**: offline is guaranteed for **documents the user has checked out**.

**Growth path (future)**: expand offline from “checked-out only” to broader scenarios (e.g., selected Matters/Projects or curated “offline sets”) subject to policy.

Governance principles:
- Offline copies must be **explicit** (user intent) and **auditable**.
- Offline data must be **encrypted at rest** using OS-protected keys.
- Offline data must be **manageable** (retention, removal, remote wipe posture where possible).

### 9.2 Offline Vault Data Model
- `OfflineItem`
  - DocumentId
  - StorageContainerId (internal)
  - DriveId, ItemId (Graph)
  - Version/ETag
  - Local state: `downloaded | dirty | uploadPending | conflict`
  - File metadata (name, mime type, size)
  - Last sync time
- Content stored as encrypted content (required; see 9.4)

Recommended storage implementation (desktop):
- **SQLite** for metadata and sync state
- Encrypted file blobs stored in an **application vault directory**
  - Keys protected by **DPAPI** (Windows) and **Keychain** (macOS)
  - “Secure delete” behavior depends on OS/filesystem; design should support at least a best-effort wipe plus key rotation

### 9.3 Offline Sync State Machine
- **Checkout** → download latest content → mark offline available
- **Edit** (desktop Office) → user indicates “I updated locally” or app detects change via wrapper (if available)
- **Check-in** → upload → finalize check-in → remove offline copy (policy option)

Legal DMS considerations:
- Offline items should retain an **audit trail** (downloaded, opened, modified, uploaded, check-in)
- If policy requires it, enforce that offline availability implies an active **checkout/lock**

### 9.4 Offline Security Options (choose per corporate policy)
Offline posture (desktop baseline):

- **At-rest encryption**: all offline content encrypted with keys protected by DPAPI/Keychain.
- **Key lifecycle**: support key rotation; consider per-user or per-vault keys.
- **Session controls**: optional re-auth/re-unlock to open offline content after idle.
- **Data minimization**: offline is opt-in per item and (future) per record/offline set; show size and last sync.
- **Removal & wipe posture**:
  - Provide “Remove offline copy” per item and (future) per record/offline set.
  - Provide “Clear Offline Vault” (admin/support scenario).
  - If required by IT, integrate with device management posture (e.g., “on sign-out / on policy change, clear vault”).

**Recommendation**: offline encryption + vault management are **v1 must-haves** for legal DMS credibility.

### 9.5 Offline Search

Offline search is valuable in legal workflows, but it must be scoped to what can be guaranteed locally.

- **Scope**: only documents that are locally available (Offline Vault), typically checked out by the user.
- **Capabilities (v1)**:
  - Fast keyword search over: file name, associated record name/number, and stored metadata
  - Search over previously computed AI metadata (summary, entities, keywords) *if available locally*
- **Implementation approach (desktop)**:
  - Use SQLite full-text search (FTS) (or an equivalent local index) to index Offline Vault metadata
  - Optionally store small, precomputed embeddings for offline items when online (future), but do not assume “full semantic search offline” as a baseline

User experience principle: when offline, clearly label results as “Offline results only”.

---

## 10. Integration Design with Spaarke/SDAP

### 10.1 Authentication
- App uses **Entra ID** via MSAL (desktop)
  - Prefer system browser interactive auth; store tokens securely
- App calls **Sprk.Bff.Api** with bearer token
- BFF performs **On-Behalf-Of (OBO)** to Microsoft Graph where needed

### 10.2 Authorization
- Enforce access via the existing SDAP rule engine (operation-to-rights mapping)
- Use SDAP reason codes to produce user-friendly errors (e.g., “You don’t have permission to delete documents on this Matter.”)

### 10.3 Key API Surfaces to Use (existing)
(Names may evolve; verify in API docs / code)

- Documents by record association (Dataverse-backed)
  - List documents for a Matter / Project / Invoice / Account / Contact
  - Retrieve document metadata (including record associations)
- Record lookup/search (Dataverse-backed)
  - Search or resolve a Matter/Project/Invoice/etc. to allow navigation
- Global document search
  - Search across all accessible documents with facets for record type/record
  - Semantic search endpoint(s) for hybrid relevance + AI snippets
- File access
  - Preview URL / preview
  - Download content
  - Office web URL
  - Open-links (desktop protocol URL + web fallback)
- Document lifecycle
  - Checkout / checkin / discard
  - Delete (if allowed)

- AI analysis and enrichment (existing SDAP patterns)
  - Document analysis (summary/entities/keywords)
  - Record matching / association support

- My Checked Out
  - List documents checked out by current user (with record associations and offline availability)
- Permissions
  - Capability matrix for a document (canPreview/canDownload/canCheckOut/etc.)

### 10.4 Checkout/Check-in Semantics
Align with the existing direction in the document viewer work:
- Preview should use the “safe” approach (e.g., embed.aspx)
- Edit mode is explicit (checkout)
- “Open in Web” is a fallback and may expose Share/Delete controls; position carefully

### 10.5 Deep Links
Define deep-link routes that can be distributed in Teams/email:
- `/matters/{matterId}/documents`
- `/projects/{projectId}/documents`
- `/invoices/{invoiceId}/documents`
- `/accounts/{accountId}/documents`
- `/contacts/{contactId}/documents`
- `/documents/{documentId}`

---

## 11. Required Components

### 11.1 Client (Documents App)
- React/TypeScript UI
- Desktop shell (Tauri recommended) with a minimal native bridge for:
  - OS-protected secure storage (DPAPI/Keychain)
  - Local file IO for Offline Vault
  - Optional background queue processing
- UI primitives: record navigator (list/search), virtualized document list, command bar, preview pane
- Offline Vault module
- Search module (online semantic search + offline local search)
- “My Checked Out” module/view
- Telemetry module
- Configuration for environment endpoints (local/dev/prod)

### 11.2 Backend (Sprk.Bff.Api enhancements)
- Confirm/extend endpoints needed for:
  - Record lookup/search (Matters/Projects/Invoices/Accounts/Contacts)
  - List documents by record association (paged)
  - Global semantic document search (hybrid + facets + snippets)
  - My Checked Out list (documents checked out by current user)
  - Offline manifest (ETags/hashes)
  - Preferences (recents/pins)
- Ensure consistent auth filters per existing ADR patterns

### 11.3 Deployment / IT Packaging
Primary (recommended):
- **Signed desktop installers** for Windows and macOS
  - Windows: MSIX (preferred in many enterprises) or signed EXE installer
  - macOS: signed/notarized PKG/DMG
- **Managed distribution**: Intune / Company Portal (or equivalent) with version governance
- **Update strategy**: controlled update channel + rollback plan (IT-friendly)

### 11.4 Reusability (Model-Driven App / Power Apps)

Reusability is a core development focus, but it must be scoped correctly.

**What can be reused (high value):**
- **PCF React/Fluent UI components**: most Spaarke PCF controls are React + Fluent UI v9; the *inner React components* (view layer) are reusable in a desktop app (Tauri/Electron) because they are still “just web UI”.
- **Shared UI utilities** (theme handling, formatting, common widgets): existing controls already hint at extracting these into shared packages.
- **API client logic and DTOs**: request/response shapes, error handling, and capability gating logic can be shared (desktop app and PCF both call the BFF).
- **Non-UI utilities**: telemetry helpers, feature-flag utilities, and small validators.

**What generally cannot be reused directly:**
- **Model-driven screens themselves** (forms, views, command bar rules, sitemap navigation).
- **PCF host bindings** (`ComponentFramework.Context`, `Xrm.*`, form context) and any logic tightly coupled to Dataverse page lifecycle.

**Existing model-driven web resources (Ribbon/Command Bar) to review and “wire in”:**

We already have JavaScript web resources used by command bar (ribbon) buttons for document operations and theming. These should be treated as **reference implementations** and/or **candidates for extraction**, not copied verbatim into the desktop app runtime.

- Document operations (checkout/checkin/discard/status caching): `src/client/webresources/js/sprk_DocumentOperations.js`
- Document delete: `src/client/webresources/js/sprk_DocumentDelete.js`
- Theme menu handler (command bar): `src/client/webresources/js/sprk_ThemeMenu.js`

Key implications:
- These scripts are **Power Apps-hosted** and depend on `Xrm.*` and command bar invocation patterns; the desktop app will not have those APIs.
- They include **environment detection** and **auth bootstrapping** that is specific to Dataverse hosting (e.g., deriving environment from org URL).
- They call the BFF endpoints for checkout/checkin/delete; the desktop app should call the same BFF endpoints, but via a shared TypeScript client rather than duplicating webresource-style global functions.

Recommended action:
- Inventory all ribbon/command bar functions used for document operations, map them to BFF endpoints, and ensure the desktop app exposes equivalent actions.
- Where logic is non-host-specific (request payload construction, error mapping, status handling), extract it into shared packages and keep only thin host adapters in the web resources.

**Recommended approach (to maximize reuse without coupling):**
- Refactor PCF controls so each one has a thin “PCF adapter” layer and a host-agnostic React component library.
- Extract reusable UI into a shared package (e.g., `@spaarke/ui-components`) and reusable service logic into another (e.g., `@spaarke/sdap-client`).
- Standardize on React 18.2.x + Fluent UI v9 across desktop app and PCF controls (matches the existing PCF standardization direction).
- Introduce small host abstractions so shared code doesn’t import PCF/Xrm directly:
  - `TokenProvider` (PCF uses `Xrm.Utility.getGlobalContext().getAccessToken()`, desktop uses MSAL)
  - `HostInfoProvider` (environment URLs, tenant info)
  - `TelemetrySink`

Outcome: the Documents desktop app reuses the same UI building blocks as the model-driven experience, while avoiding hard dependencies on the Power Apps runtime.

### 11.5 Alternative Approach: Power Apps Canvas Apps

Canvas apps can accelerate delivery for simple, mobile-first workflows, but they are not a good fit as the primary solution for the Documents desktop app goals in this spec.

**Where Canvas apps can help**
- Lightweight “companion” experiences (mobile/tablet) for simple tasks: search + view metadata, launch preview, quick upload/capture.
- Rapid iteration on specific workflows embedded in the broader Power Platform experience.

**Why Canvas apps are not recommended as the primary approach for this project**
- The requirement is a **desktop-first app (Windows/macOS)** with an **encrypted offline vault** for checked-out documents; Canvas apps do not provide the same controlled local filesystem access and encryption/key management primitives needed for a legal-grade offline vault.
- Offline in Canvas apps is possible for specific patterns, but it is not designed for large, encrypted document vaults with deterministic sync/ETag handling.
- Deep OS integrations expected of a desktop document experience (shell open, file handlers, background sync queues, vault storage governance) are limited.

Recommendation: keep Canvas apps as an optional Phase 3+ complement for mobile/tablet, but proceed with a desktop app as the primary solution.

---

## 12. Development Plan

### Phase 0 — Alignment (1–2 weeks)
- Confirm corporate IT standards for desktop packaging: code signing, distribution, update cadence
- Confirm offline data classification requirements and required at-rest controls
- Confirm target data classification requirements for offline
- Confirm supported browsers/devices
- Inventory existing model-driven command bar (ribbon) document actions and map to BFF endpoints (checkout/checkin/discard/delete/status/theme)
- Confirm reuse packaging approach (shared UI package + shared BFF client package) and versioning strategy

**Exit criteria**:
- Confirmed decision: desktop shell technology (Tauri vs Electron vs MAUI) + packaging path
- Documented reuse plan and action mapping for model-driven ribbon/webresources

### Phase 1 — MVP Desktop Experience (4–6 weeks)
- Desktop shell + core UI
- Record navigation (Projects/Matters/Invoices/Accounts/Contacts)
- Record search + selection
- Document list for selected record (flat)
- Global search (v1: keyword + filters)
- Semantic search groundwork (index + security trimming + basic semantic ranking)
- My Checked Out view
- Preview pane (secure-by-default)
- Open in Desktop + web fallback
- Permissions-aware command surface

**Exit criteria**:
- Users can find, preview, and open documents quickly

### Phase 2 — Checkout + Offline Vault (4–6 weeks)
- Checkout/checkin/discard flows
- Offline Vault for checked-out docs:
  - Download/store
  - Offline open/preview of stored content
  - Clear status and storage management
  - At-rest encryption and key management integrated (DPAPI/Keychain)

**Exit criteria**:
- A user can take a checked-out doc offline and later check it in

### Phase 3 — Mobile/Tablet Adaptation (3–5 weeks)
- Responsive layouts
- Touch-friendly list navigation (record-first)
- Mobile-appropriate preview/open flows

### Phase 4 — Hardening + Scale (ongoing)
- Performance at scale for large records (virtualization, paging)
- Search relevance tuning and facets
- AI enrichment coverage improvements (summaries/entities indexed for more docs)
- Offline search improvements (local index coverage, query UX, performance)
- Performance tuning (virtualization, caching)
- Enhanced audit + admin diagnostics
- Optional wrapper (if needed)

---

## 13. Risks & Mitigations

- **Desktop packaging + updates are governance-heavy**
  - Mitigation: align early with IT (MSIX/PKG, signing, rollout rings, rollback)
- **Cross-platform desktop differences (Windows vs macOS)**
  - Mitigation: keep native surface area minimal; invest in a small, well-tested bridge
- **SharePoint Embedded iframe theming limitations**
  - Mitigation: stable loading UX, theme surrounding UI only
- **Office “Open in Web” exposes Share/Delete**
  - Mitigation: prefer Open in Desktop; capability-based UI; tenant policy controls
- **Large libraries (performance + paging)**
  - Mitigation: server paging, client virtualization, incremental search
- **Concurrent checkout conflicts**
  - Mitigation: clear messaging; refresh; future admin unlock

---

## 14. Open Questions (to resolve early)

1. Desktop packaging requirements: MSIX/PKG standards, signing process, update governance, device management constraints?
2. What is the minimum acceptable offline security posture (encryption, key storage, retention, remote wipe expectations)?
3. Do we require “edit offline” (local Office edits) in v1, or is offline read-only acceptable?
4. Should Recents/Pins be stored in Dataverse (enterprise) or local-only (fast)?
5. What is the expected scale (documents per Matter/Project, and total accessible documents) for performance targets?

---

## Appendix A — Design Notes Specific to Current SDAP Work

- SDAP already implements granular operation-based authorization (Dataverse AccessRights → Graph operations). The app should request capabilities from the backend and render UI accordingly.
- Existing work has established:
  - Secure preview patterns (embed.aspx)
  - Checkout/checkin endpoints
  - Desktop open-links endpoint and Office protocol URL handling

This Documents app should reuse those proven patterns rather than re-implementing direct Graph calls from the client.

---

## Appendix B — API Contract Sketch (for alignment)

This section is intentionally a **sketch** (not a final API spec). The goal is to make dependencies explicit for parallel backend/frontend work and reduce ambiguity.

Document listing and record navigation:
- `GET /api/records/search?type={matter|project|invoice|account|contact}&q={text}&top={n}`
- `GET /api/{recordType}/{recordId}/documents?skip={n}&top={n}&q={optionalText}` (record-scoped list + optional keyword filter)

My Checked Out:
- `GET /api/documents/checked-out/me?skip={n}&top={n}`

Global search:
- `GET /api/documents/search?q={text}&skip={n}&top={n}&recordType={optional}&offlineOnly={optional}`
- `GET /api/documents/semantic-search?q={text}&skip={n}&top={n}&recordType={optional}&myCheckedOut={optional}`
  - Response includes: document id/name, associated record (type/id/name), last modified, capability flags, and optional AI snippet.

Checkout lifecycle:
- `POST /api/documents/{documentId}/checkout`
- `POST /api/documents/{documentId}/checkin`
- `POST /api/documents/{documentId}/discard-checkout`
- `GET /api/documents/{documentId}/checkout-status`

File access:
- `GET /api/documents/{documentId}/preview-url`
- `GET /api/documents/{documentId}/content` (download)
- `GET /api/documents/{documentId}/open-links` (Office protocol URLs)

Offline support:
- `GET /api/documents/{documentId}/offline-manifest` (ETag/hash + metadata needed for offline validation)

Notes:
- All endpoints remain **security-trimmed**: results must not reveal documents/records the caller cannot access.
- Consider a shared “capabilities” object in list/search responses to avoid duplicative permission calls.

---

## Appendix C — Operational Readiness (Telemetry, Diagnostics, Support)

To make the desktop app supportable in enterprise deployments, define from the start:

- **Telemetry** (OpenTelemetry-compatible):
  - App start time, page navigation timings, list/search latency, preview latency, checkout/checkin success/fail
  - Correlation IDs propagated: Desktop App → BFF → Graph/Dataverse/Azure AI Search
- **Client logging**:
  - Structured logs with PII-safe defaults (no document contents; avoid full filenames if policy requires)
  - A user-facing “Diagnostics” export (logs + environment info + last errors) for helpdesk escalation
- **Feature flags / kill switches**:
  - Ability to disable high-risk features remotely (e.g., Open in Web, offline editing) without redeploying
- **Error taxonomy**:
  - Standard reason codes from BFF surfaced as consistent UX messages; include a support code for troubleshooting
- **Privacy & retention**:
  - Define retention limits for local logs and offline metadata/indexes; include “Clear local data” control
