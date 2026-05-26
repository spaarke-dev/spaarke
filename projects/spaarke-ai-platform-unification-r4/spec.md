# Spaarke AI Platform Unification R4 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-05-26
> **Source**: `plan.md` (authoritative WBS) + `backlog.md` (per-item rationale + 2026-05-25 scoping decisions)
> **Predecessor**: R3 spec at [`projects/spaarke-ai-platform-unification-r3/spec.md`](../spaarke-ai-platform-unification-r3/spec.md) (shipped at master `3813af32`, task 140)

---

## Executive Summary

R4 consolidates the ~30 follow-up items that surfaced during R3 (Moment 1: Arrival) into a single post-shipping round. The scope was operator-finalized on 2026-05-25 as **34 IN items across 8 phases, ~116h estimated effort**. Three architectural shifts shape the work: (a) the new authoritative framing of the SpaarkeAi **two-wrapper model** (Dashboard wrapper via `LegalWorkspaceApp` + Direct widget wrapper via `WorkspaceWidgetRegistry`); (b) the formal **retirement of the standalone LegalWorkspace code page** (`sprk_corporateworkspace`) — components retained as a library, code page no longer deployed; and (c) the binding **BFF Hygiene §10** governance from CLAUDE.md that adds Placement Justification + publish-size verification rules to every BFF-touching task.

R3's FR-25 / NFR-10 ("standalone LegalWorkspace must continue to function identically") **no longer applies going forward** — superseded by W-6.

## Scope

### In Scope

All 34 IN items, grouped by phase per [`plan.md`](plan.md) §4:

**Phase 0 — R3 wrap-up + retroactive memo (~4h)**
- **E-1** R3 project wrap-up — lessons-learned, README→Complete, `/repo-cleanup`
- **F-1** BFF placement-justification retroactive memo for R3 attachment feature (light, one-time, scoped)

**Phase 1 — Documentation round (~21h)**
- **W-1** Write `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` (authoritative two-wrapper model)
- **W-2** Rewrite `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` with two-wrapper decision tree
- **A-2** Author ADR-025 (PaneEventBus) + ADR-026 (stage lifecycle) in concise + full forms
- **C-1** Write `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` (Xrm.WebApi vs BFF)
- **C-2** Write `docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`
- **D-2** Amend ADR-026 with "Heavy library handling" subsection (ADR amendment only — no implementation)
- **F-3** Document publish-size baseline + per-task verification rule in `.claude/constraints/azure-deployment.md` and CLAUDE.md §10

**Phase 2 — BFF governance audit (~2h)**
- **F-2** Audit remaining direct CRUD→AI dependencies outside `Services/Ai/PublicContracts/` facade (memo against 2026-05-20 baseline of 20)

**Phase 3 — UQ-03 verify + fix (~10h)**
- **A-5** Verify-first tab persistence behavior + fix the actually-broken gap (operator feedback contradicts original R3 UQ-03 verification)

**Phase 4 — Workspace builder + mount-source wiring (~19h)**
- **W-3** Fix `WorkspaceLayoutWizard` catalog drift — read `SECTION_REGISTRY` instead of hardcoded `SECTION_CATALOG`
- **W-4** Wire Assistant → Workspace mount source (first end-to-end `widget_load` demo)
- **W-5** Wire Context → Workspace mount source for one wizard (Create Project recommended)
- **W-6** Document LegalWorkspace code page retirement + update deploy scripts

**Phase 5 — Substantive code changes (~31h)**
- **A-4** File-attachment policy + raise client/server cap from 5 MB → 25 MB + publish `CHAT-ATTACHMENT-POLICY.md`
- **C-3** Consolidate dual `useWorkspaceLayouts` hooks into a single shared-lib hook
- **C-4** Introduce `WorkspaceRenderer` interface; `LegalWorkspaceApp` becomes the default renderer
- **B-4** Add `WorkspaceLayoutDto.modifiedOn` field; Manage pane renders it
- **B-5** BFF PUT → PATCH (or PUT + `If-Match` ETag) with concurrency safety
- **B-6** Reconcile `CalendarSidePane.CalendarSection` divergence; import from `@spaarke/events-components`

