# Spaarke AI Platform Unification R4 — Implementation Plan

> **Status**: Active
> **Created**: 2026-05-25
> **Predecessor**: spaarke-ai-platform-unification-r3 (shipped at master `3813af32`, task 140)
> **Companion**: `backlog.md` (full per-item analysis); `README.md` (project overview)

---

## 1. Executive Summary

### Purpose

R4 consolidates the ~30 follow-up items that surfaced during R3 (tasks 001-140 + 13 rounds of operator polish) into a coherent post-shipping round. Scope was decided on 2026-05-25 with explicit IN / DEFER calls on each item.

R4 also introduces a new architectural framing surfaced during scoping conversations: the **two-wrapper model** (Dashboard wrapper via `LegalWorkspaceApp` + Direct widget wrapper via `WorkspaceWidgetRegistry`) and the formal recognition that **LegalWorkspace's standalone code page is being retired** while the LegalWorkspace components continue as a library embedded in SpaarkeAi.

### Scope

- **34 work items** across 8 phases
- **~116 hours estimated effort** (~14-15 working days)
- Heavy doc + small-code phase early; substantive code refactors in the middle; build hygiene cluster at the end

### Timeline estimate

Sequential execution: ~3-4 weeks. With parallelization on independent items (build hygiene + ADRs + docs), ~2-3 weeks is realistic.

### Out of scope (DEFERRED)

- Stages 2-4 chrome (Moment 2+ work)
- AI-vs-User visual + AIReasoningSurface conventions (strategy-level work)
- Hoist remaining 5 LW-internal section factories (no forcing function; Calendar precedent gives forward path)
- `runtimeConfig` hoist to `@spaarke/auth` (works today)
- Section registry plug-in style (no 3rd-party need)
- Bundle-size Option 2 separate-web-resources implementation (only the ADR amendment is in scope)

---

## 2. Architecture Context

### Inherited foundations from R3

R3 shipped Moment 1 (Arrival) with these load-bearing patterns established:
- **PaneEventBus** — typed multi-subscriber bus with `workspace`, `context`, `conversation`, `safety` channels
- **Widget framework** — `WorkspaceWidgetRegistry` + `ContextWidgetRegistry` + `widget_load` dispatch
- **Dashboard pipeline** — `WorkspaceLayoutWidget` → `<LegalWorkspaceApp embedded>` → section registry → section factories
- **Dual-use pattern** — Calendar (Task 115) + Daily Briefing (Task 069): components in shared lib + thin LegalWorkspace registration shim
- **ThreePaneLayout** — Assistant + Workspace + Context surfaces with all-collapsed empty state
- **Auth v2** — `@spaarke/auth` with `useAuth` + `authenticatedFetch`, function-based contract

### Conceptual model adopted in R4

R4 work establishes and documents this mental model:

```
Spaarke AI page = 3 surfaces (Assistant, Workspace, Context)
   ↓
Each surface mounts content via PaneEventBus widget_load dispatch
   ↓
Content sources:
   - User picker (today)
   - Assistant pane (NEW in R4 — W-4)
   - Context pane (NEW in R4 — W-5)
   - Workspace dropdown / system defaults (today)
   ↓
Two wrappers receive mounts:
   - Dashboard wrapper (LegalWorkspaceApp) — for composable multi-section layouts
   - Direct widget wrapper (WorkspaceWidgetRegistry) — for sophisticated single-purpose tools
   ↓
Both wrappers consume shared lib components (@spaarke/ui-components, @spaarke/ai-widgets,
@spaarke/events-components, @spaarke/auth, etc.)
```

Both wrappers are retained intentionally — they serve genuinely different use cases (compose vs. sophisticated single-purpose). The conversation during scoping concluded this is a strength of the architecture, not a problem to unify.

### LegalWorkspace status

- Standalone code page (`sprk_corporateworkspace`) **being retired** — no longer deployed
- LegalWorkspace components + `LegalWorkspaceApp` renderer **retained as a library**, consumed via embed in SpaarkeAi
- The R3 spec's FR-25 / NFR-10 ("standalone LegalWorkspace must continue to function identically") **no longer applies** going forward
- Treatment in docs going forward: LegalWorkspace IS the dashboard framework / engine

