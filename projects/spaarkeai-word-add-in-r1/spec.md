# Spaarke Office Add-in (Word + Outlook) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-09-04
> **Source**: `design.md` (+ `ADDIN-CONTEXT-FROM-EMAIL-R2.md`, `DEDUP-AND-SAVE-BACK-IDENTITY.md`)

## Executive Summary

Turn the existing save-only Spaarke Office add-in into a useful Spaarke surface inside Word and Outlook. Users open documents from **any source** — desktop, OneDrive, a DMS, Harvey, Claude — and draft however they like; Spaarke does not participate in the drafting. Spaarke participates at the moments that matter: **filing the work product correctly**, and **surfacing the matter context** (related record, AI profile, similar documents, to-dos) that makes filing and drafting sensible.

This is a UX and productivity project, not an AI project. It fixes UAT-reported defects in the save flow, adds a tabbed pane, and closes a live data-loss bug on the client upload path.

---

## Scope

### In Scope

- **Conditional document identity** — when a document came from Spaarke, the pane knows which `sprk_document` and matter it is
- **Save flow fixes** — filename/Document Name defaulting, profile fields, version-not-duplicate, related-record card, open-record
- **A data-loss fix** on the client upload path (silent REPLACE on filename collision)
- **Tabbed pane** — Save | Find (+ a launch affordance for the existing Copilot agent if a mechanism exists)
- **Find** — true content-similarity search over documents and records
- **Server-side record-creation completeness** — number, owner, mapped fields (Matter + Project)
- **Add To Do** from the pane, carrying document + related record
- **Send Email** via Outlook with document/record links
- **Outlook parity** for all shared-tier capabilities
- **Housekeeping**: Word unified JSON manifest, adapter consolidation, ribbon commands, typecheck debt

### Out of Scope

- **Competing with legal drafting tools** — no tracked-change authoring, redlining, or drafting agent (design L-1)
- **MCP / external-tool interop** — owned by `spaarkeai-word-native-r1` (L-4)
- **Extending Spaarke AI capability** — surfacing only (L-5)
- **Building duplicate detection** — already shipped; r1 consumes it
- **Requiring Spaarke as the document source** (L-2)
- **Deferred requests** (documented in `design.md` §4.2): Send Message modal · Send Email via Spaarke email client modal · Event task (`sprk_event`) · "+More" fields on create · Tier-2 semantic near-duplicate detection
- **Migrating the Create*Wizard components** to the new server-side creation service — evaluated separately after r1 (`design.md` §7.1)

### Affected Areas

| Path | Change |
|---|---|
| `src/client/office-addins/shared/taskpane/**` | Tabs, views, cards, profile display, Find, To Do, Send Email — **shared-first** |
| `src/client/office-addins/shared/adapters/**` | Consolidate Word onto tested `WordAdapter` via `HostAdapterFactory` |
| `src/client/office-addins/word/**` | Manifest → unified JSON; ribbon commands; retire duplicate `WordHostAdapter` |
| `src/client/office-addins/outlook/**` | Parity wiring only |
| `src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs` | Identity resolve, profile trigger, Find, creation-completeness |
| `src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs` | Same; plus collision fix |
| `src/server/api/Sprk.Bff.Api/Api/FileAccessEndpoints.cs` | Document-identity resolver (extends `/api/documents`) |
| `tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs` | Contract coverage |

---

## Requirements

### Functional Requirements

**Foundation**

1. **FR-01 — Conditional document identity.** Resolve the open document to a `sprk_document` when it originated from Spaarke: `Office.context.document.url` → base64url → Graph `GET /shares/u!{enc}/driveItem` → `driveId`+`itemId` → `sprk_document` via the `sprk_graphitemid_uk` alternate key. — *Acceptance*: a Spaarke-sourced document resolves to the correct record and matter; a desktop-sourced document resolves to nothing and is treated as new.
2. **FR-02 — Document GUID stamp (server-side).** On every Spaarke save, stamp the `sprk_document` GUID into a custom XML part **in the uploaded bytes server-side** — never by mutating the user's open document. A stamped document that is downloaded and later re-uploaded self-identifies without a Graph round-trip. — *Acceptance*: download a saved document, re-open it from disk, and the pane identifies the original record.
3. **FR-03 — Tabbed pane.** Extend `NavigationTab` and build real views. r1 tabs: **Save | Find**. Existing `share`/`recent` placeholders remain unbuilt and hidden. — *Acceptance*: both tabs render and function in Word and Outlook.
4. **FR-04 — Adapter consolidation.** Route Word through `HostAdapterFactory` onto the tested `shared/adapters/WordAdapter.ts`; reconcile the `.docx` save capability r2 added to the bypassed `word/WordHostAdapter.ts`; delete the duplicate. — *Acceptance*: one Word adapter, reached via the factory, with unit tests covering the `.docx` path.
5. **FR-05 — Word unified JSON manifest.** Migrate `word-manifest.xml` to the unified JSON manifest, following `outlook/manifest.json` as the precedent. — *Acceptance*: Word add-in installs and runs from the unified manifest on desktop and web.

