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
