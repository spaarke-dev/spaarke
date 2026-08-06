# AI Advanced Capabilities — Analysis Hub & Session Persistence — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-28
> **Source**: `design-discussion.md` (code-verified 2026-07-28; owner decisions locked §11.7 / §12 / §13)
> **Project**: `ai-advanced-capabilities-analysis-hub-r1` · **Round**: r1 · **Owner**: ralph.schroeder
> **Spawned from**: `ai-advanced-capabilities-nda-r1` UAT · **Sibling**: `ai-advanced-capabilities-research-r1`
> **Program umbrella**: `projects/ai-advanced-capabilities-development/` (`PROGRAM-ROADMAP.md`)

---

## Executive Summary

Generalize the proven NDA advisory vertical into a first-class **Analysis platform**. Deliver (1) a durable
`sprk_analysis` business spine that lets users create, find, reopen, and associate AI analyses; (2) two-tier
session persistence bound to the Analysis record so sessions survive close/refresh/reopen and never get lost;
and (3) an **Analysis hub widget** + per-type **creation wizard**. There is ONE Analysis experience (the
SpaarkeAi three-pane); it renders in two hosting contexts — the SpaarkeAi workspace and a code-page modal
launched from a record form. The project is **≈80% composition of existing frameworks**; the net-new core is
the data spine, the session↔Analysis binding (fork-on-analysis), and the record-form integration. It also
executes a **clean retirement** of the superseded `sprk_analysisworkspace` code page (no capability-migration
scope per §13.6).

---

## Scope

### In Scope

1. **`sprk_analysis` durable spine** — consume owner-created work-type + regarding schema; add the client
   `sprk_analysis` TS type (none exists today); reach files through the existing `sprk_documentid` lookup (SPE hop).
2. **Session persistence + binding** — bind sessions to `sprk_analysis` via a new `sprk_aichatsummary → sprk_analysis`
   lookup; standardize on the `ChatEndpoints` (Redis→Cosmos) path; Cosmos = durable transcript store-of-record,
   Dataverse = business anchor.