**Phase 6 — Build hygiene cluster (~21h)**
- **B-1** Add tracked build artifacts to `.gitignore`; `git rm --cached` stale tracked files
- **B-2** Fix `@spaarke/ai-widgets` tsc cross-rootDir error; CI gates `tsc --noEmit`
- **B-3** Rename `TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE → TELEMETRY_HISTORY_LOAD_FAILURE`; audit App Insights queries
- **B-7** Extract `useEventsBulkActions` hook into `@spaarke/events-components`
- **B-8** Upgrade `CalendarDrawer.eventDates` from `string[]` to `IEventDateInfo[]`
- **B-9** Migrate `Spaarke.UI.Components` ESLint v8 → v9 (flat config)
- **B-10** Redeploy standalone EventsPage web resource
- **B-11** Tighten type-drift casts bottom-up

**Phase 7 — R4 wrap-up (~2h)**
- R4 lessons-learned + README→Complete + `/repo-cleanup`

### Out of Scope

**Deferred items** (per `backlog.md` §Scoping Decisions, 2026-05-25):

- **A-1** Stages 2-4 (active chat / review / complete) header treatment — Moment 2 scope
- **A-3** AI-vs-User visual + AIReasoningSurface convention — major strategy work
- **C-5** Hoist remaining 5 LW-internal section factories — no forcing function; Calendar precedent gives forward path
- **C-6** Hoist `runtimeConfig` to `@spaarke/auth` — works today; cleaner later
- **C-7** Section registry plug-in style — no 3rd-party section requirement
- **D-2** (implementation only) Bundle-size Option 2 separate web resources — ADR amendment is IN, implementation deferred
- **D-3** Bundle-analyzer verification — only relevant after D-2 implementation
- **D-1** SessionPersistence tab state — merged into A-5

**Superseded R3 acceptance criteria**:
- R3 **FR-25 / NFR-10** ("standalone LegalWorkspace continues to function identically") — superseded by W-6 (code page retired). No longer applies forward.

### Affected Areas

| Path | Items | Description |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/` | A-4, B-4, B-5, F-2, F-3 | Attachment payload extension; `WorkspaceLayoutDto` extension; PATCH/ETag; facade audit; publish-size rule |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/` | F-2 | Facade audit boundary |
| `src/client/code-pages/SpaarkeAi/` (a.k.a. `src/solutions/SpaarkeAi/`) | W-3 (consumer), W-4, W-5, B-3 | Mount-source wiring; telemetry constant rename |
| `src/client/shared/Spaarke.UI.Components/` | B-9 | ESLint v9 migration |
| `src/client/shared/Spaarke.AI.Widgets/` | B-2, C-3, C-4 | tsc rootDir fix; consolidated `useWorkspaceLayouts`; `WorkspaceRenderer` interface |
| `src/client/shared/Spaarke.events-components/` (a.k.a. `@spaarke/events-components`) | B-6, B-7, B-8, B-11 | CalendarSection unification; `useEventsBulkActions` extraction; `CalendarDrawer.eventDates` upgrade; type-drift cleanup |
| `src/solutions/WorkspaceLayoutWizard/src/App.tsx` | W-3 | Read `SECTION_REGISTRY` instead of hardcoded `SECTION_CATALOG` |
| `src/solutions/CalendarSidePane/src/components/CalendarSection.tsx` | B-6 | Delete local copy; import from shared lib |
| `src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts` | C-3 | Adapted to consume new consolidated hook |
| `src/solutions/EventsPage/` | B-10 | Redeploy |
| `docs/architecture/` | W-1, C-2, W-6 | New: SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL, LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT, LEGALWORKSPACE-RETIREMENT |
| `docs/guides/` | W-2 | Rewrite BUILD-A-NEW-WORKSPACE-WIDGET |
| `docs/standards/` | C-1, A-4 | New: DATA-ACCESS-DECISION-CRITERIA, CHAT-ATTACHMENT-POLICY |
| `docs/adr/` + `.claude/adr/` | A-2, D-2 | New ADR-025, ADR-026 (concise + full); ADR-026 heavy library handling amendment |
| `.claude/constraints/azure-deployment.md` | F-3 | Publish-size baseline + per-task rule |
| `CLAUDE.md` (root) | F-3 | §10 publish-size rule |
| `scripts/Deploy-*.ps1` | W-6 | Skip standalone LegalWorkspace deploy |
| `projects/spaarke-ai-platform-unification-r3/` | E-1, F-1 | R3 wrap-up + retroactive memo |

