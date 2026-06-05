# Spaarke AI Platform Unification R4 — Consolidated Backlog

> **Status**: **Scoping decisions made 2026-05-25.** See "R4 Scoping Decisions" section below for the formal IN / DEFER list. The full analysis (Categories A-F + Decision Matrix) is preserved below for reference, but `plan.md` is now the authoritative project plan.
>
> **Date created**: 2026-05-24
> **Date decisions finalized**: 2026-05-25
> **R3 final commit**: `59c1ac3f` (R3 branch) / `3813af32` (master) — task 140 (Fluent Dropdown min-width fix).

---

## R4 Scoping Decisions (2026-05-25)

After review, the operator made the following IN / DEFER decisions plus added a new **Group W** (Workspace + Dashboard architecture) capturing the conversation work on the SpaarkeAi widget framework and LegalWorkspace-as-dashboard-engine framing.

### Status legend
- **IN**: Confirmed for R4 scope.
- **DEFER**: Not in R4; tracked but no near-term work planned. May convert to a separate project or wait for forcing function.
- **MODIFIED**: Item taken into scope with a change to original scope/effort.

### Decisions table

| ID | Item | Decision | Notes |
|---|---|---|---|
| **A-1** | Stages 2-4 (active chat / review / complete) header treatment | DEFER | Moment 2 scope |
| **A-2** | ADR-030 (PaneEventBus) + ADR-031 (stage lifecycle) | **IN** | ~6h doc |
| **A-3** | AI-vs-User visual + AIReasoningSurface convention | DEFER | Major strategy work; out of R4 scope |
| **A-4** | File-attachment policy (FR-07) | **IN — MODIFIED** | **Operator: actual code change (not just doc); raise cap to 25 MB.** ~6h (code + policy doc) |
| **A-5** | Persistence of non-Home tabs across session restore | **IN — MODIFIED** | **Operator: verify first — user feedback says tabs ARE persisting; the operator-visible issue is that browser reopen lands on last tab rather than home/default. Separate from original UQ-03 finding.** Verify-then-fix: ~2h verify + ~4-8h fix depending on what's actually broken |
| **B-1** | `.gitignore` for tracked build artifacts | **IN** | ~1h |
| **B-2** | `@spaarke/ai-widgets` tsc cross-rootDir build error | **IN** | ~3h |
| **B-3** | Telemetry constant rename | **IN** | ~2h |
| **B-4** | `WorkspaceLayoutDto.modifiedOn` missing | **IN** | ~2h |
| **B-5** | BFF PUT → PATCH with ETag | **IN** | ~4-6h |
| **B-6** | `CalendarSidePane` divergent `CalendarSection` | **IN** | ~4h |
| **B-7** | `useEventsBulkActions` hook extraction | **IN** | ~3h |
| **B-8** | `CalendarDrawer.eventDates` API drift | **IN** | ~3h |
| **B-9** | `Spaarke.UI.Components` ESLint v9 migration | **IN** | ~2h |
| **B-10** | Standalone EventsPage redeploy | **IN** | ~30min |
| **B-11** | Type-drift casts cleanup | **IN** | ~4h |
| **C-1** | Xrm.WebApi vs BFF decision criteria doc | **IN** | ~2h |
| **C-2** | Embedded-mode contract doc | **IN** | ~3h |
| **C-3** | Consolidate dual `useWorkspaceLayouts` hooks | **IN** | ~8h |
| **C-4** | `WorkspaceLayoutWidget` hard-wired to LegalWorkspaceApp (introduce `WorkspaceRenderer` interface) | **IN** | ~4h |
| **C-5** | Hoist remaining 5 section factories | DEFER (or RETIRE) | Forcing function absent; Calendar precedent gives forward path. R3 conversation concluded this is non-blocking. |
| **C-6** | Hoist `runtimeConfig` to `@spaarke/auth` | DEFER | Works today; cleaner later |
| **C-7** | Section registry plug-in style | DEFER | No 3rd-party section requirement |
| **D-1** = A-5 | SessionPersistence tab state | (see A-5) | Combined into A-5 work |
| **D-2** | Bundle-size Option 2 (separate web resources) | **IN — ADR ONLY** | **Operator: ADR amendment only; no implementation in R4.** ~4h doc |
| **D-3** | Bundle-analyzer verification deferred | DEFER | Only matters with D-2 implementation |
| **E-1** | R3 project wrap-up (task 090) | **IN** | ~2h — first item in R4 |
| **F-1** | BFF placement-justification audit of R3 (retroactive) | **IN — LIGHT** | **Operator: light notation only.** ~1-2h memo |
| **F-2** | `Services/Ai/PublicContracts/` facade audit | **IN** | ~2h audit |
| **F-3** | Publish-size baseline + per-task verification rule | **IN** | ~1h |

### NEW: Group W — Workspace + Dashboard architecture

Added during 2026-05-25 review based on the conversation that established the two-wrapper mental model (Dashboard wrapper via `LegalWorkspaceApp` + Direct widget wrapper via `WorkspaceWidgetRegistry`) and uncovered the wizard catalog bug.

| ID | Item | Effort | Why |
|---|---|---|---|
| **W-1** | **Write `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`** — new authoritative architecture doc framing surfaces (Assistant/Workspace/Context), the two-wrapper model (Dashboard wrapper + Direct widget wrapper), mount sources (user picker / Assistant / Context), dual-use pattern (Calendar / Daily Briefing), LegalWorkspace as dashboard engine | ~6h | The model exists in the code but is not written down anywhere. Future authors need this. |
| **W-2** | **Rewrite `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`** — replace single-path guide with decision tree: composable section (Pattern D) vs sophisticated single-purpose widget (direct) vs dual-use (both). Include corrected "widget" terminology distinguishing tab-content widgets vs modal-launcher dispatchers vs Context pane widgets. | ~3h | Existing guide pre-dates the conversation's terminology refinement |
| **W-3** | **Fix WorkspaceLayoutWizard catalog drift** — replace hardcoded `SECTION_CATALOG` (5 entries) in `WorkspaceLayoutWizard/src/App.tsx` with read from `SECTION_REGISTRY` (7 entries today, growable). Calendar and Daily Briefing become pickable in the dashboard builder. Future sections auto-appear. | ~3h | **Real bug.** Discovered during R4 scoping. The TODO comment in the code says "In a production build this would be fetched from GET /api/workspace/sections" — it was never wired up. |
| **W-4** | **Wire Assistant → Workspace mount source** — first end-to-end demo of `PaneEventBus.workspace.widget_load` dispatched FROM the Assistant pane. Example: user uploads PDF in chat → DocumentViewer widget appears as a workspace tab. Proves the surface model works beyond user picker. | ~8h | The bus mechanism exists; no Assistant-side caller dispatches today |
| **W-5** | **Wire Context → Workspace mount source (one wizard)** — pick one wizard (e.g., Create Project) → add "Add result to Workspace" final step → mounts result widget as workspace tab. Establishes the pattern; future wizards follow. | ~6h | Same — bus exists, no Context-side caller dispatches today |
| **W-6** | **Document LegalWorkspace code page retirement** — record the decision to stop deploying `sprk_corporateworkspace` (the standalone LegalWorkspace web resource). LegalWorkspace continues as a library consumed via embed in SpaarkeAi only. Update deploy scripts. | ~2h | Operator stated 2026-05-25: "the existing legal workspace code page is likely to not be used anymore (replaced by the new sprk ai UI/UX); the components are still useful but we won't deploy the code page UI" |

**Group W total**: ~28h

### Backlog totals

| Tier | Items | Estimated effort |
|---|---|---|
| IN items (decided 2026-05-25) | 28 items | ~88h |
| Group W (new architecture work) | 6 items | ~28h |
| **Combined R4 scope** | **34 items** | **~116h (~14-15 working days)** |
| DEFERRED items | 8 items | n/a (not in R4) |

### Phase strategy (high level — full detail in plan.md)

1. **Phase 0 — R3 wrap-up + retroactive memo** (~4h): E-1, F-1
2. **Phase 1 — Documentation round** (~21h): W-1, W-2, A-2, C-1, C-2, D-2 ADR, F-3
3. **Phase 2 — BFF governance audit** (~2h): F-2
4. **Phase 3 — Critical verification + fix** (~10h): A-5 (verify-then-fix tab persistence)
5. **Phase 4 — Workspace builder fix + mount-source wiring** (~17h): W-3, W-4, W-5, W-6
6. **Phase 5 — Substantive code changes** (~27h): A-4, C-3, C-4, B-4, B-5, B-6
7. **Phase 6 — Build hygiene cluster** (~21h): B-1, B-2, B-3, B-7, B-8, B-9, B-10, B-11
8. **Phase 7 — R4 wrap-up** (~2h): lessons-learned, README → Complete, /repo-cleanup