### Tech stack

| Layer | Stack |
|---|---|
| Frontend (Code Pages) | React 19 + Vite + Fluent UI v9 + Griffel (`makeStyles`) |
| Frontend (PCFs) | React 18 + Fluent UI v9 (per ADR-021) |
| Backend | .NET 8 Minimal API (`Sprk.Bff.Api`) per ADR-001 |
| Data | Dataverse (Power Platform) |
| Auth | MSAL via `@spaarke/auth` (ADR-028) |
| Deploy targets | Dataverse web resources (Code Pages); Azure App Service (BFF) |

### Architectural constraints inherited from CLAUDE.md

- **CLAUDE.md §10 BFF Hygiene** (binding) — Any BFF additions require Placement Justification (forward-going from §F-1)
- **ADR-012** — Shared components live in `@spaarke/ui-components` family; context-agnostic
- **ADR-021** — Fluent v9 tokens only; no hex/rgba/v8
- **ADR-022** — React 19 only for Code Pages
- **ADR-028** — Function-based auth; no token snapshots in props/state

### Integration points

| System | How R4 integrates |
|---|---|
| Dataverse | Schema reads/writes via Xrm.WebApi (sections) + BFF (workspace layouts, sessions) |
| BFF (Sprk.Bff.Api) | Attachment payload extension (A-4 raises cap to 25 MB); `WorkspaceLayoutDto.modifiedOn` (B-4); PUT → PATCH (B-5); placement-justification audit (F-1, F-2) |
| Azure App Service | Publish-size baseline tracking (F-3) |
| Microsoft Graph | Unchanged from R3 |
| SharePoint Embedded | Unchanged from R3 |

---

## 3. Implementation Approach

### Phasing philosophy

1. **Documentation FIRST** — establish the conceptual frame (dashboard + widget model, ADRs, decision criteria) before any architectural code refactors. Reasoning: subsequent phases reference the docs; building docs after the code locks in stale terminology.
2. **Verification BEFORE remediation** for ambiguous items — A-5 (tab persistence) starts with a 2h verification spike because user feedback contradicts the original R3 verification finding. Don't pre-build a fix for a non-issue.
3. **Substantive code changes in the MIDDLE phases** — after docs land, before build hygiene. Reasoning: hygiene fixes (linting, type cleanup) are best done LAST so they don't churn during refactors.
4. **Build hygiene CLUSTERED at the end** — most B-items are independent and parallelizable. Doing them together reduces context-switching cost.
5. **R3 wrap-up FIRST** — closes the prior project cleanly. Frees `current-task.md`, marks R3 README → Complete, runs `/repo-cleanup`.

### Parallelization opportunities

| Phase | Parallelizable? | Cap |
|---|---|---|
| Phase 0 | No | 1 task at a time |
| Phase 1 — Docs | Yes — most docs are independent | 4-6 concurrent agents |
| Phase 2 — F-2 audit | No | 1 task |
| Phase 3 — A-5 | Sequential within phase | 1 task |
| Phase 4 — Workspace + mount sources | Sequential (depends on W-1 doc) | 1-2 concurrent |
| Phase 5 — Substantive refactors | Partial — A-4 / C-3 / C-4 independent | 3 concurrent |
| Phase 6 — Build hygiene | Yes — mostly independent | 4-6 concurrent |
| Phase 7 | No | 1 task |

Hard cap of 6 concurrent agents per wave (per `task-execute` skill).

### Source of truth ordering

Per CLAUDE.md §2: code wins over docs. R4 docs reflect the code as of `3813af32` + R4 changes; if any doc contradicts current code state, fix the doc.

---

## 4. Work Breakdown Structure (WBS)

### Phase 0 — R3 wrap-up + retroactive memo (~4h)

**Objective**: Cleanly close R3 and address retroactive governance.