---

## Requirements

### Functional Requirements

Code-touching items expressed as FRs. Each lists the backlog item ID in parentheses.

- **FR-01 (W-3)**: `WorkspaceLayoutWizard` MUST read available sections from `SECTION_REGISTRY` (dynamic source of truth) instead of the hardcoded `SECTION_CATALOG` constant. — Acceptance: Open the wizard; picker shows all 7 sections including Calendar and Daily Briefing; build a custom dashboard combining Calendar + My Documents; persists and renders correctly.
- **FR-02 (W-4)**: Assistant pane MUST be able to dispatch `widget_load` on the `PaneEventBus.workspace` channel, mounting a widget as a new workspace tab. The R4 demo scenario is operator-chosen at task time (recommended: user uploads PDF in chat → `DocumentViewer` widget mounts as tab). — Acceptance: Operator-visible end-to-end demo; widget appears as a tab; tab is selectable/closable; behavior persists per existing tab semantics.
- **FR-03 (W-5)**: One Context-pane wizard (recommended: Create Project) MUST add an "Add result to Workspace" final step that dispatches `widget_load` on completion, mounting the result widget as a workspace tab. — Acceptance: Operator-visible end-to-end demo from wizard launch through resulting workspace tab.
- **FR-04 (A-4)**: Client-side attachment cap in `useChatFileAttachment.ts` and server-side `MaxAttachmentSizeBytes` in `ChatEndpoints.cs` MUST be raised from 5 MB to 25 MB. `MaxAttachmentTextCharsPerFile` MUST be reviewed and scaled proportionally if warranted. — Acceptance: Boundary tests pass at 1 MB / 10 MB / 24 MB; 25 MB succeeds; 26 MB rejected with clear user-facing error.
- **FR-05 (A-5)**: Tab persistence behavior across browser refresh and browser close/reopen MUST be verified against operator expectation, then any actually-broken gap MUST be remediated. — Acceptance: `notes/tab-persistence-verification-2026-05.md` documents current behavior + spec/UX expectation + storage layer (sessionStorage / localStorage / BFF / Cosmos); remediation (if any) lands; operator confirms end-to-end behavior matches expectation.
- **FR-06 (B-3)**: Telemetry constant `TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE` MUST be renamed to `TELEMETRY_HISTORY_LOAD_FAILURE` throughout the codebase, and any App Insights queries referencing the old name MUST be migrated. — Acceptance: Grep clean for the old name; App Insights queries audited; no telemetry-loss window during the cutover.
- **FR-07 (B-4)**: `WorkspaceLayoutDto` MUST expose a `modifiedOn: DateTimeOffset` field, mapped from Dataverse rowversion / `modifiedon`. The Manage Workspace pane MUST render the date for each layout. — Acceptance: Manage pane shows accurate per-layout modified date; new layouts show "just now"; CLAUDE.md §10 Placement Justification recorded.
- **FR-08 (B-5)**: BFF workspace-layout updates MUST support concurrency safety. The endpoint MUST either (a) keep PUT semantics and add `If-Match` ETag header validation, or (b) introduce a separate PATCH endpoint using RFC 6902 JSON Patch. — Acceptance: Two concurrent edits — second one is rejected with 412 (or merged via JSON Patch); CLAUDE.md §10 Placement Justification recorded.
- **FR-09 (B-6)**: `CalendarSidePane`'s local `CalendarSection` copy MUST be deleted; the component MUST be imported from `@spaarke/events-components`. — Acceptance: `CalendarSidePane` on record forms looks and behaves visually + behaviorally identical to SpaarkeAi's embedded Calendar widget.
- **FR-10 (B-7)**: `useEventsBulkActions` hook MUST be extracted to `@spaarke/events-components/src/hooks/`. Both `EventsPage` and the embedded Calendar widget MUST consume it. — Acceptance: Single source of truth; behavior identical in both surfaces.
- **FR-11 (B-8)**: `CalendarDrawer.eventDates` API MUST be upgraded from `string[]` to `IEventDateInfo[]`. Call sites MUST pass the richer shape; rendering MUST use rich info (badges, counts). — Acceptance: Drawer shows event-count badges + overdue indicators per the rich shape; all call sites updated.
- **FR-12 (B-11)**: Type-drift casts (`IEventRecord` index signature, Combobox `onInput` drift, etc.) MUST be tightened bottom-up. Each cast is fixed in its own commit; typecheck passes after each. — Acceptance: 0 type errors on `tsc --noEmit` across affected packages; no remaining intentional casts that papered over real type issues.
- **FR-13 (C-3)**: A single `useWorkspaceLayouts` hook MUST live in `@spaarke/ai-widgets` (or a new `@spaarke/workspace-layouts` package). The hook MUST accept `bffBaseUrl + authenticatedFetch + isAuthenticated` as injected deps, plus optional `parseLayoutJson` + `fallbackLayout` flags. Both LegalWorkspace and SpaarkeAi MUST consume it. SessionStorage cache MUST be centralized inside the hook. — Acceptance: Both surfaces work identically to pre-change; only one hook implementation exists; cache invalidation logic is centralized.
- **FR-14 (C-4)**: A new `WorkspaceRenderer` interface MUST be introduced in `@spaarke/ui-components`. `LegalWorkspaceApp` MUST implement it as the default renderer. `WorkspaceLayoutWidget` MUST accept an injected renderer (or use a renderer registry). Default registration MUST point to `LegalWorkspaceApp` — zero behavioral change today. — Acceptance: Today's behavior identical to pre-refactor; future hosts can register an alternate renderer without modifying `WorkspaceLayoutWidget`.

