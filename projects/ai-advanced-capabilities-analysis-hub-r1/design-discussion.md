# AI Advanced Capabilities — Analysis Hub & Session Persistence — Design Discussion

> **Project**: `ai-advanced-capabilities-analysis-hub-r1` · **Round**: r1
> **Date**: 2026-07-28 · **Owner**: ralph.schroeder
> **Status**: 🟡 Discussion / pre-design — raw material to formalize via `/design-to-spec`
> **Spawned from**: `ai-advanced-capabilities-nda-r1` UAT (the NDA advisory-review vertical). This project
> generalizes NDA into a first-class **work-type platform** and adds the durable **`sprk_analysis`** spine.
> **Program umbrella**: `projects/ai-advanced-capabilities-development/` (`PROGRAM-ROADMAP.md`) — the program's
> "genuinely ABSENT net-new candidate" list already names **`sprk_analysis` durable results table** and a
> **tabular doc×question grid**, both of which this project delivers.
> **Sibling**: `ai-advanced-capabilities-research-r1` (the "Legal Research" work type — a *different* surface).

---

## 0. What this project is

NDA review proved the **Compose-based advisory surface**. This project turns that into a **named,
reusable product surface** ("Agreement Analysis") and gives every AI analysis a **durable home** so
users can create, find, reopen, and continue their work — with session history that never gets lost.

Three deliverables:
1. **`sprk_analysis` durable spine** — the business record that anchors an analysis (work-type + files +
   associations + status + outputs + its chat session(s)).
2. **Session storage/persistence** — sessions survive pane close/refresh and are reopenable; a clean
   two-tier model (loose sessions vs Analysis-owned sessions).
3. **The "Analysis" hub widget** — a home surface: create-new-by-type cards + a grid of existing analyses;
   plus a per-type **creation wizard**.

---

## 1. The three-level work-type model (foundation — already partly built)

Established in `ai-advanced-capabilities-nda-r1` (see its
`notes/contextual-ai-tool-library-design.md` §10, and `../ai-advanced-capabilities-research-r1/COMPETITIVE-LANDSCAPE.md`
which validates it against Harvey/Legora/CoCounsel):

| Level | What it is | Example | Governs |
|---|---|---|---|
| **1. Work type** | the product surface the user picks by intent | Agreement Analysis · Legal Research · Patent Application | the widget/UX **and the tool palette** |
| **2. Knowledge sub-domain** | grounding variation *within* a work type | NDA vs MSA vs employment | **only grounding** — same UI, same tools |
| **3. UI affordance** | *where* a tool renders | selection BubbleMenu · review-note ⋮ | which menu a tool appears in |

**Already shipped** (in nda-r1): the Contextual AI Tool Library — tools tagged by `surfaces` × `workTypes`,
`getToolsForSurface(surface, activeWorkType)`. NDA = the first knowledge sub-domain of the
`agreement-analysis` work type. This project consumes that seam.

---

## 2. `sprk_analysis` — the durable spine

An **Analysis record = one unit of AI work the user can name, find, reopen, and associate.** It anchors:

- **Work type** (`agreement-analysis`, `legal-research`, …) — drives which surface + tool palette + wizard.
- **Knowledge sub-domain** (nda / msa / …) — drives grounding (playbook / knowledge sources).
- **File pointer(s)** — the reviewed document(s). Stored in **SharePoint Embedded (SPE)** — the Analysis
  holds `speDriveItemId` / `sprkDocumentId` pointers, NOT the bytes (see §4).
- **Associations** — a lookup to Matter / Project / etc. (this is what lets the SpaarkeAi three-pane UX
  surface *inside* a Matter/Project form — the embedded-mode contract).
- **Status** + name + description + created/modified.
- **Outputs** — findings / advisory notes / accepted redlines (already in the ADR-040 ledger; the Analysis
  is the business key over them).
- **Chat session(s)** — the Assistant history for this analysis. **One Analysis can own MULTIPLE sessions**
  via **`sprk_analysischatmessage`** (grouped by session) — review today, continue tomorrow, same Analysis.