| Task | Effort | Deliverable |
|---|---|---|
| E-1 R3 project wrap-up | ~2h | R3 lessons-learned.md + R3 README → Complete + R3 plan.md milestones ✅ + `/repo-cleanup` |
| F-1 BFF placement-justification retroactive memo (light) | ~1-2h | `projects/spaarke-ai-platform-unification-r3/notes/bff-placement-justification-retroactive.md` — answers §10 decision criteria for the R3 attachment payload feature; cross-linked from R3 lessons-learned and R4 design |

**Phase 0 acceptance**: R3 marked Complete in README + plan; F-1 memo published.

**Phase 0 outputs**: R3 closure; retroactive audit trail.

---

### Phase 1 — Documentation round (~21h)

**Objective**: Establish authoritative conceptual frame for SpaarkeAi widget + dashboard architecture, plus the ADRs and decision criteria that bind subsequent work.

| Task | Effort | Deliverable |
|---|---|---|
| W-1 Write `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` | ~6h | New auth doc framing surfaces + sources + two-wrapper model + dual-use pattern + LegalWorkspace-as-engine. Cross-linked from CLAUDE.md and from SPAARKEAI-WORKSPACE-ARCHITECTURE.md |
| W-2 Rewrite `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` | ~3h | Decision tree: composable section (Pattern D) vs sophisticated single-purpose widget (direct) vs dual-use vs Context-pane widget vs modal-launcher. Corrects terminology. Cross-linked from W-1 doc |
| A-2 Write ADR-030 (PaneEventBus pattern) + ADR-031 (stage lifecycle pattern) | ~6h | `.claude/adr/ADR-030-pane-event-bus.md` (concise) + `docs/adr/ADR-030-*.md` (full). Same for ADR-031. Both codify R2-invented patterns that R3 used extensively |
| C-1 Document Xrm.WebApi vs BFF decision criteria | ~2h | `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` — when to use each, with worked examples from current code |
| C-2 Document embedded-mode contract formally | ~3h | `docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` — host requirements (config init, theme ownership, sessionStorage sentinels, webApi shim, mount semantics). Reflects LW retirement context. |
| D-2 ADR-031 amendment (heavy library handling) | ~4h | Amend ADR-031 with "Heavy library handling" subsection — singlefile vs lazy-import incompatibility, Option 2 pattern (separate web resources) as reference, link to R3 bundle-size investigation |
| F-3 Publish-size baseline + per-task verification rule documentation | ~1h | Update `.claude/constraints/azure-deployment.md` and CLAUDE.md §10 with explicit "every BFF-touching task MUST run `dotnet publish` + diff vs ~60 MB baseline" rule |

**Phase 1 acceptance**: All 7 docs published; W-1 + W-2 + C-1 + C-2 reviewed for terminology consistency.

**Phase 1 outputs**: Authoritative architecture frame established before any code change.

**Parallelization**: W-1 and A-2 and C-1 are independent; C-2 depends on W-1; D-2 ADR + F-3 are independent. Wave 1 (parallel): W-1, A-2, C-1, F-3. Wave 2 (sequential): W-2, C-2, D-2.

---

### Phase 2 — BFF governance audit (~2h)

**Objective**: Audit + count remaining direct CRUD→AI dependencies; establish baseline for migration tracking.

| Task | Effort | Deliverable |
|---|---|---|
| F-2 `Services/Ai/PublicContracts/` facade audit | ~2h | `notes/bff-ai-facade-audit-2026-05.md` — count remaining direct injections of `IOpenAiClient` / `IPlaybookService` outside `Services/Ai/`. Compare against the 2026-05-20 baseline of 20 direct deps. Recommend migration scope if non-zero |

**Phase 2 acceptance**: Audit memo published; remaining count documented.

---

### Phase 3 — UQ-03 verification + fix (~10h)

**Objective**: Verify current state of tab persistence (operator feedback contradicts original R3 verification); address the actually-broken behaviors.