### Non-Functional Requirements

| ID | Requirement | Target / Acceptance |
|---|---|---|
| **NFR-01 (F-3)** | BFF publish-size verification — binding workflow rule | Every BFF-touching task MUST run `dotnet publish` and verify ≤60 MB compressed baseline. Diff vs baseline reported in task notes. CLAUDE.md §10 + `.claude/constraints/azure-deployment.md` updated. |
| **NFR-02 (F-1)** | Retroactive BFF placement justification (R3 attachment feature) | `projects/spaarke-ai-platform-unification-r3/notes/bff-placement-justification-retroactive.md` published; answers §10 decision criteria; explicitly scoped to R3 (one-time, no precedent for blanket back-audits). |
| **NFR-03 (F-2)** | BFF AI facade audit — count remaining direct injections | `notes/bff-ai-facade-audit-2026-05.md` published. Compares against 2026-05-20 baseline of 20 direct `IOpenAiClient` / `IPlaybookService` deps outside `Services/Ai/`. Recommends migration scope if non-zero. |
| **NFR-04 (B-1)** | Repo hygiene — no tracked build artifacts | After fresh clone + build, `git status` is clean. `.gitignore` covers `deploy/api-publish/*.dll/*.pdb/*.exe` and `src/client/shared/Spaarke.AI.Outputs/src/**/*.{js,d.ts,*.map}`. Currently-tracked stale files removed via `git rm --cached`. |
| **NFR-05 (B-2)** | TypeScript build cleanliness — `@spaarke/ai-widgets` | `tsc --noEmit` passes on the package via `composite: true` project references OR widened rootDir. CI gates the check. |
| **NFR-06 (B-9)** | Linting — ESLint v9 flat config | `Spaarke.UI.Components` migrated from `.eslintrc.json` to `eslint.config.js`. ESLint deps bumped. 0 lint errors. |
| **NFR-07 (B-10)** | Deploy parity — standalone EventsPage | EventsPage web resource redeployed; visual + behavioral parity with embedded version confirmed (de-synced since R10 hoist). |
| **NFR-08 (bundle)** | Bundle-size budget — SpaarkeAi | SpaarkeAi gzip bundle MUST NOT regress by more than 50 KB vs the 918 KB R3 baseline, unless justified in the task notes. F-3 measurement rule applies. |
| **NFR-09 (CVE)** | Security — no new HIGH-severity CVEs | `dotnet list package --vulnerable --include-transitive` on any task that adds/upgrades NuGet packages: 0 new HIGH-severity findings (per CLAUDE.md §10). |

