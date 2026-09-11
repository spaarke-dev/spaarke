# Design — Spaarke Office Add-in (Word + Outlook) — r1

> **Status**: DRAFT v1 — 2026-09-04
> **Split from**: `spaarkeai-word-native-r1` (which retains the open-platform/MCP + Spaarke-native-AI strategy)
> **Next step**: owner review → `/design-to-spec`

### Locked decisions (owner)

| # | Decision |
|---|---|
| L-1 | **Not competing with legal drafting tools.** No Harvey/Legora/Microsoft-AI parity ambitions in this project. |
| L-2 | **Users open documents from any source** — desktop, OneDrive, DMS, Harvey, Claude. No Spaarke connection required to edit. |
| L-3 | **Focus**: save the document to Spaarke, and surface Spaarke information inside Office to make drafting more useful. |
| L-4 | **No MCP.** External-tool interop belongs to `spaarkeai-word-native-r1`. |
| L-5 | **Not extending Spaarke AI capability** — only surfacing what already exists. |
| L-6 | **Outlook is in scope alongside Word.** Parity where it makes sense; the shared task pane is the delivery vehicle. |

---

## 0. Thesis

The Spaarke Office add-in already saves a document to Spaarke. This project makes that save *good*, and turns the pane into a small, useful window onto Spaarke while the user drafts — **whatever tool they are drafting with**.

The user opens a document from anywhere and edits it however they like. Spaarke does not participate in the drafting. Spaarke participates at the moments that matter: **filing the work product**, and **showing the matter context that makes filing and drafting sensible** — the related record, the AI profile, similar documents, a to-do, an email.

This is a UX and productivity project, not an AI project.

---

## 1. Read these first