| Task | Effort | Deliverable |
|---|---|---|
| A-5a Verify current tab-persistence behavior | ~2h | `notes/tab-persistence-verification-2026-05.md` — answers: (1) Do tabs persist across browser REFRESH today? (2) Do tabs persist across browser CLOSE/REOPEN today? (3) On reopen, does the active tab default to Home, last-active, or something else? (4) Where is the state stored — sessionStorage, localStorage, BFF, Cosmos? (5) Is the spec/UX expectation that browser-reopen → Home tab? |
| A-5b Fix the actual gap | ~4-8h | Depends on verify result. If sessionStorage holds tabs (cleared on close) → extend to localStorage or BFF for browser-close survival. If active-tab logic doesn't default to Home on reopen → add the default. Acceptance: operator-confirmed behavior matches expectation |

**Phase 3 acceptance**: Verification memo + remediation deployed; operator confirms behavior end-to-end.

**Phase 3 outputs**: UQ-03 fully resolved with corrected understanding.

---

### Phase 4 — Workspace builder fix + mount-source wiring (~19h)

**Objective**: Fix the wizard catalog drift bug discovered during scoping; wire the two not-yet-implemented mount sources; record LW retirement.

| Task | Effort | Deliverable |
|---|---|---|
| W-3 Fix WorkspaceLayoutWizard catalog drift | ~3h | `src/solutions/WorkspaceLayoutWizard/src/App.tsx` reads from `SECTION_REGISTRY` instead of hardcoded `SECTION_CATALOG`. Calendar + Daily Briefing become pickable in the dashboard builder. Acceptance: open wizard, see 7 sections in picker, build a custom dashboard with Calendar + My Documents combined |
| W-6 Document LegalWorkspace code page retirement | ~2h | `docs/architecture/LEGALWORKSPACE-RETIREMENT.md` — records the decision to stop deploying `sprk_corporateworkspace`; updates `scripts/Deploy-*.ps1` to skip LW deploy; updates CLAUDE.md system entry points pointer; ARCHIVE link if appropriate |
| W-4 Wire Assistant → Workspace mount source | ~8h | Pick one demo scenario (recommended: user uploads PDF in chat → DocumentViewer widget appears as workspace tab via `widget_load`). Implement the Assistant-side dispatcher; choose/build a DocumentViewer direct widget; demonstrate end-to-end. Acceptance: operator-visible demo end-to-end |
| W-5 Wire Context → Workspace mount source (one wizard) | ~6h | Pick one wizard (recommended: Create Project) → add "Add result to Workspace" final step → on click, dispatch `widget_load` to mount a result widget as a workspace tab. Establishes the pattern for future wizards. Acceptance: operator-visible demo end-to-end |

**Phase 4 acceptance**: Wizard picker shows all 7 sections; Assistant + Context demos work end-to-end; LW retirement doc published.

**Phase 4 outputs**: All three mount sources (user picker + Assistant + Context) demonstrated working in production.

**Parallelization**: W-3 and W-6 are independent; W-4 and W-5 can be parallel after W-1 + W-3 land.

---

### Phase 5 — Substantive code changes (~31h)

**Objective**: Address operator-mandated code changes (A-4 attachment cap raise) + the architectural refactors deferred from R3 (C-3 hook consolidation, C-4 renderer interface) + BFF DTO/endpoint improvements.

