# Lessons Learned — spaarke-dataset-grid-framework-r2

> **Written**: 2026-07-02 by main session at task 090 wrap-up
> **Project duration**: single-day autonomous execution 2026-07-02 (spec → plan → 21 tasks → 3 phased commits on `work/spaarke-dataset-grid-framework-r2`)
> **PR**: [#537](https://github.com/spaarke-dev/spaarke/pull/537) — code complete; PR merge + deploy + regression pending user validation

---

## What worked

### 1. `/design-to-spec` owner-clarification interview was high-leverage

Three targeted questions during design-to-spec (pageSize default, Issue 12 scope, widthPreference defaults) changed the project meaningfully:
- pageSize 100 → 25 became a code change (not just doc alignment)
- Issue 12 Option B moved from "future project" to R2 scope, adding FR-10 (~1 day)
- widthPreference: 'full' on all 6 widgets set the wizard-warning intent

These would have surfaced later as spec-vs-implementation friction if skipped. The 5-minute interview saved likely 2-4 hours of downstream churn.

**Takeaway**: `/design-to-spec` gap-targeted questions are cheap. Skipping to `/project-pipeline` is false economy.

### 2. Spec's ADR Tensions section successfully caught two Path C candidates upfront

- ADR-022 (React 19 / PCF compat): candidate tension → resolved as Path C (comply) because wizard UI additions live in Vite React 18 code page, not in shared lib. Analysis done at spec-time, not code-review-time.
- CLAUDE.md §11 (Component Justification): FR-10's new package required concrete justification (existing / extension / cost-of-doing-nothing). Path C proceed with justification cited.

Neither surfaced as surprises during execution. **Takeaway**: the ADR Tensions section is worth the 15 minutes it takes to fill out during `/design-to-spec`.

### 3. Wave-based parallel dispatch was efficient

11 waves across 21 tasks in ~1 hour of autonomous subagent execution. Parallel dispatches:
- Wave 1: parallel(001, 002) — shared-lib schema, no file overlap
- Wave 2: parallel(003, 005) — pageSize + unwind (accepted uncommitted deps from Wave 1)
- Wave 5: parallel(010, 014) — Phase 2 schemas
- Wave 13: parallel(022, 023) — SpaarkeAi rewire + LW/WLW no-op

Sequential dispatches:
- Wave 3 (004), 6 (012), 7-9 (011/013/015 all touch App.tsx), 14 (025 main-session-only)

**Takeaway**: pre-computing file overlap during task-create pays off. Auto-demotion rule (any `.claude/` touch → `parallel-safe: false`) worked correctly for task 025.

### 4. Subagent reports were consistently high-signal

Every subagent report included: rigor level declaration, files modified list, deviations from POML, task file status update, and "for main session" handoff notes. Multiple subagents flagged their own POML errors and adapted correctly (e.g., task 022 noticed the tsconfig `.d.ts` anomaly and fixed it as side effect).

**Takeaway**: dispatch prompts should ALWAYS include (a) file ownership boundaries, (b) "don't touch these" list for parallel siblings, (c) required report format.

---

## What surprised (in a good way)

### 5. Package name `@spaarke/legal-workspace` already matched SpaarkeAi's existing alias

Task 020 agent verified SpaarkeAi's vite.config.ts + tsconfig.json already used the exact npm name we chose. Consequence: task 022 was a repoint (change target path), not a rename. Zero SpaarkeAi source-file changes needed for FR-10.

**Takeaway**: check consumer's existing conventions before scaffolding new packages — the "right name" is often already implicit in the ecosystem.

### 6. Task 013 agent correctly identified the placeholder-stub as legitimate scope escape

Wizard "Advanced" panel's `configId` picker cannot query `sprk_gridconfiguration` records at wizard runtime (no `Xrm.WebApi` in scope; `SectionMetadata` doesn't expose entity name). Agent implemented the UI + state + JSON round-trip fully but placeholder-stubbed the option list source. Filed DEF-002 for the follow-on. This is the correct outcome — not fake it, not skip it, do the framework work + document the boundary.

**Takeaway**: subagents can and should make judgment calls about scope escapes when documented.

---

## What surprised (needing course-correction)

### 7. Reality-vs-spec delta on the tactical hack shape (v2 vs v1)

Task 005 agent discovered the tactical `maxHeight` hack was actually `maxHeight: '80vh'` inside a `renderContent` wrapper div (v2 refinement 2026-07-01), NOT `maxHeight: '480px'` in top-level `style` as design.md documented. Agent adapted correctly — removed the wrapper — but this suggests design.md wasn't refreshed after the v1→v2 refinement.