### Documentation Requirements

Doc-only items expressed as DRs. Each lists the backlog item ID in parentheses.

- **DR-01 (W-1)**: Publish `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`. Must frame surfaces (Assistant / Workspace / Context), the two-wrapper model (Dashboard wrapper via `LegalWorkspaceApp` + Direct widget wrapper via `WorkspaceWidgetRegistry`), mount sources (user picker / Assistant / Context / workspace dropdown), dual-use pattern (Calendar / Daily Briefing), and LegalWorkspace-as-dashboard-engine framing. Cross-linked from CLAUDE.md §16 and from `SPAARKEAI-WORKSPACE-ARCHITECTURE.md`. — Acceptance: Doc published; cross-links validated; future widget authors can determine which wrapper to use from this doc.
- **DR-02 (W-2)**: Rewrite `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` with a two-wrapper decision tree (composable section vs sophisticated single-purpose direct widget vs dual-use vs Context-pane widget vs modal-launcher). Terminology corrected. Cross-linked from W-1 doc. — Acceptance: Decision tree present; existing Pattern D (Calendar) worked example retained and updated; new author can pick the right wrapper without back-channel.
- **DR-03 (W-6)**: Publish `docs/architecture/LEGALWORKSPACE-RETIREMENT.md`. Must record the decision to stop deploying `sprk_corporateworkspace`, the rationale (replaced by SpaarkeAi), the components-as-library continuation, and any consumer audit (Dataverse form references etc.). Deploy scripts (`scripts/Deploy-*.ps1`) updated to skip LW deploy. CLAUDE.md System Entry Points pointer updated. — Acceptance: Doc published; deploy scripts skip LW; no consumer breakage discovered post-cutover.
- **DR-04 (A-2)**: Author **ADR-025** (PaneEventBus pattern) and **ADR-026** (stage lifecycle pattern) in both concise (`.claude/adr/`) and full (`docs/adr/`) forms. Both codify R2-invented patterns that R3 used extensively. — Acceptance: Both ADRs published; both forms cross-linked; ADR INDEX updated.
- **DR-05 (D-2)**: Amend ADR-026 with a "Heavy library handling" subsection covering singlefile vs lazy-import incompatibility, Option 2 (separate web resources) pattern, and a link to the R3 bundle-size investigation. — Acceptance: Amendment merged; cross-reference to ADR-026 source-of-truth maintained.
- **DR-06 (C-1)**: Publish `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` — when to use `Xrm.WebApi` vs BFF for Dataverse access, with worked examples from current code. — Acceptance: Doc published; covers both directions with concrete from-the-repo examples; cross-linked from CLAUDE.md §16.
- **DR-07 (C-2)**: Publish `docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` — host requirements (config init, theme ownership, sessionStorage sentinels, `webApi` shim, mount semantics). Reflects LW retirement context (the doc captures what hosts other than SpaarkeAi would need to provide; SpaarkeAi is the only host today). — Acceptance: Doc published; contract is testable; cross-linked from W-1.

### Process Requirements

Process / housekeeping items expressed as PRs.