**Save flow**

6. **FR-06 — Filename defaults to Document Name.** The Document Name field defaults to the filename and is editable in-pane via a pencil affordance. — *Acceptance*: default matches the filename; edits persist to `sprk_documentname`; the field can be removed from the Dataverse form without loss.
7. **FR-07 — Profile section.** Rename "Description" to "Profile" and populate `sprk_filesummary`, `sprk_filetldr`, `sprk_filekeywords`, `sprk_documenttype` from the record. — *Acceptance*: for an identified document with a completed profile, all four fields display; for `sprk_filesummarystatus` in a non-complete state, the pane shows that state rather than blank fields.
8. **FR-08 — Generate Profile.** A button re-runs document profiling for the current document. — *Acceptance*: clicking it re-dispatches profiling and the pane reflects the updated status; profiling completes successfully (#919 fixed on master).
9. **FR-09 — Related-to record card.** When the document is identified and associated, show a card for the related record. — *Acceptance*: card shows record type, name and number; clicking opens the record (FR-10).
10. **FR-10 — Open record / open Document record.** Open the related record and the `sprk_document` record from the pane. **Preferred mechanism: Office Dialog API** (see Spike-2). — *Acceptance*: the record opens and is usable; edits made there are reflected in the pane on return.
11. **FR-11 — Version-save with override.** When FR-01 resolves an existing `sprk_document`, **default to saving a new version** of that record via `SaveRequest.ExistingDocumentId`. Offer an explicit **"Save as new document"** override. — *Acceptance*: default path creates a version, not a second row; the override path creates a new document and is routed through the editable **link/graduate** dedup mode.
12. **FR-12 — Consume the shipped collision handling (verify, do not rebuild).** ⚠️ **The data-loss bug is already fixed** — `unified-access-control-r2` shipped it 2026-09-02: explicit `conflictBehavior` on the small-upload path (`9c208a7f2`), server default `Fail` so a same-named file returns **409 with the existing file untouched** (`93d5e673e`), typed `UploadNameConflictError` through the upload clients consolidated onto `@spaarke/sdap-client` (`086b9e9ce`, `047c9df8d`), and a two-option collision dialog — *Keep both* / *Save as new version* (`09025ab39`). **Do not rebuild any of this.** The add-in's job is to surface the typed conflict error correctly in the pane and honor the two-option choice. Remaining refinement (pre-flight probe before bytes move, plus "Use existing") is **owned by UAC-r2 task 094** — coordinate, do not duplicate. — *Acceptance*: a filename collision from the add-in surfaces the two-option dialog and the existing file's bytes are provably untouched; no new collision logic is introduced.

**Surfacing Spaarke**

13. **FR-13 — Record-creation completeness (server-side).** Move number generation, owner assignment and Field Mapping Framework population into a **shared server-side creation service** called by `QuickCreateAsync`. Scope: **Matter and Project only**. — *Acceptance*: a Matter created from the pane has `sprk_matternumber` and owner populated plus mapped fields applied; same for Project and `sprk_projectnumber`. Existing `Create*Wizard` components are **not** modified.
14. **FR-14 — Add To Do.** Create a first-class `sprk_todo` from the pane with **both** the document and the related record as regarding, reusing `POST /api/office/todo` and `CreateTodoView`. — *Acceptance*: the To Do is created with correct regarding for both Word (document + record) and Outlook (communication + record).
15. **FR-15 — Send Email via Outlook.** Open Outlook pre-populated with links to the document and/or related record. — *Acceptance*: the compose window opens with working links honoring existing share-link expiry bounds.
16. **FR-16 — Find: true content similarity, gated on index state.** Find returns documents and records similar **to the open document's content**, reusing the existing `documentVector3072` cosine-KNN "Find Similar" engine. **Similarity requires the document to be indexed — there is no on-demand embedding path.** The tab branches on state:

    | State | Pane shows |
    |---|---|
    | No `sprk_document` (unsaved / external) | *"Save this document to Spaarke so it can be indexed for AI similarity search."* + a link to the Save tab |
    | `sprk_document` exists, `sprk_searchindexed` = **no or null** | *"This document isn't indexed yet."* + a **Run Index** button |
    | `sprk_searchindexed` = **yes** | Similarity results |

    **Run Index** submits the file to the indexing pipeline and, on success, `sprk_searchindexed` flips to yes (with `sprk_searchindexedon` stamped) — written by `RagIndexingJobHandler`, not by the add-in. ⚠️ Per [`.claude/patterns/ai/indexing-pipeline.md`](.claude/patterns/ai/indexing-pipeline.md), the call **MUST pass `documentId`** (the `sprk_document` GUID) or chunks land as orphans and the tracking fields cannot be written. — *Acceptance*: each of the three states renders correctly; Run Index flips the field and results appear afterwards; results are permission-trimmed (NFR-02) and lazy-scrolled (NFR-05).
17. **FR-17 — Word ribbon commands.** Wire the stubbed `quickSave` and `shareDocument` commands. — *Acceptance*: both execute from the ribbon without opening the pane where that is the intended behavior.
18. **FR-18 — Typecheck debt.** Clear the ~397 pre-existing `exactOptionalPropertyTypes` errors in `src/client/office-addins`. **Do this first**, so new errors are visible during feature work. — *Acceptance*: `npm run typecheck` is clean; CI gates it going forward.
19. **FR-19 — Outlook parity.** Every shared-tier capability works in both hosts or is explicitly gated by `hostAdapter.getCapabilities()`. — *Acceptance*: no host-type conditionals scattered through views; parity verified per capability.
20. **FR-20 — Copilot agent launch affordance** *(spike-gated, see Spike-3)*. If a mechanism exists to open the Copilot pane / the "Spaarke AI" agent, expose it as a **button or icon** — not a tab. If none exists, omit it. — *Acceptance*: either the affordance opens the agent, or the spike is documented as negative and the item is closed.

### Non-Functional Requirements

- **NFR-01 — Publish size.** Measure BFF publish size on every BFF-touching task. Ceiling ≤60 MB compressed; baseline ~44.96 MB. Escalate at ≥+5 MB single-task delta.
- **NFR-02 — Per-row authorization on search.** Find must use permission-trimmed endpoints. `RecordSearchAuthorizationFilter` publishes `RequiresPerRowRecordAuthorization`; an endpoint receiving it and not performing the row check **MUST refuse**. Verified by a negative test.
- **NFR-03 — No `Xrm` in the add-in.** `Xrm.*` is unavailable in an Office host. Xrm-bound wizard components MUST NOT be imported; recreate layouts (the pattern r2 established).
- **NFR-04 — Fluent v9 + Office theme.** All UI uses Fluent UI v9 (ADR-021); honor Office light/dark theme via `useOfficeTheme`.
- **NFR-05 — Infinite lazy-scroll.** Find results use progressive load-on-scroll with the canonical thin scrollbar. **No pager** — no numbered pages, prev/next, or "Load more" (ADR-051).
- **NFR-06 — GUID canonicalization.** Every Dataverse GUID is canonicalized to bare-lowercase at every boundary via the shared `cleanGuid` (ADR-044). No hand-rolled brace-stripping; never interpolate a raw GUID into an OData key predicate.
- **NFR-07 — Alternate key is inviolable.** MUST NOT relax `sprk_graphitemid_uk` on the SPE item id — Compose's transient-key dedup and promote-idempotency both rest on it.
- **NFR-08 — Editable dedup mode.** For editable documents, a content-hash hit MUST use **link/graduate** (mirroring `ComposeService.PromoteIfEphemeralAsync`), never the immutable suppress path. Suppress-forever on an editable document collapses two distinct drafts into one record — data loss.
- **NFR-09 — Per-environment Entra registration.** Each environment MUST register two SPA redirect URIs: `brk-multihub://<swa-host>` and `https://<swa-host>/auth-callback.html`.
- **NFR-10 — Shared-first.** Capabilities land in `shared/taskpane/` and are gated by host capability, not by host-type branching in views.
- **NFR-11 — Accessibility.** Pane is keyboard-navigable with announced state changes (existing `useAnnounce` pattern).

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|---|---|
| **ADR-001** | Minimal API + BackgroundService for all new BFF surface |
| **ADR-007** | `SpeFileStore` facade — no Graph SDK types leak above it |
| **ADR-008** | Endpoint filters for authorization; no global auth middleware |
| **ADR-010** | DI minimalism — ≤15 non-framework registrations |
| **ADR-012** | Shared component library discipline |
| **ADR-021** | Fluent UI v9; dark mode required |
| **ADR-028** | Spaarke Auth v2 — NAA, managed identity outbound, **secret-free** |
| **ADR-029** | BFF publish hygiene + size ratchet |
| **ADR-038** | Testing strategy — integration-heavy; KEEP-path rules; banned mock patterns |
| **ADR-044** | **`cleanGuid` canonicalization at every Dataverse boundary** |
| **ADR-050** | Canonical modal shell — if dialog content renders Spaarke UI |
| **ADR-051** | **Infinite lazy-scroll, never a pager** |

### MUST Rules

- ✅ MUST canonicalize Dataverse GUIDs via `cleanGuid` at every boundary (ADR-044)
- ✅ MUST use endpoint filters for authorization (ADR-008)
- ✅ MUST keep Graph types behind `SpeFileStore` (ADR-007)
- ✅ MUST use NAA via `@spaarke/auth` `OfficeNaaStrategy` — no direct MSAL construction (ADR-028, arch-test enforced)
- ✅ MUST use infinite lazy-scroll for Find results (ADR-051)
- ✅ MUST measure publish size on BFF-touching tasks (ADR-029)
- ❌ MUST NOT use `.WithClientSecret` (ADR-028, arch-test enforced)
- ❌ MUST NOT import `Xrm`-bound components into the add-in (NFR-03)
- ❌ MUST NOT relax `sprk_graphitemid_uk` (NFR-07)
- ❌ MUST NOT use the immutable suppress path for editable documents (NFR-08)
- ❌ MUST NOT add a pager to any list (ADR-051)

### Existing Patterns to Follow

- **BFF-backed record op from the pane** — `POST /api/office/todo` → `OfficeService.CreateTodoAsync` (the working template)
- **SSE job progress** — `GET /api/office/jobs/{jobId}/stream` + `services/SseClient.ts`
- **Save-context threading** — `SaveView.onSaved` → `App.savedContext`
- **Auth** — `@spaarke/auth` `OfficeNaaStrategy` wrapped by `shared/services/AuthService.ts`
- **Editable dedup** — `ComposeService.PromoteIfEphemeralAsync` (link/graduate)
- **As-built map** — `docs/architecture/office-outlook-teams-integration-architecture.md`, `src/client/office-addins/CLAUDE.md`

---

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>N</spaarkeai>
  <ci-workflows>Y</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Placement justification** — all BFF additions extend existing route groups; no new deployable. Identity resolver extends `/api/documents` (existing Graph + Dataverse plumbing, latency-coupled to existing document routes). Profile trigger, Find and creation-completeness extend `/api/office`. Capability surfacing needs **no new BFF code**. Per `.claude/constraints/bff-extensions.md`, the ≤60 MB publish ceiling applies per task.

### New Components (§11 three-question gate)

| New component | Existing overlap | Can extend instead? | Cost-of-doing-nothing |
|---|---|---|---|
| `WordDocumentIdentityService` (BFF) | `getItemId()` in `WordAdapter.ts:78-106` — a title+author+timestamp hash | **No** — the existing impl is structurally incapable of identifying a record; there is nothing to extend | Version-save targets the wrong record or creates duplicates; profile, record card and open-record are all impossible |
| Shared server-side creation service (BFF) | `QuickCreateAsync` (minimal fields only) + `matterService.ts:254` (client-side number generation) | **Partly** — `QuickCreateAsync` is extended, but the numbering/owner/field-mapping logic must be lifted out of the client wizard into a service both can call | Records created from the pane arrive with `sprk_matternumber` empty — the exact UAT complaint |
| Find view + content-similarity endpoint | Outlook's `search` placeholder tab; `documentVector3072` "Find Similar" engine | **Extend the engine**, build the view | No way to find related precedent while drafting |
| Custom XML part stamper (server-side) | None | n/a — no existing stamp mechanism | A document that leaves Spaarke and returns cannot be re-identified; every round-trip creates a new record |

To Do (FR-14), Send Email (FR-15) and the ribbon commands (FR-17) are **adaptations of shipped endpoints/views**, not new components.

---

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-012** | Shared component library — UI belongs in `@spaarke/ui-components` | The add-in cannot consume `@spaarke/ui-components` wholesale: its components assume React 19 and some are `Xrm`-bound, neither of which holds in an Office webview. r2 already resolved this by **recreating** layouts. | **A — project-scoped exception** | Documented precedent (r2). The add-in keeps its own thin views under `shared/taskpane/`, consuming `@spaarke/auth` only. Importing Xrm-bound components would break at runtime (NFR-03). Re-evaluate if a PCF-safe subset of the library emerges. |
| **ADR-050** | Canonical modal shell — one `SprkModal`, no bespoke chrome | FR-10 opens records via the **Office Dialog API**, which is a host-owned window, not a Fluent `Dialog`. `SprkModal` cannot wrap it. | **C — comply in spirit** | ADR-050 governs Spaarke-rendered modals. The Office dialog is host chrome, outside its scope. **Any Spaarke UI rendered *inside* the dialog still follows ADR-050 and ADR-021.** |

> No other ADR tensions surfaced at design time. All other listed ADRs apply without exception.

---

## Success Criteria

1. [ ] A Spaarke-sourced document opened in Word desktop resolves to the correct `sprk_document` and matter — *Verify*: manual + integration test against a seeded document
2. [ ] A desktop-sourced document claims no identity and saves cleanly as new — *Verify*: integration test
3. [ ] A stamped document, downloaded and re-opened from disk, self-identifies — *Verify*: end-to-end test
4. [ ] Saving an identified document defaults to a **version**, not a duplicate row — *Verify*: integration test asserting one `sprk_document` row and an incremented SPE version
5. [ ] The "Save as new document" override creates a new record via the **link/graduate** path — *Verify*: integration test asserting `sprk_canonicaldocument` linkage
6. [ ] A filename collision surfaces the two-option dialog with the existing file's bytes untouched, using the **shipped** handling — *Verify*: integration test asserting no new collision logic was introduced (FR-12)
7. [ ] Profile fields display; Generate Profile completes successfully — *Verify*: manual + contract test
8. [ ] A Matter created from the pane has number + owner + mapped fields populated — *Verify*: integration test
9. [ ] Find returns content-similar results, permission-trimmed — *Verify*: negative test (a user denied access to a matter sees none of its documents)
10. [ ] A To Do created from Word carries document **and** record as regarding — *Verify*: integration test
11. [ ] Every shared capability works in both hosts or is capability-gated — *Verify*: parity checklist
12. [ ] `npm run typecheck` is clean — *Verify*: CI
13. [ ] Publish-size delta measured and within ceiling — *Verify*: per-task measurement

---

## Dependencies

### Prerequisites

- **#919 document profiling fix** — ✅ already on master (PR #923, `f5c7687d8`)
- **Content dedup layer** — ✅ already on master (`ContentDedupDetector`, `sprk_canonicalhash`, graduate-on-divergence)
- **NAA auth** — ✅ shipped, desktop and web
- **Upload-collision handling** — ✅ already on master (UAC-r2, 2026-09-02): `conflictBehavior`, server default `Fail`, typed `UploadNameConflictError`, two-option dialog
- **`sprk_searchindexed` / `sprk_searchindexedon`** — ✅ exist on `sprk_document`, written by `RagIndexingJobHandler`
- **Per-environment Entra SPA redirect registration** (NFR-09) — provisioning step, must precede deployment to each environment

### Cross-project coordination

| Project | Overlap | Action |
|---|---|---|
| `unified-access-control-r2` | **Task 094** — upload-collision pre-flight probe + "Use existing"; **task 095** — document-record multi-association (two many-to-one slots per type) | **Do not duplicate.** FR-12 consumes their shipped work; FR-09's record card must respect 095's two-slot model |
| `email-communication-intelligence-r2` | Shipped the add-in's current state and the content-dedup layer | Consume; both handoff docs are in this folder |
| `spaarkeai-word-native-r1` | Owns MCP + the declarative agent | FR-20 only *launches* their agent |

### Spikes (Phase 0 — gate downstream scope)

| # | Spike | Gates |
|---|---|---|
| **Spike-1** | `Office.context.document.url` shape for SPE files in **Word desktop** (documented for web only) | FR-01 — the keystone |
| **Spike-2** | Office Dialog API for record open: (a) can it host an MDA record form without framing refusal, or must we host a Spaarke code page? (b) auth context via `messageParent`? (c) does the form function at dialog size? (d) does a change propagate back to the pane? | FR-10, and FR-13's "finish editing in the record" premise |
| **Spike-3** | Is there any documented mechanism for a task pane to open the Copilot pane / a named agent? **Timeboxed** — a negative result closes FR-20 | FR-20 |

### External

- Word/Outlook add-in distribution via M365 Admin Center → Integrated Apps (manual, version bump required)

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Find scope | Similar to what — open document content, a query box, or context-seeded? | **True content similarity** | FR-16 reuses the `documentVector3072` engine |
| Find gating | What if the document isn't indexed? | **Require indexing first.** Gate on `sprk_searchindexed`; if no/null show "Save the document in order to index for AI similarity search" + a **Run Index** button that indexes and flips the field | FR-16 three-state design. **Removes the on-demand-embedding slow path entirely** — FR-16 returns to MED complexity |
| Version save | Forced version-only, or offered? | **Default version, allow override** | FR-11 keeps a "Save as new document" path, so the override MUST route through link/graduate (NFR-08) |
| Document stamp | Is writing a Spaarke GUID into the user's document acceptable? | **Yes, stamp on Spaarke saves** | FR-02. Spec refines this to **server-side stamping into the uploaded bytes** — see Assumptions |
| Create types | Which entity types can the pane create? | **Matter + Project only** | FR-13 scope; three fewer sets of required-field rules |
| Event vs To Do | Both in r1? | **To Do only; defer Event** | FR-14; Event in design.md §4.2 |
| Record open | Dialog API, browser tab, or read-only pane? | **Dialog API (a) preferred — investigate first** | Spike-2; (c) is the documented fallback |
| Ribbon commands | Wire `quickSave`/`shareDocument`? | **Include** | FR-17 |
| Typecheck debt | Clean in this project? | **Yes** | FR-18, sequenced first |
| Wizard migration | Should the server-side service replace client-side wizard creation? | **Evaluate after r1** | Out of scope; `design.md` §7.1 |

---

## Assumptions

Proceeding with these where the owner did not specify:

- **FR-02 stamping is server-side, into the uploaded bytes** — not client-side into the open document. Client-side stamping would dirty the user's document and prompt an unexpected save. Server-side stamping still achieves the goal (anyone downloading from Spaarke gets a stamped copy) without touching the live document.
  ⚠️ **Residual risk accepted by the owner**: a Spaarke GUID travels inside documents sent to opposing counsel. If that becomes a concern, mitigations are (a) document it in customer-facing material, or (b) add a strip-on-export option. Not built in r1.
- **Profile fields are read-only in the pane** (FR-07). Editing happens in the record via FR-10.
- **Generate Profile overwrites** the existing profile (matching `refresh-profile` semantics), with no confirmation prompt.
- **Send Email includes both** a document link and a related-record link where both exist, using existing share-link endpoints and honoring their expiry bounds.
- **Outlook parity covers** the tab shell, Find, profile display, record card, To Do, Send Email and creation-completeness. Word-only: document identity, `.docx` save, version-save. Outlook-only: email/attachment save, triage, linked-todos.
- **FR-12 is fixed at the shared client upload path**, benefiting every client caller, not patched only in the add-in.
- **`share`/`recent` tabs stay hidden** — the `NavigationTab` union retains them, but no views are built in r1.

---

## Unresolved Questions

- [ ] **Spike-1 outcome** — if `document.url` is unusable on Word desktop for SPE files, FR-01's primary path fails and FR-02's stamp becomes the *only* identity mechanism, which would not work for documents saved before this release. *Blocks*: FR-01, and by extension FR-07/09/10/11.
- [ ] **Spike-2 outcome** — if the Office Dialog API cannot host a usable record editor, FR-13's "finish editing in the record" premise weakens and the "+More fields" request (deferred) may need reopening. *Blocks*: FR-10; weakens FR-13.
- [ ] **Backfill for FR-02** — should existing `sprk_document` rows be retroactively stamped, or does the stamp apply only to documents saved after this release? Retroactive stamping means rewriting stored bytes for every existing document. *Blocks*: FR-02 scope sizing.
- [ ] **Does a manual "Run Index" trigger already exist**, or is a new `/api/office` endpoint needed? The pipeline (`FileIndexingService` → `IPostUploadIndexingEnqueuer` → `RagIndexingJobHandler`) is invoked post-upload; whether a user-initiated re-index route exists is unconfirmed. *Blocks*: FR-16 Run Index sizing (reuse vs. new endpoint).

---

*AI-optimized specification. Original design: `design.md`*