**Takeaway**: design docs get stale between discovery and implementation. Agents should verify shape assumptions before applying edits.

### 8. `sectionRegistry.ts` file location was different from spec's assumption

Spec.md and initial POML for FR-04 referenced `WorkspaceShell/sectionRegistry.ts`, but the file actually lives at `src/solutions/LegalWorkspace/src/sectionRegistry.ts`. Corrected in spec at discovery time (Step 2 explorer agent caught it) and again in task 015 execution.

**Takeaway**: file-path assumptions in specs are frequently wrong. A light discovery pass at `/project-pipeline` Step 2 catches many of these before they turn into POML bugs.

### 9. Wave 5 dispatch: file-overlap analysis found FILE OVERLAP that TASK-INDEX missed

My original TASK-INDEX marked 001-004 as all parallel Wave 1. Reality: 003 + 004 both edit the DataGrid config guide → race risk. Main session split into serial 2-3 sub-waves. Task 013 later ALSO reported that `SectionInstance` type needed to live in ArrangeStep.tsx to avoid a circular import (App.tsx couldn't own it despite POML implying so).

**Takeaway**: `task-create` step 3.8 auto-demotion rule for `.claude/` paths worked; auto-demotion for OTHER same-file overlaps between tasks would help.

### 10. `SectionMetadata` alone wasn't the whole picture — `SectionRegistration` also needed the field

Task 001 agent discovered `buildDynamicWorkspaceConfig` reads from `SectionRegistration` (the builder-side interface), not `SectionMetadata` (the catalog-side interface). Both interfaces got the `contentSizing` field. Task 005 accommodated this in the 6 registration files.

**Takeaway**: the "one canonical interface" mental model breaks when there are builder/catalog dual interfaces. Discovery agents should map ALL relevant interface layers.

---

## Recurring patterns worth documenting

### 11. Source-only shared packages (DailyBriefing precedent)

Multiple shared packages under `src/client/shared/` (DailyBriefing, now LegalWorkspace) are source-only libs with `@spaarke/*` peerDeps and `tsc --noEmit` build scripts. Standalone type-check FAILS by design — TS2307 cannot find peer packages. Type-check happens via consumer solution's tsconfig `paths` mappings.

This pattern is:
- Not obvious to newcomers (task 020 agent flagged it explicitly)
- Consistently maintained across packages (task 021 barrel-level re-export = 1 line)
- Documented via `Build-AllClientComponents.ps1` exclusion comments

**Recommendation**: a `docs/procedures/SHARED-PACKAGE-PATTERNS.md` guide could formalize this. Currently only discoverable by reading DailyBriefing's shape.

### 12. Wizard code page test runner is not wired (DEF-001)

3 test files scaffolded during R2 (rowHeight, sectionInstanceAdvanced, widthPreferencePlacement) cannot execute — no jest config, no test script, no `@testing-library/react` devDeps. Pre-existing scaffold `TemplateStep.test.tsx` was already in the same state.

**Recommendation**: R2 didn't unblock this because scope was tight, but a follow-on ~1-hour task adds the runner + all 4 tests run.

### 13. Vite standalone builds require pre-built peer packages (ISS-003)

`LegalWorkspace`, `SpaarkeAi`, `WorkspaceLayoutWizard` — none can `npm run build` standalone. They need `@spaarke/*` peer packages' `dist/` folders present first. `Build-AllClientComponents.ps1` orchestrates this. New contributors hit this and don't know to run the orchestrator.

**Recommendation**: preflight check in each solution's `npm run build` OR a `WORKSPACE-BUILD.md` guide.

---

## Metrics

- **Duration**: single-day autonomous execution (2026-07-02, ~4 hours wall-clock including scaffolding + all 3 phases)
- **Tasks**: 21 planned → 21 executed (100% completion rate)
- **Subagent dispatches**: 14 (across 11 waves)
- **Parallel efficiency**: 4 parallel waves saved ~50% of theoretical serial time
- **New files created**: 22 (3 shared-lib tests, 4 wizard test scaffolds, 1 LegalWorkspace test, 3 config templates, 4 shared-package files, 3 project artifacts, 3 doc note)
- **Source files modified**: 25+ across 5 packages
- **Tests added**: 66 runnable (shared lib + LegalWorkspace) + 21 scaffolded (wizard, pending DEF-001)
- **Commits**: 3 phased commits + 3 CI auto-format commits from Prettier
- **Reality-vs-spec deltas caught by agents**: 3 (v2 hack shape, sectionRegistry.ts location, SectionRegistration parallel interface)

---

## Recommendations for future projects

1. **Do the owner-clarification interview.** Cheap, high-signal.
2. **Include ADR Tensions section in every spec.** Path C is fine if concrete.
3. **When dispatching parallel subagents, always tell them what NOT to touch.** Prevents accidental overlap even when TASK-INDEX misses a file conflict.
4. **Trust subagent judgment on scope escapes when they document the boundary.** Task 013's configId placeholder was correct.
5. **Verify file-path assumptions during Step 2 discovery.** Cheap; catches spec bugs before they propagate to POMLs.
6. **For source-only shared packages, cite the DailyBriefing precedent.** Standalone build fails by design; type-check happens at consumer.
7. **After each phase's commit, update the PR body.** Keeps reviewers oriented as work accumulates.
8. **Skip formal `/test-diet` at wrap-up ONLY if there's no way to invoke it safely.** Document the skip in the PR (CLAUDE.md §7 binding rule).

---

*Companion: [`notes/defer-issues.md`](defer-issues.md) — 8 deferred concerns filed for follow-on work.*

---

## UAT rounds 1 + 2 addendum (2026-07-03)

> Written after two UAT rounds on `fix/r2-uat-followup-1` (PR #547) shipped 14 additional fixes on top of the original R2 delivery. The lessons below are specific to UAT-round work, not the R2 project itself.

### What worked

#### 1. Batch verification cycles beat one-at-a-time deploys

Operator asked for "batch all 12 in one PR (fastest)" at UAT round 2. This was correct — I did the 12 items across ~4 hours with one build+deploy per round, then user verified the whole batch. Each round-trip took ~15 minutes for build+deploy. If we'd deployed after each item, that's 12 × 15m = 3h in deploy overhead alone.

**Takeaway**: for UAT rounds where fixes are independent (no shared state), batch aggressively. Per-fix deploy is only justified when a fix might destabilize the shell or affect other fixes.

#### 2. Adding "why" comments with dated markers paid off during round 3

Every fix in rounds 1 + 2 was commented with `R2 UAT §X.Y (2026-07-03 round N): <rationale>`. When round 2 revealed §5.5 hit-test still broken despite my counter-based cleanup, I could immediately locate the round-1 comment explaining the counter approach — and see it was incomplete. Rewrote to `getBoundingClientRect` with a fresh comment referencing round 2. Six months from now, an archaeologist can trace the fix evolution without git blame.

**Takeaway**: `<Project> UAT §<item> (<date> round N): <rationale>` markers are dirt cheap and prevent "why is this here?" archaeological expeditions.

#### 3. `/code-review` at UAT-close caught pre-existing large-file drift

Running `/code-review` on the final commit surfaced three files over the 500-line critical threshold: `ArrangeStep.tsx` (2071), `DataGrid.tsx` (1479), `App.tsx` (1001). None are new bloat from R2 — all pre-existing — but the review made the tech debt visible with a specific extraction candidate list (RowSettingsHeader, GridSlot, AdvancedSectionControl, RowHeightPopoverField). Filed as follow-on work.

**Takeaway**: run `/code-review` even on "just UAT fixes" — it catches the drift you'd otherwise never notice.

### What didn't work / caused rework

#### 1. First §5.5 hit-test fix was wrong — counter-based dragEnter/Leave

Round 2 first attempt: added dragEnter+dragLeave counter to suppress child-element flicker. User reported "still not fixed — drop zone activates FAR ABOVE the pointer". Root cause was different: `dragEnter` fires on ancestors when the drag preview extends past the pointer position. Counter can't fix that — the events fire on the wrong element.

Right fix (round 3): hit-test `e.clientX`/`e.clientY` against `getBoundingClientRect()`. If pointer isn't literally inside the slot's box, we're not a drop target for this event.

**Takeaway**: HTML5 DnD is subtle. When symptoms don't match the counter-based fix pattern, don't assume the counter is right and there's some OTHER bug — investigate whether the events are firing on the wrong element entirely. See new [`.claude/FAILURE-MODES.md#g-10-html5-dnd-draganter-fires-on-ancestors-when-preview-extends-beyond-pointer`](../../../.claude/FAILURE-MODES.md).

#### 2. First §5.6 fix was wrong — `height: 100%` in section style

Round 2 first attempt: buildDynamicWorkspaceConfig set section style to `height: 100%, maxHeight: 100%` when jsonRow.rowHeight is set. Logic: row wrapper is `height: rowHeight`, so 100% of that = rowHeight. Should work.

Didn't. User reported "row wrapper adjusts but DataGrid inside stays 500-600px". Root cause: the grid→flex chain from row wrapper to DataGrid crosses several `flex: 1` items with `minHeight: 0`, and `100%` doesn't propagate reliably through all of them across all browsers.

Right fix (round 3): set section style to the LITERAL `rowHeight` value (e.g., `height: '80vh'`), not `100%`. Bypasses chain propagation entirely.

**Takeaway**: `height: 100%` in a nested grid/flex layout is unreliable when the containing block is itself computed via flex distribution. When you have a specific target value in hand, use it literally. See addendum in [`.claude/patterns/ui/embedded-widget-sizing.md`](../../../.claude/patterns/ui/embedded-widget-sizing.md).

#### 3. First §3.3 fix was wrong — `window.__dialogResult` cross-window

Round 2: wizard writes `window.__dialogResult = { confirmed: true, layoutId }` on save; SpaarkeAi shell reads after `navigateTo` promise resolves. Should work.

Didn't. Because `Xrm.Navigation.navigateTo({ target: 2 })` opens a separate window whose `window` object is different from the opener's. `window.__dialogResult` was being written to the popup's window, not the shell's.

Right fix (round 3): sessionStorage bridge with age-gated result. Both windows share sessionStorage per-origin per-tab-set. Wizard writes `{ confirmed, layoutId, at: Date.now() }`; shell reads with `MAX_AGE_MS = 60_000` guard against stale reuse. See new pattern [`.claude/patterns/ui/navigateto-popup-result-bridge.md`](../../../.claude/patterns/ui/navigateto-popup-result-bridge.md).

**Takeaway**: `Xrm.Navigation.navigateTo({ target: 2 })` creates a separate window. Cross-window signaling requires shared storage (sessionStorage / localStorage / BroadcastChannel), not `window.*` globals.

#### 4. Edit mode was silently broken since ship

Root cause of §3.1 "screen is blank" + §3.1 CORS error: the wizard's edit mode was designed to accept `sectionsJson` as a URL param (saveAs pattern) but never fetched the layout from BFF for `mode=edit`. Layout would open with empty state, then PUT with empty state on save.

This was an OG bug from before R2, exposed by R2's edit-mode UX changes. Fix: `App.tsx` mount effect that fetches `/api/workspace/layouts/{id}` when `mode === "edit"` and populates all state.

**Takeaway**: features that "work" only because no one uses them fully will surface as bugs when a follow-on project actually uses them. Regression test the edit path in the next iteration.

### Cross-cutting observation

Three of the four "first fix was wrong" cases (§5.5, §5.6, §3.3) share a theme: **the first fix passed my mental model check but failed empirically because I made assumptions about the runtime behavior I didn't test**. The counter should work because dragEnter/Leave are paired. `height: 100%` should work because the parent has determinate height. `window.__dialogResult` should work because they're both loaded from the same origin.

All three assumptions were wrong for specific runtime reasons (dragEnter fires on ancestors; flex distribution breaks % propagation; navigateTo target:2 is a different window). **The pattern**: when a CSS/DnD/browser-behavior fix "should work" but user reports it doesn't, distrust the mental model and get empirical evidence (DevTools Network tab, computed styles, event target inspection) BEFORE writing the second fix.

### Docs written during close-out

Six documentation artifacts landed alongside PR #547 to memorialize these learnings:
- This addendum
- [`.claude/FAILURE-MODES.md#ap-5`](../../../.claude/FAILURE-MODES.md#ap-5-abortcontroller-in-a-useeffect-whose-deps-include-your-own-state-transition) — AbortController + status-in-deps trap
- [`.claude/FAILURE-MODES.md#g-10`](../../../.claude/FAILURE-MODES.md#g-10-html5-dnd-dragenter-fires-on-ancestors-when-preview-extends-beyond-pointer) — HTML5 DnD hit-testing
- [`.claude/patterns/ui/navigateto-popup-result-bridge.md`](../../../.claude/patterns/ui/navigateto-popup-result-bridge.md) — cross-window signaling
- [`.claude/patterns/ui/embedded-widget-sizing.md`](../../../.claude/patterns/ui/embedded-widget-sizing.md) — row-height addendum
- [`.claude/patterns/ui/fluent-v9-component-authoring.md`](../../../.claude/patterns/ui/fluent-v9-component-authoring.md) — WizardShell.initialStepId prop