- **PR-01 (E-1)**: R3 project closure. `projects/spaarke-ai-platform-unification-r3/notes/lessons-learned.md` written. R3 README→Status: Complete. R3 `plan.md` milestones flipped to ✅. `/repo-cleanup projects/spaarke-ai-platform-unification-r3` completed; ephemeral files removed/archived. — Acceptance: R3 README shows Status: Complete; lessons-learned.md present; repo-cleanup audit clean.
- **PR-02 (Phase 7)**: R4 project closure. `notes/lessons-learned.md` for R4 written. R4 README→Status: Complete + Last Updated + Phase: Complete + Completed Date. Graduation criteria boxes checked. `/repo-cleanup projects/spaarke-ai-platform-unification-r4` completed. `current-task.md` reset to none. — Acceptance: R4 README shows Status: Complete; graduation checklist all green; repo-cleanup audit clean.

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|---|---|
| **ADR-001** (Minimal API) | Load-bearing. B-4 (`WorkspaceLayoutDto` extension), B-5 (PUT→PATCH/ETag) inherit Minimal API endpoint patterns. |
| **ADR-008** (Endpoint filters) | B-5 PATCH endpoint inherits the existing endpoint-filter pipeline (auth, rate limit, validation). |
| **ADR-010** (DI minimalism) | A-4 (BFF attachment payload extension), C-3 (consolidated hook) MUST register in existing feature modules — no new modules. |
| **ADR-012** (Shared components) | Load-bearing. C-3 (consolidated `useWorkspaceLayouts`), C-4 (`WorkspaceRenderer` interface), B-6 (CalendarSection import), B-7 (`useEventsBulkActions` hook) all touch shared-lib placement. |
| **ADR-013** (AI architecture) | F-1, F-2 use this as the criterion for placement justification. A-4 attachment path remains in BFF in-process (no new service). |
| **ADR-021** (Fluent design system) | Load-bearing. All UI-touching tasks (W-3, W-4, W-5, B-3, B-6, B-7, B-8) MUST use Fluent v9 tokens only — no hex/rgba/v8. |
| **ADR-022** (React 19 for Code Pages) | Load-bearing for W-3, W-4, W-5 (SpaarkeAi + WorkspaceLayoutWizard). React 19 only. |
| **ADR-025** (PaneEventBus) | NEW — authored by A-2. W-4 and W-5 mount-source wiring MUST conform. |
| **ADR-026** (Stage lifecycle + heavy library handling) | NEW (A-2) + amendment (D-2). Any future heavy-library decision must reference this. |
| **ADR-028** (Spaarke auth v2) | Load-bearing. A-5 (tab persistence), A-4 (attachment auth), C-3 (consolidated hook — takes `authenticatedFetch` as injected dep) all MUST use function-based auth contract. No token snapshots. |
| **ADR-029** (BFF publish hygiene) | F-3 codifies this as a workflow rule. NFR-01 enforces. |

### MUST Rules

- ✅ **MUST** follow CLAUDE.md §10 Placement Justification for every BFF addition (A-4, B-4, B-5, F-1, F-2, F-3). Cite decision criteria from `.claude/constraints/bff-extensions.md`.
- ✅ **MUST** verify publish size ≤60 MB compressed on every BFF-touching task (F-3 / NFR-01).
- ✅ **MUST** use `authenticatedFetch` from `@spaarke/auth` for every BFF call. No token snapshotting (ADR-028 INV-1..INV-8).
- ✅ **MUST** keep PaneEventBus channels typed — no `any` payloads (ADR-025).
- ✅ **MUST** verify A-5 BEFORE remediating — the R3 verification finding may be wrong. Verify-then-fix protocol per plan.md §3.
- ❌ **MUST NOT** introduce new direct injections of `IOpenAiClient` or `IPlaybookService` outside `Services/Ai/` (per F-2 facade rule; refined ADR-013).
- ❌ **MUST NOT** regenerate hardcoded section catalogs in workspace builder code paths (W-3 fix establishes `SECTION_REGISTRY` as single source of truth).
- ❌ **MUST NOT** deploy standalone `sprk_corporateworkspace` web resource (W-6 retirement).
- ❌ **MUST NOT** treat R3 FR-25 / NFR-10 ("standalone LegalWorkspace continues to function identically") as a forward constraint — superseded by W-6.
- ❌ **MUST NOT** add new BFF DI feature modules (ADR-010); extend existing modules.
- ❌ **MUST NOT** introduce React 16 fallbacks (ADR-022); React 19 only for Code Pages.

### Existing Patterns to Follow