See `plan.md` for full phase breakdown, dependencies, acceptance criteria, and risk register.

---

## Executive summary

R3 shipped its full scope (FR-01 through FR-25 + 12 NFRs) plus 13 rounds of operator-driven polish (tasks 091-140). During that work, the team flagged ~30 follow-up items that were intentionally NOT addressed because they were either (a) out of R3 scope, (b) cosmetic-only with no operator-visible behavior change, (c) cross-cutting docs that needed their own dedicated round, or (d) dependent on a future host/use-case not on the current roadmap.

The items cluster into six categories:

| Category | Count | Total est. effort | Risk if deferred |
|---|---|---|---|
| **A.** R3 design out-of-scope (Moments 2-4, AI conventions) | 5 | weeks–months | Low (intentional scope cut; pickup any time) |
| **B.** Multi-round rolling follow-ups (build / type / lint hygiene) | 11 | ~16h | Medium (compounds over rounds) |
| **C.** Componentization audit findings | 7 | ~57h total / ~5h near-term | Medium (drift cost grows; near-term items are doc-only) |
| **D.** R3 verification gaps (session persistence, bundle size) | 3 | ~10h + decisions | Medium (UQ-03 is operator-visible) |
| **E.** R3 project wrap-up | 1 | ~2h | None (housekeeping only) |
| **F.** Cross-cutting BFF / governance | 3 | ~4h | High (BFF size + AI extraction surface area) |

**Highest-priority near-term work** (across all categories):

1. **C-1**: Document Xrm.WebApi vs BFF decision criteria (~2h, doc-only, blocks future divergence) — every audit and deploy memo since round 8 has flagged this.
2. **C-2**: Document embedded-mode contract formally (~3h, doc-only) — informal today; new hosts (Outlook, Teams) would have to reverse-engineer.
3. **D-1**: Decide on task 065 (extend SessionPersistence for tab-state) — **UQ-03 gap is confirmed**: tabs are NOT persisted across refresh today.
4. **E-1**: R3 project wrap-up (task 090) — lessons-learned + status flip + repo cleanup.

**Total recommended R4 minimum scope** (items 1-4 above): ~13 hours, doc + housekeeping heavy. Code change is only D-1 (~8h).

**Bigger options** for R4 if appetite is larger:
- C-3 (consolidate dual `useWorkspaceLayouts`) adds ~8h, eliminates a permanent drift risk.
- B items (build hygiene cluster) add ~16h, clean up build noise that's been compounding.
- F items (BFF governance follow-through) add ~4h, codify the rules R3 introduced.

---

## A. R3 design out-of-scope (carried forward from project start)

R3 design.md explicitly enumerated items the project would not address. They are stated here verbatim so R4 can decide which (if any) to pick up.

### A-1. Stages 2–4 (active chat / review / complete) header treatment

**Source**: R3 [design.md §H, line 319](../spaarke-ai-platform-unification-r3/design.md).

**What R3 said**: "Stages 2–4 (active chat / review / complete) header treatment."

**Context**: R3 only delivered the welcome-stage (Stage 1 / "Arrival") header treatment — the `<PaneHeader>` primitive (FR-01), the unified pane title typography across Assistant / Workspace / Context, and the welcome-stage-specific content (FR-02 through FR-22). The other three stages (Stage 2 = active chat in progress; Stage 3 = review / human-in-the-loop; Stage 4 = complete) reuse R2's chrome.

**Implications**: Stage 2-4 chrome is currently inconsistent — different fonts, different button sets, no PaneHeader. When a user transitions Stage 1 → Stage 2 by sending a chat, the Assistant pane chrome shifts visibly (Stage 1 PaneHeader + tab-less; Stage 2 the older tab strip + larger title).

**Why deferred from R3**: Spec explicitly scoped R3 to "Moment 1: Arrival" (welcome state). Stages 2-4 would have doubled scope.

**Recommendation for R4**: **DEFER** unless an operator-visible inconsistency surfaces. Stage-transition polish is logical Moment 2 (Triage) scope.

**Effort if picked up**: 3-5 days for the three-stage header treatment.

---

### A-2. ADR-030 PaneEventBus pattern + ADR-031 stage lifecycle pattern as ADRs

**Source**: R3 [design.md §H, line 320](../spaarke-ai-platform-unification-r3/design.md).

**What R3 said**: "ADR-030 PaneEventBus pattern + ADR-031 stage lifecycle pattern (lessons-learned candidates from R2)."

**Context**: R2 invented two cross-cutting patterns that R3 inherited and used extensively:

- **PaneEventBus**: A typed, multi-subscriber, DOM-free event bus with channels `workspace`, `context`, `conversation`, `safety`. Lives at [`src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBusContext.tsx`](../../src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBusContext.tsx). Wizard widgets route through `workspace` channel via `widget_load` event. Multi-subscriber pattern (multiple panes can react to the same dispatch).
- **Stage lifecycle**: `determineStage(SessionState)` is a pure function. ShellStageManager maintains a single SessionState ref. All panes read the same stage. No divergence. Lives in `ShellStageManager`.

Neither pattern has an ADR. They are described in R2 lessons-learned and replicated in code.

**Why it matters**: Cross-cutting patterns without ADRs drift. Future authors invent variants. Today's audit ([SPAARKEAI-COMPONENTIZATION-AUDIT.md §9](../../docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md)) lists both as "unambiguously clean" — codifying them as ADRs would lock that in.

**Why deferred from R3**: Out of scope; the patterns are stable and not in conflict.

**Recommendation for R4**: **DEFER** but consider in a "documentation round" alongside C-1 + C-2 (all three are doc-only and benefit from co-authoring).

**Effort if picked up**: ADR-030 ~3h, ADR-031 ~3h, total ~6h.

---

### A-3. Cross-cutting AI-vs-User visual convention + AIReasoningSurface convention

**Source**: R3 [design.md §H, line 321](../spaarke-ai-platform-unification-r3/design.md) and [§N, line 431](../spaarke-ai-platform-unification-r3/design.md).

**What R3 said**: "Cross-cutting AI-vs-User visual convention + AIReasoningSurface convention (Phase A foundations from strategy doc)."

**Context**: The Spaarke UI/UX strategy doc (`projects/spaarke-UI-UX-strategy-plan/`) defines two conventions that have NOT been implemented:

- **AI-vs-User visual distinction** (strategy §5.1 #1): A consistent visual treatment that distinguishes content / actions that originated from the AI vs the user. Today the convention is ad hoc — chat bubbles look different from user inputs, but other AI-generated content (Daily Briefing, Get Started cards from AI, AI-pre-filled forms) does not have a unified treatment.
- **AI working indicator** (strategy §5.1 #3): A consistent "AI is doing something" affordance. Today: SprkChat has a streaming indicator, Daily Briefing has a Spinner, Workspace tabs have a tab-loading badge — three different patterns for the same semantic.
- **AI reasoning surface** (strategy §5.1 #5/#23): A consistent surface for showing the AI's reasoning trace, citations, sources. Today: chat shows inline citations; Daily Briefing has separate sources list; SemanticSearchControl has yet another model.

**Why deferred from R3**: These are Phase A foundations from the strategy doc, sized as a multi-week project on their own. R3 scope was Moment 1 chrome polish, not foundation work.

**Recommendation for R4**: **DEFER**. This is more "next major round" scope than "R4 polish round" scope. But it should be tracked as a known future need — the longer it's deferred, the more inconsistencies accumulate.

**Effort if picked up**: Strategy spec + design = 2-3 days; implementation = 1-2 weeks per surface × 3 surfaces.

---

### A-4. File-attachment size cap + content-type allow-list policy (FR-07)

**Source**: R3 [design.md §H, line 323](../spaarke-ai-platform-unification-r3/design.md).

**What R3 said**: "File-attachment size cap + content-type allow-list policy (F-2) — needs a policy doc; defaulting to <10 MB and text/PDF/DOCX for v1."

**Context**: R3 shipped FR-07 (in-message file attach, max 5 files per message, client-side PDF/DOCX/text extraction via pdfjs-dist + mammoth). The frontend hook ([useChatFileAttachment.ts](../../src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts)) enforces:
- Max 5 files per message
- Allowlist: `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `text/plain`, `text/markdown`
- Size cap of 5 MB per file (per the spike memo)

BFF-side (ChatEndpoints.cs after task 050) validates:
- Filename length ≤256 chars
- TextContent length ≤2.5M chars
- Sum of all TextContent lengths capped (avoid 5 × 2.5M = 12.5M ballooning the LLM prompt)

But there is **no policy doc** stating WHY these limits, what the rollout plan is, and how a customer would request raising them.

**Why deferred from R3**: Spec said "needs a policy doc"; R3 shipped the implementation but not the doc.

**Recommendation for R4**: **INCLUDE if R4 also touches BFF AI**. Light: 2-3 page doc + cross-link from FR-07 in spec and ADR-013. ~3h.

**Effort if picked up**: ~3h doc-only.

---

### A-5. Persistence of which non-Home tabs are open across session restore

**Source**: R3 [design.md §H, line 322](../spaarke-ai-platform-unification-r3/design.md) AND [notes/backwards-compat-verification.md task 063 result](../spaarke-ai-platform-unification-r3/notes/backwards-compat-verification.md).

**What R3 said (design.md)**: "Persistence of which non-Home tabs are open across session restore (D-08 already restores widget state; confirm coverage)."

**What task 063 verification found**: ❌ **GAP CONFIRMED**. UQ-03 surfaced during R3 Phase F verification. Tabs are NOT persisted across refresh today.

**Detail**: R2's `SessionPersistenceService` serializes individual widget data but does NOT serialize the tabs list (which tab IDs exist, what kind, what `displayName`, which is active). On refresh:
- Today: User opens 3 non-Home tabs (e.g., "Create Project", "Find Similar", "Workspace ABC"). Refreshes browser. All 3 tabs are gone; only Home tab remains.
- Desired: All 3 tabs restore, active tab is preserved, widget data re-fetches via D-08 data-refreshed pattern.

**Recommended follow-up task (from verification memo)**: New POML task **065-extend-sessionpersistence-tab-state** with this scope:

1. Extend `StoredSession` schema (BFF + Cosmos) with a `tabs: WorkspaceTab[]` field. Each entry: `{id, kind, widgetType, displayName}`. EXCLUDE `Component` and `widgetData` (keep payload small; rely on D-08 data-refreshed restore to rehydrate widget data via re-fetch).
2. Wire `WorkspaceTabManager` to dispatch a write-through call after each `addTab` / `closeTab` / `setActiveTab` / `clearAllTabs` mutation. Use `authenticatedFetch` per ADR-028.
3. Wire `WorkspacePane` to read the tabs array from `SessionRestoreService` response on mount. Replay `addTab(widgetType, data, displayName)` for each restored tab in canonical order. Then `setActiveTab(activeTabId)`.
4. **p95 restore latency < 500 ms** target inherited from D-08.
5. **Acceptance**: Open 3 non-Home tabs, refresh, observe all 3 tabs + the selected active tab restored within 500 ms.

**Spec acknowledgement (already in R3 docs)**:
- `spec.md` UQ-03 (R3 spec line 277): "does R2's SessionPersistenceService already serialize the tabs list, or only individual widget state? Blocks: NFR-09 verification — may surface a backend sub-task if not covered."
- `spec.md` A-4 (R3 spec line 267): "If verification (§G #13) reveals gaps, extend SessionPersistenceService to include the tabs array — tracked as design.md §H follow-up."
- `design.md` R-9 (R3 design line 420): "Session-restore (R2 D-08) currently restores widget state but may not include the tabs-list across reloads… Confirm coverage in §G #9-13; if missing, extend SessionPersistenceService to include tabs array."
- `plan.md` (R3 plan line 186): "Session persistence of non-Home tabs (NFR-09 / UQ-03): may require BFF/Cosmos extension. Mitigation: verify in Phase F task 063; opens sub-task if gap surfaces."

**Recommendation for R4**: **INCLUDE — high priority**. The gap is operator-visible (lost tabs on refresh) and the spec already designated R4 / Phase H as the place to address it.

**Effort**: ~8h. BFF schema extension + frontend write-through + restore replay + acceptance test.

---

## B. Multi-round rolling follow-ups (build / type / lint hygiene)

These are "carried from task X" items that have been re-flagged across multiple rounds (R8, R10, R11, R13) without being fixed. They are noise in build output, drift risk, or maintenance smells.

### B-1. `.gitignore` entries missing for tracked build artifacts

**Source**: Flagged in [r8-plan.md line 56](../spaarke-ai-platform-unification-r3/notes/r8-plan.md), then re-flagged in EVERY subsequent task's deploy memo as "Pre-existing follow-ups discovered (NOT fixed)".

**Detail**: Two clusters of build artifacts are committed to the repo and dirty in every worktree:

1. **`deploy/api-publish/*.dll`, `*.pdb`, `*.exe`**: BFF publish output. Should be in `.gitignore` (or moved out of `deploy/` if that path is the canonical publish target).
2. **`src/client/shared/Spaarke.AI.Outputs/src/**/*.{js,d.ts,*.map}`**: Stray tsc output sitting next to .ts source files. Should be either (a) configured to output to a separate `dist/` dir or (b) `.gitignore`d.

Today: every `git status` in any worktree shows ~10-30 dirty files that aren't actually changes — they're stale build output. This is noise that masks real changes during code review.

**Why deferred from R3**: Cosmetic; touching `.gitignore` mid-project would interfere with cherry-pick workflows.

**Recommendation for R4**: **INCLUDE**. Single PR, ~1h, eliminates noise across all worktrees forever.

**Effort**: ~1h.

---

### B-2. `@spaarke/ai-widgets` tsc cross-rootDir build error (Vite production builds succeed)

**Source**: [r8-plan.md line 57](../spaarke-ai-platform-unification-r3/notes/r8-plan.md), re-flagged in task 107 deploy memo (line 3553).

**Detail**: Running `npm run build` (which invokes `tsc`) on `@spaarke/ai-widgets` fails with cross-rootDir typings errors. Vite production builds (`npm run build` in solutions that consume it) succeed because Vite uses esbuild and doesn't apply the strict tsc rootDir check.

**Why it matters**:
- CI can't gate on `tsc --noEmit` for that package (would always fail).
- IDE shows red squiggles on legitimate cross-package imports.
- New contributors are confused: "the build is broken but the app deploys".

**Recommended fix path**:
- Investigate the tsconfig — is `rootDir` set too narrowly? Is a sibling package leaking types via `paths`?
- Likely fix: either (a) widen `rootDir` to the shared root, or (b) make the cross-package imports go through proper `composite: true` project references.

**Why deferred from R3**: Not a runtime issue; deferred to a dedicated build-hygiene round.

**Recommendation for R4**: **INCLUDE**. Touches one tsconfig.json + maybe one package.json change. ~3h to investigate + fix + verify.

**Effort**: ~3h.

---

### B-3. Telemetry constant rename `TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE → TELEMETRY_HISTORY_LOAD_FAILURE`

**Source**: [r8-plan.md line 58](../spaarke-ai-platform-unification-r3/notes/r8-plan.md), with detailed rationale in [deploys/2026-05-20-deploy.md task 097 section, line 2263-2265](../spaarke-ai-platform-unification-r3/notes/deploys/2026-05-20-deploy.md).

**Detail**: The constant `TELEMETRY_HISTORY_OVERLAY_LOAD_FAILURE` (in `src/solutions/SpaarkeAi/src/telemetry/errorTelemetry.ts`) references the obsolete "overlay" surface — R3 Task 097 replaced the History overlay with a History dropdown menu pattern. The constant still works but its name is misleading.

**What R3 said about deferring**:
> "Renaming to `TELEMETRY_HISTORY_LOAD_FAILURE` (and updating App Insights queries that consume it) is a candidate cosmetic follow-up. NOT fixed here because (a) the rename would touch the telemetry test file and require coordinating with any App Insights queries that already filter on the existing name, (b) operator-visible behavior is unchanged, (c) sibling Wave-2 tasks are landing in parallel and we want minimal cross-task churn."

**Why it matters**:
- App Insights queries written against the OLD name will silently match nothing if we rename the emitter without updating queries. **Coordination is required**.
- Stale name encourages new authors to assume there's still an "overlay" concept.

**Recommendation for R4**: **INCLUDE** if R4 includes a "dev experience" mini-phase. Pair the rename with an App Insights query inventory — confirm no production dashboard / alert depends on the old name.

**Effort**: ~2h (rename + test update + App Insights query inventory).

---

### B-4. BFF `WorkspaceLayoutDto` missing `modifiedOn` field

**Source**: [r8-plan.md line 59](../spaarke-ai-platform-unification-r3/notes/r8-plan.md).

**Detail**: The BFF DTO `WorkspaceLayoutDto` (returned by `GET /api/workspace/layouts`) does NOT include a `modifiedOn` field. The Manage Workspace pane shows a fallback placeholder ("—" or similar) where the last-modified date would go.

**Why it matters**:
- Manage pane is functionally incomplete — operators can't tell when a layout was last edited.
- The data IS in Dataverse (`sprk_workspacelayout.modifiedon`); just not projected into the DTO.

**Recommended fix**: Extend `WorkspaceLayoutDto` with `modifiedOn: DateTimeOffset`. Update the `WorkspaceLayoutsEndpoint.cs` mapping. Update frontend `WorkspaceLayoutDto` TS shape. Update Manage pane to render the date.

**Why deferred from R3**: Not a blocker; the Manage pane was acceptable with the fallback.

**Recommendation for R4**: **INCLUDE if R4 touches the Manage pane**. Otherwise defer — it's a single-DTO field that takes ~2h round-trip but only helps a specific surface.

**Effort**: ~2h.

---

### B-5. BFF `PUT /api/workspace/layouts/{id}` is full-overwrite, not PATCH

**Source**: [r8-plan.md line 60](../spaarke-ai-platform-unification-r3/notes/r8-plan.md).

**Detail**: The current endpoint for updating a workspace layout is `PUT` (full replacement). There is no `PATCH` semantic. There is also no ETag concurrency control.

**Why it matters**:
- **Lost-update problem**: If two users edit the same layout concurrently, the last writer wins silently. Today this is unlikely (most layouts are user-owned, not shared) but becomes a risk if shared layouts are introduced.
- **Bandwidth**: Updating just one section name re-sends the entire `sectionsJson` (often 5-10 KB). For frequent toggles (e.g., section visibility), this adds up.
- **API hygiene**: REST best practice is PATCH for partial updates with ETag for concurrency.

**Recommended fix path**:
- Add `If-Match` header support to the PUT endpoint (ETag from `modifiedon` rowversion).
- OR introduce a separate PATCH endpoint with JSON Patch RFC 6902 semantics.

**Why deferred from R3**: Not operator-visible today; correct API design but no current pain.

**Recommendation for R4**: **DEFER** unless shared layouts become a roadmap item. Document as "known limitation, will revisit when shared layouts ship". ~0h to defer; ~4-6h to actually fix.

**Effort if picked up**: ~4-6h.

---

### B-6. `CalendarSidePane` web resource still maintains its own copy of `CalendarSection` divergent from `@spaarke/events-components`

**Source**: Flagged in tasks 114/115/116/118/119/120/121/122 deploy memos (carried from R10 onward — see [deploys line 5439-5442](../spaarke-ai-platform-unification-r3/notes/deploys/2026-05-20-deploy.md)).

**Detail**: When R3 Task 114 hoisted Events components to `@spaarke/events-components` (the new shared lib), the standalone `CalendarSidePane` web resource was left with its own divergent copy at `src/solutions/CalendarSidePane/src/components/CalendarSection.tsx`. Tasks 115-122 polished the shared-lib version (event-day highlight, click-to-filter, layout='horizontal' mode, collapse-to-filter-row, etc.) but CalendarSidePane's local copy was NOT updated.

**Why it matters**:
- **Visual / behavioral inconsistency**: CalendarSidePane's calendar looks and behaves like the pre-R10 version. SpaarkeAi's embedded Calendar widget looks like the post-R13 version. Same component conceptually, two different UXs.
- **Drift risk**: Future polish to the shared `CalendarSection` will not propagate to CalendarSidePane — divergence widens with each round.
- **Operator confusion**: A user who sees both surfaces (CalendarSidePane in a record form + Calendar widget in SpaarkeAi) sees two "calendars" with different behavior.

**Recommended fix**: Reconcile `CalendarSidePane/src/components/CalendarSection.tsx` against `@spaarke/events-components/src/components/CalendarSection.tsx`. Either (a) delete the local copy and import from the shared lib, or (b) keep the local copy but match feature parity.

**Why deferred from R3**: Operator focus was SpaarkeAi only for R10-R13.

**Recommendation for R4**: **INCLUDE**. Single file consolidation. ~4h.

**Effort**: ~4h.

---

### B-7. `useEventsBulkActions` hook extraction (bulk-action handler duplication)

**Source**: Carried from task 115; re-flagged in tasks 116/118/119/120/121/122. See [deploys line 5216-5218](../spaarke-ai-platform-unification-r3/notes/deploys/2026-05-20-deploy.md).

**Detail**: The standalone EventsPage and the Calendar widget BOTH implement the same bulk-action handlers (select all, deselect, delete selected, etc.). The logic is currently duplicated across two files instead of extracted to a shared hook.

**Why it matters**:
- **Drift risk**: Bug fixed in EventsPage's handler won't propagate to Calendar widget's handler.
- **Maintenance**: Two places to update for every new bulk action.
- **Test surface**: Same logic tested twice with slightly different test setups.

**Recommended fix**: Extract to `@spaarke/events-components/src/hooks/useEventsBulkActions.ts`. Replace both consumers. Single source of truth.

**Recommendation for R4**: **INCLUDE** — natural follow-on to Task 114's events-components hoist. ~3h.

**Effort**: ~3h.

---

### B-8. `CalendarDrawer.eventDates: string[]` vs `IEventDateInfo[]` API drift

**Source**: Carried from task 116; re-flagged in tasks 118/119/120/121/122. See [deploys line 5222-5224](../spaarke-ai-platform-unification-r3/notes/deploys/2026-05-20-deploy.md).

**Detail**: The `CalendarDrawer` component accepts `eventDates: string[]` (just a list of date strings). The newer `IEventDateInfo[]` shape (which includes `{ date, eventCount, hasOverdue, ... }`) is richer and is what consumers actually want. Today the call sites bridge with a cast — they pass `IEventDateInfo[].map(d => d.date)` to convert to the older `string[]` shape, losing the metadata.

**Why it matters**:
- **Information loss**: CalendarDrawer can't render badges, overdue indicators, or counts because the older shape strips that info.
- **Cast smell**: The cast at the call site is brittle — easy to forget when adding a new consumer.

**Recommended fix**: Update `CalendarDrawer` to accept `eventDates: IEventDateInfo[]`. Update call sites to pass the rich shape directly. Update CalendarDrawer's rendering to use the richer info where useful (badges, counts).

**Why deferred from R3**: Operator focus was on Calendar widget for SpaarkeAi, not CalendarDrawer.

**Recommendation for R4**: **INCLUDE** as part of the events-components consolidation pass alongside B-6 and B-7. ~3h.

**Effort**: ~3h.

---

### B-9. `Spaarke.UI.Components` ESLint v9 migration

**Source**: Re-flagged in multiple R10-R13 deploy memos.

**Detail**: The shared component library still uses ESLint v8 configuration. ESLint v9 introduces flat config; the rest of the repo may have already migrated. The package's CI lint step works but on the older config.

**Why it matters**:
- **Inconsistent rules**: If other packages have migrated to v9 with new rules, `Spaarke.UI.Components` enforces a slightly different ruleset.
- **Maintenance**: ESLint v9 is the support track going forward.

**Recommended fix**: Migrate `.eslintrc.json` → `eslint.config.js` (flat config). Bump ESLint dep version. Test that lint still passes.

**Why deferred from R3**: Not a runtime issue.

**Recommendation for R4**: **INCLUDE** as part of B-cluster build-hygiene round. ~2h.

**Effort**: ~2h.

---

### B-10. Standalone EventsPage code page not redeployed in R10-R13

**Source**: Carried from task 115; re-flagged in tasks 116/118/120/122. See [deploys line 5225-5226](../spaarke-ai-platform-unification-r3/notes/deploys/2026-05-20-deploy.md).

**Detail**: When Task 114 hoisted Events components to `@spaarke/events-components` and Task 115 built the Calendar widget for SpaarkeAi, the standalone EventsPage was rebuilt (verified byte-identical in tasks 116/118 with +0.32 KB / -0.01 KB deltas) but was NOT redeployed to Dataverse. Operator focus throughout R10-R13 was SpaarkeAi (`sprk_spaarkeai` web resource).

**Why it matters**:
- **Functional drift**: EventsPage's deployed web resource is the pre-R10 build (no shared-lib code). Users hitting the standalone page get the older code path.
- **Future regression risk**: If a future task touches `@spaarke/events-components` and breaks something, the bug only manifests in SpaarkeAi until EventsPage is redeployed — could mask the issue.

**Recommended action**: Build + deploy EventsPage. Verify functional parity. Move on.

**Why deferred from R3**: Operator was focused on SpaarkeAi UX; explicit "redeploy EventsPage later" decision.

**Recommendation for R4**: **INCLUDE** — single deploy step. ~30 minutes.

**Effort**: ~30min.

---

### B-11. Type-drift casts cleanup (`IEventRecord` index signature, Combobox `onInput` drift)

**Source**: Task 114 type bridges; re-flagged in tasks 115/116/118/120/121.

**Detail**: When `@spaarke/events-components` was hoisted in Task 114, several "type bridges" were introduced as casts at call sites:
- `IEventRecord` has an index signature `[key: string]: unknown` to handle dynamic Dataverse field shapes — but this defeats type-checking on field access.
- Combobox `onInput` handler drifted between expected `React.FormEvent<HTMLInputElement>` (Fluent v9 typing) and what the consumer actually passed (a raw string sometimes, sometimes the event).
- `CalendarDrawerProps.eventDates` shape (covered in B-8).

**Why it matters**:
- **Type safety holes**: Each cast is a place where the compiler can't catch a wrong access.
- **Future refactor risk**: Removing one cast may reveal latent bugs at OTHER call sites that depend on the loose typing.

**Recommended approach**: Bottom-up tightening. Fix each cast in turn; run typecheck after each; commit per cast.

**Recommendation for R4**: **INCLUDE** as part of B-cluster build-hygiene round, but estimate generously — type tightening cascades. ~4h budget.

**Effort**: ~4h.

---

## C. Componentization audit findings

These are the gap items identified in [SPAARKEAI-COMPONENTIZATION-AUDIT.md](../../docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) (Task 123 refresh, 2026-05-22). The audit assessed whether the SpaarkeAi workspace pipeline (post-Round 13 / Calendar widget + polish) is componentized and reusable. **Verdict from audit**: structurally sound and works end-to-end, but six concrete gaps affect maintenance + extendability.

Each item below states what's coupled today, why it matters, and the audit's remediation direction — with all detail inlined so no cross-reference is required.

### C-1. Document Xrm.WebApi vs BFF decision criteria 🔥 **HIGHEST PRIORITY**

**Source**: Audit §4 + §8 row 1.

**What's coupled today**: The codebase mixes Xrm.WebApi calls and BFF calls without a documented rule. Examples:

| Code path | Channel | Why |
|---|---|---|
| `useQuickSummaryCounts.ts:53,65` | `webApi.retrieveMultipleRecords` | Aggregate counts across N entities |
| `useDocumentsTabList` | LegalWorkspace `DataverseService` (wraps Xrm.WebApi) | Document list per record |
| `useDailyBriefing` | BFF (`/api/ai/dailybriefing`) | AI-curated content (must run server-side) |
| `useWorkspaceLayouts` (both copies) | BFF (`/api/workspace/layouts`) | Cross-user system layout merge logic lives server-side |
| `useChatFileAttachment` | BFF (chat endpoints) | AI session state |
| Tab persistence (`PATCH /api/ai/chat/sessions/{id}/tabs`) | BFF | Server-side persistence |

The R8 Wave 3a operator brief on My Work card counts explicitly said "use Xrm.WebApi like the existing 4 cards" — but the rationale is NOT in any guide today. Task 114 (Events components hoist) reinforced the unwritten norm: every service in `@spaarke/events-components/services/` uses Xrm.WebApi exclusively. The pattern is consistent in practice; it remains undocumented as a decision rule.

**Why it matters**:
- **New section authors guess wrong**: A developer writing a new section may default to BFF because it feels more "modern", adding load to the BFF without need. Or they default to Xrm and miss cases that need server-side aggregation.
- **Embed portability**: Xrm.WebApi is unavailable outside MDA / model-driven contexts (no Xrm in Outlook, Teams, mobile shell). If we ever ship the workspace pipeline outside Dataverse hosts, every Xrm dependency becomes a port blocker.
- **Auth surface inconsistency**: Xrm.WebApi uses Xrm's auth (Dataverse cookie / OBO via host) — `authenticatedFetch` uses Bearer via `@spaarke/auth`. They are NOT interchangeable.

**Remediation direction (from audit)**: Publish a decision-criteria addendum in `docs/standards/INTEGRATION-CONTRACTS.md` (or a new `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`):

- **Use Xrm.WebApi when**:
  - Data is in Dataverse.
  - The call is read-only OR a simple CRUD.
  - No server-side merge / cross-tenant / AI grounding required.
  - Call-site already has Xrm context.
  - There's no plan to embed in non-MDA hosts.
- **Use BFF when**:
  - Server-side merge logic (e.g., system + user layouts).
  - AI-curated content.
  - Cross-tenant aggregation.
  - NFR-09 session persistence.
  - Future-host portability anticipated.
  - The data needs SharePoint Embedded access.

**Audit recommendation**: "**Recommended next round** — this is the highest-priority doc-only item; every subsequent section author will hit the question."

**Recommendation for R4**: **INCLUDE — top priority**. Doc-only. ~2h.

**Effort**: ~2h.

---

### C-2. Document embedded-mode contract formally

**Source**: Audit §5 + §8 row 2.

**What's coupled today**: `LegalWorkspaceApp` accepts an `embedded?: boolean` prop. When `true`, it skips:
1. The internal `<PageHeader>` (Task 087).
2. The footer.
3. The outer `<FluentProvider>`.
4. The cross-device theme sync side effects (Task 087 + 105).
5. `useDailyDigestAutoPopup` indirectly (via the `spaarke_dailyDigestShown` sessionStorage sentinel set by SpaarkeAi's main.tsx — Task 105).

The contract is encoded in the file's docblock (lines 36-48) but is not formal — there's no `EmbeddedModeContract` interface, no TypeScript exhaustiveness, no test that asserts the contract.

**Why it matters**:
- **A new host doesn't know what to pre-arrange**: SpaarkeAi knows to set `sessionStorage["spaarke_dailyDigestShown"]` BEFORE mounting LegalWorkspaceApp — but only because Task 105 told us. A new host (Outlook side pane, Teams app) would have to reverse-engineer this from the source.
- **Implicit `setLegalWorkspaceRuntimeConfig` requirement**: SpaarkeAi's main.tsx calls this with the SAME config the SpaarkeAi singleton uses (lines 222-235). A new host would have to do the same — there's no fallback that would surface "you forgot to init my config" until a downstream call throws.
- **Cross-device theme sync is a side-effect-free assumption**: SpaarkeAi assumes the host owns theme. A future host that DOESN'T own theme would need its own arrangement.

**Remediation direction (from audit)**: Document a formal "embedded-mode host contract" inside `LegalWorkspaceApp.tsx` (or alongside it as `EMBEDDED-MODE-CONTRACT.md`):

- Host MUST call `setLegalWorkspaceRuntimeConfig(config)` BEFORE mounting.
- Host MUST own theme (`FluentProvider` + theme sync).
- Host MUST set `sessionStorage["spaarke_dailyDigestShown"]` before mount if it wants to suppress the auto-popup.
- Host MUST provide a stable `webApi` reference (or a non-Xrm shim implementing the `IWebApi` interface).
- Host MUST not unmount-remount on every layout change (LegalWorkspaceApp's internal hooks would re-fire).

A future hardening would convert these from prose into TypeScript types.

**Recommendation for R4**: **INCLUDE**. Doc-only (prose version). ~3h.

**Effort**: ~3h prose; ~8h if we also do the typed version.

---

### C-3. Consolidate the dual `useWorkspaceLayouts` hooks

**Source**: Audit §1 + §8 row 3.

**What's coupled today**: Two separate files implement essentially the same BFF fetch:

- `src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts` — canonical implementation. Returns `activeLayoutJson: LayoutJson` (parses sectionsJson). Has `SYSTEM_DEFAULT_LAYOUT` fallback so the workspace always renders something. Uses module-level `authenticatedFetch` + `getBffBaseUrl()`.
- `src/solutions/SpaarkeAi/src/hooks/useWorkspaceLayouts.ts` — "faithful adaptation" (per its own docblock). Sources auth from `useAiSession()` (hook-based instead of module-level — fixes a config-race bug from Task 081). Drops the LayoutJson parsing. Drops the SYSTEM_DEFAULT_LAYOUT fallback (degrades to "no workspace" empty state instead).

Both hit the same BFF endpoints (`GET /api/workspace/layouts` + `GET /api/workspace/layouts/default`). The LegalWorkspace hook runs INSIDE the embedded `LegalWorkspaceApp` to drive `WorkspaceGrid`. The SpaarkeAi hook runs inside `WorkspacePane` to populate the Workspaces dropdown + drive the auto-install default tab.

**Why it matters**:
- **Drift risk**: Any BFF schema change requires editing two files in lockstep. The docblock on the SpaarkeAi hook explicitly says "KEEP THIS FILE IN SYNC if the LegalWorkspace hook changes its fetch shape, cache strategy, or selection cascade" — that's a maintenance smell.
- **Subtly different cache shapes**: LegalWorkspace stores `LayoutJson` in sessionStorage; SpaarkeAi stores just the list. A future "share cache across both" optimization would require explicit reconciliation.
- **Auth-surface inconsistency**: One uses module-level auth, the other uses hook-deps auth. This is itself a coupling that masked the Task 081 race condition — the bug was specifically introduced by the module-level pattern.

**Remediation direction (from audit)**: Hoist the canonical hook to `@spaarke/ai-widgets` (or a new `@spaarke/workspace-layouts` package) as `useWorkspaceLayouts(opts)` that:
- Accepts `bffBaseUrl + authenticatedFetch + isAuthenticated` from caller (hook-deps style — works for both LegalWorkspace standalone via local `useAuth()` and SpaarkeAi via `useAiSession()`).
- Has an optional `parseLayoutJson: boolean` flag (default true) so LegalWorkspace gets the parsed `LayoutJson`, SpaarkeAi opts out.
- Has an optional `fallbackLayout: WorkspaceLayoutDto | null` (default null) so LegalWorkspace can pass `SYSTEM_DEFAULT_LAYOUT` and SpaarkeAi opts out.
- Owns the sessionStorage cache contract centrally.

**Audit estimate**: ~8 hours (extract + adapt both consumers + verify embedded + standalone parity).

**Recommendation for R4**: **INCLUDE if R4 has appetite for code change**. Otherwise defer to a code-quality round. ~8h, one-time consolidation that pays back every future BFF-schema change.

**Effort**: ~8h.

---

### C-4. `WorkspaceLayoutWidget` hard-wired to `LegalWorkspaceApp`

**Source**: Audit §3 + §8 row 4.

**What's coupled today**: `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/WorkspaceLayoutWidget.tsx` does exactly one thing — `<LegalWorkspaceApp version="embedded" embedded initialWorkspaceId={data.layoutId} />`. There is no abstraction layer, no "workspace renderer" interface.

**Why it matters**:
- **Future hosts cannot supply a different renderer**: If Spaarke ever wants a "lightweight workspace" with different chrome (say, a stripped-down view for Outlook), today's path is to either (a) duplicate `WorkspaceLayoutWidget` with a fork, or (b) add a feature flag inside `LegalWorkspaceApp` to control which chrome bits are rendered. Neither is clean.
- **The widget IS PRESERVED for the operator's reuse principle** ("when we have working components reuse them") — embedding the working LegalWorkspaceApp avoided a 30-file / 10K-LOC duplication. So this gap is the COST of that pragmatic choice. It is documented as such in `WorkspaceLayoutWidget.tsx`'s own docblock (lines 7-15).

**Remediation direction (from audit)**: Introduce a `WorkspaceRenderer` interface in `@spaarke/ui-components`:

```ts
export interface WorkspaceRenderer {
  /** Render a workspace by its layout id; the renderer fetches/parses its own sectionsJson. */
  render(props: { layoutId: string; webApi: unknown; userId: string }): React.ReactNode;
}
```

`LegalWorkspaceApp` becomes one implementation. `WorkspaceLayoutWidget` accepts an injected `renderer` prop (or uses a registry like the widget registry). Default registration points to `LegalWorkspaceApp`. Future renderers (e.g., "OutlookLiteWorkspace") register their own.

This is a small refactor IF we make `LegalWorkspaceApp` an implementation of the interface.

**Audit recommendation**: "Defer until a non-MDA host is on the roadmap." LOW impact today / HIGH impact if multi-host is needed.

**Recommendation for R4**: **DEFER** unless a non-MDA host (Outlook, Teams) becomes a real near-term roadmap item.

**Effort if picked up**: ~4h.

---

### C-5. Hoist remaining 5 section factories (DEFERRED per Calendar precedent)

**Source**: Audit §2 + §2A + §8 row 5.

**What's coupled today**: All 6 section factories live in `src/solutions/LegalWorkspace/src/sections/*.registration.ts`. Each factory's `renderContent()` returns a React tree that imports from `src/solutions/LegalWorkspace/src/components/<Section>/`.

The implementations REACH into LegalWorkspace-local context providers and services:
- `documents` + `todo` + `latestUpdates` use LegalWorkspace's `DataverseService` and `FeedTodoSyncContext`.
- `quick-summary` uses `useQuickSummaryCounts` (LegalWorkspace-local hook calling `webApi.retrieveMultipleRecords`).
- `daily-briefing` IS hoisted to `@spaarke/ui-components/components/WorkspaceShell/sections/dailyBriefing/` (Task 069) — but LegalWorkspace's `sections/dailyBriefing/dailyBriefing.registration.ts` is now a SHIM that re-exports from there to preserve the static export shape. The actual factory contains LegalWorkspace-local wiring for `authenticatedFetch` + `trackEvent`.
- `calendar` IS the proven canonical shared-lib widget + thin LW shim pattern (Task 115 — see audit §2A). The shim is 62 lines with zero LW-internal coupling.

**Can a non-LegalWorkspace Code Page use just one section today?** No (for the 5 LW-internal sections).

**Audit's revised view (post-Calendar)**: The original §2 said hoist each of the 5 existing sections at ~6–10 hours each (30–50 hours total). **Calendar's existence reduces the urgency of that backlog** — there is now a proven path to reuse without hoisting, *as long as the new functionality is in a shared lib from day one*. The 5 existing sections only NEED to be hoisted when a non-LegalWorkspace host needs them (e.g., a future Outlook side pane, Teams app, MDA form embed). Until that day, the 5 can stay LW-local.

**Audit recommendation**: "**DEFER** — hoist incrementally only when a non-LegalWorkspace host actually needs them. Calendar's 'shared-lib widget + thin LW shim' pattern shows you can grow NEW reuse-safe widgets without back-fitting the existing 5."

**Recommendation for R4**: **DEFER**. Not blocking. Total effort if/when picked up: 30-50h.

**Effort if picked up**: 30-50h.

---

### C-6. Hoist `runtimeConfig` to `@spaarke/auth`

**Source**: Audit §6b + §8 row 6.

**What's coupled today**: SpaarkeAi's `main.tsx` calls BOTH `setRuntimeConfig(config)` (SpaarkeAi) AND `setLegalWorkspaceRuntimeConfig(config)` (LegalWorkspace) with the same config. Two singletons hold equivalent values; the embedded code paths call `getBffBaseUrl()` from LegalWorkspace's singleton. The docblock on main.tsx lines 50-60 acknowledges this. It's not a bug — it's a coupling.

**Why it matters**: Any new host embedding LegalWorkspaceApp must remember to call BOTH setters. This is in the embedded-mode contract (C-2).

**Remediation direction (from audit)**: Long-term, hoist `runtimeConfig` to `@spaarke/auth` as a single global singleton. Risk: cross-Code-Page coupling — if a new Code Page somehow loaded both packages with different config (unlikely), the singleton would be wrong. Mitigate with init guard.

**Audit recommendation**: "Defer until next auth-package round."

**Recommendation for R4**: **DEFER** unless R4 includes an auth-package update. ~4h if picked up.

**Effort if picked up**: ~4h.

---

### C-7. Section registry plug-in style

**Source**: Audit §7.4 + §8 row 7.

**What's coupled today**: `SectionRegistration` already IS a plug-in shape, but the registration map is hardcoded in `sectionRegistry.ts`. To allow third-party registration (e.g., a customer-authored section pack), we'd need:
- A registry instance with `registerSection(reg)` + `getAllSections()` (mirror of `WorkspaceWidgetRegistry`).
- A discovery mechanism (side-effect import) so customer code can register before SpaarkeAi cold-load.

**Audit recommendation**: "LOW today, HIGH if third-party sections wanted — Defer."

**Recommendation for R4**: **DEFER** unless third-party sections become a near-term roadmap item.

**Effort if picked up**: ~6h.

---

## D. R3 verification gaps (gaps found during verification phases)

### D-1. SessionPersistence does NOT cover the tabs list (UQ-03 confirmed) ⚠️

**Covered above as A-5** — listed here also because it's a verification finding (Task 063), not just a design-time deferral. **STRONGEST RECOMMENDATION TO INCLUDE IN R4.**

See §A-5 above for full detail. ~8h.

---

### D-2. SpaarkeAi bundle size — Option 2 (separate web resources for heavy libs) NOT pursued

**Source**: [notes/bundle-size-verification.md](../spaarke-ai-platform-unification-r3/notes/bundle-size-verification.md) §7 + [notes/perf/bundle-size-investigation.md](../spaarke-ai-platform-unification-r3/notes/perf/bundle-size-investigation.md).

**What R3 measured**:

| Stage | gzip | Δ from prior |
|---|---|---|
| Pre-R3 baseline (master) | ~258 KB | — |
| Post-Wave 1 (Phase A foundations) | 508 KB | +250 KB |
| Post-Wave 2a+2c | 506 KB | -2 KB |
| Post-Wave 2b | 509 KB | +3 KB |
| Post-Wave 3 | 798 KB | +289 KB |
| Final R3 | ~918 KB | +120 KB through R10-R13 polish |
| NFR-12 budget | 508 KB (baseline + 250) | — |
| **Over budget by** | **~410 KB** | — |

**Root cause** (verified): `vite-plugin-singlefile` inlines async chunks into the main HTML. R3 task 024's `useChatFileAttachment` hook lazy-imports `pdfjs-dist` + `mammoth` via `await import('pdfjs-dist')` / `await import('mammoth')` inside the `addFiles` function body. Under a normal Vite build, those become separate JS chunks loaded on demand → the libraries are NOT in the initial bundle. Under `vite-plugin-singlefile`, the async chunks are inlined into the main HTML → the libraries ARE in the initial bundle. `pdfjs-dist` minified+gzipped is ≈ 250 KB. `mammoth` is ≈ 50 KB. Together they account for ~290 KB.

**R3 chose Option 1 (accept the overrun, document the deviation)**. Phase G smoke tasks (074) confirmed NFR-01 (pane render < 500ms) and NFR-03 (History overlay < 200ms + populate < 300ms) still passed despite the larger bundle, so Option 1 was deemed acceptable.

**Deferred to follow-on projects** (per [bundle-size-verification.md §7](../spaarke-ai-platform-unification-r3/notes/bundle-size-verification.md)):

| # | Action | Owner | Effort |
|---|---|---|---|
| Option 2 | Deploy PDF.js + mammoth as separate Dataverse web resources | TBD (recommended: BFF remediation project or dedicated project) | 1-2 weeks |
| Option 3 | Server-side extraction via BFF endpoint | BFF remediation team | Medium |
| ADR-031 amendment | Add "Heavy library handling" subsection (singlefile/lazy-import incompatibility note + Option 2 pattern reference) | Architecture / docs owner | ~4h |

**Option 2 detail**: Build `pdfjs-dist` + `mammoth` as separate web resource files. Deploy them to Dataverse (additional script for `Deploy-SpaarkeAi.ps1`). Refactor `useChatFileAttachment` to load them via dynamic `<script>` tag injection from a known Dataverse web resource URL rather than `await import()`. Risk: cross-origin / auth nuances with loading scripts from Dataverse web resources. CSP may block. Needs investigation spike.

**Option 3 detail**: Extend Phase E backend (the chat endpoint extended in task 050) to accept raw file bytes. Do extraction server-side. Return text. Risk: SPEC DEVIATION from R3 FR-07 + OC-02 (both explicitly specify "client-side text extraction"). Server-side extraction is a different architecture AND a different security posture — file bytes hit the server, not just extracted text.

**Recommendation for R4**:
- **ADR-031 amendment**: **INCLUDE**. Doc-only, ~4h. Future Code Page projects will hit the same singlefile-vs-lazy-import surprise.
- **Option 2** (separate web resources for heavy libs): **DEFER** as a dedicated project (1-2 weeks). Note this in the R4 backlog but don't try to fit in.
- **Option 3** (server-side extraction): **DEFER**. Major architectural shift; needs operator + spec sign-off.

**Effort for R4 minimum**: ~4h (ADR-031 amendment only).

---

### D-3. Bundle-analyzer verification deferred (task 024)

**Source**: [notes/perf/024-bundle-lazy.md](../spaarke-ai-platform-unification-r3/notes/perf/024-bundle-lazy.md).

**Detail**: Task 024 (the `useChatFileAttachment` hook) verified lazy-import at the SOURCE level (regex confirmed no top-level `import 'pdfjs-dist'` or `import 'mammoth'`). Full bundle-analyzer verification was deferred to task 061 (Phase F bundle size verification). Task 061 then deferred to Phase G (smoke testing) and ultimately to "post-R3 follow-on" because of the Option 1 acceptance.

**Status today**: Source-level lazy-import IS verified (CI guardrail via unit test). Bundle-analyzer level inspection has NOT been done. We KNOW from the bundle size investigation that singlefile inlines the chunks — so the bundle-analyzer would show pdfjs/mammoth in the main chunk anyway.

**Why it matters**: If we ever switch off singlefile or add a non-singlefile target, we should re-verify with bundle-analyzer that the chunks split correctly. Today's source-level test is necessary but not sufficient.

**Recommendation for R4**: **DEFER** — bundled with D-2 Option 2 work. Only matters when we attempt to split the bundle.

**Effort**: bundled with D-2.

---

## E. R3 project wrap-up

### E-1. Task 090 — Project wrap-up

**Source**: R3 [tasks/TASK-INDEX.md](../spaarke-ai-platform-unification-r3/tasks/TASK-INDEX.md) — task 090 marked 🔲 pending.

**What it does**:
1. Run final quality gates: `/code-review` on all project code; `/adr-check` on all project code. Fix any critical issues.
2. Run `/repo-cleanup projects/spaarke-ai-platform-unification-r3` — audit ephemeral files; approve removals (notes/debug/, notes/spikes/, notes/drafts/); archive handoffs (notes/handoffs/ → .archive/).
3. Update R3 [README.md](../spaarke-ai-platform-unification-r3/README.md): Status → "Complete"; Last Updated → 2026-05-24; Phase → "Complete"; Progress → "100%"; Completed Date → 2026-05-24; check all Graduation Criteria boxes; add completion entry to Changelog.
4. Update R3 [plan.md](../spaarke-ai-platform-unification-r3/plan.md): Status → "Complete"; all milestone statuses → ✅.
5. Document lessons learned: create `projects/spaarke-ai-platform-unification-r3/notes/lessons-learned.md` capturing the 13 rounds of polish + the key architectural decisions (PaneHeader hoist, Calendar widget pattern, all-panes-collapsed empty state, etc.).
6. Final verification: All task files marked completed in TASK-INDEX.md; all documentation current; no critical code-review issues remaining; repository cleanup completed.

**Why deferred**: The R3 task list ran from 001 → 140 with continuous operator polish; wrap-up was always intended as the last task.

**Recommendation for R4**: **INCLUDE as the first item in R4**. Closes R3 cleanly. ~2h.

**Effort**: ~2h.

---

## F. Cross-cutting BFF / governance

These items are not in any single document — they emerge from the BFF AI extraction assessment (May 20, 2026) and the new CLAUDE.md §10 governance.

### F-1. BFF placement-justification audit of R3 additions

**Source**: [CLAUDE.md §10 — BFF Hygiene](../../CLAUDE.md) (added 2026-05-19): "every project that adds code to the BFF MUST have a `design.md` section titled **Placement Justification** answering the decision criteria for each major component."

**Detail**: R3 added one BFF change (task 050 — extending `ChatEndpoints.cs` POST messages with `attachments[]` schema, plus task 051 unit tests). The original R3 design.md was written BEFORE the §10 rule existed (the rule was added 2026-05-19; R3 design.md is from earlier). So R3's design.md does NOT have a Placement Justification section.

**Why it matters**: The §10 rule is binding going forward. Past projects don't retro-fit, but R4 design.md (if it touches BFF) WILL need the section. R3's lack of one is a minor audit-trail gap.

**Recommendation for R4**: 
- **For R3 retroactively**: **DEFER**. Note in R3 lessons-learned (E-1) that the BFF change predated the §10 rule and was within scope per the spike memo + task 050 acceptance criteria.
- **For R4**: **INCLUDE the rule** — any R4 BFF addition MUST follow §10. This is binding.

**Effort**: ~0h for R3 retroactive (just a note); ~1h per BFF change in R4.

---

### F-2. Verify the `Services/Ai/PublicContracts/` facade is used everywhere

**Source**: [CLAUDE.md §10 item 3](../../CLAUDE.md): "Use the `Services/Ai/PublicContracts/` facade for any CRUD code that needs AI capability. Do NOT inject `IOpenAiClient`, `IPlaybookService`, or other AI-internal types directly into CRUD code (per refined ADR-013, 2026-05-20)."

**Detail**: A 2026-05-20 BFF AI extraction assessment found 20 inbound CRUD→AI direct dependencies that should ideally go through a facade. The refined ADR-013 (2026-05-20) introduced the `Services/Ai/PublicContracts/` facade pattern. We do not know whether all 20 dependencies have been migrated.

**Why it matters**:
- **Coupling debt**: Direct injection of AI-internal types into CRUD code is the structural pattern that drove the May 20 assessment's "structurally AI-dominant" finding (AI services = 69% of `Services/` LOC).
- **Future BFF extraction**: If we ever want to extract the AI subsystem to its own service, every direct dependency is a port surface.

**Recommendation for R4**: **INCLUDE an audit** — grep the BFF for `IOpenAiClient`, `IPlaybookService` injections OUTSIDE `Services/Ai/`. Count remaining direct dependencies. Decide whether to migrate them in R4 or track as a separate "AI facade migration" project.

**Effort**: ~2h audit; remediation depends on count.

---

### F-3. Publish-size baseline + per-task verification rule

**Source**: [CLAUDE.md §10 item 4](../../CLAUDE.md): "Verify publish-size impact before merging if adding NuGet packages. Baseline is ~60 MB compressed per `.claude/constraints/azure-deployment.md`."

**Detail**: The 2026-05-19 publish-size jump (65 → 75+ MB) prompted the §10 governance. R3 did NOT add NuGet packages but also did NOT establish a publish-size baseline check as a routine pre-commit / pre-merge step.

**Why it matters**: Without a baseline check, the next package addition will likely re-trigger the same problem.

**Recommendation for R4**: **INCLUDE** — add a publish-size measurement to a build script (or CI). Possibly part of E-1 lessons-learned + a constraint-doc update. ~1h.

**Effort**: ~1h.

---

## Decision matrix

For each item, recommended decision INCLUDE / DEFER / OUT-OF-SCOPE for R4:

| ID | Item | Recommend | Effort | Why |
|---|---|---|---|---|
| **A-1** | Stages 2-4 header treatment | DEFER | 3-5 days | Moment 2 scope |
| **A-2** | ADR-030 + ADR-031 (PaneEventBus + stage lifecycle) | DEFER (or include in doc round) | ~6h | Doc-only; codifies stable patterns |
| **A-3** | AI-vs-User visual + AIReasoningSurface conventions | OUT-OF-SCOPE | weeks | Major strategy work |
| **A-4** | File-attachment policy doc | INCLUDE if R4 touches BFF | ~3h | Closes FR-07 gap |
| **A-5** = **D-1** | Task 065 — SessionPersistence tab-state ⚠️ | **INCLUDE** | ~8h | Operator-visible gap |
| **B-1** | `.gitignore` build artifacts | **INCLUDE** | ~1h | Eliminates noise everywhere |
| **B-2** | `@spaarke/ai-widgets` tsc cross-rootDir fix | **INCLUDE** | ~3h | CI can't gate today |
| **B-3** | Telemetry constant rename | INCLUDE if doing dev-ex round | ~2h | App Insights coordination needed |
| **B-4** | `WorkspaceLayoutDto.modifiedOn` | INCLUDE if touching Manage pane | ~2h | Single field, single surface |
| **B-5** | PUT → PATCH with ETag | DEFER | ~4-6h | Not operator-visible |
| **B-6** | `CalendarSidePane` divergent CalendarSection | **INCLUDE** | ~4h | Visual inconsistency |
| **B-7** | `useEventsBulkActions` hook extraction | **INCLUDE** | ~3h | Natural follow-on to task 114 |
| **B-8** | `CalendarDrawer.eventDates` API drift | **INCLUDE** | ~3h | Bundles with B-6/B-7 |
| **B-9** | ESLint v9 migration | INCLUDE if doing dev-ex round | ~2h | Consistency with rest of repo |
| **B-10** | Redeploy standalone EventsPage | **INCLUDE** | ~30min | Tiny |
| **B-11** | Type-drift casts cleanup | INCLUDE if doing dev-ex round | ~4h | Bundles with B-2 |
| **C-1** | Xrm.WebApi vs BFF decision criteria 🔥 | **INCLUDE — top priority** | ~2h | Doc-only; flagged across audit + every deploy |
| **C-2** | Embedded-mode contract doc | **INCLUDE** | ~3h | Doc-only |
| **C-3** | Consolidate dual `useWorkspaceLayouts` | **INCLUDE if appetite for code** | ~8h | Eliminates drift risk permanently |
| **C-4** | `WorkspaceRenderer` interface | DEFER | ~4h | Only matters if non-MDA host |
| **C-5** | Hoist remaining 5 section factories | DEFER (per Calendar precedent) | 30-50h | Calendar pattern works without |
| **C-6** | Hoist `runtimeConfig` to `@spaarke/auth` | DEFER | ~4h | Works today; cleaner later |
| **C-7** | Section registry plug-in style | DEFER | ~6h | Only matters if 3rd-party sections |
| **D-1** | Task 065 — SessionPersistence (= A-5) | **INCLUDE** | ~8h | Same as A-5 |
| **D-2** | Bundle Option 2 (separate web resources) | DEFER as dedicated project | 1-2 weeks | Major; needs own scope |
| **D-2 ADR** | ADR-031 amendment | **INCLUDE** | ~4h | Doc-only; future Code Pages |
| **D-3** | Bundle-analyzer verification | DEFER (bundled with D-2) | bundled | Only matters with Option 2 |
| **E-1** | R3 project wrap-up | **INCLUDE — first item** | ~2h | Closes R3 |
| **F-1** | BFF Placement Justification rule going forward | **INCLUDE the rule** | ~1h/change | §10 binding |
| **F-2** | `Services/Ai/PublicContracts/` facade audit | **INCLUDE audit** | ~2h | Coupling debt |
| **F-3** | Publish-size baseline check | **INCLUDE** | ~1h | Prevents recurrence |

---

## Recommended R4 minimum vs. expanded scope

### Tier 1 — MINIMUM (highest ROI, mostly doc, no risk)

| ID | Item | Effort |
|---|---|---|
| E-1 | R3 project wrap-up | ~2h |
| C-1 | Xrm.WebApi vs BFF decision criteria 🔥 | ~2h |
| C-2 | Embedded-mode contract doc | ~3h |
| D-2 ADR | ADR-031 amendment (heavy library handling) | ~4h |
| D-1 / A-5 | Task 065 — SessionPersistence tab-state ⚠️ | ~8h |
| F-1/F-3 | BFF governance follow-through (rule + size check) | ~2h |
| **Tier 1 total** | | **~21h (~3 days)** |

### Tier 2 — ADD if appetite (build hygiene cluster)

| ID | Item | Effort |
|---|---|---|
| B-1 | `.gitignore` build artifacts | ~1h |
| B-2 | `@spaarke/ai-widgets` tsc cross-rootDir fix | ~3h |
| B-6 | `CalendarSidePane` divergent CalendarSection | ~4h |
| B-7 | `useEventsBulkActions` hook extraction | ~3h |
| B-8 | `CalendarDrawer.eventDates` API drift | ~3h |
| B-10 | Redeploy standalone EventsPage | ~30min |
| **Tier 2 add** | | **~14.5h (~2 days)** |

### Tier 3 — ADD if doing code-quality + dev-ex

| ID | Item | Effort |
|---|---|---|
| C-3 | Consolidate dual `useWorkspaceLayouts` | ~8h |
| B-3 | Telemetry constant rename | ~2h |
| B-9 | ESLint v9 migration | ~2h |
| B-11 | Type-drift casts cleanup | ~4h |
| F-2 | `Services/Ai/PublicContracts/` facade audit | ~2h |
| **Tier 3 add** | | **~18h (~2.5 days)** |

### Grand totals

| Scope | Effort |
|---|---|
| **Tier 1 only (minimum)** | ~21h (~3 days) |
| **Tier 1 + Tier 2** | ~35.5h (~5 days) |
| **Tier 1 + Tier 2 + Tier 3** | ~53.5h (~7 days) |

---

## Sources consulted

This consolidation pulls from these R3 artifacts and repo docs. All cited directly inline above; this list is for traceability:

| Source | Coverage |
|---|---|
| `projects/spaarke-ai-platform-unification-r3/design.md` §H, §N | A-1 through A-5 |
| `projects/spaarke-ai-platform-unification-r3/notes/r8-plan.md` | B-1 through B-5 |
| `projects/spaarke-ai-platform-unification-r3/notes/backwards-compat-verification.md` | A-5 / D-1 |
| `projects/spaarke-ai-platform-unification-r3/notes/bundle-size-verification.md` §7 | D-2 |
| `projects/spaarke-ai-platform-unification-r3/notes/perf/bundle-size-investigation.md` | D-2 root cause |
| `projects/spaarke-ai-platform-unification-r3/notes/perf/024-bundle-lazy.md` | D-3 |
| `projects/spaarke-ai-platform-unification-r3/notes/067-shared-lib-hoist-summary.md` | Context for C-5 |
| `projects/spaarke-ai-platform-unification-r3/notes/spikes/001-fr07-attachments-payload.md` | A-4 (size cap evidence) |
| `projects/spaarke-ai-platform-unification-r3/notes/deploys/2026-05-20-deploy.md` task 097 | B-3 |
| `projects/spaarke-ai-platform-unification-r3/notes/deploys/2026-05-20-deploy.md` tasks 107, 114-122 | B-1, B-2, B-6, B-7, B-8, B-10, B-11 |
| `projects/spaarke-ai-platform-unification-r3/notes/audit-auth-2026-05-20.md` | Auth audit context (C-2, C-6) |
| `docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md` §1-8 | C-1 through C-7 |
| `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md` | F-2 background |
| `CLAUDE.md` §10 (BFF Hygiene) | F-1, F-2, F-3 |

---

## Next step

Review this document. For each item in the Decision Matrix, mark **INCLUDE** / **DEFER** / **OUT-OF-SCOPE** with any notes. Once decided, the INCLUDE items become the basis for R4 `spec.md` — formalized as FRs / NFRs / tasks following the standard project-pipeline shape.

*End of R4 consolidated backlog.*