| Task | Effort | Deliverable |
|---|---|---|
| A-4 File-attachment policy + raise cap to 25 MB | ~6h | (1) `docs/standards/CHAT-ATTACHMENT-POLICY.md` — size cap rationale, content-type allow-list, upgrade path. (2) Code change: client-side cap in `useChatFileAttachment.ts` raised from 5 MB → 25 MB. Per-file `MaxAttachmentTextCharsPerFile` reviewed/adjusted. Server-side `MaxAttachmentSizeBytes` in `ChatEndpoints.cs` updated. Tests updated. (3) Bundle-size impact assessed (likely minimal — text content cap, not file binary cap). Operator-confirmed before merging |
| C-3 Consolidate dual `useWorkspaceLayouts` hooks | ~8h | Single hook in `@spaarke/ai-widgets` (or new `@spaarke/workspace-layouts` package) accepting `bffBaseUrl + authenticatedFetch + isAuthenticated` deps; optional `parseLayoutJson` + `fallbackLayout` flags. Both LegalWorkspace + SpaarkeAi consumers adapted. SessionStorage cache centralized. Acceptance: both surfaces work identically to pre-change |
| C-4 `WorkspaceRenderer` interface | ~4h | New `WorkspaceRenderer` interface in `@spaarke/ui-components`. `LegalWorkspaceApp` implements it as the default renderer. `WorkspaceLayoutWidget` accepts an injected renderer (or uses a registry). Default registration points to `LegalWorkspaceApp` — zero behavioral change today. Sets up future-host extensibility |
| B-4 `WorkspaceLayoutDto.modifiedOn` | ~2h | Extend BFF DTO with `modifiedOn: DateTimeOffset`. Update `WorkspaceLayoutsEndpoint.cs` mapping. Update frontend TS type. Update Manage pane to render the date. CLAUDE.md §10 Placement Justification entry |
| B-5 BFF PUT → PATCH with ETag | ~4-6h | Add `If-Match` header support to PUT endpoint (ETag from `modifiedon` rowversion). OR introduce separate PATCH endpoint with JSON Patch RFC 6902. Frontend write path adapts. CLAUDE.md §10 Placement Justification entry |
| B-6 Reconcile CalendarSidePane CalendarSection | ~4h | Delete local divergent copy in `src/solutions/CalendarSidePane/src/components/CalendarSection.tsx`. Import from `@spaarke/events-components` instead. Verify CalendarSidePane visual + behavioral parity with embedded version. Acceptance: CalendarSidePane on record forms looks/behaves like SpaarkeAi's embedded Calendar |

**Phase 5 acceptance**: All 6 items deployed; SpaarkeAi + CalendarSidePane regression-tested; bundle size measured.

**Phase 5 outputs**: User-visible attachment improvements (25 MB cap); architecture cleaned up; BFF audit-trail improvements.

**Parallelization**: A-4 / C-3 / C-4 / B-4 / B-5 are independent. B-6 should follow C-3 if hooks shared. Wave 1: A-4, C-3, B-4, B-5. Wave 2: C-4, B-6.

---

### Phase 6 — Build hygiene cluster (~21h)

**Objective**: Address accumulated build/type/lint debt. Mostly small independent items.

| Task | Effort | Deliverable |
|---|---|---|
| B-1 `.gitignore` for tracked build artifacts | ~1h | Add `deploy/api-publish/*.dll/.pdb/.exe` + `src/client/shared/Spaarke.AI.Outputs/src/**/*.{js,d.ts,*.map}` to `.gitignore`. Remove tracking. `git rm --cached` for currently-tracked stale files |
| B-2 `@spaarke/ai-widgets` tsc cross-rootDir fix | ~3h | Investigate tsconfig rootDir + paths. Fix via `composite: true` project references OR widening rootDir. CI gates `tsc --noEmit` on the package |
| B-3 Telemetry constant rename | ~2h | Rename `TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE → TELEMETRY_HISTORY_LOAD_FAILURE`. Audit App Insights queries that reference the old name. Coordinate the cutover. Tests updated |
| B-7 `useEventsBulkActions` hook extraction | ~3h | Extract to `@spaarke/events-components/src/hooks/useEventsBulkActions.ts`. Both EventsPage + Calendar widget consume. Single source of truth. Acceptance: behavior identical in both surfaces |
| B-8 `CalendarDrawer.eventDates` API drift | ~3h | Update `CalendarDrawer` to accept `eventDates: IEventDateInfo[]`. Update call sites to pass rich shape. Update rendering to use richer info (badges, counts). Bundles with B-6/B-7 work |
| B-9 ESLint v9 migration for Spaarke.UI.Components | ~2h | Migrate `.eslintrc.json` → `eslint.config.js` (flat config). Bump ESLint deps. Run lint clean |
| B-10 Standalone EventsPage redeploy | ~30min | Build + deploy. Verify parity. Done |
| B-11 Type-drift casts cleanup | ~4h | Bottom-up tightening: fix each cast (`IEventRecord` index signature, Combobox `onInput` drift, etc.); typecheck after each; commit per cast |

**Phase 6 acceptance**: 0 type errors, 0 lint errors, no tracked build artifacts in git status.

**Phase 6 outputs**: Clean dev experience for R4-and-beyond authors.