- **Calendar widget** (R3 task 115) — canonical Pattern D dual-use widget reference for W-4 / W-5 mount-source demos. See `src/client/shared/Spaarke.events-components/` + `src/solutions/LegalWorkspace/src/registrations/calendar.registration.ts`.
- **`WorkspaceLayoutWidget` → `LegalWorkspaceApp` pipeline** — current dashboard wrapper. W-1 must document it; C-4 must abstract it via `WorkspaceRenderer` interface without behavioral change.
- **PaneEventBus typed channels** (`workspace`, `context`, `conversation`, `safety`) — reference for W-4 / W-5 wiring. See `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBusContext.tsx`.
- **BFF `WorkspaceLayoutsEndpoint.cs`** — extend in B-4 (DTO field) and B-5 (PATCH/ETag). See `src/server/api/Sprk.Bff.Api/Endpoints/Workspace/WorkspaceLayoutsEndpoint.cs`.
- **`SECTION_REGISTRY`** — single source of truth for dashboard sections. W-3 makes `WorkspaceLayoutWizard` consume it. See `src/solutions/LegalWorkspace/src/sectionRegistry.ts`.
- **R3 BFF placement justification template** — F-1 retroactive memo mirrors the forward-going template established by §10. Forward-going examples produced by R4 BFF tasks (A-4, B-4, B-5) themselves serve as the canonical pattern.
- **Lessons learned (R3)** — `projects/spaarke-ai-platform-unification-r3/notes/lessons-learned.md` (written by E-1 in Phase 0) — must be readable before R4 implementation tasks begin.

---

## Success Criteria

Mirrors `plan.md` §7 Graduation Criteria.

### Code + deploy
1. [ ] All 34 IN items shipped to dev environment (`sprk_spaarkeai` updated; `sprk_corporateworkspace` deprecated per W-6)
2. [ ] BFF deploys (A-4, B-4, B-5) verified for publish-size delta; no new HIGH-severity CVEs (NFR-01, NFR-09)
3. [ ] All new ADRs merged to master (ADR-025, ADR-026 + D-2 amendment)
4. [ ] Build clean: 0 `tsc --noEmit` errors across packages; 0 lint errors; 0 build warnings introduced by R4
5. [ ] No tracked build artifacts in `git status` after fresh clone + build (NFR-04 verified)

### Documentation
6. [ ] `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` published (DR-01)
7. [ ] `BUILD-A-NEW-WORKSPACE-WIDGET.md` rewritten with two-wrapper decision tree (DR-02)
8. [ ] `LEGALWORKSPACE-RETIREMENT.md` published (DR-03)
9. [ ] ADR-025 + ADR-026 published in both concise and full forms (DR-04)
10. [ ] ADR-026 amended with heavy library handling section (DR-05)
11. [ ] `DATA-ACCESS-DECISION-CRITERIA.md` published (DR-06)
12. [ ] `LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` published (DR-07)
13. [ ] `CHAT-ATTACHMENT-POLICY.md` published (FR-04)
14. [ ] CLAUDE.md §10 updated with publish-size baseline rule (NFR-01)

### Behavior
15. [ ] `WorkspaceLayoutWizard` shows all 7 sections in picker including Calendar + Daily Briefing (FR-01)
16. [ ] Tab persistence behavior matches operator-confirmed expectation (FR-05)
17. [ ] Assistant → Workspace demo works end-to-end (FR-02)
18. [ ] Context → Workspace demo works end-to-end (FR-03)
19. [ ] Chat attachment upload works at 25 MB; rejects at >25 MB with clear error (FR-04)
20. [ ] Workspace layout `modifiedOn` displayed in Manage pane (FR-07)
21. [ ] PATCH semantics work for layout updates with concurrency safety (FR-08)
22. [ ] CalendarSidePane and SpaarkeAi embedded Calendar visually + behaviorally identical (FR-09)

### Process
23. [ ] R3 marked Complete (PR-01)
24. [ ] R4 lessons-learned.md written; README→Complete (PR-02)
25. [ ] `/repo-cleanup projects/spaarke-ai-platform-unification-r4` completed; ephemeral files removed/archived

---

## Dependencies

### Prerequisites
- R3 master commit `3813af32` (task 140) shipped — confirmed.
- `work/spaarke-ai-platform-unification-r4` worktree created from master — current branch.
- 2026-05-25 scoping decisions finalized — `backlog.md` §Scoping Decisions table.

### External Dependencies