Schema changes are in-scope (owner approved). The existing `sprk_analysis` / `sprk_analysischatmessage`
tables are the starting point — reconcile against them before adding columns (§11 reuse-first).

---

## 3. Session model — two tiers (the #3/#4 decision)

**Not every chat session becomes an Analysis record.** A clean two-tier model:

- **Loose sessions** — ad-hoc chats ("summarize this", quick Q&A). Persist server-side + appear in history.
  Lightweight, disposable. NOT Analysis records.
- **`sprk_analysis` records** — created when the user runs a **first-class AI analysis** (Review NDA, etc.).
  The Analysis owns its session(s); their turns persist to `sprk_analysischatmessage`.

**Rules (owner UAT input, 2026-07-28):**
- **#3 — adding a file to a running chat** → prompt **"new session"** vs **"add to current session"**.
  "New" archives the current session to history (switchable back); "add" keeps the file in the current
  session's context.
- **#4 — launching an AI analysis (Review NDA / any analysis)** → **ALWAYS forks a NEW session** bound to a
  **new Analysis record**, regardless of the #3 choice, so file + analysis + history stay packaged together.
  **Warn** the user: *"This starts a new Assistant session; your previous session is saved in history."*
- **Promotion** — a loose session may be explicitly associated to (promoted into) an Analysis. Answer to the
  owner's question: *chat sessions are saved AS Analysis records only when associated to one* (explicitly, or
  by launching an analysis) — a casual chat never auto-creates an Analysis.

---

## 4. File & data storage (verified 2026-07-28)

| Data | Store | Addressing |
|---|---|---|
| **Document FILES** (the NDAs / agreements) | **SharePoint Embedded (SPE)** — ADR-007, `ISpeFileOperations` | `speDriveItemId` / `sprkDocumentId`; session uploads resolve to SPE too |
| Session / chat / workspace-state records | **Cosmos DB** (behind `ChatEndpoints`, `ChatDocumentEndpoints`, `WorkspaceStateEndpoints`, feedback, pinned-memory) + **Redis** hot cache | server-side |
| Analysis business record + chat transcript | **Dataverse** (`sprk_analysis`, `sprk_analysischatmessage`) | Dataverse GUIDs |

→ **The Analysis record stores SPE pointers to its files** (not bytes). This addressing already exists in
the compose seed (`compose.upload`/`stored`/`speDriveItemId`).

---

## 5. The "Analysis" hub widget (owner UX vision)

A home surface for AI work (matches Harvey's workflow launcher / Legora's home — see competitive report):

- **Top: "Create new Analysis"** — cards per work type: *Agreement Review*, *Legal Research*,
  *Patent Application*, … Each card = a **work type** (the `workTypes` axis). Selecting a card opens its wizard.
- **Below: a grid of existing `sprk_analysis` records** — with a **view dropdown to filter by type**.
  → **Reuse the DataGrid framework** (`<DataGrid configId=… />` + `sprk_gridconfiguration` + view-by-type).
  Reopen an analysis from the grid → rehydrates its session + review state + files.

---

## 6. Per-type creation wizard (owner UX vision)

When a "create new" card is selected (e.g. *Agreement Review*), a guided wizard opens:
- **Step 1** — upload a file OR select an existing Document.
- **Step 2** — Access, **Associate to** (lookup: Matter/Project/etc.), Analysis **name**, **description**.
- **Step 3** — **Next Steps**: *Send Email*, *Create To Do*, …
- **On finish** — the file loads to a workspace tab and the selected analysis **runs**.