3. **Fork-on-analysis** — launching any AI analysis always forks a new session bound to a new Analysis record,
   archives the prior session to history, and warns the user (§3 rule #4). *Implementation layer deferred to design (UQ-1).*
4. **Two-tier session model** — loose sessions (disposable, not Analysis records) vs Analysis-owned sessions;
   explicit promotion (loose → Analysis).
5. **Persistence hardening** (§7) — stale-session recovery resolves against the durable (Cosmos) transcript;
   tab persistence anchored on the Analysis record (not `chatSessionId`); live editor edit restore on reopen.
6. **Analysis hub widget** — "Create new" cards (3 work-type cards; Agreement Review live, Legal Research +
   Patent Application "coming soon") + a grid of existing `sprk_analysis` records with view-by-type, reusing the
   DataGrid framework (`<DataGrid configId=… />` + `sprk_gridconfiguration`). Reopen rehydrates session + review state + files.
7. **Per-type creation wizard** — 3 steps (file/Document select · access + associate-to + name + description ·
   next-steps), reusing the `Create*Wizard` family + Field Mapping engine; on finish creates the `sprk_analysis`
   record and runs the analysis. Wire `activeWorkType` host prop (host passes `'agreement-analysis'`).
8. **Entry model (matrix 2a–2d)** — new-in-workspace (2a), new-in-record → wizard → code-page modal (2b),
   existing-in-workspace (2c), existing-in-record → code-page modal (2d). Modal open reuses `openSpaarkeAi`.
9. **Record-form integration** — record→analysis 1:N; an Analysis **subgrid/tab** on Matter/Project forms (net-new);
   a record→modal ribbon launcher mirroring `DocumentComposeLaunch` + the `composeMode`/`openSpaarkeAiCompose` precedent.
10. **Pre-existing test-debt remediation** — fix the 12 e2e failures on master (compose-session-routing /
    edit-controls / three-pane-coordination) as an early phase, before building on the three-pane foundation.
11. **Retirement of `sprk_analysisworkspace`** — ordered per §13.5: repoint server deep-links + client launch
    points → retire legacy session path + dead entities → delete web resources + source tree.

### Out of Scope (deferred)

- **Tabular doc×question review grid** — deferred to a later project (§11.7 #5).
- **Per-message Dataverse cold persistence** (immutable/compliance-grade) — defer; add only if legal retention of
  every raw message is later required (§11.7 #4). MVP: Cosmos is the transcript store-of-record.
- **Legal Research + Patent Application work types** — Research is the sibling `research-r1` project; Patent is later.
  This project ships their hub cards as "coming soon" only (no functional surface).
- **Side-by-side original-source preview** (`SourceViewerPanel`) — NOT needed per owner (§13.6); retired, not migrated.
- **Net-new Dataverse column creation** — owner pre-creates all schema (see Prerequisites); this project consumes it.
- **In-chat Assistant *offer* to open Agreement Analysis** (the reactive `surfaceLaunchRegistry` `workspace-tab` path) —
  deferred (UQ-4). Entry is record-driven / deterministic only for this project (§13.3).

### Affected Areas

- `src/solutions/SpaarkeAi/**` — hub widget, per-type wizard, `ConversationPane` fork logic, `launch-resolver.ts` (`openSpaarkeAi` analysis param), `activeWorkType` wiring. **(SpaarkeAi hot path)**
- `src/server/api/Sprk.Bff.Api/**` — `AnalysisEndpoints.cs` (retire legacy `/continue`/`/resume`, migrate `/save`+`GET /{id}`), `ChatEndpoints` / `ChatSessionManager.cs` / `ChatDataverseRepository.cs` (session↔Analysis binding), `HandoffUrlBuilder.BuildAnalysisWorkspaceUrl` + notification `actionUrl` deep-link repoint. **(BFF hot path)**
- `src/client/code-pages/AnalysisWorkspace/**` — **deleted** (retirement), incl. `Deploy-AnalysisWorkspace.ps1`.
- Dataverse web resources — `sprk_analysisworkspace` (HTML), `sprk_AnalysisWorkspaceLauncher` (JS), dead custom page `sprk_analysisworkspace_8bc0b` — **deleted / reconciled** (4-name casing per §13.1).
- Dataverse forms — Matter/Project forms gain an Analysis subgrid/tab + ribbon launcher; `sprk_matter`/`sprk_project` ribbon scripts.
- Shared libs (`@spaarke/*`) — `FindingsWidget`, `AnalysisEditorWidget`, `NdaReviewSummaryPanel`, DataGrid, `Create*Wizard`, Field Mapping — **reused, not modified structurally** (NDA-specific hooks generalized to work-type).

---

## Requirements

### Functional Requirements

**Phase 0 — Foundation remediation**
1. **FR-01**: Fix the 12 pre-existing e2e failures (compose-session-routing / edit-controls / three-pane-coordination).
   - *Acceptance*: the 12 named e2e tests pass on the project branch; failures confirmed independent of new work; green baseline established before Phase 2+ tasks touch the three-pane.

**Data spine**
2. **FR-02**: Add a client `sprk_analysis` TypeScript type (none exists today per §11.1) modeling the record incl. the
   owner-created columns.
   - *Acceptance*: a typed client model exists; hub grid + wizard + reopen consume it; no `any`-typed analysis records.
3. **FR-03**: Consume `sprk_worktype` + the regarding field-set via the existing **RegardingResolver field-set** pattern
   (single populated regarding field per Analysis).
   - *Acceptance*: given a `sprk_analysis` with exactly one populated regarding lookup, RegardingResolver resolves the
     parent (Matter/Project/Document); zero or multiple populated regarding fields is rejected/flagged.
4. **FR-04**: Reach analysis files through the existing `sprk_documentid` → `sprk_document` lookup (the SPE hop);
   do NOT duplicate `speDriveItemId`/`GraphDriveId`/`GraphItemId` onto `sprk_analysis` (§11.4).
   - *Acceptance*: file open/preview resolves SPE pointers via the document lookup; no SPE pointer columns added to `sprk_analysis`.

**Session persistence + binding**
5. **FR-05**: Bind chat sessions to `sprk_analysis` via the owner-created `sprk_aichatsummary → sprk_analysis` lookup;
   standardize on the `ChatEndpoints` (Redis→Cosmos→Dataverse-anchor) path.
   - *Acceptance*: an Analysis-owned session carries the analysis FK; "one Analysis → many sessions" is queryable by
     the `sprk_sessionid` grouping key + analysis FK; the dead `sprk_analysischatmessage` entity is NOT written.
6. **FR-06**: Fork-on-analysis — launching an AI analysis always (a) creates a new `sprk_analysis` record, (b) creates a
   new session, (c) binds them, (d) archives the prior session to history (switchable back), (e) warns *"This starts a new
   Assistant session; your previous session is saved in history."*
   - *Acceptance*: launching any analysis mid-conversation produces a new bound Analysis+session; prior session is retrievable
     from history; warning shown. **Implementation layer (client vs BFF) resolved in design per UQ-1.**
7. **FR-07**: Two-tier session model — loose sessions persist server-side + appear in history but are NOT Analysis records;
   a loose session can be explicitly **promoted** (associated) into an Analysis. A casual chat never auto-creates an Analysis.
   - *Acceptance*: an ad-hoc chat creates no `sprk_analysis`; explicit promotion binds an existing loose session to a new/named Analysis.
8. **FR-08**: Add-file-to-running-chat (§3 #3) prompts **"new session"** vs **"add to current session"**. "New" archives
   the current session (switchable back); "add" keeps the file in the current session context.
   - *Acceptance*: adding a file to a live chat shows the two-choice prompt and behaves per the selected option.
9. **FR-09**: Persistence hardening (§7): (a) stale/expired server session resolves against the durable Cosmos transcript
   instead of silently creating an empty session; (b) tab persistence is anchored on the Analysis record, not gated on
   `chatSessionId`; (c) live editor edits restore on reopen (edit state or saved SPE version).
   - *Acceptance*: after TTL expiry, reopen restores prior history (no empty-session data loss); a pre-session/ribbon compose
     tab survives refresh; reopening an Analysis restores edit state.

**Hub widget**
10. **FR-10**: Analysis hub widget — top "Create new" cards for 3 work types (Agreement Review **live**; Legal Research +
    Patent Application **"coming soon"/disabled**); below, a grid of existing `sprk_analysis` records with a view dropdown
    to filter by type, using the DataGrid framework + `sprk_gridconfiguration`.
    - *Acceptance*: hub renders 3 cards (1 actionable, 2 disabled-with-affordance); grid lists analyses; view-by-type filters correctly.
11. **FR-11**: Reopen an analysis from the grid → rehydrates its session + review state + files.
    - *Acceptance*: selecting a grid row opens the three-pane surface with the analysis's session history, findings/review
      state, and files restored.

**Creation wizard**
12. **FR-12**: Per-type creation wizard — Step 1 upload OR select existing Document; Step 2 access + associate-to
    (regarding lookup) + name + description; Step 3 next-steps (Send Email / Create To Do / …). On finish, create the
    `sprk_analysis` record (wizard populates its fields) and run the analysis; file loads to a workspace tab. Reuse
    `Create*Wizard` + `AssociateToStep` + Field Mapping engine.
    - *Acceptance*: completing the wizard creates a valid `sprk_analysis` (name, description, work-type, regarding, access,
      next-steps) and launches the analysis; "associate-to" and "next-steps" are Field-Mapping-driven.
13. **FR-13**: Wire the `activeWorkType` host prop — host passes `'agreement-analysis'` when launching (ComposeEditor prop
    defaults `'*'`); tool palette scopes via the shipped `getToolsForSurface(surface, activeWorkType)`.
    - *Acceptance*: launching Agreement Review scopes the tool palette to `agreement-analysis` tools.

**Entry model + record integration**
14. **FR-14**: Entry matrix 2a–2d — 2a new-in-workspace runs in the SpaarkeAi surface; 2b new-in-record opens the wizard →
    then the code-page modal with regarding pre-set to the parent + "Create new" cards showing; 2c existing-in-workspace
    opens in the workspace; 2d existing-in-record opens as a code-page modal.
    - *Acceptance*: each of the 4 paths opens the correct host with correct pre-set context.
15. **FR-15**: Record→analysis 1:N + an Analysis subgrid/tab on Matter/Project forms (net-new; no subgrid exists today per §13.4).
    - *Acceptance*: a Matter/Project form shows a subgrid of its analyses (analyses where the matching regarding field = the
      record); a record can have many analyses; each Analysis belongs to exactly one record (one populated regarding field).
16. **FR-16**: Record→modal ribbon launcher — extend `openSpaarkeAi` with `analysisId`/`worktype`/`regarding` params
    (mirroring the `composeMode`/`openSpaarkeAiCompose` precedent); add a `sprk_matter`/`sprk_project` ribbon script that
    reads the form record id and calls it (mirror `DocumentComposeLaunch`).
    - *Acceptance*: a ribbon button on Matter/Project opens the three-pane modal with regarding context; opening an existing
      analysis passes `analysisId`.
17. **FR-17**: Record-driven opens MUST NOT route through `surfaceLaunchRegistry` (that is the reactive in-chat path, §13.3).
    - *Acceptance*: record ribbon/subgrid opens go via `openSpaarkeAi` deep-link, not the surface-launch registry. (A
      `workspace-tab` registry entry is added ONLY if the Assistant should also *offer* "open Agreement Analysis" in chat.)

**Retirement (ordered per §13.5)**
18. **FR-18**: Repoint the 4 server-side deep-links (compiled C#, would 404): `HandoffUrlBuilder.BuildAnalysisWorkspaceUrl`
    (callers: `PlaybookStatusEndpoints`, `PlaybookInvocationService`, `AgentErrorHandler`) + notification `actionUrl`
    (`AnalysisEndpoints.cs:783`) → the `openSpaarkeAi` `sprk_spaarkeai` deep-link shape.
    - *Acceptance*: all 4 deep-links resolve to the SpaarkeAi surface; no reference to `sprk_analysisworkspace` remains in C#.
19. **FR-19**: Repoint client launch points — `sprk_analysis` form OnLoad/ribbon launcher + PlaybookLibrary `navigateTo` →
    `openSpaarkeAi`; remove the dead custom-page path in `sprk_analysis_commands.js`.
    - *Acceptance*: client launches open `sprk_spaarkeai`; dead custom-page path removed.
20. **FR-20**: Retire the legacy session path — `POST /{id}/continue`, `POST /{id}/resume` (in-memory), the
    `sprk_analysis.sprk_chathistory` JSON blob read, and the dead `sprk_analysischatmessage` shell.
    - *Acceptance*: legacy endpoints removed/return gone; no code reads `sprk_chathistory`; `sprk_analysischatmessage` not referenced.
21. **FR-21**: Delete the web resources + `Deploy-AnalysisWorkspace.ps1` + the `src/client/code-pages/AnalysisWorkspace/`
    source tree; reconcile the 4-name casing so nothing dangles (`sprk_analysisworkspace`, `sprk_AnalysisWorkspace`,
    `sprk_AnalysisWorkspaceLauncher`, dead `sprk_analysisworkspace_8bc0b`).
    - *Acceptance*: source tree + deploy script deleted; no dangling web-resource references; build + deploy clean.
22. **FR-22**: Migrate (keep-with-changes) — `POST /{id}/save` + `GET /{id}` retained but drop the `sprk_chathistory` read
    (transcript comes from Cosmos); generalize NDA-specific conversation hooks to work-type; fold the AnalysisWorkspace
    3-pane behavior into SpaarkeAi. Shared widgets (`FindingsWidget`, `AnalysisEditorWidget`, `NdaReviewSummaryPanel`) survive unchanged.
    - *Acceptance*: `/save` + `GET /{id}` work without `sprk_chathistory`; hooks are work-type-parameterized; shared widgets still render.

### Non-Functional Requirements

- **NFR-01**: BFF publish-size ≤60 MB compressed on every BFF-touching task (baseline ~49.63 MB incl. PDBs; ≥+5 MB
  single-task delta → justify; ≥55 MB → architecture review). Report absolute size + diff per BFF task (CLAUDE.md §10 bullet 4).
- **NFR-02**: The code-page modal host (2b/2d) MUST honor `LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` (config init, theme
  ownership, sessionStorage sentinels, webApi shim, mount semantics, lifecycle hooks — 21 testable MUSTs).
- **NFR-03**: Reuse-first (CLAUDE.md §11). No new service/abstraction/endpoint/entity without a concrete cost-of-doing-nothing
  justification (see New Components table). The data spine + session binding are the only justified net-new core.
- **NFR-04**: No session/transcript data loss across pane close, refresh, or reopen; Cosmos is the durable transcript
  store-of-record; reopen is fast (warm Redis/Cosmos read).
- **NFR-05**: Regarding integrity — exactly one populated regarding field per Analysis (single-valued); clean 1:N record→analysis.
- **NFR-06**: No new HIGH-severity CVE from `dotnet list package --vulnerable --include-transitive` on BFF-touching tasks.
- **NFR-07**: Retirement leaves nothing dangling — no 404 deep-links, no orphaned web-resource references, casing reconciled.

---

## Technical Constraints

### Applicable ADRs

- **ADR-007** — SharePoint Embedded file storage (`ISpeFileOperations`); files via the `sprk_document` hop, not duplicated on `sprk_analysis`.
- **ADR-013** — BFF AI facade; CRUD code uses `Services/Ai/PublicContracts/`, not AI-internal types directly.
- **ADR-028** — Spaarke Auth v2 (all new endpoints authenticated).
- **ADR-032** — Null-Object kill-switch pattern for any feature-gated service (e.g. "coming soon" work types if server-gated).
- **ADR-039** — Assistant surface-launch mechanism; record-driven opens stay in CODE via `openSpaarkeAi`, NOT `surfaceLaunchRegistry`.
- **ADR-040** — Analysis output ledger (`sprk_analysisoutput` survives as the outputs store).
- **ADR-024** — regarding/association modeling (11-entity regarding precedent) informs the regarding field-set.

### MUST / MUST NOT Rules

- ✅ MUST bind sessions to `sprk_aichatsummary` + the new `→ sprk_analysis` lookup (the live, sessionId-grouped store).
- ❌ MUST NOT revive or build on `sprk_analysischatmessage` (dead empty shell — §11 reuse-first violation).
- ✅ MUST standardize on `ChatEndpoints` (Redis→Cosmos); ❌ MUST NOT extend the deprecated `AnalysisEndpoints` in-memory session model.
- ✅ MUST reach files through `sprk_documentid` → `sprk_document`; ❌ MUST NOT duplicate SPE pointers onto `sprk_analysis`.
- ✅ MUST route record-driven modal opens through `openSpaarkeAi`; ❌ MUST NOT route them through `surfaceLaunchRegistry`.
- ✅ MUST keep exactly one populated regarding field per Analysis (single-valued regarding).
- ✅ MUST run the publish-size + CVE checks on every BFF-touching task.

### Existing Patterns to Follow

- Modal open: `src/solutions/SpaarkeAi/src/utils/launch-resolver.ts` → `openSpaarkeAi(params, target=2)`; mirror
  `composeMode`/`openSpaarkeAiCompose`.
- Ribbon launcher: mirror `DocumentComposeLaunch`.
- Wizard: `Create*Wizard` family + `AssociateToStep` + Field Mapping engine (`FieldMappingService.ts`).
- Grid: `<DataGrid configId=… />` + `sprk_gridconfiguration` + `ViewSelector`.
- Widget mount: `WorkspaceWidgetRegistry` + PaneEventBus `widget_load`.
- Tool scoping: `getToolsForSurface(surface, activeWorkType)` (Contextual AI Tool Library, shipped in nda-r1).
- Chat store: `ChatDataverseRepository.cs:37-45,128-137` (sessionId grouping); `ChatSessionManager.cs:108` (BFF-minted session GUID).

---

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**BFF=Y** — the project repoints compiled-C# deep-links (`HandoffUrlBuilder`), adds session↔Analysis binding consumed by
`ChatEndpoints`/`ChatSessionManager`/`ChatDataverseRepository`, and retires legacy `AnalysisEndpoints` routes.
**SpaarkeAi=Y** — hub widget, per-type wizard, `ConversationPane` fork logic, `launch-resolver` extension, `activeWorkType` wiring.

**Placement Justification (BFF components)** — all BFF work is *modification of existing surface* (repoint, retire, add a
lookup FK to an existing repository path). The only potential *new* BFF surface is the fork-on-analysis endpoint **if** UQ-1
resolves to "BFF session service"; that decision carries its own placement justification + publish-size check per
`.claude/constraints/bff-extensions.md`. Session binding reads/writes go through the existing `ChatEndpoints` path (no new AI-internal type injection; ADR-013 facade respected).

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| Client `sprk_analysis` TS type | None (§11.1 — no client type exists) | No — nothing to extend | Hub grid, wizard, and reopen cannot type analysis records; `any`-typed data spine |
| Analysis hub widget shell | `WorkspaceWidgetRegistry` widgets (compose, calendar) | No — new intent (home/launcher); composes existing DataGrid + cards | Users have no surface to create-new-by-type / find / reopen analyses |
| `sprk_aichatsummary → sprk_analysis` lookup | Dead `sprk_analysischatmessage` (rejected) | Yes — this IS extending the live `sprk_aichatsummary` | Sessions cannot bind to an Analysis; "one Analysis → many sessions" impossible |
| Per-type creation wizard | `Create*Wizard` family + `AssociateToStep` | Yes — reuse pattern; net-new = work-type card registry + coming-soon cards | No guided create → no name/regarding/next-steps capture before an analysis runs |
| Record→modal ribbon launcher | `DocumentComposeLaunch` / `openSpaarkeAi` `composeMode` | Yes — mirror precedent, add `analysisId`/`worktype`/`regarding` param | No record-driven way to open/create an analysis in modal context (2b/2d) |
| Analysis subgrid/tab on Matter/Project forms | None (§13.4 — no subgrid, no record→analysis 1:N today) | No — no existing relationship to extend | A record cannot show or launch its own analyses; §12.2 model unrealizable |
| Fork-on-analysis binding logic | None (100% net-new on ChatEndpoints, §11.3) | No — no existing fork/bind path | Launching an analysis cannot package file+analysis+history together (§3 #4) |

All Dataverse **columns** are owner-created (see Prerequisites) — not new components authored by this project.

---

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-040 (output/transcript durability) | Durable analysis artifacts persisted to Dataverse | MVP makes **Cosmos** the transcript store-of-record and **defers** per-message Dataverse cold persistence; Dataverse holds the anchor + memo + structured outputs only | **A (project-scoped exception)** | Owner-locked (§11.7 #4): Cosmos is cheaper, complete, and fast for reopen; immutable per-message Dataverse retention adds only if legal retention is later required. Dataverse remains the business anchor + deliverables store, so ADR-040's ledger intent (`sprk_analysisoutput`) is preserved. |
| ADR-013 / §10 BFF Hygiene | New BFF surface requires placement justification + publish-size check | Fork-on-analysis adds a BFF endpoint (`POST /api/ai/analysis/fork`) — UQ-1 resolved to Option B | **A (project-scoped exception, owner-approved 2026-07-28)** | Session GUIDs are server-minted (`ChatSessionManager.cs:108`); atomic fork must be server-side. One handler reusing DI'd services, ~0 publish-size delta, consumes `Services/Ai/PublicContracts/` (no fork). Placement Justification stated in the fork endpoint PR; publish-size + CVE checks apply per task. |

> All other listed ADRs (007, 028, 032, 039, 024) apply without exception. This section may be updated if tensions
> emerge during implementation (per §6.5).

---

## Success Criteria

1. [ ] The 12 pre-existing e2e failures pass (green baseline) — *Verify by*: run the named e2e suites on the project branch.
2. [ ] A user can create an Agreement Review from the hub (card → wizard → running analysis) — *Verify by*: end-to-end UI test.
3. [ ] An analysis is a durable `sprk_analysis` record, findable + reopenable from the hub grid with session/review/files
   restored — *Verify by*: create → close pane → refresh → reopen; assert history + findings + files intact.
4. [ ] Launching an analysis forks a new bound session + archives the prior with warning — *Verify by*: mid-chat launch;
   assert new Analysis+session, prior in history, warning shown.
5. [ ] Sessions survive TTL expiry via Cosmos (no empty-session data loss) — *Verify by*: expire session, reopen, assert history restored.
6. [ ] A Matter/Project record shows an Analysis subgrid; new-in-record opens wizard → modal with regarding pre-set;
   existing-in-record opens modal (2b/2d) — *Verify by*: record-form UI test.
7. [ ] `sprk_analysisworkspace` fully retired — no 404 deep-links, no dangling web-resource refs, source tree + deploy
   script deleted, casing reconciled — *Verify by*: grep for `analysisworkspace` in C#/JS/solution; build + deploy clean.
8. [ ] BFF publish-size ≤60 MB on every BFF task + no new HIGH CVE — *Verify by*: `dotnet publish` size report + `dotnet list package --vulnerable`.
9. [ ] Record-driven opens route via `openSpaarkeAi`, not `surfaceLaunchRegistry` — *Verify by*: code review + grep.

---

## Dependencies

### Prerequisites (HUMAN GATE — owner pre-creates before code tasks run)

Per owner decision (2026-07-28), **all net-new Dataverse schema is created by the owner** before the consuming code tasks
execute. The project consumes this contract; it does NOT include `dataverse-create-schema` tasks for these. Exact contract:

| Table | Column (logical) | Type | Target / Options | Purpose |
|---|---|---|---|---|
| `sprk_analysis` | `sprk_worktype` | Choice (option set) | `agreement-analysis`, `legal-research`, `patent-application` | Drives surface + tool palette + wizard |
| `sprk_analysis` | `sprk_regardingmatter` | Lookup | → `sprk_matter` | Regarding field-set (RegardingResolver) |
| `sprk_analysis` | `sprk_regardingproject` | Lookup | → `sprk_project` | Regarding field-set |
| `sprk_analysis` | `sprk_regardingdocument` | Lookup | → `sprk_document` | Regarding context field (separate from `sprk_documentid` SPE hop) |
| `sprk_analysis` | `sprk_description` | Text (multiline) | — | Analysis description **(created 2026-07-28 by owner)** |
| `sprk_aichatsummary` | `sprk_analysis` (FK) | Lookup | → `sprk_analysis` | Session↔Analysis binding (fork-on-analysis, one Analysis → many sessions) |

> Owner also confirms: **KEEP `sprk_documentid`** as the SPE subject-pointer; `sprk_regardingdocument` is a *separate*
> regarding/context field — for document-only analyses both may point to the same record (owner accepts the duplicate;
> different roles). Subgrids on Matter/Project forms are added as part of this project's form-customization tasks once the
> regarding field-set exists.

**Task-planning implication**: `/project-pipeline` must gate the data-spine + session-binding + subgrid tasks on the
schema existing. Add an explicit "verify owner schema present" preflight task; code tasks assume the columns exist.

### Existing (reuse — verified present §11.6 / §13.2)

- DataGrid framework, `Create*Wizard` + `AssociateToStep`, Field Mapping engine, `WorkspaceWidgetRegistry` + PaneEventBus.
- Contextual AI Tool Library (`getToolsForSurface`, shipped nda-r1).
- `ChatEndpoints` (Redis→Cosmos→`sprk_aichatsummary`), `ChatSessionManager`, `ChatDataverseRepository`.
- SPE (`ISpeFileOperations`) + compose seed addressing.
- Shared widgets: `FindingsWidget`, `AnalysisEditorWidget`, `NdaReviewSummaryPanel`.
- KEEP server: `POST /api/ai/analysis/create`, `POST /execute` (R7-modernized), `POST /export`, `IAnalysisDataverseService`,
  `AnalysisResultPersistence`, `AnalysisRagProcessor`, `sprk_analysisoutput`.

### External

- Owner-created Dataverse schema (above) — blocking for the data-spine/session/subgrid phases. `sprk_description`
  created by owner 2026-07-28; `sprk_project` logical name confirmed. Remaining owner-created columns
  (`sprk_worktype`, regarding field-set, `sprk_aichatsummary.sprk_analysis` FK) to be created before those phases run.

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Schema ownership | Who creates the net-new columns (`sprk_worktype`, regarding field-set, session-binding FK)? | **Owner pre-creates; project consumes.** | Spec adds a HUMAN GATE + exact schema contract; no `dataverse-create-schema` tasks for these; pipeline adds a "verify schema present" preflight. |
| Hub card scope | Which work-type cards ship this project? | **Three cards; Legal Research + Patent Application render "coming soon".** | FR-10 ships 1 live + 2 disabled cards; wizard + card built work-type-driven; Research/Patent are data/enablement later. |
| Pre-existing test debt | Are the 12 e2e failures (§10) in scope? | **Yes — remediation phase in this project.** | FR-01 (Phase 0) fixes them first; green baseline gates three-pane work. |
| Fork-on-analysis location | Where does the fork/bind logic live (client vs BFF)? | **Defer to design.md.** | Listed as UQ-1 (blocks design phase + BFF hot-path finalization); spec does not lock the architecture. |

## Assumptions

- **Work-type option set**: `sprk_worktype` is a **Choice** column with the three values above; if the owner prefers a
  reference-table lookup, FR-03/FR-10 adjust accordingly (flag at schema-creation).
- **Coming-soon cards**: Legal Research + Patent cards are UI-disabled with a visible affordance; no server gating needed
  (if server-gated later, apply ADR-032 kill-switch).
- **Reopen durability**: "restore session on reopen" relies on Cosmos (warm) durability, not Dataverse cold persistence
  (which is stubbed, §11.5) — consistent with the locked storage model.
- **Ordering**: work-type rollout is Agreement Analysis (this) → Legal Research (`research-r1`, separate surface) → Patent (later).

## Unresolved Questions

- [x] **UQ-1 (RESOLVED 2026-07-28, project-pipeline)**: Fork-on-analysis implementation layer → **Option B — new BFF
  endpoint `POST /api/ai/analysis/fork`** (owner-confirmed). Decisive fact: session GUIDs are minted 100% server-side
  (`ChatSessionManager.cs:108`); the client only reads back the id. One handler composes create+mint+bind+archive
  atomically, reusing DI'd services (~0 publish-size delta). **This resolves the deferred ADR-013 / §10 BFF Hygiene
  tension as a §6.5 Path A project-scoped exception (owner-approved)** — Placement Justification stated in the fork
  endpoint's PR; publish-size + CVE checks apply. Archive-durability (AIPL-054 stub) scoped as a dedicated task.
- [x] **UQ-2 (RESOLVED 2026-07-28)**: `sprk_analysis.sprk_description` (multiline text) added to Dataverse by owner.
- [x] **UQ-3 (RESOLVED 2026-07-28)**: Project entity logical name confirmed = `sprk_project`.
- [x] **UQ-4 (RESOLVED 2026-07-28)**: In-chat Assistant *offer* to open Agreement Analysis — **NO for this project**
  (record-driven `openSpaarkeAi` only, per §13.3). The reactive `surfaceLaunchRegistry` `workspace-tab` path is a deferred
  later enhancement; deferring avoids chip mis-fire risk while the deterministic entry model is established.

---

*AI-optimized specification. Original design preserved at `design-discussion.md`.*
