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
