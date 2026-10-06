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
5. **FR-05 — Word unified JSON manifest.** Migrate `word-manifest.xml` to the unified JSON manifest, following `outlook/manifest.json` as the precedent. — *Acceptance*: Word add-in installs and runs from the unified manifest on desktop and web. **Status 2026-09-30 (task 078)**: delivered as ONE unified app package for Outlook AND Word (schema 1.30; `packaging/mergeUnifiedManifest.js`; CI artifact `spaarke-addin-unified-package`) — the owner chose the long-term path over amending this FR. Schema-valid and CI-built; the OBSERVED install on desktop and web is the owner's rollout step (`notes/078-manifest-decision.md` §6). Both XML add-ins stay deployed as fallbacks (Outlook on Mac; Word < 2501). Note: task 011's 2026-09-18 "close" was an XML re-upload — this acceptance was never met before task 078.

**Save flow**

6. **FR-06 — Filename defaults to Document Name.** The Document Name field defaults to the filename and is editable in-pane via a pencil affordance. — *Acceptance*: default matches the filename; edits persist to `sprk_documentname`; the field can be removed from the Dataverse form without loss.
7. **FR-07 — Profile section.** Rename "Description" to "Profile" and populate `sprk_filesummary`, `sprk_filetldr`, `sprk_filekeywords`, `sprk_documenttype` from the record. — *Acceptance*: for an identified document with a completed profile, all four fields display; for `sprk_filesummarystatus` in a non-complete state, the pane shows that state rather than blank fields.
8. **FR-08 — Generate Profile.** A button re-runs document profiling for the current document. — *Acceptance*: clicking it re-dispatches profiling and the pane reflects the updated status; profiling completes successfully (#919 fixed on master).
9. **FR-09 — Related-to record card.** When the document is identified and associated, show a card for the related record. — *Acceptance*: card shows record type, name and number; clicking opens the record (FR-10).
10. **FR-10 — Open record / open Document record.** *(Amended 2026-10-02 by task 079, owner sign-off: "yes open Spaarke". The original "preferred mechanism: Office Dialog API" was not shipped — Spike-2 found Dynamics forms never message back to a dialog and their usability at dialog size unverified.)* The pane shows the related record read-only (FR-09's card) and opens the record, or the `sprk_document`, **in Spaarke in a browser tab** via `Office.context.ui.openBrowserWindow` (Spike-2 Option 3, task 027). That call always opens a normal browser tab or window — Office offers no address-bar-free option; the only chrome-free window is the dialog this FR moved away from. The record opens as a **focused record page**: the Spaarke app's navigation is hidden (`navbar=off`) and its command bar is kept so Save and the record's actions stay available (task 086). — *Acceptance*: the record opens in Spaarke and is usable; when the user returns to the pane (window focus or visibility) it re-reads and shows their edits.
11. **FR-11 — Version-save with override.** When FR-01 resolves an existing `sprk_document`, **default to saving a new version** of that record via `SaveRequest.ExistingDocumentId`. Offer an explicit **"Save as new document"** override. — *Acceptance*: default path creates a version, not a second row; the override path creates a new document and is routed through the editable **link/graduate** dedup mode.
12. **FR-12 — A name collision on the add-in's create save never overwrites; it is surfaced before anything is recorded.** *(Amended 2026-10-02 by task 079, owner sign-off the same day; the original "consume the shipped handling, do not rebuild" text was falsified by Spike-4 — see the FR-12 row in ADR Tensions.)* The add-in saves through `POST /api/office/save`, which uploads app-only, by file name, into the destination record's container. It does **not** use the client/OBO upload path that `unified-access-control-r2` protected on 2026-09-02, so that fix could not be consumed as shipped (Spike-4, `notes/spikes/spike-4-collision-path.md`). This path **reuses its mechanism**: (a) create uploads state `ConflictBehavior.Fail` through the existing explicit `SpeFileStore` overload and its 409 translation — Document saves since task 025; typed-name Email/Attachment saves since task 054, where a byte-identical file is still treated as the same capture — so a colliding upload moves no bytes; (b) the server answers **409 `OFFICE_020`** before any `sprk_document` row is created, updated or linked; (c) the pane offers **Keep both** (the server stores the file under a non-colliding name) or **Save as new version** (FR-11's version save into the colliding document, never a second create). Save as new version is offered, and the colliding document named, only when the caller can read it **and** it is filed to the same record as the save (task 055, #1005); otherwise only Keep both. (d) A name orphaned by a create that died before its row is reclaimed rather than refusing retries forever. A version save (FR-11) never takes this path. The pre-flight probe and "Use existing" remain **UAC-r2 task 094's**. — *Acceptance*: a create save whose file name collides returns `OFFICE_020` with the existing file's bytes and version unchanged and no row written; Keep both creates exactly one new file and one new row; Save as new version writes a version, never a second row — asserted by `tests/integration/data-mutation/OfficeVersionSave/OfficeCreateCollisionTests.cs`; the pane's choice, its dismissal and the naming rules by `shared/taskpane/components/__tests__/SaveFlowCollision.test.tsx`.

**Surfacing Spaarke**

13. **FR-13 — Record-creation completeness (server-side).** Move number generation, owner assignment and Field Mapping Framework population into a **shared server-side creation service** called by `QuickCreateAsync`. Scope: **Matter and Project only**. — *Acceptance*: a Matter created from the pane has `sprk_matternumber` and owner populated plus mapped fields applied; same for Project and `sprk_projectnumber`. Existing `Create*Wizard` components are **not** modified.
    **Number format — owner decision 2026-10-02 (interim, task 076):** Matters get **`MAT-######`** and Projects **`PRJ-######`**, both **sequential**. A numbering-schema component will replace this later; it is not part of this project. Uniqueness of existing production numbers is checked manually by the owner. (Both columns are their table's primary name, so a missing number is a blank name — the defect 076 fixes.)
    **Mechanism — owner decisions 2026-10-02 (task 076), INTERIM:** *"the dataverse auto numbering is just an interim solution until we build the numbering function."* Until that function exists, the number is assigned by **Dataverse's platform autonumber** on each column (`MAT-{SEQNUM:6}` / `PRJ-{SEQNUM:6}`), set up in each environment by `scripts/Set-RecordNumberingSchema.ps1` (the seed is per environment and not carried by a solution import; existing blank records are numbered on the first run; alternate keys `sprk_MatterNumber` / `sprk_ProjectNumber`). So "move number generation into the creation service" is met by the platform rather than by service code: the creation service leaves the number unset so the platform assigns it, retries a create the key refuses, and warns when no number came back. Because the platform assigns it, every create that does not supply a number gets one — not only the pane — while a supplied number (the Create Matter wizard's `{typeCode}-{random6}`) is kept, so the wizards stay unmodified. Retiring it when the numbering function ships: clear the two formats; keep the keys.
14. **FR-14 — Add To Do.** Create a first-class `sprk_todo` from the pane with **both** the document and the related record as regarding, reusing `POST /api/office/todo` and `CreateTodoView`. — *Acceptance*: the To Do is created with correct regarding for both Word (document + record) and Outlook (communication + record).
15. **FR-15 — Send Email.** *(Amended 2026-10-02 by task 079, owner sign-off: option A plus option B, the user choosing. Supersedes the 2026-09-15 "hidden in Word" decision.)* Open a new email pre-populated with links to the document and/or related record, honoring existing share-link expiry bounds.
    - **Outlook** (shipped, task 036): Outlook's own compose window (`displayNewMessageForm`).
    - **Word** (task 086): the user chooses between **(A) Spaarke email** — Spaarke's composer (the Communication page in compose mode) in a browser tab, pre-filled with the subject and links and associated to the related record, so the sent email is recorded in Spaarke — and **(B) Outlook on the web** — a compose deep link in a browser tab, sent from the user's own mailbox and not recorded in Spaarke. Both are gated by the `canOpenBrowserWindow` capability, never by a host-type branch.
    — *Acceptance*: Outlook's compose window opens with working links (unchanged); in Word both choices are offered and each opens with working links; the Spaarke choice arrives associated to the related record.
16. **FR-16 — Find: similar documents and related records, gated on index state.** *(Amended 2026-10-02 by task 079 to describe what exists and how it works — owner sign-off the same day. The original first sentence said documents **and records** come from the content-similarity engine; only documents do.)* For the open document — in Outlook, the email being read, once saved — Find shows two lists, composed in the pane because no single endpoint returns both (discovery F-c):
    - **Similar documents — true content similarity.** The existing `documentVector3072` cosine-KNN "Find Similar" engine, `GET /api/ai/visualization/related/{documentId}`: at most 50, ranked, each row permission-trimmed (task 032).
    - **Related records (Matters, Projects, …) — matched by topic, not by content similarity.** `POST /api/ai/search/records`, seeded with the document's AI-profile **keywords** (falling back to its TL;DR, then its summary), each row permission-trimmed (`AuthorizeRowsAsync`). With no completed profile there is no seed and the pane says so; no query is ever invented from the title. Record-to-content vector similarity does not exist in the index and is **not** built (task 077).

    **Similarity requires the document to be indexed — there is no on-demand embedding path.** The tab branches on state:

    | State | Pane shows |
    |---|---|
    | No `sprk_document` (unsaved / external) | *"Save this document to Spaarke so it can be indexed for AI similarity search."* + a link to the Save tab |
    | `sprk_document` exists, `sprk_searchindexed` = **no or null** | *"This document isn't indexed yet."* + a **Run Index** button |
    | `sprk_searchindexed` = **yes** | Similarity results |

    In Outlook the noun is *email*, decided by the `canGetSender` capability, not the host type. A document or email saved in this pane session is found at once: the save's id is used when identity resolution finds no record — never to override a conflict, a denial or an error (task 077).

    **Run Index** (`POST /api/ai/rag/send-to-index`) submits the file to the indexing pipeline and, on success, `sprk_searchindexed` flips to yes (with `sprk_searchindexedon` stamped) — written by `RagIndexingJobHandler`, not by the add-in. ⚠️ Per [`.claude/patterns/ai/indexing-pipeline.md`](.claude/patterns/ai/indexing-pipeline.md), the call **MUST pass `documentId`** (the `sprk_document` GUID) or chunks land as orphans and the tracking fields cannot be written. — *Acceptance*: each of the three states renders correctly; Run Index flips the field and results appear afterwards; both lists are permission-trimmed per row (NFR-02) and lazy-scrolled (NFR-05; the records list pages under the ADR-051 exception in ADR Tensions).
17. **FR-17 — Word ribbon commands.** Wire the stubbed `quickSave` and `shareDocument` commands. — *Acceptance*: both execute from the ribbon without opening the pane where that is the intended behavior.
18. **FR-18 — Typecheck debt.** Clear the ~397 pre-existing `exactOptionalPropertyTypes` errors in `src/client/office-addins`. **Do this first**, so new errors are visible during feature work. — *Acceptance* (**amended 2026-09-19, task 056 / [#996](https://github.com/spaarke-dev/spaarke/issues/996)**): `npm run typecheck` reports **0 production errors**; the ~111 remaining errors are test-file only (`__tests__/`, `*.test.*`, `__mocks__`) and are the owner-approved 2026-09-09 accept, narrowed 290 → 111 on 2026-09-12. CI **reports** the **production** count via the `typecheck` job in `.github/workflows/office-addins-tests.yml`: the job fails its own run on any production error, but it is not a required check, so it **does not block a merge** (amended 2026-10-02 by task 079, owner sign-off — it previously said "CI gates"). Promoting it to a blocking gate is [#996](https://github.com/spaarke-dev/spaarke/issues/996); it is not a settings toggle, because the workflow runs only when add-in files change and a required check that never runs leaves other PRs waiting forever. ⚠️ The original wording — "is clean; CI gates it going forward" — asserted a property that was never true and never built; it is superseded here rather than left to read as satisfied.
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
- **NFR-08 — Editable dedup mode.** For editable documents, a content-hash hit MUST use **link/graduate** (mirroring `ComposeService.PromoteIfEphemeralAsync`), never the immutable suppress path. Suppress-forever on an editable document collapses two distinct drafts into one record — data loss. **Amended 2026-09-17 (owner decision; ADR Tensions row "NFR-08 / content identity"):** the binding half is the prohibition — a create MUST still always create, never suppress. The *link* half becomes **best-effort**: once FR-02 stamping ships, two byte-identical Word-pane saves to different records embed different ids, so their stored bytes never hash-equal and no `sprk_canonicaldocument` link is produced for that path. A lost link costs a notification; it never costs a record.
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
| **ADR-012** | Shared component library — UI belongs in `@spaarke/ui-components` | ~~The add-in cannot consume `@spaarke/ui-components` wholesale: its components assume React 19 and some are `Xrm`-bound.~~ **Amended 2026-10-05 (owner: "we should use shared library wherever possible (if technically possible)")**: the React reason expired (the add-in is on React 19). What remains true is narrow: the library's **barrel** (`@spaarke/ui-components` root) re-exports `Xrm`-bound components that fail in an Office webview. | **A — narrowed to the barrel only** | **Default is reuse.** The add-in consumes shared components by **exact-path alias** to the component source (never the barrel) — the mechanism task 096 proved for `SendEmailPane` (`webpack.config.js` / `jest.config.js` `$` aliases). A shared component that needs `Xrm` or a model-driven host is made host-agnostic (injected callbacks) rather than copied; until then it is out of reach, and the gap is recorded in project `CLAUDE.md`. New add-in UI must check the library first (CLAUDE.md §11). |
| **ADR-050** | Canonical modal shell — one `SprkModal`, no bespoke chrome | ~~FR-10 opens records via the **Office Dialog API**, which is a host-owned window, not a Fluent `Dialog`. `SprkModal` cannot wrap it.~~ **Updated 2026-10-02 (task 079, owner sign-off):** what shipped opens records **in a browser tab** (`Office.context.ui.openBrowserWindow`, Spike-2 Option 3) — no modal of any kind is involved. | **C — comply** | ADR-050 governs Spaarke-rendered modals; a browser tab is not one, so there is no tension left. Kept as a record of the design-time concern. |
| **ADR-044** | Use the canonical `cleanGuid` from `@spaarke/ui-components` | ~~This package cannot import `@spaarke/ui-components` (the ADR-012 row above).~~ **Superseded 2026-10-05** by the amended ADR-012 row: `cleanGuid` is host-agnostic, so the add-in can consume the canonical one by exact-path alias. | **C — comply (scheduled)** | The local `shared/taskpane/utils/cleanGuid.ts` is replaced by the shared one in UAT round 5 work (task 099); its gated test is kept, pointed at the shared implementation. Until that lands, the 2026-09-11 local copy stands. |
| **ADR-038 §2** | Only the listed KEEP test paths are protected at `/test-diet` | Every KEEP path is a repo-root .NET path; no category covers front-end jest, so a path-only check would flag every office-addins suite — including the `.docx` save regression guard — as scaffolding to delete. | **A — project-scoped exception** | `src/client/office-addins/**/__tests__/**` is classified by content, not path, at project close (2026-09-09, task 010). The structural fix is a Path B amendment adding a front-end KEEP category, to be filed separately. |
| **ADR-051** | Lists fetch progressively as the user scrolls; no pager | The Find tab's only source, `GET /api/ai/visualization/related/{documentId}`, returns one bounded, per-row-authorized response (at most 50) and has no paging. Adding paging to that security-hardened route would re-run the per-row authorization for every page. | **A — project-scoped exception, Find list only** | Owner decision 2026-09-15 (task 034). The list is a ranked "top matches" answer: rows already in hand are revealed as the user scrolls, `hasMore` never implies more pages, there is no pager or "Load more", and the `PARTIAL_RESULTS` warning discloses any trimming. Evidence: `notes/034-route-paging-escalation.md`. |
| **ADR-024** | A To Do has one "regarding" lookup | FR-14 requires a To Do created from the pane to link both the record (Matter / Project / Invoice) and the document or communication it came from. | **A — project-scoped exception, pane To Dos only** | Owner decision 2026-09-15 (task 035). The record lookup is the primary "regarding"; the document or communication is a second, source lookup. The single-slot resolver fields (`sprk_regardingrecordid` / `-name` / `-type`) always describe the record, so there is still one consistent "what is this about" answer. Evidence: `notes/035-todo-regarding-decision.md`. |
| **NFR-08 / content identity** (spec-level, recorded here for discoverability) | "A content-hash hit MUST use link/graduate" — read as requiring the `sprk_canonicaldocument` link on every editable dedup hit, incl. the FR-11 "Save as new document" override (SC-5) | FR-02 stamping writes each record's own id into the stored bytes, so two byte-identical Word-pane saves to different records can never hash-equal. The link half of link/graduate becomes unreachable for that path, and **SC-3 (stamp) and SC-5 (link on override) contradict each other in production**. Also: a pre-release hash-linked copy graduates on its first stamped save even if its content did not change. | **A — project-scoped exception** | Owner decision 2026-09-17 (build the invisible marker + the collision prompt; hash stays supporting). The *data-safety* half of NFR-08 is untouched and still binding — a create always creates, suppress is never used for editable documents. Only the *link* becomes best-effort, costing a notification rather than a record. The alternative (option B in the design note: re-hashing with the stamp removed) replaces the dedup hash source on a column shared with `email-communication-intelligence-r2` and Compose — cross-project, and out of proportion to what a link is worth. Evidence: `notes/014-xml-part-stamp-decisions.md` §6c, §7. |
| **ADR-051** (records half, task 077) | *Full page ⇒ more; short or empty page ⇒ end* (the page-fullness `hasMore` rule for a custom scroller) | Find's records come from `POST /api/ai/search/records`, whose `AuthorizeRowsAsync` REMOVES rows the caller cannot read. A page of 25 hits with 3 unreadable arrives as 22, which the literal rule reads as the end — reintroducing the "shows only the first page" failure ADR-051 exists to prevent. | **A — project-scoped exception, APPROVED by owner 2026-09-30** | Records page on *non-empty ⇒ more, empty ⇒ end*, advancing `offset` by the REQUESTED window (never the surviving count). Cost: one extra request at the true end. Accepted limitation: a page on which every row is unreadable also arrives empty and stops paging — errs toward showing too little, never toward showing what the caller cannot read. Both rules are seeded (`useFindRecordMatches.test.tsx`), so a change in either direction fails CI. **Path B worth considering repo-wide**: amend ADR-051 to "page fullness, unless the source trims rows server-side" — every per-row-authorized list will meet this. Detail: `notes/077-find-gaps.md` § ADR-051. |
| **ADR-002 write path (gap G5) / task 076's container rule** | Task 076 removed the acting-user business-unit fallback for SPE containers: with no resolvable record, REFUSE rather than guess | Record OWNERSHIP (write-path invariant I-6, task 080) keeps an acting-user fallback: a create filed against NOTHING is owned by the caller's business-unit default owner team. Refusing would block every legitimately unfiled save — the Word ribbon quick-save and every pane save with no "Related to" (~80 of ~85 corpus save bodies), a required use case (owner, 065) | **A — project-scoped divergence (owner decision 2026-09-25)** | The two failures cost differently: a wrong CONTAINER puts bytes somewhere SPE cannot un-share, so 076 is right to refuse; an unfiled record owned by its creator's own unit is the correct answer for a record that belongs to nobody else yet. The secure-record risk behind 076's rule does not arise: the fallback is reached ONLY when no target was named — a named-but-unresolvable target REFUSES (`RecordOwnershipResolver`, pinned by `RecordOwnershipResolverTests`). Recorded 2026-09-30 by task 080's ADR check, which found the divergence declared in code but not here. |
| **FR-12 / CLAUDE.md §11 (reuse, do not rebuild)** (spec-level, recorded here for discoverability) | FR-12 as written: *"Consume the shipped collision handling … Do not rebuild any of this … no new collision logic is introduced"* | Spike-4 (task 005, 2026-09-08) found the add-in saves through `POST /api/office/save` — app-only, by name — not the client/OBO upload path UAC-r2 protected, so there was nothing to consume: the add-in path had **no** collision protection, and a pane "new document" override could overwrite another document's file. | **A — project-scoped exception, owner decision 2026-09-17 ("BUILD IT")** | The shipped *mechanism* is reused, not rebuilt: the existing explicit `ConflictBehavior.Fail` overload and its 409 translation. What is new on this path is only what the pane needs to surface it: the typed `OFFICE_020`, the two-option choice (Keep both via `AllowRename`; Save as new version via FR-11's existing version save), the same-record and read-access guards (task 055, #1005) and orphaned-name reclaim. No second conflict detector or pre-flight probe; the probe and "Use existing" remain UAC-r2 task 094's. Evidence: `notes/spikes/spike-4-collision-path.md`, `notes/025-residual-collision-surface.md`, TASK-INDEX 025. **Recorded 2026-10-02 by task 079** — the deviation had been claimed since 09-17 with no row here. |

| **ADR-002 WP-1** (task 076, write-path invariant I-11) | *"Every invariant has exactly one owner: a server-side component in the BFF write path"*; declarative column mechanisms are *"not a substitute for WP-1 owners"* | I-11 (a Matter/Project always has a number — its primary name) is owned by **Dataverse's platform autonumber**, not BFF code. The platform fires inside EVERY create (pane, wizards, MDA forms, AI tool, import), so it also covers WP-5 without a fix-up job; a BFF owner would number the pane only. Raised by task 076's Step 9.5 adr-check and code review | **A — project-scoped exception, interim** (owner 2026-10-03, "A now, B as its own task") → **B DONE 2026-10-03 (task 087)**: ADR-002 WP-1 amended (owner-approved wording, business rules excluded); the exception is closed | The owner chose autonumber as the interim mechanism (2026-10-02). `RecordCreationService` keeps the BFF's half (never sends the number; retries a key-refused create; warns when none came back), and `scripts/Set-RecordNumberingSchema.ps1 -Verify` checks the platform half per environment. Task 087 amends ADR-002 so a platform-native declarative mechanism that fires on every write path and runs no Spaarke code may own an invariant — autonumber columns are already used across the schema |
> The ADR-012 and ADR-050 rows were identified at design time (ADR-012 amended 2026-10-05 by the owner: reuse by default); the other rows were raised and resolved during execution (each is also in project `CLAUDE.md` Decisions). All other listed ADRs apply without exception.

---

## Success Criteria

1. [ ] A Spaarke-sourced document opened in Word desktop resolves to the correct `sprk_document` and matter — *Verify*: manual + integration test against a seeded document
2. [ ] A desktop-sourced document claims no identity and saves cleanly as new — *Verify*: integration test
3. [ ] A stamped document, downloaded and re-opened from disk, self-identifies — *Verify*: end-to-end test
4. [ ] Saving an identified document defaults to a **version**, not a duplicate row — *Verify*: integration test asserting one `sprk_document` row and an incremented SPE version
5. [ ] The "Save as new document" override **always creates its own record and is never suppressed** — *Verify*: integration test asserting a new `sprk_document` row exists and the original's bytes are untouched. **Amended 2026-09-17** (owner decision; see the NFR-08 ADR Tensions row): the `sprk_canonicaldocument` **link** assertion applies only to unstamped save paths. For a Word-pane save the stamp makes the stored bytes differ by construction, so no link is produced — that is the accepted, documented consequence of shipping FR-02, not a regression. The original wording ("creates a new record via the link/graduate path", verified by asserting linkage) would have been untrue in production the day stamping shipped.
6. [ ] A create save whose file name collides returns `OFFICE_020` with the existing file's bytes and version untouched and no row written, and the pane offers Keep both / Save as new version (FR-12) — *Verify*: `OfficeCreateCollisionTests` (integration, CI-run) + `SaveFlowCollision.test.tsx` (pane). **Amended 2026-10-02** (task 079, owner sign-off): the original wording — "using the **shipped** handling … integration test asserting no new collision logic was introduced" — named a test that does not exist and a property that is false; FR-12's handling was built on the Office save path by owner decision (ADR Tensions, FR-12 row).
7. [ ] Profile fields display; Generate Profile completes successfully — *Verify*: manual + contract test
8. [ ] A Matter created from the pane has number + owner + mapped fields populated — *Verify*: integration test
9. [ ] Find returns content-similar results, permission-trimmed — *Verify*: negative test (a user denied access to a matter sees none of its documents)
10. [ ] A To Do created from Word carries document **and** record as regarding — *Verify*: integration test
11. [ ] Every shared capability works in both hosts or is capability-gated — *Verify*: parity checklist
12. [ ] `npm run typecheck` reports **0 production errors** (test-file errors are the accepted 2026-09-09 baseline) — *Verify*: CI **reports** it, via the `typecheck` job in `office-addins-tests.yml` — a reporting check, not a merge gate (amended 2026-10-02, task 079; promotion is #996) (added by task 056 / [#996](https://github.com/spaarke-dev/spaarke/issues/996); measured at `23fd17991`: 111 total, 0 production, 9 files)
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
| Find scope | Similar to what — open document content, a query box, or context-seeded? | **True content similarity** | FR-16 reuses the `documentVector3072` engine — for **documents**. Records are matched by the document's AI-profile topic (no record-to-content vector index exists); FR-16 amended 2026-10-02 to say so |
| Find gating | What if the document isn't indexed? | **Require indexing first.** Gate on `sprk_searchindexed`; if no/null show "Save the document in order to index for AI similarity search" + a **Run Index** button that indexes and flips the field | FR-16 three-state design. **Removes the on-demand-embedding slow path entirely** — FR-16 returns to MED complexity |
| Version save | Forced version-only, or offered? | **Default version, allow override** | FR-11 keeps a "Save as new document" path, so the override MUST route through link/graduate (NFR-08) |
| Document stamp | Is writing a Spaarke GUID into the user's document acceptable? | **Yes, stamp on Spaarke saves** | FR-02. Spec refines this to **server-side stamping into the uploaded bytes** — see Assumptions |
| Create types | Which entity types can the pane create? | **Matter + Project only** | FR-13 scope; three fewer sets of required-field rules |
| Event vs To Do | Both in r1? | **To Do only; defer Event** | FR-14; Event in design.md §4.2 |
| Record open | Dialog API, browser tab, or read-only pane? | **Dialog API (a) preferred — investigate first** → **resolved 2026-10-02: open in Spaarke in a browser tab** (owner) | Spike-2 chose Option 3 (read-only card + browser tab); FR-10 amended |
| Ribbon commands | Wire `quickSave`/`shareDocument`? | **Include** | FR-17 |
| Typecheck debt | Clean in this project? | **Production yes (0); test-file no (~111, accepted)** | FR-18, sequenced first. Amended 2026-09-19 by task 056 — the 290 → 111 narrowing was owner-approved 2026-09-12, and CI now **reports** the production count — it does not block a merge (amended 2026-10-02; promotion is [#996](https://github.com/spaarke-dev/spaarke/issues/996)) |
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
- ~~**FR-12 is fixed at the shared client upload path**, benefiting every client caller, not patched only in the add-in.~~ **False — corrected 2026-10-02 (task 079).** The add-in never used that path (Spike-4). FR-12 is fixed at the shared **Office save path** (`OfficeService` → `OfficeStorageUploader`), for both hosts, by reusing the shipped `Fail` overload.
- **`share`/`recent` tabs stay hidden** — the `NavigationTab` union retains them, but no views are built in r1.

---

## Unresolved Questions

- [ ] **Spike-1 outcome** — if `document.url` is unusable on Word desktop for SPE files, FR-01's primary path fails and FR-02's stamp becomes the *only* identity mechanism, which would not work for documents saved before this release. *Blocks*: FR-01, and by extension FR-07/09/10/11.
- [x] **RESOLVED (Spike-2, task 003; FR-10 amended 2026-10-02): records open in Spaarke in a browser tab**, not the Office Dialog API. Original question: **Spike-2 outcome** — if the Office Dialog API cannot host a usable record editor, FR-13's "finish editing in the record" premise weakens and the "+More fields" request (deferred) may need reopening. *Blocks*: FR-10; weakens FR-13.
- [ ] **Backfill for FR-02** — should existing `sprk_document` rows be retroactively stamped, or does the stamp apply only to documents saved after this release? Retroactive stamping means rewriting stored bytes for every existing document. *Blocks*: FR-02 scope sizing.
- [x] **RESOLVED (planning discovery, recorded 2026-10-02 by task 079): it exists** — `POST /api/ai/rag/send-to-index`, which the Find tab's Run Index calls; no new endpoint was added. Original question: **Does a manual "Run Index" trigger already exist**, or is a new `/api/office` endpoint needed? The pipeline (`FileIndexingService` → `IPostUploadIndexingEnqueuer` → `RagIndexingJobHandler`) is invoked post-upload; whether a user-initiated re-index route exists is unconfirmed. *Blocks*: FR-16 Run Index sizing (reuse vs. new endpoint).

---

*AI-optimized specification. Original design: `design.md`*