**Parallelization**: All 8 items independent. Run in 1-2 waves of 4-6 agents each.

---

### Phase 7 — R4 wrap-up (~2h)

**Objective**: Close R4 cleanly.

| Task | Effort | Deliverable |
|---|---|---|
| R4 wrap-up | ~2h | `notes/lessons-learned.md` for R4 — record what worked, what didn't, what surprised; update R4 README → Status: Complete, Last Updated, Phase: Complete, Completed Date; check graduation criteria boxes; run `/repo-cleanup projects/spaarke-ai-platform-unification-r4`; reset `current-task.md` to none |

**Phase 7 acceptance**: R4 marked Complete; repo cleanup approved; project archived to graveyard / closed state.

---

## 5. Dependencies

### Within-project dependencies

```
Phase 0 ──┬──> Phase 1 ──┬──> Phase 2
          │              │
          │              ├──> Phase 3 ──> (independent)
          │              │
          │              └──> Phase 4 ──> Phase 5 ──> Phase 6 ──> Phase 7
          │                   (W-3 + W-4 + W-5 depend on W-1 doc)
          │
          └──> (parallel: Phase 6 items can interleave with Phase 5)
```

### External dependencies

| External system | Required when | Risk |
|---|---|---|
| Dataverse dev environment (`spaarkedev1`) | Phases 4, 5 (workspace builder fix, mount sources, BFF DTO changes) | Low — already used in R3 |
| Azure App Service (BFF deploy slots) | Phase 5 (B-4, B-5) + Phase 2 (F-2 if any code changes) | Medium — publish-size verification (F-3) must run on every BFF-touching task |
| Operator review | Phase 3 A-5 verification + Phase 4 demos | Medium — verify-then-fix requires operator to confirm current behavior + remediated behavior |

### File-overlap dependencies (parallelization constraints)

Per `task-create` Step 3.8 + CLAUDE.md "Sub-Agent Write Boundary":
- Any task touching `.claude/` paths MUST be sequential (main session only)
- Two tasks touching the same file MUST be sequential

R4 file-overlap risk areas:
- `ChatEndpoints.cs` — A-4 only
- `WorkspaceLayoutsEndpoint.cs` — B-4 + B-5 (sequence them)
- `WorkspaceLayoutWizard/App.tsx` — W-3 only
- `@spaarke/events-components` — B-6, B-7, B-8 (sequence them OR clean modular split)
- ADR files in `.claude/adr/` — A-2 (sequential main session only)
- `docs/architecture/` — W-1, C-2, W-6 (sequential or sharded by file)

Build hygiene cluster (B-1 .. B-11) has minimal overlap — designed for parallel execution.

---

## 6. Testing Strategy

### Per-task verification

Every code-touching task includes:
- Unit tests where applicable (new behaviors)
- Build verification: `npm run build` or `dotnet build` clean (0 errors, warnings tracked)
- Smoke verification per the `ui-test` skill where applicable
- Bundle-size measurement for SpaarkeAi/LegalWorkspace changes (per F-3 rule going forward)

### Phase-level verification

- **Phase 0**: R3 graduation criteria checklist confirmed
- **Phase 1**: All docs reviewed for terminology consistency (W-1 = canonical source); cross-links validated
- **Phase 2**: Audit memo numerical claims verified by spot-checking 3-5 facade consumers
- **Phase 3**: Operator end-to-end test: open browser, open tabs, close browser, reopen, observe behavior matches the verified expectation
- **Phase 4**: Operator end-to-end test for each of: dashboard builder showing 7 sections; chat upload → workspace tab; wizard "Add to Workspace" → workspace tab
- **Phase 5**: SpaarkeAi + CalendarSidePane regression test; A-4 attachment uploads at sizes [1MB, 10MB, 24MB, 26MB] (boundary)
- **Phase 6**: 0 errors on `tsc --noEmit` per package; 0 lint errors; `git status` shows no tracked build artifacts after fresh clone+build
- **Phase 7**: `/repo-cleanup` audit clean; R4 graduation criteria checked

### Integration testing

