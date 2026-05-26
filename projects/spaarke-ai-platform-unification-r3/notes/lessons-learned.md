# Spaarke AI Platform Unification R3 — Lessons Learned

> **Project**: spaarke-ai-platform-unification-r3
> **Status**: Complete (2026-05-26)
> **Final commit (master)**: `f5015c2a`
> **Duration**: 2026-05-20 (project init via /project-pipeline) → 2026-05-26 (wrap-up)
> **Tasks completed**: 140 (original 25 FRs / 12 NFRs + 13 rounds of operator-driven polish)

---

## 1. Shape of the project

R3 was scoped as "Moment 1: Arrival" — visual + structural polish of the SpaarkeAi welcome state. Original spec had **25 FRs + 12 NFRs + 12 ADRs**. The implementation plan called for 7 phases (Foundations → Assistant → Workspace → Context → Backend → Verification → Deploy) with ~46 initial tasks.

**Actual delivery**: 140 tasks across **13 rounds of polish**. The original plan was the foundation; 60%+ of total task volume came from operator-driven iteration after initial Phase A-G work landed. This is a pattern worth recognizing.

---

## 2. Architectural insights that crystallized during the project

### 2.1 The two-wrapper model (Dashboard + Direct widget) was always implicit but never written down

R3 inherited two parallel rendering pipelines in the workspace pane:

- **Dashboard wrapper** (`WorkspaceLayoutWidget` → `<LegalWorkspaceApp embedded>` → section registry): renders a `sprk_workspacelayout` JSON as a grid of sections. Used by all system + user-created "workspaces."
- **Direct widget wrapper** (`WorkspaceWidgetRegistry` + `widget_load` dispatch): mounts a single React component as a tab. Used by `RedlineViewerWidget`, R1-wrapped output widgets, and (de-facto) by the modal-launcher widgets.

Both wrappers existed before R3. **Neither was documented as a deliberate architecture.** This caused real confusion during R4 scoping — the team couldn't articulate "where do new widgets go" without first reverse-engineering the two paths from code.

**Lesson**: Cross-cutting architectural patterns that the codebase relies on for years should have a written-down explanation. R4 W-1 is the resulting work item.

### 2.2 Calendar widget (Task 115) established the canonical "Pattern D" forward path

Before R3, the only shared-lib section was Daily Briefing (Task 069). Calendar (Task 115) shipped the **complete** canonical pattern:

1. Component in shared lib (`@spaarke/events-components/widgets/CalendarWorkspaceWidget`)
2. Thin LW registration shim (62 lines, zero LW-internal coupling)
3. System workspace entry in `system-layouts.json`
4. Pattern documented (in this file's predecessor + in `BUILD-A-NEW-WORKSPACE-WIDGET.md`)

The componentization audit (Task 113 / 123) **revised its position** post-Calendar: it had originally recommended hoisting all 5 LW-internal sections (~30-50h). Calendar's existence demonstrated you can grow NEW reuse-safe widgets without back-fitting the 5 — the audit changed the recommendation to "defer hoisting; new sections follow Pattern D."

**Lesson**: A worked example moves the audit recommendation more than abstract analysis does. Calendar shifted "must hoist 5 sections" to "don't need to."

### 2.3 The Round 8 Option B unification is good architecture, not legacy debt

Round 8 made the architectural decision to embed `LegalWorkspaceApp` whole inside SpaarkeAi rather than re-author its 30+ files. This created the `WorkspaceLayoutWidget → LegalWorkspaceApp embedded` indirection that initially looks like "wrapping the whole legacy app in a single tab."

During R4 scoping, this was reframed as: **LegalWorkspace is the dashboard engine; SpaarkeAi is the host surface.** Same code, different conceptual lens. The reframing dissolved a lot of perceived debt — "5 sections trapped in LegalWorkspace" became "5 sections that happen to live in the dashboard engine, which is fine."

**Lesson**: Architectural compromises made early in a project can look like debt later — until the right conceptual frame makes them look correct. Worth investing in the framing.

### 2.4 The wizard catalog drift bug was invisible until R4 scoping

`WorkspaceLayoutWizard` has its own hardcoded `SECTION_CATALOG` (5 entries) instead of reading from `SECTION_REGISTRY` (7 entries after Calendar + Daily Briefing). A TODO comment in `App.tsx` line 85 said "In a production build this would be fetched from GET /api/workspace/sections" — that TODO never got closed.

Result: Calendar and Daily Briefing are registered as sections (and pickable as system workspaces) but **invisible in the user-facing dashboard builder.** Users can only build dashboards from the 5 original sections.

This bug shipped with R3 and was only discovered during R4 scoping when the operator asked "where do new widgets go in the builder?" The codebase didn't fail; it just silently omitted features.

**Lesson**: Loosely-coupled subsystems (wizard ↔ section registry) can drift silently when one updates without the other. End-to-end smoke tests that check "is X visible in Y surface after registration" would have caught this earlier. R4 W-3 is the fix.

---

## 3. Process patterns that worked

### 3.1 Spike-first for ambiguous backend dependencies

Task 001 (FR-07 attachments payload spike) was a 30-minute investigation that determined whether Phase E (backend extension) was required. Result: REQUIRED. Phase E unlocked accordingly. Without the spike, the team would have either over-built (assumed extension always needed) or under-built (frontend payload would be silently ignored).

**Pattern**: When a feature's implementation depends on an unverified backend assumption, spike first, plan second.

### 3.2 Operator-driven polish cycles as the bulk of work

The original 46-task plan delivered FR-01 through FR-25. Tasks 091-140 (50 additional tasks, ~13 rounds) were all operator feedback responses:
- Visual polish (icon sizes, font weights, spacing)
- Behavioral refinement (pin button placement, dropdown labels, tab ordering)
- Edge cases revealed only after using the shipped UI
- Cross-pane consistency adjustments
- Bug fixes surfaced by real usage (OData errors, timezone bugs, Dropdown overflow)

This is **not a planning failure** — it's the natural shape of UI/UX work. The spec defines the destination; iteration defines the experience.

**Pattern**: Budget for iteration. R3 planned ~46 tasks and shipped ~140. Future UI projects should expect 2-3x task multiplication from polish cycles.

### 3.3 Cherry-pick to master workflow

R3 used the worktree pattern: work happens in `c:/code_files/spaarke-wt-spaarke-ai-platform-unification-r3/` (R3 branch), each commit cherry-picks to `c:/code_files/spaarke/` (master), both push to origin. This kept master continuously deployable while preserving branch-level history.

**Pattern**: For long-running projects with frequent operator-visible polish, cherry-pick-as-you-go beats merge-at-end.

### 3.4 Project-level CLAUDE.md + current-task.md as recovery state

R3 weathered multiple compaction events without losing context. The `current-task.md` + per-task POML files + TASK-INDEX.md gave Claude Code recoverable state. Lessons from earlier projects (R2's PaneEventBus pattern, write-through Cosmos persistence) propagated cleanly into R3 via the project-level CLAUDE.md.

**Pattern**: Persistent file-based state survives session boundaries. Verbose is better than terse for these files.

---

## 4. Things that surprised us

### 4.1 Vite-plugin-singlefile inlines async chunks

R3 added `pdfjs-dist` (~250 KB) + `mammoth` (~50 KB) as lazy-imports (`await import('pdfjs-dist')`). Under a normal Vite build, these become separate JS chunks loaded on demand. Under `vite-plugin-singlefile` (used for Dataverse Code Page deployment per ADR-026), **async chunks are inlined into the main HTML.** Net: lazy imports lose their lazy benefit. Bundle size jumped ~290 KB.

This was non-obvious. The lazy-import code looks correct in isolation; the bundling tool changes the runtime behavior silently.

**Lesson**: Bundle-size assumptions are tool-dependent. R4 D-2 work amends ADR-026 with a "Heavy library handling" subsection so future Code Page projects don't repeat this discovery.

### 4.2 Fluent v9 Dropdown intrinsic min-width occludes parent margins

Task 140 (last R3 task) diagnosed a 6-iteration mystery: the operator reported "no visible gap between filter fields" despite `style="margin-right: 28px"` being correctly applied. The cause: Fluent v9 Dropdown component sets a hard `min-width: ~240px` via atomic CSS class. With `flex: 0 0 140px` on the parent, the inner Dropdown **overflowed by ~100px and painted over the 28px right-margin gap.** The margin existed; the Dropdown was just covering it.

Fix: descendant selectors `> .fui-Dropdown { min-width: 0; width: 100% }`.

**Lesson**: Visual layout debugging in component libraries with atomic CSS requires checking children's intrinsic constraints, not just parent's margins/padding. Operator's DOM inspection was the breakthrough.

### 4.3 OData filter type mismatch (Edm.Int32 vs Edm.String)

Multiple polish tasks (130-132) hit "infinite loop error" or `2147879048` errors from Dataverse OData. Root cause: status filters used `sprk_eventstatus eq '0'` (string-quoted) when the field is `Edm.Int32`. Removing quotes fixed it.

**Lesson**: Dataverse OData type errors look like infinite loops at the UI level. When the response is "infinite loop / hung," check the OData expression types first.

### 4.4 Dataverse lookup table naming conventions are not consistent

R3 had to handle multiple lookup-field-name patterns:
- `sprk_eventtype` → wrong (the *option set* style)
- `sprk_eventtype_ref` → correct (the *lookup table* style)
- `_sprk_eventtype_ref_value` → the OData expansion

Code had to alias all three. This is Dataverse schema quirk, not R3 design.

**Lesson**: Lookup field naming varies even within one solution. Schema-driven code needs to alias common variations.

---

## 5. Deferrals that became R4 items

R3 explicitly deferred several items to follow-on rounds. The R4 project (`projects/spaarke-ai-platform-unification-r4/`) consolidates these:

### In R4 scope (decided 2026-05-25)

- **A-4 attachment policy + 25 MB cap** (was: defer policy doc; now: code change + doc)
- **A-5 / D-1 SessionPersistence verify-then-fix** (UQ-03 needs re-verification)
- **B-1 through B-11 build hygiene cluster** (~21h of small items accumulated across R3 rounds)
- **C-1 Xrm.WebApi vs BFF decision criteria doc**
- **C-2 Embedded-mode contract doc** (relevant now that LW code page is being retired)
- **C-3 Consolidate dual `useWorkspaceLayouts` hooks**
- **C-4 WorkspaceRenderer interface**
- **D-2 ADR-026 amendment** (heavy library handling)
- **W-1 through W-6 Workspace + Dashboard architecture group** (new in R4 scoping)
- **F-1 retroactive memo** (this Phase 0 work)
- **F-2 facade audit, F-3 publish-size baseline**

### Deferred from R4 (also)

- Stages 2-4 chrome → Moment 2+ scope
- AI-vs-User visual + AIReasoningSurface conventions → strategy work
- Hoist remaining 5 LW-internal sections → Calendar precedent + no forcing function
- `runtimeConfig` hoist to `@spaarke/auth` → works today
- Section registry plug-in style → no 3rd-party need
- Bundle Option 2 separate web resources → dedicated future project

---

## 6. Specific deferrals worth flagging for future maintainers

### 6.1 Bundle size deviation accepted

R3 final SpaarkeAi bundle: **~918 KB gzip**. NFR-12 target: <250 KB delta vs R2 baseline (i.e., ~508 KB ceiling). Actual delta: ~660 KB.

The deviation was deliberate per Task 061's Option 1 acceptance (documented in `notes/bundle-size-verification.md`). Phase G smoke tasks (Task 074) verified NFR-01 (pane render <500ms) + NFR-03 (History overlay <200ms + populate <300ms) still passed despite the larger bundle.

**Future work** if bundle size becomes a real problem: implement Bundle Option 2 (separate Dataverse web resources for `pdfjs-dist` + `mammoth`) per the R4 D-2 ADR-026 amendment.

### 6.2 LegalWorkspace code page retirement decided 2026-05-25

Operator decision during R4 scoping: stop deploying `sprk_corporateworkspace` (standalone LegalWorkspace web resource). LegalWorkspace components continue as a library consumed via embed in SpaarkeAi.

This makes the R3 spec's FR-25 / NFR-10 ("standalone LegalWorkspace must continue to function identically") MOOT going forward. The embedded LegalWorkspaceApp continues to work; the standalone page just doesn't deploy.

R4 W-6 documents this retirement formally.

### 6.3 UQ-03 (NFR-09) tab persistence verdict shifted

Task 063 (backwards-compat verification) ORIGINALLY found tabs are NOT persisted across refresh — gap confirmed. Task 065 (extend SessionPersistence) was created and shipped.

**However**, operator feedback during R4 scoping suggested tabs ARE persisting (after Task 065 fix) — and the actually-broken behavior is that browser-close-and-reopen lands on the last active tab rather than Home/default. This is a DIFFERENT issue from the original UQ-03 gap.

R4 Phase 3 verifies the current behavior fresh and addresses the actual operator-visible issue.

---

## 7. What we'd do differently

### 7.1 End-to-end "is feature X visible in surface Y" tests

The wizard catalog drift bug (W-3) would have been caught by a test like: "After registering Calendar as a section, open the wizard, assert Calendar is in the picker." We didn't have these tests.

For R4 / future: add minimal E2E checks that verify cross-subsystem visibility after each new section.

### 7.2 Document patterns when established, not when re-encountered

The Round 8 Option B unification, the dashboard+widget mental model, the modal-dispatcher vs tab-content distinction, the dual-use pattern — all were established in code during R3 (and earlier) but only got conceptual articulation during R4 scoping conversations.

For R4 / future: when a non-trivial pattern lands, write the doc in the same PR as the pattern. The doc is the most expensive thing to write retroactively because the reasoning has to be reconstructed.

### 7.3 Earlier reconciliation of competing names

"Workspace" was overloaded throughout R3 — pane name, dashboard concept, layout JSON, tab content. This caused friction in R4 scoping when trying to articulate the architecture. Earlier glossary work would have helped.

### 7.4 Operator-driven polish budget should be EXPLICIT

R3 plan estimated ~46 tasks. R3 shipped 140 tasks. The 3x overage was almost entirely operator polish (legitimate UI/UX work), but it wasn't budgeted. Future UI projects should explicitly plan for "Round 1-N polish iterations" as a budgeted phase rather than treating the polish as overrun.

---

## 8. Carrying forward to R4

R4 (`projects/spaarke-ai-platform-unification-r4/`) is the formal follow-on. Key inheritance:

- **Architecture frame**: The two-wrapper model + LegalWorkspace-as-dashboard-engine framing established during R4 scoping. Will be documented in R4 W-1.
- **R3 conventions**: PaneHeader primitive, error-only telemetry, Pattern D for new sections, Calendar's structure as the canonical example
- **Open verification**: A-5 tab persistence (Phase 3)
- **Carry-overs**: build hygiene cluster, ADR work, BFF governance retroactive (this memo) + prospective rules

---

## 9. Final R3 status

- **All 25 FRs**: ✅ Shipped
- **All 12 NFRs**: ✅ Met or explicitly deviated-with-rationale (NFR-12 bundle size, NFR-09 tab persistence — both flagged for R4 follow-up)
- **All 12 ADRs**: ✅ Compliance verified (Task 060 + Task 062 + Task 063)
- **140 tasks**: ✅ All marked complete in TASK-INDEX.md
- **Deployment**: ✅ `sprk_spaarkeai` production-deployed; final state at master `f5015c2a`
- **Wrap-up**: ✅ This document; F-1 retroactive memo; README → Complete; plan.md → Complete

R4 begins from a clean R3 base.

---

*Lessons-learned compiled 2026-05-26 as part of Phase 0 of the R4 project.*