Two canonical docs landed 2026-09-04 (PR #942) and are the authoritative as-built map. Start there, do not re-derive from code:

| Doc | Gives you |
|---|---|
| [`docs/architecture/office-outlook-teams-integration-architecture.md`](docs/architecture/office-outlook-teams-integration-architecture.md) | As-built architecture: NAA auth model, tabbed task-pane shell, full `/api/office/*` route table, manifest rules, deploy, constraints + pitfalls |
| [`src/client/office-addins/CLAUDE.md`](src/client/office-addins/CLAUDE.md) | Module entry-point map + six load-bearing facts + build/typecheck/deploy |

Plus two handoffs in this folder from `email-communication-intelligence-r2`, which did the most recent add-in and dedup work:

- [`ADDIN-CONTEXT-FROM-EMAIL-R2.md`](ADDIN-CONTEXT-FROM-EMAIL-R2.md) — add-in as-built state
- [`DEDUP-AND-SAVE-BACK-IDENTITY.md`](DEDUP-AND-SAVE-BACK-IDENTITY.md) — the shipped dedup layers F-1/S-6 ride on. Canonical reference: [`content-identity-and-deduplication-architecture.md`](docs/architecture/content-identity-and-deduplication-architecture.md).

---

## 2. Current state (verified 2026-09-04)

| Fact | Detail |
|---|---|
| Manifest versions | **Word XML `1.0.6.0`** · **Outlook XML `1.0.22.0`** · **Outlook unified `manifest.json` `1.0.22`** |
| Unified manifest | **Outlook already has one** — a working precedent for migrating Word |
| Word save | Real `.docx` bytes via `getFileAsync(Office.FileType.Compressed)` (r2 upgraded this from a text approximation) |
| Auth | NAA via `@spaarke/auth` `OfficeNaaStrategy`, **desktop and web**. Broker redirect is portable (`brk-multihub://${window.location.hostname}`). ⚠️ **Each environment must register two SPA redirect URIs** on the Entra app — see [`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) §7.3 |
| Tab navigation | `NavigationTab = 'save' \| 'createTodo' \| 'share' \| 'recent' \| 'search'` exists in [TaskPaneNavigation.tsx](src/client/office-addins/shared/taskpane/components/TaskPaneNavigation.tsx); icons for share/recent/search commented out as *"V1: Disabled — uncomment for future releases"* |
| ⚠️ Those tabs are **stubs** | Share / Search / Recent handlers in `App.tsx` are placeholders. The *navigation* exists; the *views* do not. |
| To Do | `POST /api/office/todo` → `OfficeService.CreateTodoAsync` creates a first-class `sprk_todo` with regarding wired to the filed record. **Working reference for any pane→BFF record operation.** |
| Quick create | `POST /api/office/quickcreate/{entityType}` — Matter/Project/Invoice/Account/Contact, **minimal fields only** (`Name`, `Description`, `ClientId`, `Industry`) |
| Document profiling | ✅ **Working.** #919 fixed in PR #923 (`f5c7687d8`, on master) — renderer recursion fix **and** app-only profiling converged onto the direct-Action ADR-043 spine. |
| Word ribbon commands | `quickSave` / `shareDocument` still stubs ([word/commands/index.ts:28-61](src/client/office-addins/word/commands/index.ts#L28-L61)) |
| ⚠️ Two Word adapters | The **untested** [WordHostAdapter.ts](src/client/office-addins/word/WordHostAdapter.ts) is instantiated directly at `word/taskpane/index.tsx:126`, bypassing `HostAdapterFactory` and the tested [WordAdapter.ts](src/client/office-addins/shared/adapters/WordAdapter.ts). **r2 added +81 lines to the bypassed one.** |
| ⚠️ Document identity | `getItemId()` is a synthetic hash of title + author + timestamp. **The add-in cannot tell which `sprk_document` is open.** |
| ⚠️ Typecheck noise | ~397 pre-existing `exactOptionalPropertyTypes` errors. Build is clean. Filter to files you touch. |

---

## 3. Goals

- **G1** — Fix the save flow per UAT feedback (naming, profile, duplicate prevention).
- **G2** — **Conditional document identity**: when a document came from Spaarke, the pane knows which `sprk_document` and matter it is.
- **G3** — Surface Spaarke context in the pane: AI profile, related record, similar documents.
- **G4** — Records created from the pane are **complete** — number, owner, and mapped fields populated.
- **G5** — Tabbed pane (Save | Compose | Find) with real views behind each tab.
- **G6** — Outlook parity: shared capabilities land in `shared/taskpane/`, benefiting both hosts.

---

## 4. Non-goals and deferred requests

### 4.1 Out of scope (project boundary)

- **NOT** competing with legal drafting tools (L-1). No tracked-change authoring, no redlining, no drafting agent.
- **NOT** MCP or external-tool interop (L-4) — owned by `spaarkeai-word-native-r1`.
- **NOT** extending Spaarke AI capability (L-5) — surfacing only.
- **NOT** building duplicate detection — it is **already shipped** (§8); r1 consumes it.
- **NOT** requiring Spaarke as the document source (L-2).

### 4.2 Requested but **DEFERRED** — documented, not in r1

These came from the wish list and are explicitly out of scope for r1. Recorded so they are not lost.

| Request | Why deferred | Revisit |
|---|---|---|
| **Send Message** — Spaarke message modal on the related record | Overlaps active `messaging-communication-app-r*`. Building a second message composer would violate CLAUDE.md §11. | After messaging project lands |
| **Send Email via Spaarke email client modal** | Overlaps active `email-communication-solution-r5` (Outlook-style email widget + code page). r1 ships the Outlook path only. | After r5 lands |
| **Add Event task** (`sprk_event`) | **Owner decision 2026-09-04: ship To Do in r1, defer Event.** Duplicates To Do's shape; To Do is cheaper (endpoint + view exist). | r2 |
| **"+More" fields on create** (Assigned Attorney, Paralegal, etc.) | Superseded by the §7 approach — populate server-side, then open the record to edit the rest. A pane form recreates a wizard in 320px. | Only if §7 proves insufficient |
| **Tier-2 semantic near-duplicate detection** (`documentVector3072`) | Tier-1 exact-hash + graduate-on-divergence are **already shipped** (§8). Only the semantic half remains, and it is a validated fast-follow, not a gap. | Fast-follow |

---

## 5. Scope — the r1 feature set

### Tier 0 — Foundation

| # | Item | Complexity | Notes |
|---|---|---|---|
| F-1 | **Conditional document identity** | MED | `Office.context.document.url` → base64url → Graph `/shares/u!{enc}/driveItem` → `driveId`+`itemId` → `sprk_document` via `sprk_graphitemid_uk`. Plus a custom-XML-part GUID stamp at upload so a document that leaves and returns still self-identifies. **Spike the desktop `document.url` shape for SPE files.** |
| F-2 | **Tab shell: Save \| Compose \| Find** | LOW | Extend the existing `NavigationTab` union; build real views behind the placeholders. |
| F-3 | **Adapter consolidation** | LOW-MED | Route Word through `HostAdapterFactory` onto the tested `WordAdapter`; reconcile r2's +81 lines. |
| F-4 | **Word → unified JSON manifest** | LOW | Outlook's `manifest.json` is the precedent. |

### Tier 1 — Save improvements (UAT-driven)

| # | Item | Complexity | Notes |
|---|---|---|---|
| S-1 | Filename defaults to **Document Name**, editable via pencil icon | LOW | Lets the Document Name field come off the form |
| S-2 | **Description → Profile**; populate `sprk_filesummary`, `sprk_filetldr`, `sprk_filekeywords`, `sprk_documenttype` from the record | LOW-MED | Needs F-1 for already-saved documents |
| S-3 | **Generate Profile** button | LOW-MED | Profiling works (#919 fixed). Add an `/api/office` trigger mirroring Compose's `refresh-profile`. |
| S-4 | **Related-to record card**, click to open the record | MED | Needs F-1. ⚠️ No `Xrm` in an add-in — see §6. |
| S-5 | **Open Document record** link | MED | Same constraint as S-4 |
| S-6 | **Don't re-create; save as version** when the file already has a `sprk_document` | MED | `SaveRequest.ExistingDocumentId` already exists. See §8. |
| S-7 | **Consume the shipped collision handling** | LOW | ✅ **Already fixed** — UAC-r2 shipped it 2026-09-02 (`conflictBehavior`, server default `Fail` → 409 with the existing file untouched, typed `UploadNameConflictError`, two-option dialog). The add-in surfaces the typed error and honors the choice. **Do not rebuild.** Remaining pre-flight-probe refinement is UAC-r2 task 094. |

### Tier 2 — Surfacing Spaarke

| # | Item | Complexity | Notes |
|---|---|---|---|
| T-1 | **Record create completeness** (G4) | LOW-MED | See §7 — server-side, not a pane form |
| T-2 | **Add To Do** from Word | LOW | `POST /api/office/todo` + `CreateTodoView` exist; swap regarding to document+record |
| T-3 | **Send Email → Outlook** with document + record links | LOW | `mailto:` / Office URI. Spaarke-modal variant deferred (§4.2). |
| T-4 | **Find tab** — similar documents and records via AI Search | MED | ⚠️ Must use per-row-trimmed endpoints. See §9. |
| T-5 | **Wire the Word ribbon commands** — `quickSave` / `shareDocument` | LOW | Currently stubs ([word/commands/index.ts:28-61](src/client/office-addins/word/commands/index.ts#L28-L61), "TRACKED: GitHub #234"). Owner: include in r1. |
| T-6 | **Clear the typecheck debt** | LOW-MED | ~397 pre-existing `exactOptionalPropertyTypes` errors in `src/client/office-addins`. Owner: clean up in this project. Do it **before** feature work so new errors are visible. |

### Tier 3 — Spike before committing

| # | Item | Complexity | Notes |
|---|---|---|---|
| C-1 | **Launch affordance** for the existing **Spaarke AI declarative agent** — button/icon, not a tab | LOW *(if a mechanism exists)* | See §5.1. This is **not** `SprkChat`, and **not** a third tab. |

### 5.1 What the Compose tab actually is (corrected 2026-09-04)

Owner evidence: in Word today, the **Copilot pane** already hosts a **"Spaarke AI" declarative agent** — "Created by Spaarke ✓", with a "Chat only" mode selector and Copilot's own suggested prompts ("Summarize this document", "Ask a question about this document", "Suggest ways to improve this document"). It sits alongside Claude and Harvey for Word in the same ribbon.

So the Compose tab is **not** about hosting `SprkChat` in our task pane. It is about surfacing the agent that is **already loaded through Teams/Copilot**.

That reframes the work — and introduces a genuine platform unknown that must be resolved before any estimate:

> **Can an Office add-in task pane programmatically open the Copilot pane and/or activate a specific declarative agent?**

I could find no documented Office.js API for this. **Owner direction (2026-09-04): if a launch mechanism exists and is simple — a button or icon — that is in scope. Do not build a tab around it.**

So the shape is:

- **If a launch mechanism exists** → ship a **launch button/icon** in the pane (likely in the Save view header or the ribbon), not a third tab. Cheap, honest, and it points at the agent rather than competing with it. Tabs become **Save | Find**.
- **If no mechanism exists** → omit the affordance. The agent is one click away in the Copilot rail; a tab or button that cannot actually open it is worse than nothing.

Explicitly **not** doing: hosting a separate `SprkChat` in the pane. That would put two Spaarke chat surfaces in one window competing for the same job, and it brushes against L-5 (surface what exists; don't extend).

**Phase 0 spike**: is there a URI scheme, `Office.context.ui` hook, or documented deep-link that targets the Copilot pane / a named agent? Timebox it — the downside of "no" is one small affordance, not a phase.

---

## 6. The `Xrm` constraint (affects S-4, S-5, T-1)

**There is no `Xrm` in an Office add-in.** `Xrm.Navigation.navigateTo` is unavailable, so "open a modal of the record" cannot use the model-driven-app pattern. r2 learned this the hard way: the Outlook add-in **recreated** wizard layouts rather than importing the Xrm-bound `CreateTodoWizard` components.

Options for opening a record from the pane:
- **(a) Office Dialog API** hosting a Spaarke code page or the MDA record form — richest; needs a framing/auth spike.
- **(b) Open a browser tab** to the MDA record URL — trivial, breaks flow.
- **(c) Read-only detail in the pane**, with (b) as the escape hatch — cheapest useful.

**Owner decision (2026-09-04): (a) is the preferred route — investigate it first.** (c) becomes the documented fallback only if the spike fails.

This raises the stakes on the Phase 0 spike, because §7 deliberately leans on "open the record to finish editing" as a core flow rather than building a pane form. What the spike must establish:

1. Can `Office.context.ui.displayDialogAsync` host an **MDA record form** without framing/X-Frame-Options refusal, or must we host a **Spaarke code page** instead?
2. Does the dialog get a usable auth context — NAA token via `messageParent`, or its own sign-in?
3. Does the record form actually **function** in the dialog (save, lookups, subgrids) at dialog dimensions?
4. Does an edit made in the dialog propagate back to the pane (via `messageParent`) so the Related-to card refreshes?

If (1) fails for the MDA form but a Spaarke code page works, that is still a good outcome — it just changes what we host.

---

## 7. Record-create completeness (G4) — root cause and fix

**UAT symptom**: a record created from the pane with only a title arrives in Spaarke missing `sprk_matternumber` / `sprk_projectnumber` and other important fields.

**Root cause (verified)**: the number is generated **client-side inside the Create wizard** — [matterService.ts:254](src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts#L254) does `entity['sprk_matternumber'] = \`${typeCode}-${random6}\``. It is **not** a Dataverse autonumber column and **not** server-side. `QuickCreateRequest` accepts only `Name`, `Description`, `ClientId`, `Industry`. So any create path that isn't the wizard produces an unnumbered record.

**Fix — server-side, no pane form**:
1. Move number generation (and owner, and Field Mapping Framework population) into a **shared server-side creation service** that `QuickCreateAsync` calls.
2. Every create path — wizard, add-in, and any future caller — inherits it.
3. The user finishes any remaining editing by **opening the record from the Related-to card** (§6), not by filling a form in a 320px pane.

This is why the "+More fields" request is deferred (§4.2): it solves a problem that server-side population plus open-the-record removes.

**Side benefit**: replaces a client-side `random6` with a proper server-side scheme (collision-safe, auditable, consistent across paths).

### 7.1 Should this replace the client-side wizard creation? (owner asked — evaluate)

The wizards (`CreateMatterWizard`, `CreateProjectWizard`, and the five siblings) each own client-side creation logic today. Once a server-side creation service exists, the wizards *could* call it instead of composing entities in the browser.

**The case for**: one creation path, one place for numbering/owner/field-mapping, no client-side randomness, and the add-in stops being a second-class citizen. It also removes a class of drift where a wizard gains a field the add-in never learns about.

**The case for caution**: the wizards are shipped, UAT-hardened, and wired into the Field Mapping Framework with creation-time assigned-resource inheritance. Rewriting their creation leg is real regression risk in surface area this project does not otherwise touch, and it widens blast radius well beyond the add-in.

**Recommended sequencing** — do not couple them:
1. **r1**: build the server-side creation service and route **`QuickCreateAsync`** through it. The add-in gets complete records. Wizards untouched.
2. **Evaluate after r1**, with the service proven in production: migrate the wizards path-by-path, each with its own regression pass.

That way the UAT complaint is fixed now, and the consolidation happens against a service that has already earned trust — rather than betting both on one change. If the evaluation says the wizards should migrate, that is its own project with its own gates.

---

## 8. Duplicate handling — consuming shipped machinery

> **Corrected 2026-09-04.** Earlier drafts said duplicate detection was owned by `sdap-file-duplication-detector-r1` and pending. **That is wrong.** That project was absorbed into `email-communication-intelligence-r2` (Pillar C, FR-C1–C4) and **its content-dedup design shipped to master.** Neither it nor `unified-access-control-r2` is a live dependency. Canonical reference: [`content-identity-and-deduplication-architecture.md`](docs/architecture/content-identity-and-deduplication-architecture.md).

**Status**: ✅ item-identity dedup live · ✅ content dedup live (`ContentDedupDetector`, `quickXorHash` + `sprk_canonicalhash`, gate-after-write, graduate-on-divergence) · ⏸️ only Tier-2 semantic near-dup deferred.

**What this project does**: rides it. No parallel check, no new detector.

### Two layers the save-back touches — keep them separate

**Layer A — item identity (the S-6 primary path).** `sprk_graphitemid_uk` guarantees one `sprk_document` per SPE item. When F-1 resolves the open document to an existing record — via `Office.context.document.url` → Graph `/shares/…/driveItem` → the alternate key, or the custom-XML-part GUID stamp — the save-back **versions that record** via `SaveRequest.ExistingDocumentId` rather than creating. Pure identity, no hashing.
⚠️ **Do NOT relax the alternate key** — Compose's transient-key dedup and promote-idempotency both rest on it.

**Layer B — content identity (the safety net).** If F-1 *cannot* identify the document — one that left Spaarke and returned as a new SPE item — the save creates a new item, and `ContentDedupDetector` catches the byte-identical case *after* write, reconciling `quickXorHash` against `sprk_canonicalhash` and **notifying with a pointer to the canonical**. Never silent. The add-in does not call the detector; `/api/office/save` already does.

### 🔴 The invariant to get right: editable ⇒ link/graduate, never suppress

A Word document is **editable**. If the save-back ever treats a hash hit like an immutable copy (suppress-forever), two genuinely different drafts that happen to be byte-identical *right now* collapse into one record — **data loss**.

The platform already solved this for Compose: a byte-identical editable save is recorded as a **hash-linked copy** (`sprk_canonicaldocument` → canonical) and **graduates to its own canonical the moment it is edited**. Mirror `ComposeService.PromoteIfEphemeralAsync`, **not** the immutable suppress path in `OfficeDocumentPersistence` (which is correct only for email attachments / Assistant persist).

Practically: staying on the Layer-A version-save path for Spaarke-sourced documents (which L-2 scopes us to) sidesteps this entirely — version-save targets the same record, so there is no dedup decision. Layer B only matters for the returned-as-new-item edge case.

### Adjacent constraints (not dedup)

| Constraint | Source | Nature |
|---|---|---|
| ✅ Upload collision is **fixed** — explicit `conflictBehavior`, server default `Fail` (409, existing file untouched), typed `UploadNameConflictError`, two-option dialog. Consume it; don't rebuild. Pre-flight-probe refinement is **UAC-r2 task 094**. | UAC-r2, shipped 2026-09-02 | Resolved defect → **S-7** |
| `Document→Matter` has **two** many-to-one relationships (`sprk_matter_document` + `…_sprk_relatedmatter`); same for Project. **Two slots per type, not a many-to-many.** The related-record card must respect this. | UAC-r2 **task 095** (in flight) | Schema fact — coordinate |

---

## 9. Security

- **Find tab (T-4) must use per-row-trimmed endpoints.** `RecordSearchAuthorizationFilter` publishes `RequiresPerRowRecordAuthorization`; an endpoint receiving it and not performing the row check **must refuse**. UAC r2 demonstrated the failure mode — a user denied Read on all 442 documents of a matter still saw and downloaded them via `POST /api/ai/search`.
- **BFF reads are app-only**, so Dataverse row security is inert on that path — the UAC evaluator is mandatory.
- **Auth is only in scope for Spaarke-sourced documents** (L-2). A document opened from the desktop is not our domain; the pane simply offers to save it.
- **Per-environment Entra SPA redirect registration** is a real provisioning step (§2) — two URIs per environment.
- ⚠️ If the add-in files to `sprk_workassignment` / `sprk_event` / `sprk_todo`, `UploadFinalizationWorker.cs:611-629` must be widened alongside `EntityAccessFilter.EntitySetByType` — otherwise the switch hits `default:` and the document is created **silently unassociated**.

---

## 10. Outlook parity (L-6)

The shared task pane is the delivery vehicle. Most of this project lands in `shared/taskpane/` and benefits both hosts.

| Capability | Word | Outlook | Where it lives |
|---|---|---|---|
| Tab shell, Find, profile display, record card, To Do, Send Email, create-completeness | ✅ | ✅ | `shared/taskpane/` |
| Document identity, `.docx` save, version-save | ✅ | — | `word/` + `WordAdapter` |
| Email/attachment save, triage, linked-todos | — | ✅ | existing Outlook views |
| Compose tab (C-1) | ✅ | Spike | `shared/` if the webview constraints allow |

**Parity rule**: build in `shared/`, gate by `hostAdapter.getCapabilities()`, never by host-type conditionals scattered through views.

---

## 11. Phasing

**Phase 0 — De-risk** (three spikes, all gate downstream scope):
1. `document.url` shape for SPE files in **Word desktop** (F-1) — the keystone.
2. **Office Dialog API** for record open (§6, four questions) — §7 depends on this being usable.
3. **Can a task pane reach the Copilot pane / a specific agent?** (§5.1) — determines whether the Compose tab exists.

Also in Phase 0: **T-6 typecheck cleanup**, so new errors are visible during feature work.

**Phase 1 — Foundation + save**: F-1 → F-4, S-1 → S-7. *Gate*: a Spaarke-sourced document is identified, saves back to the correct record as a version, with profile fields shown — and the client-path collision defect (S-7) is closed with a regression test.

**Phase 2 — Surfacing**: T-1 → T-5, Outlook parity pass. *Gate*: a record created from the pane is complete (number + owner + mapped fields); Find returns permission-trimmed results.

**Phase 3 — Compose tab**: C-1, **only if** the §5.1 spike shows a real mechanism. Default is to drop it (§5.1 option ii) rather than ship a tab that duplicates a Copilot rail click.

---

## 12. Placement Justification (CLAUDE.md §10)

| Addition | Placement | Rationale |
|---|---|---|
| Document-identity resolver (F-1) | **BFF**, extending `/api/documents` | Existing Graph + Dataverse plumbing; latency-coupled to existing document routes |
| Profile trigger (S-3) | **BFF**, extending `/api/office` | Mirrors Compose's `refresh-profile`; same fire-and-forget pipeline |
| Create-completeness (T-1, §7) | **BFF**, extending `QuickCreateAsync` + a shared creation service | Fixes every create path at once; the alternative duplicates wizard logic client-side |
| Version-save (S-6) | **BFF**, extending `/api/office/save` | `ExistingDocumentId` hook already present |
| Find (T-4) | **No new BFF code** if existing trimmed search endpoints suffice | `/api/office/search/*` and `/api/ai/search` exist |
| Tabs, views, cards | **Add-in only** (`shared/taskpane/`) | Pure client |

Publish-size measured per §10 bullet 4 on every BFF-touching task.

---

## 13. Component Justification (CLAUDE.md §11)

| New component | Overlap | Why not extend | Cost of doing nothing |
|---|---|---|---|
| `WordDocumentIdentityService` | `getItemId()` | A title+author hash cannot identify a record | Version-save, profile display, record card, and open-record are all impossible |
| Shared creation service (§7) | `QuickCreateAsync` + `matterService.ts` | Number generation currently lives client-side in the wizard; extending one path leaves the other broken | Records keep arriving unnumbered — the actual UAT complaint |
| Find view | Outlook's stub `search` tab | The tab exists as a placeholder only | No way to find related documents while drafting |

To Do (T-2) is an **adaptation** of a shipped endpoint and view, not a new component.

---

## 14. Decisions and remaining questions

### Resolved (owner, 2026-09-04)

| # | Question | Decision |
|---|---|---|
| 1 | Event vs To Do | **Ship To Do in r1; defer Event** (§4.2) |
| 2 | Record open mechanism | **Office Dialog API (a) is the preferred route — investigate first**; (c) is the documented fallback only if the spike fails (§6) |
| 4 | Word ribbon commands | **Include** — wire `quickSave` / `shareDocument` (T-5) |
| 5 | Typecheck debt | **Clean it up in this project** (T-6), before feature work |
| — | Duplicate detection | **Already shipped** — consume, don't build (§8) |
| — | Record-create server-side | **Yes**; wizard migration evaluated separately after r1 (§7.1) |
| — | Acceptance criteria | Confirmed correct as drafted (§15) |

### Investigated and answered

**The deployed "Spaarke AI" agent is ours, and it is a real declarative agent + API plugin.** It lives at [`src/solutions/CopilotAgent/`](src/solutions/CopilotAgent/) — `declarativeAgent.json` (the agent), `spaarke-api-plugin.json` (an **OpenAPI-based API plugin**, `auth.type: OAuthPluginVault`), `spaarke-bff-openapi.yaml` (the BFF surface it calls), `appPackage/manifest.json`, deployed by `scripts/Deploy-CopilotAgent.ps1`. Built by `ai-m365-copilot-integration`.

Its status, from that project's notes:
- ✅ **Inbound auth works end to end.** Copilot shows a consent card → user signs in → Copilot sends a Bearer token with `scp=access_as_user`, `aud=api://1e40baad-…` → the BFF accepts it via multi-audience `PostConfigure<JwtBearerOptions>`.
- 🔴 **Downstream is not user-scoped.** The BFF endpoints the plugin calls use app-only Dataverse auth with no OBO. `GET /api/v1/events` is documented as *"Returns ALL events or none — not scoped to current user."*

That is the same shape as the UAC r2 442-document defect: the agent **works**, but results are not permission-trimmed. It is a security gap, not a functionality gap — worth knowing before anyone demos it broadly.

**Consequence for this project**: none directly — the agent is `spaarkeai-word-native-r1`'s territory. C-1 only needs to *launch* it (§5.1), and a launch affordance is safe regardless of the OBO state.

### Open

1. **Can a task pane open the Copilot pane / a named agent?** No documented Office.js API found. Timeboxed Phase 0 spike; determines whether C-1 ships (§5.1).
2. **Does the Office Dialog API accept an MDA record form**, or must we host a Spaarke code page? (§6, four spike questions.) §7 depends on the answer being usable.

---

## 15. Acceptance (draft — closed set to be finalized in spec)

1. A document opened **from Spaarke** in Word is correctly identified — right `sprk_document`, right matter.
2. A document opened from the **desktop** saves cleanly as a new document, with no identity claimed.
3. Saving a document that already has a `sprk_document` creates a **version**, not a duplicate row.
4. ⚠️ Saving a file whose name collides with an existing item **does not destroy the existing file's bytes** (S-7 regression test).
5. The filename defaults to Document Name and is editable in the pane.
6. Profile fields populate from the record; **Generate Profile** re-runs profiling successfully.
7. A record created from the pane has its **number and owner populated**, and mapped fields applied.
8. Find returns results **trimmed to the caller's permissions** — verified with a negative case.
9. A To Do created from Word carries both the document and the related record as regarding.
10. Every shared capability works in **both** Word and Outlook, or is explicitly gated by host capability.
11. Publish-size delta measured and within ceiling on every BFF-touching change.

---

<hot-path-declaration>
  <bff>Y</bff>
  <spaarke-ai>N</spaarke-ai>
  <ci-workflows>Y</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