→ **Reuse** the `Create*Wizard` pattern + the **Field Mapping** engine ("Associate to" lookup + "Next Steps"
Action bindings are exactly field-mapping's model). This is largely composition of existing frameworks.

---

## 7. Pane-persistence foundation (partly shipped in nda-r1; harden here)

**Shipped in nda-r1** (root cause: `ThreePaneLayout` unmounted panes on collapse, destroying pane-local
state):
- ✅ Panes now **keep-mounted-hidden** on collapse → Assistant session + compose tabs survive collapse/expand
  (`ce45b5f5c`).
- ✅ Tab independence — a seedless compose open no longer clobbers the active analysis tab (`283e9a989`).

**Remaining leaks to harden here** (from the nda-r1 root-cause report) so *refresh* + *reopen* survive too:
1. **Stale-session fresh-create** — when a server session TTL expires, the client silently creates a NEW
   empty session, losing the old history. Must resolve against the durable `sprk_analysis` transcript instead.
2. **Tab persistence gated on `chatSessionId`** — ribbon/pre-session compose tabs are never persisted →
   lost on refresh. The Analysis record should be the persistence anchor, not just the chat session id.
3. **Live editor edits not persisted** — only the compose *seed* round-trips, not unsaved edits. Reopen from
   an Analysis should restore edit state (or the saved SPE version).

---

## 8. Reuse inventory (§11 — build almost nothing net-new)

| Need | Reuse |
|---|---|
| Work-type tool scoping | ✅ Contextual AI Tool Library (`workTypes` × `surfaces`, `getToolsForSurface`) — shipped |
| Existing-analyses grid | ✅ DataGrid framework (`sprk_gridconfiguration`, view-by-type) |
| Creation wizard | ✅ `Create*Wizard` pattern + Field Mapping engine |
| File storage + pointers | ✅ SPE (`ISpeFileOperations`) + the compose seed addressing |
| Three-pane surface | ✅ `ThreePaneShell` + LegalWorkspace embedded-mode contract (for Matter/Project embedding) |
| Capability discovery | ✅ `/api/ai/capabilities` + `useComposeToolbarActivation` |
| Analysis anchor + transcript | ✅ (extend) `sprk_analysis` + `sprk_analysischatmessage` |

**Net-new**: the hub widget shell; the session↔Analysis binding + fork-on-analysis logic; schema additions on
`sprk_analysis`; the persistence hardening (§7). Everything else is composition.

---

## 9. Open questions / decisions for design

1. **Hub widget form** — a workspace layout/widget vs a standalone code-page dashboard? (Likely a widget so it
   embeds in the three-pane + in Matter/Project forms.)
2. **`activeWorkType` host wiring** — the ComposeEditor prop exists (defaults `'*'`); the host passes
   `'agreement-analysis'` when launching. Wire in this project as the first work-type-scoped tools land.
3. **Session ↔ Analysis binding mechanics** — where does the fork-on-analysis happen (client `ConversationPane`
   vs BFF session service)? How is a session rehydrated from `sprk_analysischatmessage`?
4. **Multiple work types timeline** — Agreement Analysis (this) → Legal Research (research-r1, a *different*
   surface) → Patent Application (later). Confirm the ordering.
5. **Tabular review grid** — the competitive report flags a doc×question REVIEW GRID as the eventual
   volume driver (portfolios/data rooms). In this project or a later one?

---

## 10. Constraints to honor

- **§10 BFF Hygiene** — schema/session work touching `Sprk.Bff.Api` needs Placement Justification + publish-size
  check. `<hot-path-declaration>` required (BFF Y, SpaarkeAi Y).
- **§11 Component Justification** — default to reuse (see §8); justify any new service/entity.
- **Embedded-mode contract** — surfacing the three-pane inside Matter/Project must honor
  `LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`.
- **Pre-existing test debt** — 12 e2e failures on master (compose-session-routing / edit-controls /
  three-pane-coordination) confirmed independent of nda-r1 work; address in a remediation pass before or
  during this project.

---

## 11. Ground-truth reconciliation (2026-07-28, code-verified) — READ BEFORE `/design-to-spec`

An Explore pass verified the design's load-bearing assumptions against the code + live spaarkedev1 metadata.
**Five assumptions in §2–§8 are wrong or net-new.** Corrections (all with file:line evidence in the
analysis-hub investigation):

### 11.1 `sprk_analysis` — exists, but the "work-type spine" columns do NOT
- **Exists** and is used server-side (`AnalysisEndpoints.cs`, `DataverseWebApiService.cs:258`, `Models.cs:393-403`).
- **Actual columns**: `sprk_name`, `sprk_workingdocument`, `sprk_chathistory` (JSON blob), `statuscode`,
  `_sprk_documentid_value` (lookup → `sprk_document`), `sprk_outputfileid`, `sprk_Playbook` lookup, N:N
  `sprk_analysis_skill`/`_knowledge`/`_tool`, 1:N `sprk_analysisoutput`/`_workingversion`/`_emailmetadata`.
- **NET-NEW (not "reconcile"):** `sprk_worktype` column, a **Matter/Project association lookup** (none today —
  only `sprk_documentid`), a description column (unconfirmed). §2's central work-type field + association do
  not exist. Client has NO `sprk_analysis` TS type — the surface is server + `sprk_analysisworkspace` web resource.

### 11.2 🚨 `sprk_analysischatmessage` is a DEAD, EMPTY SHELL — do NOT build on it (§11 anti-pattern)
- Metadata exists; **zero records; no code writes it**; its creation task was skipped. It is NOT the transcript store.
- **The real, live chat store is `sprk_aichatsummary` (session metadata) + `sprk_aichatmessage` (per-message)**,
  and **it already has the `sprk_sessionid` grouping key** the design wanted (`ChatDataverseRepository.cs:37-45,128-137`).
- **Correction to §2/§3/§8**: "one Analysis → many sessions" must build on **`sprk_aichatsummary`/`sprk_aichatmessage`**
  (add an analysis foreign key to `sprk_aichatsummary`), NOT extend the dead `sprk_analysischatmessage`. Reviving a
  never-written entity when a working, sessionId-grouped one exists is exactly the §11 "default to reuse" violation.
  **Recommendation: retire/supersede `sprk_analysischatmessage`; bind sessions to Analysis via a new
  `sprk_aichatsummary → sprk_analysis` lookup.**

### 11.3 Two disjoint session systems exist — the binding must pick ONE
- **(a) Current**: `ChatEndpoints` (`/api/ai/chat/sessions`) → Redis(24h)→Cosmos→Dataverse `sprk_aichatsummary`;
  session id is **BFF-minted GUID** (`ChatSessionManager.cs:108`). **(b) Legacy**: `AnalysisEndpoints`
  `/{id}/resume` (in-memory) + `/{id}/continue` (writes `sprk_chathistory` JSON) — **explicitly deprecated**
  (`AnalysisEndpoints.cs:64-66`). Neither binds a session to `sprk_analysis`.
- **Recommendation**: standardize the hub on the **ChatEndpoints (Cosmos/Redis) path**; treat the legacy
  AnalysisEndpoints session model + `sprk_chathistory` JSON as retired. Fork-on-analysis + session↔Analysis
  binding is **100% net-new** on top of ChatEndpoints.

### 11.4 SPE pointers live on `sprk_document`, not `sprk_analysis` (§4 correction)
- `sprk_analysis` holds a **lookup to `sprk_document`**; the `speDriveItemId`/`GraphDriveId`/`GraphItemId` live on
  `sprk_document`. Functionally one hop away — the "Analysis stores SPE pointers" wording is wrong; the Analysis
  reaches files **through** the document lookup. Keep that indirection (reuse) rather than duplicating pointers.

### 11.5 §7 "resolve stale session against durable transcript" presumes a store that isn't populated
- The Dataverse **cold read path is stubbed** (`GetMessagesAsync` returns empty; Redis+Cosmos are authoritative,
  `ChatDataverseRepository.cs:145-171`). So "reopen resolves against the durable `sprk_analysis` transcript" first
  requires **building durable per-message persistence** (net-new), or the reopen contract must rely on Cosmos
  (warm) durability. This is a real deliverable, not a "harden existing."

### 11.6 What IS solid (reuse verified) — §5/§6 hub surfaces all real
- DataGrid framework (`<DataGrid/>` + `sprk_gridconfiguration` + `ViewSelector`), `Create*Wizard` family +
  `AssociateToStep`, Field Mapping engine, `WorkspaceWidgetRegistry` + PaneEventBus `widget_load` — all present
  and reusable as §5/§6/§8 claim. The hub *shell* is largely composition; the *data spine + session binding* is
  the net-new core.

### 11.7 Readiness verdict — ✅ ALL DECISIONS LOCKED (owner, 2026-07-28) → SPEC-READY

1. **Session store binding** — ✅ **Bind to `sprk_aichatsummary` + a new `→ sprk_analysis` lookup.**
   **Retire/supersede** the dead `sprk_analysischatmessage` (do not revive).
2. **Legacy retirement** — ✅ **Retire** the deprecated `AnalysisEndpoints` in-memory session path +
   `sprk_analysis.sprk_chathistory` JSON blob. Standardize on `ChatEndpoints` (Cosmos/Redis).
3. **Association model** — ✅ **RegardingResolver field-SET (per-type typed lookups), NOT a single polymorphic field.**
   New fields on `sprk_analysis`: `sprk_regardingmatter`, `sprk_regardingproject`, **`sprk_regardingdocument`**, …
   (owner sets these up), resolved by the existing RegardingResolver field-set + logic/code.
   **KEEP `sprk_documentid`** as the SPE subject-pointer (the file hop). `sprk_regardingdocument` is a *separate*
   regarding-context field; when an analysis is document-only, `sprk_documentid` and `sprk_regardingdocument`
   point to the same record — **owner confirms the duplicate is fine** (different roles: SPE hop vs context/rollup).
   New `sprk_worktype` column also net-new.
4. **Durable-transcript contract** — ✅ **MVP: Cosmos = the durable transcript store-of-record** (long-term
   primary, cheap, complete, fast reopen). **Dataverse = the business anchor** (`sprk_analysis` metadata +
   important field-level data + some JSON, incl. the Review Summary Memo). **Defer** per-message Dataverse cold
   persistence (immutable/compliance-grade) — add later only if legal retention of every raw message is required.
5. **Tabular doc×question review grid** — ✅ **Deferred** to a later project.

**Storage model (locked, shared with agreements-r1):** full chat history → **Cosmos**; business anchor +
structured deliverables (memo, outputs) → **Dataverse** (`sprk_analysis` / `sprk_analysisoutput`).

**Next step:** run `/design-to-spec` → `/project-pipeline` on this design.

---

## 12. Surface + entry model (owner, 2026-07-28) — LOCKED

**The working surface for an Analysis is the SpaarkeAi code page (the three-pane).** There is ONE Analysis
experience; it renders in two hosting contexts — the SpaarkeAi workspace, or a **code-page modal** launched from
a record form. The hub widget + wizard feed into that same surface.

### 12.1 Entry / open matrix

| # | Trigger | Behavior |
|---|---|---|
| **2a** | **New** analysis started in the **SpaarkeAi workspace** | Runs in the SpaarkeAi surface (exactly as NDA does today) — Assistant + Compose three-pane. |
| **2b** | **New** analysis started **in a record** | The **analysis-type wizard opens** → user steps through → the analysis opens in the **code-page modal (three-pane)** with **regarding pre-set to the parent record** and the **"Create new" cards showing**. |
| **2c** | **Existing** analysis opened from the **SpaarkeAi workspace dataset grid** | Opens in the SpaarkeAi surface/context. |
| **2d** | **Existing** analysis opened **in a record** (from its Analysis subgrid) | Opens as a **code-page modal** (three-pane). |

So: **new-in-record → wizard first, then modal**; **existing → straight to the surface** (workspace or modal by
context). The modal host = the code-page-modal pattern (`navigateTo` + embedded-mode contract); investigate the
exact reuse path (see §13 retirement/reuse investigation).

### 12.2 Record ↔ Analysis relationship (LOCKED)
- A record (Matter/Project/…) has a **tab/subgrid of its Analysis records**.
- **A record can have MANY analyses; an Analysis belongs to exactly ONE record.** → the regarding is
  **single-valued** (one populated regarding field per Analysis), which the RegardingResolver field-set pattern
  models exactly (`sprk_regardingmatter` / `sprk_regardingproject` / `sprk_regardingdocument` — one populated).
  This is a clean 1:N record→analysis; the subgrid is the record-side view of that relationship.

### 12.3 Analysis is a Dataverse record; the wizard populates it
- Creating an Analysis = creating the `sprk_analysis` **Dataverse record**; the **create wizard populates its
  fields** (name, description, work-type, regarding = parent record, access, next-steps) before the analysis runs.
- 2b's "regarding pre-set to the parent record" = the wizard is launched *from* the record, so the parent is known
  and the regarding field is pre-filled (the user doesn't pick it).

### 12.4 Implication for scope
The hub must deliver: **(i)** the SpaarkeAi-hosted hub widget (2a/2c) · **(ii)** the record-form **Analysis
subgrid/tab** + a **"new analysis" launch → wizard → code-page modal** path (2b) · **(iii)** an **open-existing →
code-page modal** path (2d) with regarding context. (i) is workspace-hosted; (ii)/(iii) are record-form-hosted via
the code-page modal. Both host the same three-pane Analysis surface.

---

## 13. Existing-Analysis retirement + reuse map (code-verified 2026-07-28)

Two Explore agents mapped every existing Analysis component. Verdict + plan below.

### 13.1 `sprk_analysisworkspace` code page → **RETIRE-WITH-MIGRATION**
- **Source**: `src/client/code-pages/AnalysisWorkspace/` (webpack, React 19). **Stale** (last real commit 2026-07-06)
  while SpaarkeAi is under daily dev — functionally superseded, operationally still wired.
- **What it is**: a full 3-pane workspace — **Editor** (Lexical analysis-output) | **Source** (side-by-side doc
  preview: PDF iframe + Office embed, `SourceViewerPanel.tsx`) | **Chat** (shared `SprkChat`) — plus
  `DiffReviewPanel` (accept/reject redlines), export, auto-save, 12 hooks.
- **4 names to reconcile**: `sprk_analysisworkspace` (HTML web resource) · `sprk_AnalysisWorkspace` (navigateTo
  casing) · `sprk_AnalysisWorkspaceLauncher` (JS) · dead custom page `sprk_analysisworkspace_8bc0b`.

### 13.2 Consolidated KEEP / MIGRATE / RETIRE

**KEEP (reuse in hub, no change):**
- Server: `POST /api/ai/analysis/create` (durable-record creation), `POST /execute` (**already modernized** R7
  Wave 4 → dispatches `IPlaybookOrchestrationService`), `POST /export` (Email/Teams/PDF/DOCX),
  `IAnalysisDataverseService` (`CreateAnalysisAsync`/`CreateAnalysisOutputAsync`/`AssociateScopesAsync`),
  `AnalysisResultPersistence`, `AnalysisRagProcessor`, `sprk_analysisoutput`.
- Client: `FindingsWidget` (`Spaarke.AI.Widgets`), `AnalysisEditorWidget` (`Spaarke.AI.Outputs`),
  `NdaReviewSummaryPanel` + Compose widgets, **`launch-resolver.openSpaarkeAi`** (the modal-open primitive — see 13.3).

**MIGRATE:**
- `POST /{id}/save` + `GET /{id}` (keep record read/save; **drop the `sprk_chathistory` blob read** — transcript
  comes from Cosmos), `AnalysisOrchestrationService` (save/export/get glue only), `sprk_analysisworkingversion`
  (working-doc edits move to compose/Cosmos), NDA-specific conversation hooks → **generalize to work-type**, the
  AnalysisWorkspace 3-pane → **fold into SpaarkeAi**.

**RETIRE (superseded):**
- `POST /{id}/continue`, `POST /{id}/resume` (legacy in-memory session), `sprk_analysis.sprk_chathistory` JSON,
  `sprk_analysischatmessage` (dead shell), AnalysisWorkspace's **unique** hooks/services (`analysisApi.ts`,
  `useAnalysis*`, `AnalysisAiContext`, `ChatPanel`, `SourceViewerPanel`, `useInlineAiToolbar`), and the web
  resources themselves (`sprk_analysisworkspace` HTML + `sprk_AnalysisWorkspaceLauncher` + dead custom page).
  Session binding standardizes on `ChatEndpoints` + `sprk_aichatsummary` (+ new `→ sprk_analysis` FK).
- **Shared widgets SURVIVE** — retiring the code page retires only its *unique* stack; `FindingsWidget`,
  `AnalysisEditorWidget`, `NdaReviewSummaryPanel` live in `@spaarke/*` libs and are what the hub reuses.

**NET-NEW:** client `sprk_analysis` TS type; `sprk_worktype` column; `sprk_regardingmatter/project/document`
field-set; record→analysis 1:N + **subgrids on Matter/Project forms**; the Analysis hub widget; session↔Analysis
fork-on-analysis logic; a record→modal ribbon launcher reusing `openSpaarkeAi`.

### 13.3 Modal-open path (answers entry-matrix 2b/2d) — SOLVED, reuse `openSpaarkeAi`
The reusable primitive is **`src/solutions/SpaarkeAi/src/utils/launch-resolver.ts` → `openSpaarkeAi(params, target=2)`**
— `navigateTo({pageType:'webresource', webresourceName:'sprk_spaarkeai', data}, {target:2, 80%×80%})`, accepts
`entityLogicalName`+`entityId`+`matterId`. Mirror the existing `composeMode`/`openSpaarkeAiCompose` precedent to add
an `analysisId`/`worktype`/`regarding` param; add a `sprk_matter`/`sprk_project` ribbon script (mirror
`DocumentComposeLaunch`) that reads the form record id and calls it. **Do NOT route through `surfaceLaunchRegistry`**
— that's the reactive *in-chat* path (agent offers a surface), explicitly distinct from record-driven opens. (Add a
`workspace-tab` registry entry ONLY if you also want the Assistant to *offer* "open Agreement Analysis" in chat.)