| System | Required when | Risk |
|---|---|---|
| Dataverse dev environment (`spaarkedev1`) | Phases 4, 5 (workspace builder, mount sources, BFF DTO changes) | Low — already used in R3 |
| Azure App Service (BFF deploy slots, production + warmup) | Phase 5 (B-4, B-5, A-4); Phase 2 (F-2 if code changes) | Medium — publish-size verification on every BFF-touching task |
| Operator review | Phase 3 (A-5 verify + fix); Phase 4 (W-3, W-4, W-5 demos) | Medium — verify-then-fix gates remediation; demos are operator-visible |

---

## Owner Clarifications

R4-specific decisions captured during 2026-05-25 scoping. (Reference: `backlog.md` §R4 Scoping Decisions.)

| ID | Topic | Question | Decision | Impact |
|---|---|---|---|---|
| **OC-R4-01** | A-4 attachment cap | Doc-only or also raise the cap? | Code change + raise to 25 MB + policy doc | A-4 became Phase 5 code task, not just Phase 1 doc |
| **OC-R4-02** | A-5 (UQ-03) | Original R3 verification said tabs persist; user feedback contradicts. Verify or remediate first? | Verify first; user feedback says different gap is the visible issue | Phase 3 verify-then-fix structure (~2h verify + ~4-8h fix) |
| **OC-R4-03** | F-1 retroactive memo | Full back-audit of all past projects, or scoped one-time? | Light memo only, scoped to R3 attachment feature | Memo explicitly notes "one-time retroactive close-out; rule applies prospectively from 2026-05-19" |
| **OC-R4-04** | D-2 (bundle size Option 2) | Implement separate web resources or ADR amendment only? | ADR amendment only | Phase 1 scope cap; implementation deferred indefinitely (D-3 follows) |
| **OC-R4-05** | LegalWorkspace future | Standalone code page retired? Components retained? | Standalone code page retired; components retained as library for embed in SpaarkeAi | W-6 retirement doc; deploy script update; R3 FR-25 / NFR-10 superseded |
| **OC-R4-06** | Two-wrapper unification | Merge `LegalWorkspaceApp` + `WorkspaceWidgetRegistry` into one? | Keep both — they serve distinct use cases (compose vs sophisticated single-purpose) | W-1 doc explicitly framed as "two wrappers, intentionally" |
| **OC-R4-07** | W-4 demo scenario | Which scenario for first Assistant → Workspace mount? | Operator chooses at task time; recommended: PDF upload in chat → DocumentViewer tab | W-4 acceptance criteria written flexibly to accommodate operator pick |
| **OC-R4-08** | B-5 API design | PUT + `If-Match` ETag or PATCH + JSON Patch RFC 6902? | Either acceptable; pick at task time based on client-side complexity tradeoff | FR-08 acceptance criteria covers both shapes |

---

## Assumptions

- `LegalWorkspaceApp` components remain available as library imports indefinitely (only the code page deploy is retired per W-6).
- BFF deploy slots (production + warmup) remain stable through R4. No tenant changes.
- Operator availability for verification gates: A-5 (Phase 3); W-3, W-4, W-5 demos (Phase 4).
- 6-concurrent-agent cap holds per `task-execute` skill.
- The 2026-05-20 BFF AI extraction assessment baseline of 20 direct CRUD→AI deps remains the comparison point for F-2 / NFR-03.
- Dataverse rowversion field on `sprk_workspacelayout` table is available for B-4 / B-5 ETag derivation.

---

## Unresolved Questions

Most decisions were operator-finalized 2026-05-25. The remaining open items are task-time choices, not blockers:

- [ ] **FR-02 (W-4) demo scenario** — recommended is PDF upload → DocumentViewer; final pick made at task start in Phase 4
- [ ] **FR-08 (B-5) API design** — PUT+If-Match vs PATCH+JSON-Patch; decide based on client-side complexity tradeoff at task start
- [ ] **NFR-08 bundle delta justification policy** — what counts as "justified" if SpaarkeAi bundle exceeds the +50 KB budget (e.g., is C-3 hook consolidation a free pass since it's a unification refactor?)

---

*AI-optimized specification. Source: [`plan.md`](plan.md) (authoritative WBS) + [`backlog.md`](backlog.md) (per-item rationale). No `design.md` exists for R4 — the project was scoped directly into plan.md + backlog.md.*