- After Phase 5: full SpaarkeAi smoke (Assistant + Workspace + Context) on dev environment
- After Phase 6: same smoke + standalone EventsPage smoke (B-10 ensures it's redeployed)
- After Phase 7: production-readiness gate (operator sign-off)

### Bundle-size budget

Inherited from R3 + F-3 rule:
- SpaarkeAi: current baseline ~918 KB gzip; R4 must not regress by >50 KB unless justified
- LegalWorkspace: current baseline ~589 KB gzip (will be retired but still measured during transition)
- BFF publish: ~60 MB compressed baseline; F-3 rule applies per task

---

## 7. Acceptance Criteria (Graduation)

R4 graduates when ALL of:

### Code + deploy
- [ ] All 34 INCLUDE items shipped to dev environment (`sprk_spaarkeai` web resource updated; `sprk_corporateworkspace` deprecated per W-6)
- [ ] BFF deploys (if any) verified for publish-size delta; no new HIGH-severity CVEs introduced (F-1/F-3 rules applied)
- [ ] All ADRs (A-2: 025, 026; D-2 amendment) merged to main
- [ ] Build clean: 0 errors on `tsc --noEmit` across all packages; 0 lint errors; 0 build warnings introduced by R4 changes
- [ ] No tracked build artifacts in `git status` after fresh clone + build (B-1 verified)

### Documentation
- [ ] `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` published as authoritative architecture source (W-1)
- [ ] `BUILD-A-NEW-WORKSPACE-WIDGET.md` rewritten with two-wrapper decision tree (W-2)
- [ ] `DATA-ACCESS-DECISION-CRITERIA.md` published (C-1)
- [ ] `LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md` published (C-2)
- [ ] `LEGALWORKSPACE-RETIREMENT.md` published (W-6)
- [ ] `CHAT-ATTACHMENT-POLICY.md` published (A-4)
- [ ] ADR-030 (PaneEventBus) + ADR-031 (stage lifecycle) published in both concise + full forms
- [ ] ADR-031 amended with heavy library handling section (D-2)
- [ ] CLAUDE.md §10 updated with publish-size baseline rule (F-3)

### Behavior
- [ ] Workspace builder shows all 7 sections in picker including Calendar + Daily Briefing (W-3)
- [ ] Tab persistence behavior matches operator-confirmed expectation (Phase 3 / A-5)
- [ ] Assistant → Workspace demo works end-to-end (W-4)
- [ ] Context → Workspace demo works end-to-end (W-5)
- [ ] Chat attachment upload works at 25 MB; rejects at >25 MB with clear error (A-4)
- [ ] Workspace layout `modifiedOn` displayed in Manage pane (B-4)
- [ ] PATCH semantics work for layout updates with concurrency safety (B-5)
- [ ] CalendarSidePane and SpaarkeAi embedded Calendar are visually + behaviorally identical (B-6)

### Process
- [ ] R3 marked Complete (Phase 0 / E-1)
- [ ] R4 lessons-learned.md written
- [ ] R4 README → Status: Complete
- [ ] `/repo-cleanup projects/spaarke-ai-platform-unification-r4` completed; ephemeral files removed/archived

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|---|---|---|---|---|
| R-1 | A-5 verification reveals the actual bug is DIFFERENT from what was originally flagged | High | Medium | Phase 3 design assumes this — verify-first approach. Re-scope remediation after verification |
| R-2 | A-4 raising attachment cap to 25 MB blows the bundle-size budget OR breaks BFF text-content limits | Medium | Medium | Bundle impact is from text extraction (capped chars, not raw file bytes) — likely minimal. Confirm in build verification. Server-side `MaxAttachmentTextCharsPerFile` may need to scale proportionally |
| R-3 | C-3 hook consolidation breaks one of the two consumers (LW standalone or SpaarkeAi embed) | Medium | High | Test both surfaces end-to-end after consolidation. Keep auth-source flexibility (hook-deps style). Roll back via revert if needed |
| R-4 | C-4 `WorkspaceRenderer` interface refactor introduces subtle behavioral diff in `LegalWorkspaceApp` mount | Low | Medium | The refactor is intended to be behaviorally identical to today. Default registration points to existing `LegalWorkspaceApp`. Verify via smoke test before merge |
| R-5 | B-2 tsc rootDir fix surfaces previously-hidden type errors across multiple packages | Medium | Medium | Allocate buffer in Phase 6 (~3h estimate may stretch to ~6h). Fix in dedicated commits, not bundled with other Phase 6 work |
| R-6 | LW code page retirement (W-6) breaks an unanticipated consumer (e.g., a Dataverse form linking directly to `sprk_corporateworkspace`) | Low | High | Audit `corporateworkspace` references in Dataverse customizations (`Default Solution > Forms > grep`). Coordinate with any consumer before retiring deploy step |
| R-7 | W-4 / W-5 mount-source wiring requires Assistant or Context pane internal changes that exceed estimate | Medium | Medium | Phase 4 estimates include 8h + 6h for these — accommodate stretch. Scope reduction: ship the dispatch + ONE viewer widget, defer broad coverage to R5 |
| R-8 | Build hygiene tasks (B-2, B-11) cascade — fixing one cast reveals others | Medium | Low | Cluster these in Phase 6 with buffer. Document discovered casts as carry-overs if budget exceeded |
| R-9 | LegalWorkspace retirement assumed but never formally signed off | Low | High | Phase 4 W-6 task includes an operator sign-off step. If not signed off, defer W-6 + retain `sprk_corporateworkspace` deploy. Adjust C-2 contract doc accordingly |
| R-10 | F-1 retroactive memo opens a Pandora's box of "audit every past project" | Low | Low | Memo explicitly says "rule applies prospectively from CLAUDE.md §10 publication date 2026-05-19; this is a one-time retroactive close-out for R3 only" |

---

## 9. Next Steps

After plan.md is approved:

1. **Create `spec.md`** from this plan (operator review + finalization). Spec is shorter; it summarizes the IN items as FRs/NFRs.
2. **Run `/project-pipeline projects/spaarke-ai-platform-unification-r4`** to formalize the project — generates README.md (auto-updated), CLAUDE.md, current-task.md, tasks/ folder + POML files via `task-create`.
3. **Create new worktree** for R4: `git worktree add -b work/spaarke-ai-platform-unification-r4 ../spaarke-wt-spaarke-ai-platform-unification-r4 origin/master` (after R3 wrap-up Phase 0 lands and master is updated).
4. **Phase 0 first**: E-1 (R3 wrap-up) + F-1 (retroactive memo). These two are independent and don't block.
5. **Phase 1 docs**: Parallel agents on independent items (W-1, A-2, C-1, F-3) → then sequential (W-2, C-2, D-2).
6. **Subsequent phases**: Execute per the WBS above.

---

## 10. References

| Document | Purpose |
|---|---|
| [`README.md`](README.md) | Project overview |
| [`backlog.md`](backlog.md) | Full per-item analysis with sources and rationale (preserved for reference) |
| `spec.md` (TBD) | Formalized requirements (FRs/NFRs) — to be created after plan approval |
| `CLAUDE.md` (TBD) | Project-scoped AI context — created by `/project-pipeline` Step 2 |
| `current-task.md` (TBD) | Active task state — created by `/project-pipeline` |
| `tasks/` (TBD) | POML task files — created by `/project-pipeline` Step 3 / `task-create` |

| External | Purpose |
|---|---|
| Predecessor: `projects/spaarke-ai-platform-unification-r3/` | R3 — shipped, master `3813af32` |
| `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` | Current workspace pipeline reference (will be supplemented by W-1) |
| `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md` | Component inventory (informs W-1) |
| `docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md` | Audit that surfaced many C-items |
| `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` | Will be rewritten in W-2 |
| `.claude/constraints/bff-extensions.md` | Binding constraint for F-1, F-2, F-3 |
| `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md` | Evidence base for F-2 |
| `CLAUDE.md` (root) | §10 BFF Hygiene binding rule |

---

## 11. Changelog

| Date | Change | Author |
|---|---|---|
| 2026-05-25 | Initial creation. Phases, WBS, dependencies, risk register, acceptance criteria established. | Claude / spaarke-dev |

---

*End of R4 implementation plan.*