### 13.4 Record subgrid — 100% NET-NEW
**No form has a `sprk_analysis` subgrid/tab today; no record→analysis 1:N wiring exists.** `sprk_analysis`'s only
lookups are `_sprk_documentid_value` + `sprk_Playbook`. The §12.2 model (record has a subgrid of its analyses;
analysis belongs to one record) requires the new regarding field-set + subgrids added to the parent forms.

### 13.5 Retirement sequence (becomes explicit hub tasks — ordered, nothing dangles)
1. **Repoint 4 server-side deep-links** (compiled C#, would 404): `HandoffUrlBuilder.BuildAnalysisWorkspaceUrl`
   (← `PlaybookStatusEndpoints`, `PlaybookInvocationService`, `AgentErrorHandler`) + notification `actionUrl`
   (`AnalysisEndpoints.cs:783`) → target `sprk_spaarkeai` via `openSpaarkeAi` deep-link shape.
2. **Repoint client launch points**: `sprk_analysis` form OnLoad/ribbon launcher + PlaybookLibrary navigateTo →
   `openSpaarkeAi`. Remove the dead custom-page path in `sprk_analysis_commands.js`.
3. **Parity — RESOLVED (no migration needed):** redlines/export/auto-save covered; side-by-side source preview not
   needed (owner). See §13.6.
4. **Delete** the web resources + `Deploy-AnalysisWorkspace.ps1` + the `AnalysisWorkspace/` source tree; reconcile
   the casing so nothing dangles.

### 13.6 Capability parity — RESOLVED, no gap → CLEAN RETIREMENT
SpaarkeAi/Compose covers: accept/reject redlines ✅, export (the `/export` endpoint is KEEP) ✅, auto-save ✅ (to SPE).
The one candidate gap — **side-by-side ORIGINAL-SOURCE preview** (`SourceViewerPanel`) — is **NOT NEEDED per owner
(2026-07-28): retire it.** So there is **no capability-migration scope**: the retirement is purely (1) repoint the
invocation points (§13.5 steps 1–2) → (2) delete (step 4). `SourceViewerPanel` + its PDF-iframe/Office-embed move
firmly to **RETIRE**. (Note the PDF-preview *pattern* it demonstrated is still useful reference for the separate
agreements-r1 `compose-r5` PDF work, but nothing is carried forward from this code page.)
